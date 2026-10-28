using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using TronEvolutionPatcher.Patching;
using TronEvolutionPatcher.Services;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class DxvkSettingsServiceTests
    {
        private sealed class FaultMutation : IFileMutation
        {
            private readonly FileMutation actual = new FileMutation();
            private bool failed;
            public bool AfterChange;
            public bool FailRollback;

            public void Install(string stagedFile, string destination, bool replace)
            {
                if (failed && FailRollback) throw new IOException("Simulated config rollback failure");
                Run(() => actual.Install(stagedFile, destination, replace));
            }

            public void Delete(string path) { Run(() => actual.Delete(path)); }
            public void SetAttributes(string path, FileAttributes attributes) { actual.SetAttributes(path, attributes); }

            private void Run(Action action)
            {
                if (!failed)
                {
                    failed = true;
                    if (AfterChange) action();
                    throw new IOException("Simulated config mutation failure");
                }
                action();
            }
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void UpdateWritesSelectedOptionsAndReplacesOldConfiguration(bool hud, bool anisotropy)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.WriteText("dxvk.conf", "old.option = value\n");
                var result = new DxvkSettingsService().Update(temp.Root, hud, anisotropy);
                Assert.That(result.ConfigurationPath, Is.EqualTo(path));
                Assert.That(result.ConfigurationExists, Is.EqualTo(hud || anisotropy));
                Assert.That(result.ContentChanged, Is.True);
                Assert.That(result.RecoveryDirectory, Is.Null);
                if (hud || anisotropy)
                {
                    var expected = (hud ? "dxvk.hud = fps,devinfo,api\n" : "") +
                        (anisotropy ? "d3d9.samplerAnisotropy = 16\nd3d9.forceSamplerFilter = true\n" : "");
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(Encoding.UTF8.GetBytes(expected)));
                }
                else Assert.That(File.Exists(path), Is.False);
                Assert.That(Directory.GetDirectories(temp.Root), Is.Empty);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RepeatingUpdateDoesNotRewriteConfig(bool enabled)
        {
            using (var temp = new TemporaryDirectory())
            {
                var service = new DxvkSettingsService();
                var first = service.Update(temp.Root, enabled, enabled);
                Assert.That(first.ContentChanged, Is.EqualTo(enabled));
                var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                if (enabled) File.SetLastWriteTimeUtc(first.ConfigurationPath, timestamp);
                var second = service.Update(temp.Root, enabled, enabled);
                Assert.That(second.ContentChanged, Is.False);
                Assert.That(second.ConfigurationExists, Is.EqualTo(enabled));
                if (enabled) Assert.That(File.GetLastWriteTimeUtc(second.ConfigurationPath), Is.EqualTo(timestamp));
                Assert.That(Directory.GetDirectories(temp.Root), Is.Empty);
            }
        }

        [Test]
        public void UpdatingConfigDoesNotOpenOrChangeLockedExeDllAndBackup()
        {
            using (var temp = new TemporaryDirectory())
            {
                var exe = temp.WriteText("GridGame.exe", "unchanged exe");
                var dll = temp.WriteText("vk09.dll", "unchanged dll");
                var backup = temp.WriteText(GameStatusService.BackupName, "unchanged backup");
                using (File.Open(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (File.Open(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (File.Open(backup, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.That(new DxvkSettingsService().Update(temp.Root, true, true).ContentChanged, Is.True);
                Assert.That(File.ReadAllText(exe), Is.EqualTo("unchanged exe"));
                Assert.That(File.ReadAllText(dll), Is.EqualTo("unchanged dll"));
                Assert.That(File.ReadAllText(backup), Is.EqualTo("unchanged backup"));
                Assert.That(Directory.GetFiles(temp.Root).Length, Is.EqualTo(4));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReadOnlyConfigRejectsWritingAndDeletion(bool enabled)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.WriteText("dxvk.conf", "original");
                File.SetAttributes(path, FileAttributes.ReadOnly);
                Assert.Throws<IOException>(() => new DxvkSettingsService().Update(temp.Root, enabled, enabled));
                Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
                Assert.That(File.GetAttributes(path) & FileAttributes.ReadOnly, Is.Not.Zero);
                Assert.That(Directory.GetDirectories(temp.Root), Is.Empty);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LockedConfigRejectsWritingAndDeletion(bool enabled)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.WriteText("dxvk.conf", "original");
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.Throws<IOException>(() => new DxvkSettingsService().Update(temp.Root, enabled, enabled));
                Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
            }
        }

        [Test]
        public void MissingDirectoryIsNotCreatedAndDirectoryCollisionIsNotDeleted()
        {
            using (var temp = new TemporaryDirectory())
            {
                var missing = Path.Combine(temp.Root, "missing");
                Assert.Throws<DirectoryNotFoundException>(() => new DxvkSettingsService().Update(missing, true, true));
                Assert.That(Directory.Exists(missing), Is.False);
                var collision = Path.Combine(temp.Root, "dxvk.conf");
                Directory.CreateDirectory(collision);
                Assert.Throws<IOException>(() => new DxvkSettingsService().Update(temp.Root, false, false));
                Assert.That(Directory.Exists(collision), Is.True);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void FailedWriteOrDeleteRestoresConfigAndTimestamp(bool afterChange, bool delete)
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.WriteText("dxvk.conf", "original config");
                var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, timestamp);
                var service = new DxvkSettingsService(new FaultMutation { AfterChange = afterChange });
                Assert.Throws<IOException>(() => service.Update(temp.Root, !delete, !delete));
                Assert.That(File.ReadAllText(path), Is.EqualTo("original config"));
                Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(timestamp));
                Assert.That(Directory.GetDirectories(temp.Root), Is.Empty);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedCreationDoesNotLeaveNewConfig(bool afterChange)
        {
            using (var temp = new TemporaryDirectory())
            {
                var service = new DxvkSettingsService(new FaultMutation { AfterChange = afterChange });
                Assert.Throws<IOException>(() => service.Update(temp.Root, true, true));
                Assert.That(Directory.GetFileSystemEntries(temp.Root), Is.Empty);
            }
        }

        [Test]
        public void FailedRollbackRetainsOriginalConfiguration()
        {
            using (var temp = new TemporaryDirectory())
            {
                temp.WriteText("dxvk.conf", "original config");
                var service = new DxvkSettingsService(new FaultMutation { AfterChange = true, FailRollback = true });
                var error = Assert.Throws<PatchTransactionException>(() => service.Update(temp.Root, true, true));
                Assert.That(File.ReadAllText(Path.Combine(error.RecoveryDirectory, "original-0.bin")), Is.EqualTo("original config"));
            }
        }
    }
}
