using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Services
{
    public sealed class GameSettingsService
    {
        private static readonly string RelativeIniPath = Path.Combine("Disney Interactive Studios", "Tron Evolution",
            "UnrealEngine3", "GridGame", "Config", "GridEngine.ini");
        private readonly string documentsDirectory;
        private readonly IFileMutation mutation;

        public GameSettingsService() : this(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
        {
        }

        public GameSettingsService(string documentsDirectory) : this(documentsDirectory, new FileMutation())
        {
        }

        internal GameSettingsService(string documentsDirectory, IFileMutation mutation)
        {
            if (string.IsNullOrWhiteSpace(documentsDirectory))
                throw new ArgumentException("Не задан путь к папке документов.", nameof(documentsDirectory));
            this.documentsDirectory = Path.GetFullPath(documentsDirectory);
            this.mutation = mutation;
        }

        // win32Directory must be the resolved directory containing GridGame.exe.
        public string GetIniPath(string win32Directory = null, bool? usePortableProfile = null)
        {
            if (usePortableProfile == true && string.IsNullOrWhiteSpace(win32Directory))
                throw new ArgumentException("Для UserData нужна папка игры.", nameof(win32Directory));
            if (!string.IsNullOrWhiteSpace(win32Directory))
            {
                var gameRoot = Path.GetFullPath(Path.Combine(win32Directory, "..", ".."));
                var portable = Path.Combine(gameRoot, "UserData", RelativeIniPath);
                if (usePortableProfile == true || (usePortableProfile != false && File.Exists(portable)))
                    return portable;

                var executable = Path.Combine(win32Directory, "GridGame.exe");
                if (usePortableProfile != false && File.Exists(executable))
                {
                    using (var stream = File.OpenRead(executable))
                        if (PortablePatchInspector.IsUserDirectoryRedirected(stream))
                            return portable;
                }
            }
            return Path.Combine(documentsDirectory, RelativeIniPath);
        }

        public GameSettings Read(string win32Directory = null, bool? usePortableProfile = null)
        {
            var path = GetIniPath(win32Directory, usePortableProfile);
            if (!File.Exists(path))
                return new GameSettings();
            using (var reader = new StreamReader(path))
                return Parse(reader);
        }

        public GameSettingsWriteResult Apply(GameSettings settings, string win32Directory = null, bool? usePortableProfile = null)
        {
            var normalized = Normalize(settings);
            var path = GetIniPath(win32Directory, usePortableProfile);
            byte[] original = null;
            try { original = ReadIniFile(path); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            if (original == null) throw MissingConfiguration(path);
            var updated = new GameIniDocument(original).Apply(normalized);
            var directory = Path.GetDirectoryName(path);
            var recovery = new FileChangeTransaction(mutation).Execute(directory, new List<FileChange>
            {
                new FileChange { Path = path, Contents = updated, ExpectedOriginal = original, ProtectAfterWrite = true }
            });
            return new GameSettingsWriteResult
            {
                IniPath = path, CreatedFromDefault = false,
                ContentChanged = !original.SequenceEqual(updated), RecoveryDirectory = recovery
            };
        }

        private static byte[] ReadIniFile(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("INI должен быть обычным файлом: " + path);
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var copy = new MemoryStream())
            {
                file.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static GameSettings Normalize(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var normalized = new GameSettings
            {
                ResolutionWidth = settings.ResolutionWidth, ResolutionHeight = settings.ResolutionHeight,
                MotionBlur = settings.MotionBlur, VSync = settings.VSync, FpsLimit = settings.FpsLimit,
                SmoothFrameRate = settings.SmoothFrameRate, Language = settings.Language?.Trim().ToUpperInvariant()
            };
            if (normalized.ResolutionWidth <= 0 || normalized.ResolutionHeight <= 0 || normalized.FpsLimit <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "Разрешение и числовой лимит FPS должны быть положительными.");
            if (normalized.Language == null || normalized.Language.Length != 3 || normalized.Language.Any(value => value < 'A' || value > 'Z'))
                throw new ArgumentException("Язык должен быть трёхбуквенным кодом, например RUS или INT.", nameof(settings));
            return normalized;
        }

        private static FileNotFoundException MissingConfiguration(string path)
        {
            return new FileNotFoundException("Пользовательский GridEngine.ini отсутствует. Запустите игру для создания профиля или скопируйте существующий профиль. DefaultEngine.ini не используется для создания пользовательских настроек.", path);
        }

        public GameSettings Parse(TextReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            var settings = new GameSettings();
            foreach (var entry in IniEntries.Read(reader))
            {
                int number;
                bool flag;
                if (entry.Section == "systemsettings")
                {
                    switch (entry.Key)
                    {
                        case "resx":
                            if (TryPositiveInteger(entry.Value, out number)) settings.ResolutionWidth = number;
                            break;
                        case "resy":
                            if (TryPositiveInteger(entry.Value, out number)) settings.ResolutionHeight = number;
                            break;
                        case "motionblur":
                            if (IniEntries.TryBoolean(entry.Value, out flag)) settings.MotionBlur = flag;
                            break;
                        case "usevsync":
                            if (IniEntries.TryBoolean(entry.Value, out flag)) settings.VSync = flag;
                            break;
                    }
                }
                else if (entry.Section == "engine.engine")
                {
                    switch (entry.Key)
                    {
                        case "language":
                            if (entry.Value.Length > 0) settings.Language = entry.Value.ToUpperInvariant();
                            break;
                        case "bsmoothframerate":
                            if (IniEntries.TryBoolean(entry.Value, out flag)) settings.SmoothFrameRate = flag;
                            break;
                        case "maxsmoothedframerate":
                            double fps;
                            if (double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out fps) &&
                                fps >= 1 && fps <= int.MaxValue)
                                settings.FpsLimit = (int)fps;
                            break;
                    }
                }
            }
            return settings;
        }

        private static bool TryPositiveInteger(string value, out int result)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result > 0;
        }
    }
}
