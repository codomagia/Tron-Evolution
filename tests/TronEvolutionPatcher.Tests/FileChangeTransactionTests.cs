using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class FileChangeTransactionTests
    {
        private sealed class BackupRaceMutation : IFileMutation
        {
            public void Install(string stagedFile, string destination, bool replace)
            {
                File.WriteAllText(destination, "external backup");
                throw new IOException("Destination appeared during installation");
            }

            public void Delete(string path) { File.Delete(path); }
            public void SetAttributes(string path, FileAttributes attributes) { File.SetAttributes(path, attributes); }
        }

        [Test]
        public void ExternalBackupAppearingDuringInstallIsNotDeletedByRollback()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = Path.Combine(temp.Root, "backup");
                var changes = new List<FileChange>
                {
                    new FileChange { Path = path, Contents = new byte[] { 1 }, MustNotExist = true }
                };
                var error = Assert.Throws<PatchTransactionException>(() =>
                    new FileChangeTransaction(new BackupRaceMutation()).Execute(temp.Root, changes));
                Assert.That(File.ReadAllText(path), Is.EqualTo("external backup"));
                Assert.That(Directory.Exists(error.RecoveryDirectory), Is.True);
            }
        }

        [Test]
        public void FileAppearingBeforeCaptureIsNotOverwrittenWhenCreationWasRequired()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.WriteText("backup", "external backup");
                var changes = new List<FileChange>
                {
                    new FileChange { Path = path, Contents = new byte[] { 1 }, MustNotExist = true }
                };
                Assert.Throws<IOException>(() => new FileChangeTransaction(new FileMutation()).Execute(temp.Root, changes));
                Assert.That(File.ReadAllText(path), Is.EqualTo("external backup"));
                Assert.That(Directory.GetDirectories(temp.Root), Is.Empty);
            }
        }

        [Test]
        public void ExternallyChangedExecutableRejectsTransactionBeforeWriting()
        {
            using (var temp = new TemporaryDirectory())
            {
                var path = temp.Write("exe", new byte[] { 1 });
                var changes = new List<FileChange>
                {
                    new FileChange { Path = path, Contents = new byte[] { 2 }, ExpectedOriginal = new byte[] { 0 } }
                };
                Assert.Throws<IOException>(() => new FileChangeTransaction(new FileMutation()).Execute(temp.Root, changes));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1 }));
                Assert.That(Directory.GetDirectories(temp.Root), Is.Empty);
            }
        }
    }
}
