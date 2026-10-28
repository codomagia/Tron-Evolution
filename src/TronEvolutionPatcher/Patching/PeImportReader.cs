using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TronEvolutionPatcher.Patching
{
    public sealed class RendererImport
    {
        public string LibraryName { get; internal set; }
        public long NameOffset { get; internal set; }
    }

    // Reads PE32 x86 import descriptors; arbitrary DLL strings elsewhere are ignored.
    public sealed class PeImportReader
    {
        private sealed class Section
        {
            public uint Rva;
            public uint RawSize;
            public uint RawOffset;
        }

        public RendererImport Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek || !stream.CanRead)
                throw new ArgumentException("Требуется поток с чтением и позиционированием.", nameof(stream));

            using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                RequireRange(stream, 0, 64);
                stream.Position = 0;
                if (reader.ReadUInt16() != 0x5A4D) throw Invalid("Нет заголовка MZ.");
                stream.Position = 0x3C;
                var peOffset = reader.ReadUInt32();
                RequireRange(stream, peOffset, 24);
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550) throw Invalid("Нет заголовка PE.");
                if (reader.ReadUInt16() != 0x14C) throw Invalid("Ожидается x86 PE32.");
                var sectionCount = reader.ReadUInt16();
                stream.Position = peOffset + 20L;
                var optionalSize = reader.ReadUInt16();
                var optionalOffset = peOffset + 24L;
                if (optionalSize < 96) throw Invalid("Повреждён optional header.");
                RequireRange(stream, optionalOffset, optionalSize);
                stream.Position = optionalOffset;
                if (reader.ReadUInt16() != 0x10B) throw Invalid("Ожидается формат PE32.");
                stream.Position = optionalOffset + 60;
                var headerSize = reader.ReadUInt32();
                stream.Position = optionalOffset + 92;
                var directoryCount = reader.ReadUInt32();
                if (directoryCount < 2) return null;
                if (optionalSize < 112) throw Invalid("Нет места для каталога импорта.");
                stream.Position = optionalOffset + 104;
                var importRva = reader.ReadUInt32();
                var importSize = reader.ReadUInt32();
                if (importRva == 0 && importSize == 0) return null;
                if (importRva == 0 || importSize < 20) throw Invalid("Повреждён каталог импорта.");

                var sections = new List<Section>();
                var sectionOffset = optionalOffset + optionalSize;
                RequireRange(stream, sectionOffset, sectionCount * 40L);
                for (var index = 0; index < sectionCount; index++)
                {
                    stream.Position = sectionOffset + index * 40L + 12;
                    sections.Add(new Section
                    {
                        Rva = reader.ReadUInt32(),
                        RawSize = reader.ReadUInt32(),
                        RawOffset = reader.ReadUInt32()
                    });
                }

                RendererImport rendererImport = null;
                for (long index = 0; (index + 1) * 20 <= importSize; index++)
                {
                    var descriptor = Map(stream, importRva + index * 20, 20, headerSize, sections);
                    stream.Position = descriptor;
                    var lookup = reader.ReadUInt32();
                    var timestamp = reader.ReadUInt32();
                    var forwarder = reader.ReadUInt32();
                    var nameRva = reader.ReadUInt32();
                    var addressTable = reader.ReadUInt32();
                    if ((lookup | timestamp | forwarder | nameRva | addressTable) == 0) return rendererImport;
                    if (nameRva == 0) throw Invalid("Нет имени импортируемой библиотеки.");

                    var nameOffset = Map(stream, nameRva, 1, headerSize, sections);
                    var name = new StringBuilder();
                    var terminated = false;
                    for (var character = 0; character < 260; character++)
                    {
                        var characterOffset = Map(stream, nameRva + (long)character, 1, headerSize, sections);
                        if (characterOffset != nameOffset + character)
                            throw Invalid("Имя библиотеки не является непрерывным диапазоном файла.");
                        stream.Position = characterOffset;
                        var value = reader.ReadByte();
                        if (value == 0) { terminated = true; break; }
                        if (value > 127) throw Invalid("Некорректное имя библиотеки.");
                        name.Append((char)value);
                    }
                    if (!terminated) throw Invalid("Имя библиотеки не завершено нулём.");
                    var library = name.ToString().ToLowerInvariant();
                    if (library == "d3d9.dll" || library == "vk09.dll")
                    {
                        if (rendererImport != null) throw Invalid("Неоднозначный импорт рендера.");
                        rendererImport = new RendererImport { LibraryName = library, NameOffset = nameOffset };
                    }
                }
                throw Invalid("Таблица импорта не завершена нулевым дескриптором.");
            }
        }

        private static long Map(Stream stream, long rva, int size, uint headerSize, List<Section> sections)
        {
            if (rva < 0 || rva > uint.MaxValue) throw Invalid("RVA вне диапазона PE32.");
            if (rva < headerSize && size <= headerSize - rva)
            {
                RequireRange(stream, rva, size);
                return rva;
            }
            foreach (var section in sections)
            {
                var relative = rva - section.Rva;
                if (relative < 0 || relative + size > section.RawSize) continue;
                var offset = section.RawOffset + relative;
                RequireRange(stream, offset, size);
                return offset;
            }
            throw Invalid("RVA не указывает на данные файла.");
        }

        private static void RequireRange(Stream stream, long offset, long size)
        {
            if (offset < 0 || size < 0 || offset > stream.Length - size)
                throw Invalid("Данные PE выходят за границы файла.");
        }

        private static InvalidDataException Invalid(string message)
        {
            return new InvalidDataException(message);
        }
    }
}
