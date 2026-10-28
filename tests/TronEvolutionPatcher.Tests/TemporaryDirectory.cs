using System;
using System.IO;

namespace TronEvolutionPatcher.Tests
{
    internal sealed class TemporaryDirectory : IDisposable
    {
        private readonly string parent = Path.GetFullPath(Path.GetTempPath());
        public string Root { get; }

        public TemporaryDirectory()
        {
            Root = Path.Combine(parent, "TronPatcherTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Write(string relative, byte[] bytes)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public string WriteText(string relative, string text)
        {
            return Write(relative, System.Text.Encoding.UTF8.GetBytes(text));
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var boundary = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("TronPatcherTests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Отказ удаления за пределами временной папки теста.");
            if (Directory.Exists(resolved))
            {
                foreach (var path in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
                Directory.Delete(resolved, true);
            }
        }
    }
}
