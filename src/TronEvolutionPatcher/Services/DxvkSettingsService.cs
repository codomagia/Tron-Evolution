using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Services
{
    public sealed class DxvkSettingsService
    {
        private readonly IFileMutation mutation;

        public DxvkSettingsService() : this(new FileMutation()) { }
        internal DxvkSettingsService(IFileMutation mutation) { this.mutation = mutation; }

        // Updates only dxvk.conf; does not open or deploy the EXE, backup or DXVK DLL.
        public DxvkSettingsWriteResult Update(string win32Directory, bool enableHud, bool enableAnisotropy)
        {
            if (string.IsNullOrWhiteSpace(win32Directory)) throw new ArgumentException("Не задана папка игры.", nameof(win32Directory));
            var directory = Path.GetFullPath(win32Directory);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Папка игры не найдена: " + directory);
            var path = Path.Combine(directory, "dxvk.conf");
            byte[] original = null;
            var attributes = ProfileDirectories.Attributes(path);
            if (attributes.HasValue)
            {
                if ((attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
                    throw new IOException("dxvk.conf должен быть обычным файлом с разрешённой записью.");
                original = File.ReadAllBytes(path);
            }
            var contents = DxvkConfigurationFile.Create(enableHud, enableAnisotropy);
            var recovery = new FileChangeTransaction(mutation).Execute(directory, new List<FileChange>
            {
                new FileChange
                {
                    Path = path, Contents = contents, ExpectedOriginal = original, MustNotExist = original == null
                }
            });
            return new DxvkSettingsWriteResult
            {
                ConfigurationPath = path, ConfigurationExists = contents != null,
                ContentChanged = original == null ? contents != null : contents == null || !original.SequenceEqual(contents),
                RecoveryDirectory = recovery
            };
        }
    }
}
