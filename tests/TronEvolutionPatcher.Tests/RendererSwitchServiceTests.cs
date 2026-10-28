using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class RendererSwitchServiceTests
    {
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
                    throw new IOException("Simulated rollback failure");
                Run(destination, () => actual.Install(stagedFile, destination, replace));
            }

            public void Delete(string path) { Run(path, () => actual.Delete(path)); }
            public void SetAttributes(string path, FileAttributes attributes) { actual.SetAttributes(path, attributes); }

            private void Run(string path, Action operation)
            {
                if (!failed && Path.GetFileName(path) == FailureName)
                {
                    failed = true;
                    if (AfterChange) operation();
                    throw new IOException("Simulated file mutation failure");
                }
                operation();
            }
        }

        private static string Prepare(TemporaryDirectory temp, string renderer = "d3d9.dll")
        {
            temp.Write("Game/GridGame.exe", PeFixture.Create(renderer));
            return temp.Write("Payload/vk09.dll", PeFixture.CreatePayload());
        }

        private static string Game(TemporaryDirectory temp) { return Path.Combine(temp.Root, "Game"); }

        private static Dictionary<string, byte[]> Snapshot(string directory)
        {
            return Directory.GetFiles(directory).ToDictionary(Path.GetFileName, File.ReadAllBytes);
        }

        private static void AssertSnapshot(string directory, Dictionary<string, byte[]> expected)
        {
            var actual = Snapshot(directory);
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
            foreach (var entry in expected) Assert.That(actual[entry.Key], Is.EqualTo(entry.Value), entry.Key);
            Assert.That(Directory.GetDirectories(directory, ".tronpatch-*"), Is.Empty);
        }

        private static byte[] DirectXVersion(byte[] source)
        {
            var expected = (byte[])source.Clone();
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("d3d9.dll\0"), 0, expected, 0x240, 9);
            return expected;
        }

        [Test]
        public void VulkanCreatesBackupDeploysPayloadAndChangesOnlyImportName()
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                var directory = Game(temp);
                var original = File.ReadAllBytes(Path.Combine(directory, "GridGame.exe"));
                var expected = (byte[])original.Clone();
                Buffer.BlockCopy(Encoding.ASCII.GetBytes("vk09.dll\0"), 0, expected, 0x240, 9);
                var result = new RendererSwitchService().SwitchToVulkan(directory, payload);
                Assert.Multiple(() =>
                {
                    Assert.That(result.Renderer, Is.EqualTo(RendererState.Vulkan));
                    Assert.That(result.BackupCreated, Is.True);
                    Assert.That(result.ExecutableChanged, Is.True);
                    Assert.That(result.RecoveryDirectory, Is.Null);
                    Assert.That(File.ReadAllBytes(Path.Combine(directory, "GridGame.exe")), Is.EqualTo(expected));
                    Assert.That(File.ReadAllBytes(Path.Combine(directory, GameStatusService.BackupName)), Is.EqualTo(original));
                    Assert.That(File.ReadAllBytes(Path.Combine(directory, "vk09.dll")), Is.EqualTo(File.ReadAllBytes(payload)));
                    Assert.That(new GameStatusService().Read(directory).Renderer, Is.EqualTo(RendererState.Vulkan));
                    Assert.That(Directory.GetDirectories(directory), Is.Empty);
                });
            }
        }

        [Test]
        public void DirectXPreservesPortableModificationsAndExistingBackup()
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                var directory = Game(temp);
                var service = new RendererSwitchService();
                service.SwitchToVulkan(directory, payload);
                var backup = File.ReadAllBytes(Path.Combine(directory, GameStatusService.BackupName));
                var path = Path.Combine(directory, "GridGame.exe");
                using (var file = File.OpenWrite(path))
                {
                    file.Position = 0x11383E0;
                    file.Write(new byte[] { 0xB8, 1, 0, 0, 0, 0xC3 }, 0, 6);
                }
                var expected = DirectXVersion(File.ReadAllBytes(path));
                temp.WriteText("Game/GridGame_d3d9.log", "log");
                temp.WriteText("Game/unrelated.txt", "keep");
                var result = service.SwitchToDirectX(directory);
                Assert.That(result.ExecutableChanged, Is.True);
                Assert.That(result.BackupCreated, Is.False);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(expected));
                Assert.That(File.ReadAllBytes(Path.Combine(directory, GameStatusService.BackupName)), Is.EqualTo(backup));
                Assert.That(new GameStatusService().Read(directory).NoRegistryPatched, Is.True);
                Assert.That(Directory.GetFiles(directory).Select(Path.GetFileName), Is.EquivalentTo(
                    new[] { "GridGame.exe", GameStatusService.BackupName, "unrelated.txt" }));
            }
        }

        [Test]
        public void RepeatedSwitchesAreIdempotentAndDoNotRewriteBackupOrExe()
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                var directory = Game(temp);
                var service = new RendererSwitchService();
                service.SwitchToVulkan(directory, payload);
                var before = Snapshot(directory);
                var executable = Path.Combine(directory, "GridGame.exe");
                var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(executable, timestamp);
                var repeated = service.SwitchToVulkan(directory, payload);
                Assert.That(repeated.BackupCreated, Is.False);
                Assert.That(repeated.ExecutableChanged, Is.False);
                Assert.That(File.GetLastWriteTimeUtc(executable), Is.EqualTo(timestamp));
                AssertSnapshot(directory, before);
                service.SwitchToDirectX(directory);
                before = Snapshot(directory);
                Assert.That(service.SwitchToDirectX(directory).ExecutableChanged, Is.False);
                AssertSnapshot(directory, before);
            }
        }

        [Test]
        public void ExistingVulkanWithoutBackupGetsDirectXSnapshot()
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp, "vk09.dll");
                var original = File.ReadAllBytes(Path.Combine(Game(temp), "GridGame.exe"));
                var result = new RendererSwitchService().SwitchToVulkan(Game(temp), payload);
                Assert.That(result.BackupCreated, Is.True);
                Assert.That(result.ExecutableChanged, Is.False);
                Assert.That(File.ReadAllBytes(Path.Combine(Game(temp), GameStatusService.BackupName)), Is.EqualTo(DirectXVersion(original)));
            }
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void VulkanWritesOnlySelectedConfiguration(bool hud, bool anisotropy)
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                temp.WriteText("Game/dxvk.conf", "obsolete=value");
                new RendererSwitchService().SwitchToVulkan(Game(temp), payload, hud, anisotropy);
                var path = Path.Combine(Game(temp), "dxvk.conf");
                Assert.That(File.Exists(path), Is.EqualTo(hud || anisotropy));
                if (File.Exists(path))
                {
                    var expected = (hud ? "dxvk.hud = fps,devinfo,api\n" : "") +
                        (anisotropy ? "d3d9.samplerAnisotropy = 16\nd3d9.forceSamplerFilter = true\n" : "");
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(Encoding.UTF8.GetBytes(expected)));
                }
                var status = new GameStatusService().Read(Game(temp));
                Assert.That(status.HudEnabled, Is.EqualTo(hud));
                Assert.That(status.AnisotropyEnabled, Is.EqualTo(anisotropy));
            }
        }

        [Test]
        public void ValidReadOnlyBackupIsNeverOverwritten()
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                var backup = PeFixture.Create();
                backup[0x350] = 0x42;
                var path = temp.Write("Game/" + GameStatusService.BackupName, backup);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try
                {
                    var result = new RendererSwitchService().SwitchToVulkan(Game(temp), payload);
                    Assert.That(result.BackupCreated, Is.False);
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(backup));
                    Assert.That(File.GetAttributes(path) & FileAttributes.ReadOnly, Is.Not.Zero);
                }
                finally { File.SetAttributes(path, FileAttributes.Normal); }
            }
        }

        [TestCase("corrupt")]
        [TestCase("vulkan")]
        public void InvalidExistingBackupRejectsVulkanBeforeWriting(string kind)
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                temp.Write("Game/" + GameStatusService.BackupName, kind == "corrupt" ? new byte[0] : PeFixture.Create("vk09.dll"));
                var before = Snapshot(Game(temp));
                Assert.Throws<InvalidDataException>(() => new RendererSwitchService().SwitchToVulkan(Game(temp), payload));
                AssertSnapshot(Game(temp), before);
            }
        }

        [TestCase("missing")]
        [TestCase("corrupt")]
        [TestCase("not-dll")]
        [TestCase("x64")]
        [TestCase("no-export")]
        [TestCase("bad-export")]
        public void InvalidPayloadLeavesGameUntouched(string kind)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Prepare(temp);
                var bytes = PeFixture.CreatePayload();
                if (kind == "missing") File.Delete(path);
                else
                {
                    if (kind == "corrupt") bytes = new byte[0];
                    if (kind == "not-dll") bytes[0x97] = 0;
                    if (kind == "x64") PeFixture.SetUInt32(bytes, 0x84, 0x00018664);
                    if (kind == "no-export") bytes[0x2D0] = (byte)'X';
                    if (kind == "bad-export") PeFixture.SetUInt32(bytes, 0x2C4, uint.MaxValue);
                    File.WriteAllBytes(path, bytes);
                }
                var before = Snapshot(Game(temp));
                Assert.That(() => new RendererSwitchService().SwitchToVulkan(Game(temp), path), Throws.Exception);
                AssertSnapshot(Game(temp), before);
            }
        }

        [TestCase("missing")]
        [TestCase("corrupt")]
        [TestCase("no-import")]
        [TestCase("dll")]
        public void InvalidExecutableRejectsBothSwitchesWithoutWriting(string kind)
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                var path = Path.Combine(Game(temp), "GridGame.exe");
                if (kind == "missing") File.Delete(path);
                if (kind == "corrupt") File.WriteAllText(path, "d3d9.dll");
                if (kind == "no-import") File.WriteAllBytes(path, PeFixture.Create("kernel32.dll"));
                if (kind == "dll")
                {
                    var bytes = PeFixture.Create();
                    bytes[0x97] = 0x20;
                    File.WriteAllBytes(path, bytes);
                }
                var before = Snapshot(Game(temp));
                var service = new RendererSwitchService();
                Assert.That(() => service.SwitchToVulkan(Game(temp), payload), Throws.Exception);
                AssertSnapshot(Game(temp), before);
                Assert.That(() => service.SwitchToDirectX(Game(temp)), Throws.Exception);
                AssertSnapshot(Game(temp), before);
            }
        }

        [TestCase("GridGame.exe")]
        [TestCase("vk09.dll")]
        [TestCase("dxvk.conf")]
        public void ReadOnlyTargetRejectsBeforeAnyChange(string name)
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                var path = Path.Combine(Game(temp), name);
                if (!File.Exists(path)) File.WriteAllText(path, "original");
                var before = Snapshot(Game(temp));
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try
                {
                    Assert.That(() => new RendererSwitchService().SwitchToVulkan(Game(temp), payload), Throws.Exception);
                    AssertSnapshot(Game(temp), before);
                }
                finally { File.SetAttributes(path, FileAttributes.Normal); }
            }
        }

        [TestCase("vk09.dll")]
        [TestCase("dxvk.conf")]
        [TestCase(GameStatusService.BackupName)]
        public void DirectoryAtTargetPathRejectsBeforeAnyChange(string name)
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                Directory.CreateDirectory(Path.Combine(Game(temp), name));
                var before = Snapshot(Game(temp));
                Assert.That(() => new RendererSwitchService().SwitchToVulkan(Game(temp), payload), Throws.Exception);
                AssertSnapshot(Game(temp), before);
            }
        }

        [TestCase("GridGame.exe")]
        [TestCase("vk09.dll")]
        [TestCase("dxvk.conf")]
        public void LockedTargetRejectsVulkanBeforeAnyChange(string name)
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                var path = Path.Combine(Game(temp), name);
                if (!File.Exists(path)) File.WriteAllText(path, "original");
                var before = Snapshot(Game(temp));
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.Throws<IOException>(() => new RendererSwitchService().SwitchToVulkan(Game(temp), payload));
                AssertSnapshot(Game(temp), before);
            }
        }

        [Test]
        public void LockedLogPreventsDirectXCleanupBeforeChangingExe()
        {
            using (var temp = new TemporaryDirectory())
            {
                Prepare(temp, "vk09.dll");
                var log = temp.WriteText("Game/GridGame_d3d9.log", "log");
                var before = Snapshot(Game(temp));
                using (File.Open(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.Throws<IOException>(() => new RendererSwitchService().SwitchToDirectX(Game(temp)));
                AssertSnapshot(Game(temp), before);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void FailedVulkanInstallRestoresAllFilesIncludingMissingTargets(bool afterChange, bool existingTargets)
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                if (existingTargets)
                {
                    temp.WriteText("Game/vk09.dll", "old DLL");
                    temp.WriteText("Game/dxvk.conf", "old config");
                }
                var before = Snapshot(Game(temp));
                var exe = Path.Combine(Game(temp), "GridGame.exe");
                var time = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(exe, time);
                var service = new RendererSwitchService(new FaultMutation { FailureName = "vk09.dll", AfterChange = afterChange });
                Assert.Throws<IOException>(() => service.SwitchToVulkan(Game(temp), payload));
                AssertSnapshot(Game(temp), before);
                Assert.That(File.GetLastWriteTimeUtc(exe), Is.EqualTo(time));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedDirectXDeletionRestoresRemovedAssetsAndPatchedExe(bool afterChange)
        {
            using (var temp = new TemporaryDirectory())
            {
                Prepare(temp, "vk09.dll");
                temp.WriteText("Game/vk09.dll", "DLL");
                temp.WriteText("Game/dxvk.conf", "config");
                temp.WriteText("Game/GridGame_d3d9.log", "log");
                var before = Snapshot(Game(temp));
                var service = new RendererSwitchService(new FaultMutation { FailureName = "dxvk.conf", AfterChange = afterChange });
                Assert.Throws<IOException>(() => service.SwitchToDirectX(Game(temp)));
                AssertSnapshot(Game(temp), before);
            }
        }

        [Test]
        public void FailedRollbackRetainsOriginalFilesAndReportsRecoveryDirectory()
        {
            using (var temp = new TemporaryDirectory())
            {
                var payload = Prepare(temp);
                var original = File.ReadAllBytes(Path.Combine(Game(temp), "GridGame.exe"));
                var service = new RendererSwitchService(new FaultMutation { FailureName = "vk09.dll", FailRollback = true });
                var error = Assert.Throws<PatchTransactionException>(() => service.SwitchToVulkan(Game(temp), payload));
                Assert.That(Directory.Exists(error.RecoveryDirectory), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(error.RecoveryDirectory, "recovery.txt")), Does.Contain("GridGame.exe"));
                Assert.That(Directory.GetFiles(error.RecoveryDirectory, "original-*.bin")
                    .Any(path => File.ReadAllBytes(path).SequenceEqual(original)), Is.True);
                Assert.That(error.InnerException, Is.InstanceOf<AggregateException>());
            }
        }
    }
}
