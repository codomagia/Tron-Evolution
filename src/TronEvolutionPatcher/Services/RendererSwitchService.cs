using System;
using System.Collections.Generic;
using System.IO;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Services
{
    public sealed class RendererSwitchService
    {
        private readonly IFileMutation mutation;

        public RendererSwitchService() : this(new FileMutation()) { }
        internal RendererSwitchService(IFileMutation mutation) { this.mutation = mutation; }

        public RendererSwitchResult SwitchToVulkan(string win32Directory, string payloadPath,
            bool enableHud = true, bool enableAnisotropy = true)
        {
            var directory = ValidateDirectory(win32Directory);
            if (string.IsNullOrWhiteSpace(payloadPath)) throw new ArgumentException("Не задан путь к vk09.dll.", nameof(payloadPath));
            var payload = File.ReadAllBytes(payloadPath);
            DxvkPayloadValidator.Validate(payload);

            var executablePath = Path.Combine(directory, "GridGame.exe");
            var executable = ReadExecutable(executablePath);
            var import = ReadRenderer(executable);
            var changes = new List<FileChange>();
            var backup = GameExecutableFile.PrepareBackup(directory, executable, import);
            var createBackup = backup != null;
            if (backup != null) changes.Add(backup);
            var changed = import.LibraryName != "vk09.dll";
            // Always verify the EXE in the transaction, including an idempotent switch.
            changes.Add(new FileChange
            {
                Path = executablePath, ExpectedOriginal = executable,
                Contents = WithRenderer(executable, import, "vk09.dll")
            });
            changes.Add(new FileChange { Path = Path.Combine(directory, "vk09.dll"), Contents = payload });
            changes.Add(new FileChange { Path = Path.Combine(directory, "dxvk.conf"), Contents = DxvkConfigurationFile.Create(enableHud, enableAnisotropy) });
            var recovery = new FileChangeTransaction(mutation).Execute(directory, changes);
            return new RendererSwitchResult
            {
                Renderer = RendererState.Vulkan, BackupCreated = createBackup,
                ExecutableChanged = changed, RecoveryDirectory = recovery
            };
        }

        public RendererSwitchResult SwitchToDirectX(string win32Directory)
        {
            var directory = ValidateDirectory(win32Directory);
            var executablePath = Path.Combine(directory, "GridGame.exe");
            var executable = ReadExecutable(executablePath);
            var import = ReadRenderer(executable);
            var changes = new List<FileChange>
            {
                new FileChange
                {
                    Path = executablePath, ExpectedOriginal = executable,
                    Contents = WithRenderer(executable, import, "d3d9.dll")
                },
                new FileChange { Path = Path.Combine(directory, "vk09.dll") },
                new FileChange { Path = Path.Combine(directory, "dxvk.conf") },
                new FileChange { Path = Path.Combine(directory, "GridGame_d3d9.log") }
            };
            var recovery = new FileChangeTransaction(mutation).Execute(directory, changes);
            return new RendererSwitchResult
            {
                Renderer = RendererState.DirectX9, ExecutableChanged = import.LibraryName != "d3d9.dll",
                RecoveryDirectory = recovery
            };
        }

        private static string ValidateDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Не задана папка игры.", nameof(directory));
            var fullPath = Path.GetFullPath(directory);
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("Папка игры не найдена: " + fullPath);
            return fullPath;
        }

        private static byte[] ReadExecutable(string path)
        {
            return GameExecutableFile.ReadForUpdate(path);
        }

        private static RendererImport ReadRenderer(byte[] executable)
        {
            return GameExecutableFile.Inspect(executable);
        }

        private static byte[] WithRenderer(byte[] original, RendererImport import, string target)
        {
            return GameExecutableFile.WithRenderer(original, import, target);
        }

    }
}
