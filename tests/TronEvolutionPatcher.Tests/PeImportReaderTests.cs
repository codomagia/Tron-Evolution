using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class PeImportReaderTests
    {
        [TestCase("d3d9.dll", "d3d9.dll")]
        [TestCase("vk09.dll", "vk09.dll")]
        [TestCase("D3D9.DLL", "d3d9.dll")]
        public void ReadsRendererImportAndItsPhysicalOffset(string library, string expected)
        {
            var bytes = PeFixture.Create(library);
            using (var stream = new MemoryStream(bytes, false))
            {
                var import = new PeImportReader().Read(stream);
                Assert.That(import.LibraryName, Is.EqualTo(expected));
                Assert.That(import.NameOffset, Is.EqualTo(0x240));
                Assert.That(stream.CanRead, Is.True, "The reader must leave the caller's stream open.");
            }
            Assert.That(bytes, Is.EqualTo(PeFixture.Create(library)));
        }

        [Test]
        public void IgnoresRendererStringsOutsideImportDescriptors()
        {
            using (var stream = new MemoryStream(PeFixture.Create("kernel32.dll")))
                Assert.That(new PeImportReader().Read(stream), Is.Null);
        }

        [Test]
        public void ReturnsNoRendererForPeWithoutImports()
        {
            var bytes = PeFixture.Create();
            PeFixture.SetUInt32(bytes, 0x100, 0);
            PeFixture.SetUInt32(bytes, 0x104, 0);
            using (var stream = new MemoryStream(bytes))
                Assert.That(new PeImportReader().Read(stream), Is.Null);
        }

        [TestCase(0, 0u)] // MZ signature
        [TestCase(0x3C, uint.MaxValue)] // PE header outside file
        [TestCase(0x80, 0u)] // PE signature
        [TestCase(0x84, 0x00018664u)] // x64 machine, one section
        [TestCase(0x98, 0x20Bu)] // PE32+
        [TestCase(0x100, uint.MaxValue)] // import RVA outside sections
        [TestCase(0x104, 1u)] // undersized descriptor directory
        [TestCase(0x20C, uint.MaxValue)] // invalid name RVA
        [TestCase(0x18C, uint.MaxValue)] // raw section data outside file
        public void RejectsMalformedHeadersAndAddresses(int offset, uint value)
        {
            var bytes = PeFixture.Create();
            PeFixture.SetUInt32(bytes, offset, value);
            using (var stream = new MemoryStream(bytes))
                Assert.Throws<InvalidDataException>(() => new PeImportReader().Read(stream));
        }

        [TestCase(0)]
        [TestCase(63)]
        [TestCase(150)]
        [TestCase(520)]
        public void RejectsTruncatedFiles(int length)
        {
            var bytes = PeFixture.Create();
            using (var stream = new MemoryStream(bytes, 0, length))
                Assert.Throws<InvalidDataException>(() => new PeImportReader().Read(stream));
        }

        [Test]
        public void RejectsUnterminatedName()
        {
            var bytes = PeFixture.Create();
            for (var index = 0x240; index < bytes.Length; index++) bytes[index] = 65;
            using (var stream = new MemoryStream(bytes))
                Assert.Throws<InvalidDataException>(() => new PeImportReader().Read(stream));
        }

        [Test]
        public void RejectsAmbiguousRendererImports()
        {
            var bytes = PeFixture.Create();
            PeFixture.SetUInt32(bytes, 0x104, 60);
            PeFixture.SetUInt32(bytes, 0x214, 0x1060);
            PeFixture.SetUInt32(bytes, 0x220, 0x1070);
            PeFixture.SetUInt32(bytes, 0x224, 0x1060);
            System.Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("vk09.dll\0"), 0, bytes, 0x270, 9);
            using (var stream = new MemoryStream(bytes))
                Assert.Throws<InvalidDataException>(() => new PeImportReader().Read(stream));
        }

        [Test]
        public void KnownRendererDoesNotHideMissingDescriptorTerminator()
        {
            var bytes = PeFixture.Create();
            PeFixture.SetUInt32(bytes, 0x104, 20);
            using (var stream = new MemoryStream(bytes))
                Assert.Throws<InvalidDataException>(() => new PeImportReader().Read(stream));
        }
    }
}
