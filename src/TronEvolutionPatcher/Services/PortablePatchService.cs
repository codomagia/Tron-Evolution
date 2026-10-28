using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Services
{
    public sealed class PortablePatchService
    {
        private readonly string documents;
        private readonly IFileMutation mutation;

        public PortablePatchService() : this(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) { }
        public PortablePatchService(string documentsDirectory) : this(documentsDirectory, new FileMutation()) { }
        internal PortablePatchService(string documentsDirectory, IFileMutation mutation)
        {
            if (string.IsNullOrWhiteSpace(documentsDirectory)) throw new ArgumentException("Не задана папка документов.", nameof(documentsDirectory));
            documents = Path.GetFullPath(documentsDirectory);
            this.mutation = mutation;
        }

        public PortablePatchResult SetNoRegistry(string win32Directory, bool enable)
        {
            return ApplyCore(win32Directory, enable, null);
        }

        public PortablePatchResult SetUserDirectory(string win32Directory, bool enable)
        {
            return ApplyCore(win32Directory, null, enable);
        }

        public PortablePatchResult Apply(string win32Directory, bool noRegistry, bool redirectUserDirectory)
        {
            return ApplyCore(win32Directory, noRegistry, redirectUserDirectory);
        }

        private PortablePatchResult ApplyCore(string win32Directory, bool? noRegistry, bool? userDirectory)
        {
            if (string.IsNullOrWhiteSpace(win32Directory)) throw new ArgumentException("Не задана папка игры.", nameof(win32Directory));
            var directory = Path.GetFullPath(win32Directory);
            var executablePath = Path.Combine(directory, "GridGame.exe");
            var original = GameExecutableFile.ReadForUpdate(executablePath);
            var import = GameExecutableFile.Inspect(original);
            var updated = PortableExecutablePatcher.Apply(original, noRegistry, userDirectory);
            var migration = new PortableProfileMigration();
            string gameRoot = null;
            if (userDirectory.HasValue)
            {
                var info = new DirectoryInfo(directory);
                if (!string.Equals(info.Name, "Win32Live", StringComparison.OrdinalIgnoreCase) ||
                    info.Parent == null || !string.Equals(info.Parent.Name, "Binaries", StringComparison.OrdinalIgnoreCase) || info.Parent.Parent == null)
                    throw new ArgumentException("UserData требует расположения EXE в Binaries\\Win32Live.", nameof(win32Directory));
                gameRoot = info.Parent.Parent.FullName;
                if (userDirectory.Value) migration.Prepare(documents, gameRoot);
            }
            var changes = new List<FileChange>();
            var backup = GameExecutableFile.PrepareBackup(directory, original, import);
            if (backup != null) changes.Add(backup);
            changes.AddRange(migration.Changes);
            // Enable the redirection only after every copied file has been installed successfully.
            changes.Add(new FileChange { Path = executablePath, ExpectedOriginal = original, Contents = updated });
            var created = new List<string>();
            string recovery;
            try
            {
                foreach (var target in migration.Directories.Distinct(StringComparer.OrdinalIgnoreCase))
                    ProfileDirectories.Ensure(gameRoot, target, created);
                recovery = new FileChangeTransaction(mutation).Execute(directory, changes);
            }
            catch
            {
                ProfileDirectories.RemoveNewEmpty(created);
                throw;
            }
            using (var stream = new MemoryStream(updated, false))
                return new PortablePatchResult
                {
                    NoRegistryEnabled = PortablePatchInspector.IsNoRegistryPatched(stream),
                    UserDirectoryRedirected = PortablePatchInspector.IsUserDirectoryRedirected(stream),
                    ExecutableChanged = !original.SequenceEqual(updated), BackupCreated = backup != null,
                    CopiedConfigurationFiles = migration.ConfigurationFiles, SkippedConfigurationFiles = migration.SkippedConfigurationFiles,
                    CopiedSaveFiles = migration.SaveFiles, SaveDataAlreadyPresent = migration.SaveDataAlreadyPresent,
                    RecoveryDirectory = recovery
                };
        }
    }
}
