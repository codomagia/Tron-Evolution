using System.IO;
using NUnit.Framework;
using TronEvolutionPatcher.Patching;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    public class DxvkPayloadValidatorTests
    {
        [TestCase(0x2C8, 1u)] // ordinal outside the function table
        [TestCase(0x2C0, 0u)] // exported name without a function
        [TestCase(0x2C0, uint.MaxValue)] // function outside mapped file data
        public void RejectsInvalidExportTarget(int offset, uint value)
        {
            var bytes = PeFixture.CreatePayload();
            PeFixture.SetUInt32(bytes, offset, value);
            Assert.Throws<InvalidDataException>(() => DxvkPayloadValidator.Validate(bytes));
        }
    }
}
