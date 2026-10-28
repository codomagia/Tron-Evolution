using System;
using System.IO;
using System.Security;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Services
{
    public sealed class GameStatusService
    {
        public const string BackupName = "GridGame.exe.bak_d3d9";

        public GameStatus Read(string win32Directory)
        {
            if (string.IsNullOrWhiteSpace(win32Directory))
                throw new ArgumentException("Не задана папка игры.", nameof(win32Directory));

            var executable = Path.Combine(win32Directory, "GridGame.exe");
            var dxvkConfig = Path.Combine(win32Directory, "dxvk.conf");
            var status = new GameStatus
            {
                Renderer = RendererState.NotFound,
                ExeExists = File.Exists(executable),
                BackupExists = File.Exists(Path.Combine(win32Directory, BackupName)),
                VulkanDllExists = File.Exists(Path.Combine(win32Directory, "vk09.dll")),
                DxvkConfigExists = File.Exists(dxvkConfig)
            };
            if (!status.ExeExists) return status;
            status.Renderer = RendererState.Unknown;
            try
            {
                using (var stream = File.OpenRead(executable))
                {
                    status.NoRegistryPatched = PortablePatchInspector.IsNoRegistryPatched(stream);
                    status.UserDirectoryRedirected = PortablePatchInspector.IsUserDirectoryRedirected(stream);
                    var import = new PeImportReader().Read(stream);
                    status.ImportedLibrary = import?.LibraryName;
                    if (status.ImportedLibrary == "d3d9.dll") status.Renderer = RendererState.DirectX9;
                    if (status.ImportedLibrary == "vk09.dll") status.Renderer = RendererState.Vulkan;
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException ||
                error is UnauthorizedAccessException || error is SecurityException)
            {
                status.Error = error.Message;
            }

            // A config read error must not erase successfully detected renderer information.
            if (status.DxvkConfigExists)
            {
                try
                {
                    using (var reader = new StreamReader(dxvkConfig))
                    {
                        foreach (var entry in IniEntries.Read(reader))
                        {
                            if (entry.Key == "dxvk.hud")
                                status.HudEnabled = IsEnabled(entry.Value, true);
                            if (entry.Key == "d3d9.sampleranisotropy")
                                status.AnisotropyEnabled = IsEnabled(entry.Value, false);
                        }
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SecurityException)
                {
                    status.Error = string.IsNullOrEmpty(status.Error) ? error.Message : status.Error + Environment.NewLine + error.Message;
                }
            }
            return status;
        }

        private static bool IsEnabled(string value, bool hud)
        {
            value = value.ToLowerInvariant();
            return value.Length > 0 && value != "0" && value != "false" && value != "off" && (!hud || value != "none");
        }
    }
}
