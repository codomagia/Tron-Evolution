using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Services
{
    internal sealed class PortableProfileMigration
    {
        public List<FileChange> Changes { get; } = new List<FileChange>();
        public List<string> Directories { get; } = new List<string>();
        public int ConfigurationFiles { get; private set; }
        public int SkippedConfigurationFiles { get; private set; }
        public int SaveFiles { get; private set; }
        public bool SaveDataAlreadyPresent { get; private set; }

        public void Prepare(string documents, string gameRoot)
        {
            var relative = Path.Combine("Disney Interactive Studios", "Tron Evolution");
            var sourceBase = Path.Combine(documents, relative);
            var targetBase = Path.Combine(gameRoot, "UserData", relative);
            PrepareBetween(documents, sourceBase, gameRoot, targetBase);
        }

        internal void PrepareBetween(string documents, string sourceBase, string gameRoot, string targetBase)
        {
            if (Within(sourceBase, targetBase) || Within(targetBase, sourceBase))
                throw new IOException("Исходный и portable-профили не должны находиться друг внутри друга.");
            var sourceConfig = Path.Combine(sourceBase, "UnrealEngine3", "GridGame", "Config");
            var targetConfig = Path.Combine(targetBase, "UnrealEngine3", "GridGame", "Config");
            var sourceSave = Path.Combine(sourceBase, "SaveData");
            var targetSave = Path.Combine(targetBase, "SaveData");
            ProfileDirectories.ValidateChain(gameRoot, targetConfig);
            ProfileDirectories.ValidateChain(gameRoot, targetSave);
            Directories.Add(targetConfig);
            Directories.Add(targetSave);

            ProfileDirectories.ValidateChain(documents, sourceConfig);
            if (Directory.Exists(sourceConfig))
            {
                foreach (var source in Directory.EnumerateFiles(sourceConfig).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var target = Path.Combine(targetConfig, Path.GetFileName(source));
                    var existing = ProfileDirectories.Attributes(target);
                    if (existing.HasValue)
                    {
                        if ((existing.Value & FileAttributes.ReparsePoint) != 0) throw new IOException("Конфиг назначения является ссылкой: " + target);
                        SkippedConfigurationFiles++;
                        continue;
                    }
                    Changes.Add(Copy(source, target));
                    ConfigurationFiles++;
                }
            }
            SaveDataAlreadyPresent = Directory.Exists(targetSave) && Directory.EnumerateFileSystemEntries(targetSave).Any();
            if (SaveDataAlreadyPresent) return;
            ProfileDirectories.ValidateChain(documents, sourceSave);
            if (!Directory.Exists(sourceSave)) return;
            var pending = new Stack<KeyValuePair<string, string>>();
            pending.Push(new KeyValuePair<string, string>(sourceSave, targetSave));
            while (pending.Count > 0)
            {
                var pair = pending.Pop();
                Directories.Add(pair.Value);
                foreach (var source in Directory.EnumerateFileSystemEntries(pair.Key).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var attributes = File.GetAttributes(source);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Ссылка в исходных сохранениях: " + source);
                    var target = Path.Combine(pair.Value, Path.GetFileName(source));
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(new KeyValuePair<string, string>(source, target));
                    else
                    {
                        Changes.Add(Copy(source, target));
                        SaveFiles++;
                    }
                }
            }
        }

        private static bool Within(string parent, string path)
        {
            parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(parent, path, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static FileChange Copy(string source, string target)
        {
            var attributes = File.GetAttributes(source);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Профиль содержит ссылку или неожиданный каталог: " + source);
            var timestamp = File.GetLastWriteTimeUtc(source);
            byte[] contents;
            using (var file = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var copy = new MemoryStream())
            {
                file.CopyTo(copy);
                contents = copy.ToArray();
            }
            var copiedAttributes = attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
                FileAttributes.Archive | FileAttributes.NotContentIndexed);
            if (copiedAttributes == 0) copiedAttributes = FileAttributes.Normal;
            return new FileChange
            {
                Path = target, Contents = contents, MustNotExist = true,
                AttributesAfterWrite = copiedAttributes, LastWriteTimeAfterWriteUtc = timestamp
            };
        }
    }
}
