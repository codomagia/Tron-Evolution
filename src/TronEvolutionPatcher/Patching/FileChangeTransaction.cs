using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace TronEvolutionPatcher.Patching
{
    public sealed class PatchTransactionException : IOException
    {
        public string RecoveryDirectory { get; }

        internal PatchTransactionException(string recoveryDirectory, Exception error)
            : base("Откат выполнен не полностью. Исходные файлы сохранены в: " + recoveryDirectory, error)
        {
            RecoveryDirectory = recoveryDirectory;
        }
    }

    internal sealed class FileChange
    {
        public string Path { get; set; }
        // Null means delete; an empty array means create an empty file.
        public byte[] Contents { get; set; }
        public byte[] ExpectedOriginal { get; set; }
        public bool MustNotExist { get; set; }
        // Explicit opt-in for settings files: allow clearing Read-Only, protect the result.
        public bool ProtectAfterWrite { get; set; }
        public FileAttributes? AttributesAfterWrite { get; set; }
        public DateTime? LastWriteTimeAfterWriteUtc { get; set; }
    }

    internal interface IFileMutation
    {
        void Install(string stagedFile, string destination, bool replace);
        void Delete(string path);
        void SetAttributes(string path, FileAttributes attributes);
    }

    internal sealed class FileMutation : IFileMutation
    {
        public void Install(string stagedFile, string destination, bool replace)
        {
            if (replace) File.Replace(stagedFile, destination, null);
            else File.Move(stagedFile, destination);
        }

        public void Delete(string path) { File.Delete(path); }
        public void SetAttributes(string path, FileAttributes attributes) { File.SetAttributes(path, attributes); }
    }

    // Per-file atomic installation with rollback for exceptions, not a crash-recovery protocol.
    internal sealed class FileChangeTransaction
    {
        private sealed class Snapshot
        {
            public FileChange Change;
            public byte[] Original;
            public FileAttributes Attributes;
            public DateTime LastWriteTime;
            public string OriginalPath;
            public string PendingPath;
            public bool ContentChanged;
        }

        private readonly IFileMutation mutation;

        public FileChangeTransaction(IFileMutation mutation) { this.mutation = mutation; }

        public string Execute(string directory, IList<FileChange> changes)
        {
            var snapshots = new List<Snapshot>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
            {
                var path = System.IO.Path.GetFullPath(change.Path);
                if (!paths.Add(path)) throw new ArgumentException("Повторяющийся путь операции: " + path);
                change.Path = path;
                var snapshot = Capture(change);
                if (change.MustNotExist && snapshot.Original != null)
                    throw new IOException("Файл уже существует и не будет перезаписан: " + path);
                if (change.ExpectedOriginal != null &&
                    (snapshot.Original == null || !change.ExpectedOriginal.SequenceEqual(snapshot.Original)))
                    throw new IOException("Файл изменился после проверки: " + path);
                if (snapshot.Original == null && change.Contents == null) continue;
                snapshot.ContentChanged = snapshot.Original == null || change.Contents == null ||
                    !snapshot.Original.SequenceEqual(change.Contents);
                if (!snapshot.ContentChanged && DesiredAttributes(snapshot) == snapshot.Attributes &&
                    (!change.LastWriteTimeAfterWriteUtc.HasValue || change.LastWriteTimeAfterWriteUtc.Value == snapshot.LastWriteTime)) continue;
                snapshots.Add(snapshot);
            }
            if (snapshots.Count == 0) return null;

            var recovery = System.IO.Path.Combine(directory, ".tronpatch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(recovery);
            var preserveRecovery = false;
            var attempted = new List<Snapshot>();
            try
            {
                var manifest = new StringBuilder("Recovery snapshots. Missing original means file did not exist.\r\n");
                for (var index = 0; index < snapshots.Count; index++)
                {
                    var snapshot = snapshots[index];
                    snapshot.OriginalPath = System.IO.Path.Combine(recovery, "original-" + index + ".bin");
                    snapshot.PendingPath = System.IO.Path.Combine(recovery, "pending-" + index + ".bin");
                    if (snapshot.Original != null) WriteDurable(snapshot.OriginalPath, snapshot.Original);
                    if (snapshot.Change.Contents != null) WriteDurable(snapshot.PendingPath, snapshot.Change.Contents);
                    manifest.AppendLine(index + "\t" + snapshot.Change.Path + "\t" + snapshot.Attributes + "\t" + snapshot.LastWriteTime.ToString("O"));
                }
                WriteDurable(System.IO.Path.Combine(recovery, "recovery.txt"), Encoding.UTF8.GetBytes(manifest.ToString()));

                foreach (var snapshot in snapshots)
                {
                    VerifyUnchanged(snapshot);
                    // Record before attempting: a failing operation may already have changed the target.
                    attempted.Add(snapshot);
                    if (snapshot.ContentChanged)
                    {
                        if (snapshot.Original != null && (snapshot.Attributes & FileAttributes.ReadOnly) != 0)
                            mutation.SetAttributes(snapshot.Change.Path, snapshot.Attributes & ~FileAttributes.ReadOnly);
                        if (snapshot.Change.Contents == null) mutation.Delete(snapshot.Change.Path);
                        else mutation.Install(snapshot.PendingPath, snapshot.Change.Path, snapshot.Original != null);
                    }
                    if (snapshot.Change.Contents != null)
                    {
                        if (snapshot.Change.LastWriteTimeAfterWriteUtc.HasValue)
                        {
                            var attributes = File.GetAttributes(snapshot.Change.Path);
                            if ((attributes & FileAttributes.ReadOnly) != 0)
                                mutation.SetAttributes(snapshot.Change.Path, attributes & ~FileAttributes.ReadOnly);
                            File.SetLastWriteTimeUtc(snapshot.Change.Path, snapshot.Change.LastWriteTimeAfterWriteUtc.Value);
                        }
                        if (snapshot.Original != null || snapshot.Change.ProtectAfterWrite || snapshot.Change.AttributesAfterWrite.HasValue)
                            mutation.SetAttributes(snapshot.Change.Path, DesiredAttributes(snapshot));
                    }
                }
            }
            catch (Exception failure) when (IsFileError(failure))
            {
                var errors = new List<Exception> { failure };
                for (var index = attempted.Count - 1; index >= 0; index--)
                {
                    try { Restore(attempted[index], recovery, index); }
                    catch (Exception rollbackError) when (IsFileError(rollbackError)) { errors.Add(rollbackError); }
                }
                if (errors.Count > 1)
                {
                    preserveRecovery = true;
                    throw new PatchTransactionException(recovery, new AggregateException(errors));
                }
                throw new IOException("Изменение отменено; исходное состояние файлов восстановлено.", failure);
            }
            finally
            {
                if (!preserveRecovery) TryRemoveRecovery(recovery, directory);
            }
            return Directory.Exists(recovery) ? recovery : null;
        }

        private static Snapshot Capture(FileChange change)
        {
            var snapshot = new Snapshot { Change = change };
            FileAttributes attributes;
            try { attributes = File.GetAttributes(change.Path); }
            catch (FileNotFoundException) { return snapshot; }
            catch (DirectoryNotFoundException) { return snapshot; }
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                (!change.ProtectAfterWrite && (attributes & FileAttributes.ReadOnly) != 0))
                throw new IOException("Путь недоступен для изменения: " + change.Path);
            snapshot.Attributes = attributes;
            snapshot.LastWriteTime = File.GetLastWriteTimeUtc(change.Path);
            var access = (attributes & FileAttributes.ReadOnly) != 0 ? FileAccess.Read : FileAccess.ReadWrite;
            using (var file = File.Open(change.Path, FileMode.Open, access, FileShare.None))
            using (var copy = new MemoryStream())
            {
                file.CopyTo(copy);
                snapshot.Original = copy.ToArray();
            }
            return snapshot;
        }

        private static void VerifyUnchanged(Snapshot snapshot)
        {
            if (snapshot.Original == null)
            {
                if (File.Exists(snapshot.Change.Path) || Directory.Exists(snapshot.Change.Path))
                    throw new IOException("Путь появился после проверки: " + snapshot.Change.Path);
                return;
            }
            var current = Capture(snapshot.Change);
            if (current.Original == null || !snapshot.Original.SequenceEqual(current.Original) || current.Attributes != snapshot.Attributes)
                throw new IOException("Файл изменился после проверки: " + snapshot.Change.Path);
        }

        private void Restore(Snapshot snapshot, string recovery, int index)
        {
            if (Directory.Exists(snapshot.Change.Path))
                throw new IOException("Путь занят сторонней папкой; откат не будет её удалять: " + snapshot.Change.Path);
            var current = File.Exists(snapshot.Change.Path) ? File.ReadAllBytes(snapshot.Change.Path) : null;
            if (snapshot.Original != null && current != null && snapshot.Original.SequenceEqual(current))
            {
                RestoreMetadata(snapshot);
                return;
            }
            if (current != null && (snapshot.Change.Contents == null || !snapshot.Change.Contents.SequenceEqual(current)))
                throw new IOException("Файл изменён сторонней программой; откат не будет его перезаписывать: " + snapshot.Change.Path);
            if (current != null)
            {
                var attributes = File.GetAttributes(snapshot.Change.Path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    mutation.SetAttributes(snapshot.Change.Path, attributes & ~FileAttributes.ReadOnly);
            }
            if (snapshot.Original == null) mutation.Delete(snapshot.Change.Path);
            else
            {
                var stagedRestore = System.IO.Path.Combine(recovery, "restore-" + index + ".bin");
                File.Copy(snapshot.OriginalPath, stagedRestore, true);
                mutation.Install(stagedRestore, snapshot.Change.Path, File.Exists(snapshot.Change.Path));
                RestoreMetadata(snapshot);
            }
        }

        private void RestoreMetadata(Snapshot snapshot)
        {
            if (File.GetLastWriteTimeUtc(snapshot.Change.Path) != snapshot.LastWriteTime)
            {
                var attributes = File.GetAttributes(snapshot.Change.Path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    mutation.SetAttributes(snapshot.Change.Path, attributes & ~FileAttributes.ReadOnly);
                File.SetLastWriteTimeUtc(snapshot.Change.Path, snapshot.LastWriteTime);
            }
            if (File.GetAttributes(snapshot.Change.Path) != snapshot.Attributes)
                mutation.SetAttributes(snapshot.Change.Path, snapshot.Attributes);
        }

        private static FileAttributes DesiredAttributes(Snapshot snapshot)
        {
            var attributes = snapshot.Change.AttributesAfterWrite ?? snapshot.Attributes;
            return snapshot.Change.ProtectAfterWrite
                ? (attributes & ~FileAttributes.Normal) | FileAttributes.ReadOnly
                : attributes;
        }

        private static void WriteDurable(string path, byte[] contents)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(contents, 0, contents.Length);
                file.Flush(true);
            }
        }

        private static bool IsFileError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is SecurityException;
        }

        private static void TryRemoveRecovery(string recovery, string directory)
        {
            // Only this transaction's freshly created directory is removed, never a caller-supplied path.
            var resolved = System.IO.Path.GetFullPath(recovery);
            var expectedParent = System.IO.Path.GetFullPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            if (!string.Equals(System.IO.Path.GetDirectoryName(resolved), expectedParent, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(resolved).StartsWith(".tronpatch-", StringComparison.Ordinal))
                throw new InvalidOperationException("Неожиданный путь временной папки.");
            try { Directory.Delete(resolved, true); }
            catch (Exception error) when (IsFileError(error)) { }
        }
    }
}
