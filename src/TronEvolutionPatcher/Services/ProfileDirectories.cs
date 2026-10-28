using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace TronEvolutionPatcher.Services
{
    internal static class ProfileDirectories
    {
        public static FileAttributes? Attributes(string path)
        {
            try { return File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        public static void ValidateChain(string root, string directory)
        {
            root = Path.GetFullPath(root);
            directory = Path.GetFullPath(directory);
            var boundary = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                !directory.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Путь выходит за пределы выбранной папки профиля: " + directory);
            var current = root;
            var relative = directory.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
            var components = new List<string> { root };
            foreach (var component in relative.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                components.Add(current);
            }
            foreach (var path in components)
            {
                var attributes = Attributes(path);
                if (!attributes.HasValue) return;
                if ((attributes.Value & FileAttributes.ReparsePoint) != 0 || (attributes.Value & FileAttributes.Directory) == 0)
                    throw new IOException("Путь профиля занят файлом или ссылкой: " + path);
            }
        }

        public static void Ensure(string root, string directory, List<string> created)
        {
            ValidateChain(root, directory);
            var missing = new Stack<string>();
            for (var current = directory; !Directory.Exists(current); current = Path.GetDirectoryName(current))
            {
                if (string.IsNullOrEmpty(current)) throw new DirectoryNotFoundException(directory);
                missing.Push(current);
            }
            while (missing.Count > 0)
            {
                var current = missing.Pop();
                ValidateChain(root, current);
                if (!Directory.Exists(current))
                {
                    Directory.CreateDirectory(current);
                    created.Add(current);
                }
            }
        }

        public static void RemoveNewEmpty(List<string> created)
        {
            for (var index = created.Count - 1; index >= 0; index--)
            {
                try { Directory.Delete(created[index], false); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SecurityException) { }
            }
        }
    }
}
