using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TronEvolutionPatcher.Models;

namespace TronEvolutionPatcher.Services
{
    internal interface IPatcherBackend
    {
        string Resolve(string path);
        string Discover();
        PatcherSnapshot Load(string directory);
        IList<string> SwitchToVulkan(string directory, bool hud, bool anisotropy);
        IList<string> SwitchToDirectX(string directory);
        IList<string> ApplySettings(string directory, GameSettings settings, bool noRegistry, bool userDirectory, bool hud, bool anisotropy);
        IList<string> UpdateDxvk(string directory, bool hud, bool anisotropy);
        GameLaunchResult Launch(string directory);
        IList<string> CopyProfile(string directory, bool toPortable);
    }

    internal sealed class PatcherBackend : IPatcherBackend
    {
        private readonly string applicationDirectory;
        private readonly GameDirectoryService directories = new GameDirectoryService();
        private readonly GameStatusService status = new GameStatusService();
        private readonly GameSettingsService settings;
        private readonly PortablePatchService portable;
        private readonly GameProfileService profiles;

        public PatcherBackend() : this(AppDomain.CurrentDomain.BaseDirectory,
            DocumentsDirectory()) { }

        private static string DocumentsDirectory()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(path) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents") : path;
        }

        internal PatcherBackend(string applicationDirectory, string documentsDirectory)
        {
            this.applicationDirectory = Path.GetFullPath(applicationDirectory);
            settings = new GameSettingsService(documentsDirectory);
            portable = new PortablePatchService(documentsDirectory);
            profiles = new GameProfileService(documentsDirectory);
        }

        public string Resolve(string path) { return directories.Resolve(path); }

        public string Discover()
        {
            var candidates = new List<string>();
            var current = new DirectoryInfo(applicationDirectory);
            for (var level = 0; current != null && level < 3; level++, current = current.Parent)
            {
                candidates.Add(current.FullName);
                candidates.Add(Path.Combine(current.FullName, "Games Portable", "Tron Evolution"));
            }
            return directories.Discover(candidates.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public PatcherSnapshot Load(string directory)
        {
            var snapshot = new PatcherSnapshot { Status = status.Read(directory) };
            try
            {
                snapshot.DocumentsProfile = profiles.Inspect(directory, false);
                snapshot.PortableProfile = profiles.Inspect(directory, true);
                snapshot.IniPath = settings.GetIniPath(directory, snapshot.Status.UserDirectoryRedirected);
                snapshot.IniExists = File.Exists(snapshot.IniPath);
                snapshot.Settings = settings.Read(directory, snapshot.Status.UserDirectoryRedirected);
            }
            catch (Exception error) { snapshot.SettingsError = error.Message; }
            return snapshot;
        }

        public IList<string> SwitchToVulkan(string directory, bool hud, bool anisotropy)
        {
            var result = new RendererSwitchService().SwitchToVulkan(directory,
                Path.Combine(applicationDirectory, "Assets", "vk09.dll"), hud, anisotropy);
            var messages = new List<string> { "Включён Vulkan. Portable-патчи сохранены." };
            if (result.BackupCreated) messages.Add("Создана резервная копия GridGame.exe.bak_d3d9.");
            Recovery(messages, result.RecoveryDirectory);
            return messages;
        }

        public IList<string> SwitchToDirectX(string directory)
        {
            var result = new RendererSwitchService().SwitchToDirectX(directory);
            var messages = new List<string> { "Включён DirectX 9. Portable-патчи сохранены." };
            Recovery(messages, result.RecoveryDirectory);
            return messages;
        }

        public IList<string> ApplySettings(string directory, GameSettings requested, bool noRegistry, bool userDirectory, bool hud, bool anisotropy)
        {
            var completed = new List<string>();
            var step = "Проверка состояния игры";
            try
            {
                var current = status.Read(directory);
                if (current.Renderer != RendererState.DirectX9 && current.Renderer != RendererState.Vulkan)
                    throw new InvalidDataException("Не удалось определить рендер выбранной игры.");
                step = "Проверка профиля до изменения файлов";
                profiles.RequireInitialized(directory, userDirectory);
                var registryChanged = current.NoRegistryPatched != noRegistry;
                var userDirChanged = current.UserDirectoryRedirected != userDirectory;
                if (registryChanged || userDirChanged)
                {
                    step = "Portable-патчи";
                    var result = registryChanged && userDirChanged ? portable.Apply(directory, noRegistry, userDirectory) :
                        registryChanged ? portable.SetNoRegistry(directory, noRegistry) : portable.SetUserDirectory(directory, userDirectory);
                    completed.Add("Portable-параметры применены.");
                    if (result.CopiedConfigurationFiles + result.CopiedSaveFiles > 0)
                        completed.Add("Скопировано конфигов: " + result.CopiedConfigurationFiles + ", файлов сохранений: " + result.CopiedSaveFiles + ".");
                    if (result.SaveDataAlreadyPresent) completed.Add("Существующий профиль UserData сохранён без объединения с документами.");
                    Recovery(completed, result.RecoveryDirectory);
                }
                step = "GridEngine.ini";
                var ini = settings.Apply(requested, directory, userDirectory);
                completed.Add("GridEngine.ini сохранён и защищён от изменений игрой.");
                Recovery(completed, ini.RecoveryDirectory);
                if (current.Renderer == RendererState.Vulkan)
                {
                    step = "Параметры DXVK";
                    completed.AddRange(UpdateDxvk(directory, hud, anisotropy));
                }
                return completed;
            }
            catch (Exception error)
            {
                var prefix = completed.Count == 0 ? "Изменения не завершены." : "Уже выполнено: " + string.Join(" ", completed);
                throw new IOException(prefix + Environment.NewLine + "Не завершено: " + step + ". " + error.Message, error);
            }
        }

        public IList<string> UpdateDxvk(string directory, bool hud, bool anisotropy)
        {
            var result = new DxvkSettingsService().Update(directory, hud, anisotropy);
            var messages = new List<string> { "Параметры DXVK сохранены: HUD " + (hud ? "включён" : "выключен") +
                ", анизотропия " + (anisotropy ? "16x" : "выключена") + "." };
            Recovery(messages, result.RecoveryDirectory);
            return messages;
        }

        public GameLaunchResult Launch(string directory) { return new GameLaunchService().Launch(directory); }

        public IList<string> CopyProfile(string directory, bool toPortable) { return profiles.Copy(directory, toPortable); }

        private static void Recovery(List<string> messages, string directory)
        {
            if (!string.IsNullOrEmpty(directory)) messages.Add("Осталась папка восстановления: " + directory);
        }
    }
}
