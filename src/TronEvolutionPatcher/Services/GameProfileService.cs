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
            foreach (var path in Files(profile))
            {
                result.ContainsFiles = true;
                if (new FileInfo(path).Length == 0) continue;
                if (string.Equals(path, engine, StringComparison.OrdinalIgnoreCase)) result.HasEngineIni = true;
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
            var missing = !target.HasEngineIni && !target.HasCheckpoint ? "GridEngine.ini и сохранение CHECKPT" :
                !target.HasEngineIni ? "GridEngine.ini" : "сохранение CHECKPT";
            var message = "Профиль не подготовлен: отсутствует " + missing + "." + Environment.NewLine + target.DirectoryPath;
            if (source.IsInitialized)
                message += Environment.NewLine + "В другом расположении найден профиль: " + source.DirectoryPath + Environment.NewLine +
                    (target.ContainsFiles ? "Целевая папка не пустая. Выберите существующий профиль или сохраните неполный профиль отдельно перед переносом. Профили не смешиваются." :
                    "Нажмите «Копировать профиль», затем повторите применение настроек.");
            else message += Environment.NewLine + "Запустите игру, создайте профиль и дойдите до первого сохранения. Затем нажмите «Обновить». Патчер не создаёт игровые сохранения или пользовательские INI вместо игры.";
            throw new IOException(message);
        }

        public IList<string> Copy(string directory, bool toPortable)
        {
            var source = Inspect(directory, !toPortable);
            var target = Inspect(directory, toPortable);
            if (!source.IsInitialized) throw new IOException("В исходной папке нет GridEngine.ini и непустого CHECKPT. Сначала создайте профиль в игре: " + source.DirectoryPath);
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
    }
}
