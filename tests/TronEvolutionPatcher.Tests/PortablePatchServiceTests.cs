using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;
using TronEvolutionPatcher.Services;
using static TronEvolutionPatcher.Tests.PortableExecutablePatcherTests;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class PortablePatchServiceTests
    {
        private const string Profile = "Disney Interactive Studios/Tron Evolution/";
        private const string Config = Profile + "UnrealEngine3/GridGame/Config/";
        private const string Saves = Profile + "SaveData/";

        private static string Prepare(TemporaryDirectory temp, string renderer = "d3d9.dll")
        {
            return Path.GetDirectoryName(temp.Write("Game/Binaries/Win32Live/GridGame.exe", PortablePeFixture.Create(renderer)));
        }

        private static string Documents(TemporaryDirectory temp) { return Path.Combine(temp.Root, "Documents"); }
        private static string UserData(TemporaryDirectory temp) { return Path.Combine(temp.Root, "Game", "UserData"); }
        private static string PathIn(string root, string relative) { return Path.GetFullPath(Path.Combine(root, relative)); }
        private static string Exe(string win32) { return Path.Combine(win32, "GridGame.exe"); }

        private sealed class FaultMutation : IFileMutation
        {
            private readonly FileMutation actual = new FileMutation();
            public string FailureName;
            public bool AfterChange;
            public bool FailRollback;
            private bool failed;

            public void Install(string stagedFile, string destination, bool replace)
            {
                if (failed && FailRollback && Path.GetFileName(destination) == "GridGame.exe")
                    throw new IOException("Simulated portable rollback failure");
                if (!failed && Path.GetFileName(destination) == FailureName)
                {
                    failed = true;
                    if (AfterChange) actual.Install(stagedFile, destination, replace);
                    throw new IOException("Simulated portable install failure");
                }
                actual.Install(stagedFile, destination, replace);
            }

            public void Delete(string path) { actual.Delete(path); }
            public void SetAttributes(string path, FileAttributes attributes) { actual.SetAttributes(path, attributes); }
        }

        [TestCase("d3d9.dll", RendererState.DirectX9)]
        [TestCase("vk09.dll", RendererState.Vulkan)]
        public void CombinedPatchMigratesMissingFilesPreservesExistingDataAndRenderer(string library, RendererState renderer)
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp, library);
                var original = File.ReadAllBytes(Exe(win32));
                temp.WriteText("Documents/" + Config + "GridEngine.ini", "document engine");
                temp.WriteText("Documents/" + Config + "GridInput.ini", "document input");
                var source = temp.WriteText("Documents/" + Config + "User.ini", "read-only source");
                var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(source, timestamp);
                File.SetAttributes(source, FileAttributes.ReadOnly | FileAttributes.Hidden);
                var existing = temp.WriteText("Game/UserData/" + Config + "GridEngine.ini", "portable engine");
                File.SetAttributes(existing, FileAttributes.ReadOnly);
                temp.Write("Documents/" + Saves + "profile/CHECKPT", new byte[] { 1, 0, 2, 0xFF });
                temp.Write("Documents/" + Saves + "profile/empty-file", new byte[0]);
                Directory.CreateDirectory(PathIn(Documents(temp), Saves + "empty-folder"));
                Directory.CreateDirectory(PathIn(Documents(temp), Config + "ignored-config-subfolder"));
                var result = new PortablePatchService(Documents(temp)).Apply(win32, true, true);
                Assert.That(result.NoRegistryEnabled, Is.True);
                Assert.That(result.UserDirectoryRedirected, Is.True);
                Assert.That(result.BackupCreated, Is.True);
                Assert.That(result.ExecutableChanged, Is.True);
                Assert.That(result.CopiedConfigurationFiles, Is.EqualTo(2));
                Assert.That(result.SkippedConfigurationFiles, Is.EqualTo(1));
                Assert.That(result.CopiedSaveFiles, Is.EqualTo(2));
                Assert.That(result.SaveDataAlreadyPresent, Is.False);
                Assert.That(result.RecoveryDirectory, Is.Null);
                Assert.That(File.ReadAllText(existing), Is.EqualTo("portable engine"));
                Assert.That(File.GetAttributes(existing) & FileAttributes.ReadOnly, Is.Not.Zero);
                Assert.That(File.ReadAllText(PathIn(UserData(temp), Config + "GridInput.ini")), Is.EqualTo("document input"));
                var copiedSource = PathIn(UserData(temp), Config + "User.ini");
                Assert.That(File.ReadAllBytes(copiedSource), Is.EqualTo(File.ReadAllBytes(source)));
                Assert.That(File.GetLastWriteTimeUtc(copiedSource), Is.EqualTo(timestamp));
                Assert.That(File.GetAttributes(copiedSource) & (FileAttributes.ReadOnly | FileAttributes.Hidden),
                    Is.EqualTo(FileAttributes.ReadOnly | FileAttributes.Hidden));
                Assert.That(File.ReadAllBytes(PathIn(UserData(temp), Saves + "profile/CHECKPT")), Is.EqualTo(new byte[] { 1, 0, 2, 0xFF }));
                Assert.That(Directory.Exists(PathIn(UserData(temp), Saves + "empty-folder")), Is.True);
                Assert.That(Directory.Exists(PathIn(UserData(temp), Config + "ignored-config-subfolder")), Is.False);
                Assert.That(File.ReadAllText(PathIn(Documents(temp), Config + "GridEngine.ini")), Is.EqualTo("document engine"));
                Assert.That(File.GetLastWriteTimeUtc(source), Is.EqualTo(timestamp));
                var backup = File.ReadAllBytes(Path.Combine(win32, GameStatusService.BackupName));
                if (library == "vk09.dll")
                    Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("d3d9.dll\0"), 0, original, 0x540, 9);
                Assert.That(Hash(backup), Is.EqualTo(Hash(original)));
                Assert.That(new GameStatusService().Read(win32).Renderer, Is.EqualTo(renderer));
                Assert.That(new GameSettingsService(Documents(temp)).GetIniPath(win32), Is.EqualTo(existing));
                Assert.That(Directory.GetDirectories(win32, ".tronpatch-*"), Is.Empty);
            }
        }

        [Test]
        public void ExistingSaveProfileIsNotMergedEvenWhenSourceFileIsLocked()
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp);
                var source = temp.WriteText("Documents/" + Saves + "new-profile/CHECKPT", "source");
                var existing = temp.WriteText("Game/UserData/" + Saves + "old-profile/CHECKPT", "existing");
                PortablePatchResult result;
                using (File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    result = new PortablePatchService(Documents(temp)).SetUserDirectory(win32, true);
                Assert.That(result.CopiedSaveFiles, Is.Zero);
                Assert.That(result.SaveDataAlreadyPresent, Is.True);
                Assert.That(File.ReadAllText(existing), Is.EqualTo("existing"));
                Assert.That(Directory.GetFiles(PathIn(UserData(temp), Saves), "*", SearchOption.AllDirectories).Length, Is.EqualTo(1));
                Assert.That(File.ReadAllText(source), Is.EqualTo("source"));
            }
        }

        [Test]
        public void EmptyExistingProfileDirectoryAlsoPreventsProfileMixing()
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp);
                temp.WriteText("Documents/" + Saves + "new-profile/CHECKPT", "source");
                Directory.CreateDirectory(PathIn(UserData(temp), Saves + "existing-empty-profile"));
                var result = new PortablePatchService(Documents(temp)).SetUserDirectory(win32, true);
                Assert.That(result.SaveDataAlreadyPresent, Is.True);
                Assert.That(result.CopiedSaveFiles, Is.Zero);
                Assert.That(Directory.GetFiles(PathIn(UserData(temp), Saves), "*", SearchOption.AllDirectories), Is.Empty);
            }
        }

        [Test]
        public void RepeatedApplyIsIdempotentAndDisableKeepsPortableFilesAndNoRegistry()
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp, "vk09.dll");
                var source = temp.WriteText("Documents/" + Saves + "profile/CHECKPT", "document checkpoint");
                var service = new PortablePatchService(Documents(temp));
                service.Apply(win32, true, true);
                var backupPath = Path.Combine(win32, GameStatusService.BackupName);
                var backupHash = Hash(File.ReadAllBytes(backupPath));
                var exeHash = Hash(File.ReadAllBytes(Exe(win32)));
                var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(Exe(win32), timestamp);
                var repeated = service.Apply(win32, true, true);
                Assert.That(repeated.ExecutableChanged, Is.False);
                Assert.That(repeated.BackupCreated, Is.False);
                Assert.That(repeated.CopiedSaveFiles, Is.Zero);
                Assert.That(Hash(File.ReadAllBytes(Exe(win32))), Is.EqualTo(exeHash));
                Assert.That(File.GetLastWriteTimeUtc(Exe(win32)), Is.EqualTo(timestamp));
                var checkpoint = PathIn(UserData(temp), Saves + "profile/CHECKPT");
                File.WriteAllText(checkpoint, "portable progress");
                var disabled = service.SetUserDirectory(win32, false);
                Assert.That(disabled.NoRegistryEnabled, Is.True);
                Assert.That(disabled.UserDirectoryRedirected, Is.False);
                Assert.That(disabled.CopiedSaveFiles, Is.Zero);
                Assert.That(File.ReadAllText(checkpoint), Is.EqualTo("portable progress"));
                Assert.That(File.ReadAllText(source), Is.EqualTo("document checkpoint"));
                Assert.That(Hash(File.ReadAllBytes(backupPath)), Is.EqualTo(backupHash));
                Assert.That(Hash(Slice(File.ReadAllBytes(Exe(win32)), 0x17B0A50, 432)), Is.EqualTo(CaveHash));
                Assert.That(new GameStatusService().Read(win32).Renderer, Is.EqualTo(RendererState.Vulkan));
                Assert.That(service.SetUserDirectory(win32, false).ExecutableChanged, Is.False);
            }
        }

        [Test]
        public void MissingDocumentsCreatesOnlyEmptyPortableFolders()
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp);
                var result = new PortablePatchService(Documents(temp)).SetUserDirectory(win32, true);
                Assert.That(result.CopiedConfigurationFiles + result.CopiedSaveFiles, Is.Zero);
                Assert.That(Directory.Exists(PathIn(UserData(temp), Config)), Is.True);
                Assert.That(Directory.Exists(PathIn(UserData(temp), Saves)), Is.True);
                Assert.That(Directory.Exists(Documents(temp)), Is.False);
            }
        }

        [Test]
        public void NoRegistryWorksIndependentlyWithoutCreatingUserData()
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp);
                var originalHash = Hash(File.ReadAllBytes(Exe(win32)));
                var service = new PortablePatchService(Documents(temp));
                var enabled = service.SetNoRegistry(win32, true);
                Assert.That(enabled.NoRegistryEnabled, Is.True);
                Assert.That(enabled.UserDirectoryRedirected, Is.False);
                Assert.That(service.SetNoRegistry(win32, false).NoRegistryEnabled, Is.False);
                Assert.That(Hash(File.ReadAllBytes(Exe(win32))), Is.EqualTo(originalHash));
                Assert.That(Directory.Exists(UserData(temp)), Is.False);
            }
        }

        [TestCase("invalid-signature")]
        [TestCase("bad-backup")]
        [TestCase("read-only-exe")]
        [TestCase("profile-file-collision")]
        public void PreflightFailureDoesNotPatchOrCreateBackup(string kind)
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp);
                if (kind == "invalid-signature")
                {
                    using (var file = File.OpenWrite(Exe(win32))) { file.Position = 0x11383E0; file.WriteByte(0x42); }
                }
                if (kind == "bad-backup") temp.WriteText("Game/Binaries/Win32Live/" + GameStatusService.BackupName, "invalid");
                if (kind == "read-only-exe") File.SetAttributes(Exe(win32), FileAttributes.ReadOnly);
                if (kind == "profile-file-collision") temp.WriteText("Game/UserData", "keep file");
                var before = Hash(File.ReadAllBytes(Exe(win32)));
                Assert.That(() => new PortablePatchService(Documents(temp)).Apply(win32, true, true), Throws.Exception);
                Assert.That(Hash(File.ReadAllBytes(Exe(win32))), Is.EqualTo(before));
                if (kind == "bad-backup") Assert.That(File.ReadAllText(Path.Combine(win32, GameStatusService.BackupName)), Is.EqualTo("invalid"));
                else Assert.That(File.Exists(Path.Combine(win32, GameStatusService.BackupName)), Is.False);
                Assert.That(Directory.Exists(UserData(temp)), Is.False);
                if (kind == "profile-file-collision") Assert.That(File.ReadAllText(UserData(temp)), Is.EqualTo("keep file"));
            }
        }

        [TestCase("GridGame.exe")]
        [TestCase("source")]
        public void LockedInputRejectsWithoutCreatingUserData(string name)
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp);
                var source = temp.WriteText("Documents/" + Config + "User.ini", "config");
                var before = Hash(File.ReadAllBytes(Exe(win32)));
                var path = name == "source" ? source : Exe(win32);
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    Assert.Throws<IOException>(() => new PortablePatchService(Documents(temp)).Apply(win32, true, true));
                Assert.That(Hash(File.ReadAllBytes(Exe(win32))), Is.EqualTo(before));
                Assert.That(File.Exists(Path.Combine(win32, GameStatusService.BackupName)), Is.False);
                Assert.That(Directory.Exists(UserData(temp)), Is.False);
            }
        }

        [TestCase("User.ini", false)]
        [TestCase("User.ini", true)]
        [TestCase("GridGame.exe", false)]
        [TestCase("GridGame.exe", true)]
        public void FailureDuringCopyOrExeInstallRollsBackAllChanges(string failureName, bool afterChange)
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp);
                var source = temp.WriteText("Documents/" + Config + "User.ini", "source config");
                File.SetAttributes(source, FileAttributes.ReadOnly);
                temp.WriteText("Documents/" + Saves + "profile/CHECKPT", "source checkpoint");
                var originalHash = Hash(File.ReadAllBytes(Exe(win32)));
                var time = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(Exe(win32), time);
                var service = new PortablePatchService(Documents(temp), new FaultMutation { FailureName = failureName, AfterChange = afterChange });
                Assert.Throws<IOException>(() => service.Apply(win32, true, true));
                Assert.That(Hash(File.ReadAllBytes(Exe(win32))), Is.EqualTo(originalHash));
                Assert.That(File.GetLastWriteTimeUtc(Exe(win32)), Is.EqualTo(time));
                Assert.That(File.Exists(Path.Combine(win32, GameStatusService.BackupName)), Is.False);
                Assert.That(Directory.Exists(UserData(temp)), Is.False);
                Assert.That(File.ReadAllText(source), Is.EqualTo("source config"));
                Assert.That(File.GetAttributes(source) & FileAttributes.ReadOnly, Is.Not.Zero);
                Assert.That(Directory.GetDirectories(win32, ".tronpatch-*"), Is.Empty);
            }
        }

        [Test]
        public void FailedRollbackKeepsOriginalExecutableForRecovery()
        {
            using (var temp = new TemporaryDirectory())
            {
                var win32 = Prepare(temp);
                var before = Hash(File.ReadAllBytes(Exe(win32)));
                var service = new PortablePatchService(Documents(temp), new FaultMutation
                {
                    FailureName = "GridGame.exe", AfterChange = true, FailRollback = true
                });
                var error = Assert.Throws<PatchTransactionException>(() => service.Apply(win32, true, true));
                Assert.That(Directory.Exists(error.RecoveryDirectory), Is.True);
                Assert.That(Directory.GetFiles(error.RecoveryDirectory, "original-*.bin")
                    .Any(path => Hash(File.ReadAllBytes(path)) == before), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(error.RecoveryDirectory, "recovery.txt")), Does.Contain(Exe(win32)));
            }
        }

        [Test]
        public void FlatExecutableLocationRejectsUserDirectoryBeforeWriting()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.Write("Flat/GridGame.exe", PortablePeFixture.Create());
                var original = Hash(File.ReadAllBytes(exe));
                Assert.Throws<ArgumentException>(() => new PortablePatchService(Documents(temp)).SetUserDirectory(Path.GetDirectoryName(exe), true));
                Assert.That(Hash(File.ReadAllBytes(exe)), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(exe)).Length, Is.EqualTo(1));
            }
        }
    }
}
