using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TronEvolutionPatcher.Models;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Services
{
    internal sealed class GameProfileService
    {
        private static readonly string RelativeProfile = Path.Combine("Disney Interactive Studios", "Tron Evolution");
        private readonly string documents;
        private readonly IFileMutation mutation;

        public GameProfileService(string documents) : this(documents, new FileMutation()) { }
        internal GameProfileService(string documents, IFileMutation mutation)
        {
            this.documents = Path.GetFullPath(documents);
            this.mutation = mutation;
        }

        private string Root(string directory, bool portable) => portable
            ? Path.GetFullPath(Path.Combine(directory, "..", "..", "UserData")) : documents;

        public GameProfileStatus Inspect(string directory, bool portable)
        {
            var root = Root(directory, portable);
            var profile = Path.Combine(root, RelativeProfile);
            ProfileDirectories.ValidateChain(root, profile);
            var result = new GameProfileStatus { DirectoryPath = profile };
            if (!Directory.Exists(profile)) return result;
            var engine = Path.Combine(profile, "UnrealEngine3", "GridGame", "Config", "GridEngine.ini");
            var game = Path.Combine(Path.GetDirectoryName(engine), "GridGame.ini");
            var input = Path.Combine(Path.GetDirectoryName(engine), "GridInput.ini");
            foreach (var path in Files(profile))
            {
                result.ContainsFiles = true;
                if (new FileInfo(path).Length == 0) continue;
                if (string.Equals(path, engine, StringComparison.OrdinalIgnoreCase)) result.HasEngineIni = true;
                if (string.Equals(path, game, StringComparison.OrdinalIgnoreCase)) result.HasGameIni = true;
                if (string.Equals(path, input, StringComparison.OrdinalIgnoreCase)) result.HasInputIni = true;
                if (string.Equals(Path.GetFileName(path), "CHECKPT", StringComparison.OrdinalIgnoreCase) &&
                    path.StartsWith(Path.Combine(profile, "SaveData") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    result.HasCheckpoint = true;
            }
            return result;
        }

        private static IEnumerable<string> Files(string profile)
        {
            var pending = new Stack<string>(); pending.Push(profile);
            while (pending.Count > 0)
                foreach (var path in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Ссылка внутри профиля не поддерживается: " + path);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                    else yield return path;
                }
        }

        public void RequireInitialized(string directory, bool portable)
        {
            var target = Inspect(directory, portable);
            if (target.IsInitialized) return;
            var source = Inspect(directory, !portable);
            var message = "Конфигурация профиля не подготовлена: отсутствуют " + MissingConfigurations(target) + "." + Environment.NewLine + target.DirectoryPath;
            if (source.IsInitialized)
                message += Environment.NewLine + "В другом расположении найден профиль: " + source.DirectoryPath + Environment.NewLine +
                    (target.ContainsFiles ? "Целевая папка не пустая. Выберите существующий профиль или сохраните неполный профиль отдельно перед переносом. Профили не смешиваются." :
                    "Нажмите «Копировать профиль», затем повторите применение настроек.");
            else message += Environment.NewLine + "Запустите игру и дождитесь создания пользовательских конфигов, затем нажмите «Обновить». Сохранение прогресса CHECKPT для настройки не требуется. Патчер не создаёт пользовательские INI вместо игры.";
            throw new IOException(message);
        }

        public IList<string> Copy(string directory, bool toPortable)
        {
            var source = Inspect(directory, !toPortable);
            var target = Inspect(directory, toPortable);
            if (!source.IsInitialized) throw new IOException("В исходной папке не хватает пользовательских конфигов: " + MissingConfigurations(source) + ". Сначала запустите игру для их создания: " + source.DirectoryPath);
            if (target.ContainsFiles) throw new IOException("Целевой профиль не пустой. Копирование без смешивания и перезаписи невозможно: " + target.DirectoryPath);
            var migration = new PortableProfileMigration();
            var sourceRoot = Root(directory, !toPortable); var targetRoot = Root(directory, toPortable);
            migration.PrepareBetween(sourceRoot, source.DirectoryPath, targetRoot, target.DirectoryPath);
            var created = new List<string>();
            try
            {
                foreach (var path in migration.Directories.Distinct(StringComparer.OrdinalIgnoreCase))
                    ProfileDirectories.Ensure(targetRoot, path, created);
                var recovery = new FileChangeTransaction(mutation).Execute(target.DirectoryPath, migration.Changes);
                var result = new List<string> { "Профиль скопирован из " + source.DirectoryPath + " в " + target.DirectoryPath +
                    ". Файлов конфигурации: " + migration.ConfigurationFiles + ", сохранений: " + migration.SaveFiles + ". Исходный профиль сохранён. Нажмите «Применить настройки»." };
                if (!string.IsNullOrEmpty(recovery)) result.Add("Папка восстановления: " + recovery);
                return result;
            }
            catch { ProfileDirectories.RemoveNewEmpty(created); throw; }
        }

        public static string MissingConfigurations(GameProfileStatus profile)
        {
            var missing = new List<string>();
            if (!profile.HasEngineIni) missing.Add("GridEngine.ini");
            if (!profile.HasGameIni) missing.Add("GridGame.ini");
            if (!profile.HasInputIni) missing.Add("GridInput.ini");
            return string.Join(", ", missing);
        }
    }
}
