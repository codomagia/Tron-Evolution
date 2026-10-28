using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class PortableExecutablePatcherTests
    {
        // Computed independently from core.py USERDIR_CAVE_BYTES in the Python source.
        internal const string CaveHash = "1908c4d96bfd332546a569630728563a7d6d0eeb326744ead226bf95b14ec451";

        internal static byte[] Slice(byte[] bytes, int offset, int length)
        {
            var slice = new byte[length];
            Buffer.BlockCopy(bytes, offset, slice, 0, length);
            return slice;
        }

        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        [Test]
        public void NoRegistryChangesOnlySixBytesAndRoundTripsExactly()
        {
            var original = PortablePeFixture.Create("vk09.dll");
            var expected = (byte[])original.Clone();
            Buffer.BlockCopy(new byte[] { 0xB8, 1, 0, 0, 0, 0xC3 }, 0, expected, 0x11383E0, 6);
            var patched = PortableExecutablePatcher.Apply(original, true, null);
            Assert.That(Hash(patched), Is.EqualTo(Hash(expected)));
            Assert.That(Hash(PortableExecutablePatcher.Apply(patched, false, null)), Is.EqualTo(Hash(original)));
            Assert.That(Hash(PortableExecutablePatcher.Apply(patched, true, null)), Is.EqualTo(Hash(patched)));
        }

        [Test]
        public void UserDirectoryWritesExactLegacyPayloadAndRestoresCallsLeavingCave()
        {
            var original = PortablePeFixture.Create();
            var patched = PortableExecutablePatcher.Apply(original, null, true);
            Assert.That(Slice(patched, 0x240, 4), Is.EqualTo(new byte[] { 0, 0x08, 0x7B, 1 }));
            Assert.That(Slice(patched, 0xC249, 6), Is.EqualTo(new byte[] { 0xE8, 2, 0x48, 0x7A, 1, 0x90 }));
            Assert.That(Slice(patched, 0x1139623, 6), Is.EqualTo(new byte[] { 0xE8, 0x28, 0x74, 0x67, 0, 0x90 }));
            Assert.That(Hash(Slice(patched, 0x17B0A50, 432)), Is.EqualTo(CaveHash));
            var expected = (byte[])original.Clone();
            Buffer.BlockCopy(patched, 0x17B0A50, expected, 0x17B0A50, 432);
            var reverted = PortableExecutablePatcher.Apply(patched, null, false);
            Assert.That(Hash(reverted), Is.EqualTo(Hash(expected)));
            Assert.That(Hash(PortableExecutablePatcher.Apply(reverted, null, true)), Is.EqualTo(Hash(patched)));
            Assert.That(Hash(PortableExecutablePatcher.Apply(patched, null, true)), Is.EqualTo(Hash(patched)));
        }

        [TestCase(0x11383E0, true)]
        [TestCase(0xC249, false)]
        [TestCase(0x1139623, false)]
        [TestCase(0x240, false)]
        [TestCase(0x17B0A50, false)]
        public void UnknownSignatureRejectsWithoutChangingInput(int offset, bool registry)
        {
            var bytes = PortablePeFixture.Create();
            bytes[offset] = 0x42;
            var hash = Hash(bytes);
            Assert.Throws<InvalidDataException>(() => PortableExecutablePatcher.Apply(bytes, registry ? (bool?)true : null, registry ? null : (bool?)true));
            Assert.That(Hash(bytes), Is.EqualTo(hash));
        }

        [TestCase(0x158 + 28, 0x500000u)] // unsupported ImageBase
        [TestCase(0x238, 0u)] // wrong section name
        [TestCase(0x238 + 12, 0x1100u)] // wrong text RVA
        [TestCase(0x238 + 16, 0x600u)] // insufficient raw section size
        [TestCase(0x238 + 36, 0x40000020u)] // not executable
        [TestCase(0x158 + 56, 0x1700000u)] // insufficient virtual image size
        public void UnsupportedUserDirectoryLayoutRejects(int offset, uint value)
        {
            var bytes = PortablePeFixture.Create();
            PeFixture.SetUInt32(bytes, offset, value);
            Assert.Throws<InvalidDataException>(() => PortableExecutablePatcher.Apply(bytes, null, true));
        }

        [TestCase(0)]
        [TestCase(0x90)]
        [TestCase(0xCC)]
        public void AcceptsOnlyPaddingOrExactKnownCave(int padding)
        {
            var bytes = PortablePeFixture.Create();
            for (var index = 0; index < 432; index++) bytes[0x17B0A50 + index] = (byte)padding;
            Assert.That(Hash(Slice(PortableExecutablePatcher.Apply(bytes, null, true), 0x17B0A50, 432)), Is.EqualTo(CaveHash));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CorruptActiveCaveRejectsEnableAndDisable(bool enable)
        {
            var patched = PortableExecutablePatcher.Apply(PortablePeFixture.Create(), null, true);
            patched[0x17B0A50] ^= 1;
            Assert.Throws<InvalidDataException>(() => PortableExecutablePatcher.Apply(patched, null, enable));
        }

        [Test]
        public void PartiallyAppliedCallsAreNotTreatedAsOriginal()
        {
            var bytes = PortablePeFixture.Create();
            Buffer.BlockCopy(new byte[] { 0xE8, 2, 0x48, 0x7A, 1, 0x90 }, 0, bytes, 0xC249, 6);
            Assert.Throws<InvalidDataException>(() => PortableExecutablePatcher.Apply(bytes, true, true));
            Assert.That(Slice(bytes, 0x11383E0, 6), Is.EqualTo(new byte[] { 0x81, 0xEC, 0x90, 4, 0, 0 }));
        }

        [Test]
        public void TruncatedExeCannotBeExtendedByPatch()
        {
            var bytes = PortablePeFixture.Create().Take(0x17B0A50 + 100).ToArray();
            Assert.Throws<InvalidDataException>(() => PortableExecutablePatcher.Apply(bytes, null, true));
        }
    }
}
