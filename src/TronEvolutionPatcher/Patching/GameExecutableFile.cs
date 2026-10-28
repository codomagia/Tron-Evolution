using System;
using System.IO;
using System.Text;

namespace TronEvolutionPatcher.Patching
{
    internal static class GameExecutableFile
    {
        public static byte[] ReadForUpdate(string path)
        {
            if ((File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("EXE должен быть обычным файлом с разрешённой записью: " + path);
            using (var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var copy = new MemoryStream())
            {
                file.CopyTo(copy);
                return copy.ToArray();
            }
        }

        public static RendererImport Inspect(byte[] executable)
        {
            using (var stream = new MemoryStream(executable, false))
            using (var reader = new BinaryReader(stream))
            {
                var import = new PeImportReader().Read(stream) ??
                    throw new InvalidDataException("В таблице импорта EXE не найден DirectX 9 или Vulkan.");
                stream.Position = 0x3C;
                var pe = reader.ReadUInt32();
                stream.Position = pe + 22L;
                if ((reader.ReadUInt16() & 0x2000) != 0)
                    throw new InvalidDataException("Вместо исполняемого файла игры выбрана DLL.");
                return import;
            }
        }

        public static byte[] WithRenderer(byte[] original, RendererImport import, string target)
        {
            var copy = (byte[])original.Clone();
            if (import.LibraryName == target) return copy;
            var replacement = Encoding.ASCII.GetBytes(target + "\0");
            Buffer.BlockCopy(replacement, 0, copy, checked((int)import.NameOffset), replacement.Length);
            return copy;
        }

        public static FileChange PrepareBackup(string directory, byte[] executable, RendererImport import)
        {
            var path = Path.Combine(directory, Services.GameStatusService.BackupName);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return NewBackup(path, executable, import); }
            catch (DirectoryNotFoundException) { return NewBackup(path, executable, import); }
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Резервная копия должна быть обычным файлом.");
            if (Inspect(File.ReadAllBytes(path)).LibraryName != "d3d9.dll")
                throw new InvalidDataException("Существующая резервная копия не содержит импорт DirectX 9.");
            return null;
        }

        private static FileChange NewBackup(string path, byte[] executable, RendererImport import)
        {
            return new FileChange { Path = path, MustNotExist = true, Contents = WithRenderer(executable, import, "d3d9.dll") };
        }
    }
}
