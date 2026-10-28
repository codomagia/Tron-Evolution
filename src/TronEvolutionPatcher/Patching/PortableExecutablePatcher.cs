using System;
using System.IO;
using System.Text;

namespace TronEvolutionPatcher.Patching
{
    internal static class PortableExecutablePatcher
    {
        public static byte[] Apply(byte[] original, bool? noRegistry, bool? userDirectory)
        {
            GameExecutableFile.Inspect(original);
            var updated = (byte[])original.Clone();
            if (noRegistry.HasValue)
            {
                RequireKnown(original, PortablePatchDefinition.RegistryOffset,
                    PortablePatchDefinition.OriginalRegistry, PortablePatchDefinition.PatchedRegistry);
                Write(updated, PortablePatchDefinition.RegistryOffset,
                    noRegistry.Value ? PortablePatchDefinition.PatchedRegistry : PortablePatchDefinition.OriginalRegistry);
            }
            if (userDirectory.HasValue)
            {
                ValidateUserDirectoryLayout(original);
                var originalCalls = Matches(original, PortablePatchDefinition.SiteAOffset, PortablePatchDefinition.OriginalCall) &&
                    Matches(original, PortablePatchDefinition.SiteBOffset, PortablePatchDefinition.OriginalCall) &&
                    Matches(original, PortablePatchDefinition.VirtualSizeOffset, PortablePatchDefinition.OriginalSize);
                var patchedCalls = Matches(original, PortablePatchDefinition.SiteAOffset, PortablePatchDefinition.PatchedCallA) &&
                    Matches(original, PortablePatchDefinition.SiteBOffset, PortablePatchDefinition.PatchedCallB) &&
                    Matches(original, PortablePatchDefinition.VirtualSizeOffset, PortablePatchDefinition.PatchedSize);
                var knownCave = Matches(original, PortablePatchDefinition.CaveOffset, PortablePatchDefinition.Cave);
                if ((!originalCalls && !patchedCalls) || (patchedCalls && !knownCave))
                    throw new InvalidDataException("Патч UserData частично изменён или не соответствует поддерживаемому EXE.");
                if (!knownCave && !IsPadding(original))
                    throw new InvalidDataException("Область кода UserData занята неизвестными данными.");
                Write(updated, PortablePatchDefinition.VirtualSizeOffset,
                    userDirectory.Value ? PortablePatchDefinition.PatchedSize : PortablePatchDefinition.OriginalSize);
                Write(updated, PortablePatchDefinition.SiteAOffset,
                    userDirectory.Value ? PortablePatchDefinition.PatchedCallA : PortablePatchDefinition.OriginalCall);
                Write(updated, PortablePatchDefinition.SiteBOffset,
                    userDirectory.Value ? PortablePatchDefinition.PatchedCallB : PortablePatchDefinition.OriginalCall);
                // Like the Python patcher, disabling leaves the now-unreferenced code cave intact.
                if (userDirectory.Value) Write(updated, PortablePatchDefinition.CaveOffset, PortablePatchDefinition.Cave);
            }
            return updated;
        }

        private static void ValidateUserDirectoryLayout(byte[] bytes)
        {
            if (bytes.Length < PortablePatchDefinition.CaveOffset + PortablePatchDefinition.Cave.Length)
                throw new InvalidDataException("EXE усечён: нет места для кода UserData.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 0x3C;
                var pe = reader.ReadUInt32();
                stream.Position = pe + 6L;
                var sectionCount = reader.ReadUInt16();
                stream.Position = pe + 20L;
                var optionalSize = reader.ReadUInt16();
                var optional = pe + 24L;
                stream.Position = optional + 28;
                if (reader.ReadUInt32() != 0x400000) throw new InvalidDataException("Неизвестный ImageBase для UserData.");
                var expectedHeader = PortablePatchDefinition.VirtualSizeOffset - 8;
                var sectionStart = optional + optionalSize;
                if (expectedHeader < sectionStart || (expectedHeader - sectionStart) % 40 != 0 ||
                    (expectedHeader - sectionStart) / 40 >= sectionCount)
                    throw new InvalidDataException("Неизвестное расположение заголовка .text.");
                stream.Position = expectedHeader;
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0') != ".text")
                    throw new InvalidDataException("Секция UserData не является .text.");
                reader.ReadUInt32(); // VirtualSize is checked against exact original/patched signatures.
                var rva = reader.ReadUInt32();
                var rawSize = reader.ReadUInt32();
                var rawOffset = reader.ReadUInt32();
                if (rva != 0x1000 || rawOffset != 0x400 || rawSize < 0x17B0800 || (long)rawOffset + rawSize > bytes.Length)
                    throw new InvalidDataException("Неизвестная структура .text для UserData.");
                stream.Position = expectedHeader + 36;
                if ((reader.ReadUInt32() & 0x60000000) != 0x60000000)
                    throw new InvalidDataException("Секция .text должна разрешать чтение и исполнение.");
                // The absolute addresses and relative calls embedded in the payload depend on this layout.
                stream.Position = optional + 56;
                if (reader.ReadUInt32() < 0x17B1800) throw new InvalidDataException("SizeOfImage не вмещает код UserData.");
                for (var index = 0; index < sectionCount; index++)
                {
                    var header = sectionStart + index * 40L;
                    if (header == expectedHeader) continue;
                    stream.Position = header + 8;
                    var virtualSize = reader.ReadUInt32();
                    var sectionRva = reader.ReadUInt32();
                    var sectionRawSize = reader.ReadUInt32();
                    var sectionRawOffset = reader.ReadUInt32();
                    if (Overlaps(0x1000, 0x17B0800, sectionRva, Math.Max(virtualSize, sectionRawSize)) ||
                        Overlaps(0x400, 0x17B0800, sectionRawOffset, sectionRawSize))
                        throw new InvalidDataException("Секции EXE перекрывают область UserData.");
                }
            }
        }

        private static bool Overlaps(long start, long size, long otherStart, long otherSize)
        {
            return otherSize > 0 && start < otherStart + otherSize && otherStart < start + size;
        }

        private static bool IsPadding(byte[] bytes)
        {
            for (var index = 0; index < PortablePatchDefinition.Cave.Length; index++)
            {
                var value = bytes[PortablePatchDefinition.CaveOffset + index];
                if (value != 0 && value != 0x90 && value != 0xCC) return false;
            }
            return true;
        }

        private static void RequireKnown(byte[] bytes, int offset, byte[] original, byte[] patched)
        {
            if (!Matches(bytes, offset, original) && !Matches(bytes, offset, patched))
                throw new InvalidDataException("Неизвестные байты патча по смещению 0x" + offset.ToString("X") + ".");
        }

        private static bool Matches(byte[] bytes, int offset, byte[] expected)
        {
            if (offset > bytes.Length - expected.Length) return false;
            for (var index = 0; index < expected.Length; index++)
                if (bytes[offset + index] != expected[index]) return false;
            return true;
        }

        private static void Write(byte[] bytes, int offset, byte[] replacement)
        {
            Buffer.BlockCopy(replacement, 0, bytes, offset, replacement.Length);
        }
    }
}
