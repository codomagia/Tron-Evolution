using System;
using System.IO;
using System.Text;

namespace TronEvolutionPatcher.Patching
{
    internal static class DxvkPayloadValidator
    {
        public static void Validate(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                // Validate PE32 headers and imports before reading the export directory.
                new PeImportReader().Read(stream);
                stream.Position = 0x3C;
                var pe = reader.ReadUInt32();
                stream.Position = pe + 22L;
                if ((reader.ReadUInt16() & 0x2000) == 0) throw Invalid("Payload не является DLL.");
                var optional = pe + 24L;
                stream.Position = optional + 92;
                if (reader.ReadUInt32() < 1) throw Invalid("У DLL нет каталога экспорта.");
                stream.Position = pe + 20L;
                var optionalSize = reader.ReadUInt16();
                if (optionalSize < 104) throw Invalid("У DLL нет каталога экспорта.");
                stream.Position = optional + 96;
                var exportRva = reader.ReadUInt32();
                var exportSize = reader.ReadUInt32();
                if (exportRva == 0 || exportSize < 40) throw Invalid("У DLL нет таблицы экспорта.");
                var exportOffset = Map(reader, pe, optionalSize, exportRva, 40);
                stream.Position = exportOffset + 20;
                var functionCount = reader.ReadUInt32();
                var nameCount = reader.ReadUInt32();
                var functionsRva = reader.ReadUInt32();
                var namesRva = reader.ReadUInt32();
                var ordinalsRva = reader.ReadUInt32();
                if (nameCount == 0 || nameCount > 65536 || functionCount == 0 || functionCount > 65536)
                    throw Invalid("Некорректная таблица экспорта DLL.");
                var namesOffset = Map(reader, pe, optionalSize, namesRva, nameCount * 4L);
                var ordinalsOffset = Map(reader, pe, optionalSize, ordinalsRva, nameCount * 2L);
                var functionsOffset = Map(reader, pe, optionalSize, functionsRva, functionCount * 4L);

                for (var index = 0; index < nameCount; index++)
                {
                    stream.Position = namesOffset + index * 4L;
                    var nameRva = reader.ReadUInt32();
                    var name = new StringBuilder();
                    for (var character = 0; character < 260; character++)
                    {
                        stream.Position = Map(reader, pe, optionalSize, nameRva + (long)character, 1);
                        var value = reader.ReadByte();
                        if (value == 0) break;
                        if (value > 127 || character == 259) throw Invalid("Некорректное имя экспорта DLL.");
                        name.Append((char)value);
                    }
                    if (name.ToString() != "Direct3DCreate9") continue;
                    stream.Position = ordinalsOffset + index * 2L;
                    var ordinal = reader.ReadUInt16();
                    if (ordinal >= functionCount) throw Invalid("Некорректный ordinal Direct3DCreate9.");
                    stream.Position = functionsOffset + ordinal * 4L;
                    var functionRva = reader.ReadUInt32();
                    if (functionRva == 0) throw Invalid("Нет адреса Direct3DCreate9.");
                    Map(reader, pe, optionalSize, functionRva, 1);
                    return;
                }
                throw Invalid("DLL не экспортирует Direct3DCreate9.");
            }
        }

        private static long Map(BinaryReader reader, uint pe, ushort optionalSize, long rva, long size)
        {
            var stream = reader.BaseStream;
            if (rva <= 0 || rva > uint.MaxValue) throw Invalid("Некорректный RVA экспорта.");
            stream.Position = pe + 24L + 60;
            var headersSize = reader.ReadUInt32();
            if (rva < headersSize && size <= headersSize - rva) return RequireRange(stream, rva, size);
            stream.Position = pe + 6L;
            var sectionCount = reader.ReadUInt16();
            for (var index = 0; index < sectionCount; index++)
            {
                stream.Position = pe + 24L + optionalSize + index * 40L + 12;
                var sectionRva = reader.ReadUInt32();
                var rawSize = reader.ReadUInt32();
                var rawOffset = reader.ReadUInt32();
                var relative = rva - sectionRva;
                if (relative >= 0 && relative + size <= rawSize)
                    return RequireRange(stream, rawOffset + relative, size);
            }
            throw Invalid("Экспорт DLL находится за границами данных секций.");
        }

        private static long RequireRange(Stream stream, long offset, long size)
        {
            if (offset < 0 || size < 0 || offset > stream.Length - size) throw Invalid("Усечённая таблица экспорта DLL.");
            return offset;
        }

        private static InvalidDataException Invalid(string message) { return new InvalidDataException(message); }
    }
}
