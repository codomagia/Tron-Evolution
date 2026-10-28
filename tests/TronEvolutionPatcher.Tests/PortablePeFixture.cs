using System.IO;
using System.Text;

namespace TronEvolutionPatcher.Tests
{
    internal static class PortablePeFixture
    {
        // Artificial PE with the legacy patch's file offsets and virtual addresses; never executed.
        public static byte[] Create(string renderer = "d3d9.dll")
        {
            var bytes = new byte[0x17B0C00];
            using (var stream = new MemoryStream(bytes))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0x5A4D);
                stream.Position = 0x3C;
                writer.Write(0x140);
                stream.Position = 0x140;
                writer.Write(0x4550);
                writer.Write((ushort)0x14C);
                writer.Write((ushort)1);
                stream.Position = 0x154;
                writer.Write((ushort)0xE0);
                writer.Write((ushort)0x102);
                stream.Position = 0x158;
                writer.Write((ushort)0x10B);
                stream.Position = 0x158 + 28;
                writer.Write(0x400000);
                writer.Write(0x1000);
                writer.Write(0x200);
                stream.Position = 0x158 + 56;
                writer.Write(0x17B2000);
                writer.Write(0x400);
                stream.Position = 0x158 + 92;
                writer.Write(16);
                stream.Position = 0x158 + 104;
                writer.Write(0x1100);
                writer.Write(40);
                stream.Position = 0x238;
                writer.Write(Encoding.ASCII.GetBytes(".text\0\0\0"));
                writer.Write(0x17B0641);
                writer.Write(0x1000);
                writer.Write(0x17B0800);
                writer.Write(0x400);
                stream.Position = 0x238 + 36;
                writer.Write(0x60000020);
                stream.Position = 0x500;
                writer.Write(0x1180);
                stream.Position = 0x50C;
                writer.Write(0x1140);
                writer.Write(0x1180);
                stream.Position = 0x540;
                writer.Write(Encoding.ASCII.GetBytes(renderer + "\0"));
                stream.Position = 0x11383E0;
                writer.Write(new byte[] { 0x81, 0xEC, 0x90, 0x04, 0, 0 });
                stream.Position = 0xC249;
                writer.Write(new byte[] { 0xFF, 0x15, 0xA4, 0x28, 0xBB, 0x01 });
                stream.Position = 0x1139623;
                writer.Write(new byte[] { 0xFF, 0x15, 0xA4, 0x28, 0xBB, 0x01 });
            }
            return bytes;
        }
    }
}
