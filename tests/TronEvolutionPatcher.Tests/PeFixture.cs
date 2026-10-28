using System.IO;
using System.Text;

namespace TronEvolutionPatcher.Tests
{
    internal static class PeFixture
    {
        // Minimal PE32 headers with one .idata section, a descriptor and its DLL name.
        // It is a parser fixture, not an executable game binary.
        public static byte[] Create(string library = "d3d9.dll")
        {
            var bytes = new byte[1024];
            using (var stream = new MemoryStream(bytes))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write((ushort)0x5A4D);
                stream.Position = 0x3C;
                writer.Write(0x80);
                stream.Position = 0x80;
                writer.Write(0x4550);
                writer.Write((ushort)0x14C);
                writer.Write((ushort)1);
                stream.Position = 0x94;
                writer.Write((ushort)0xE0);
                stream.Position = 0x98;
                writer.Write((ushort)0x10B);
                stream.Position = 0x98 + 60;
                writer.Write(0x200);
                stream.Position = 0x98 + 92;
                writer.Write(16);
                stream.Position = 0x98 + 104;
                writer.Write(0x1000);
                writer.Write(40);
                stream.Position = 0x178;
                writer.Write(Encoding.ASCII.GetBytes(".idata\0\0"));
                writer.Write(0x200);
                writer.Write(0x1000);
                writer.Write(0x200);
                writer.Write(0x200);
                stream.Position = 0x200;
                writer.Write(0x1060);
                stream.Position = 0x20C;
                writer.Write(0x1040);
                writer.Write(0x1060);
                stream.Position = 0x240;
                writer.Write(Encoding.ASCII.GetBytes(library + "\0"));
                // An unrelated DLL string must never determine the renderer.
                stream.Position = 0x300;
                writer.Write(Encoding.ASCII.GetBytes("vk09.dll\0d3d9.dll\0"));
            }
            return bytes;
        }

        public static void SetUInt32(byte[] bytes, int offset, uint value)
        {
            using (var stream = new MemoryStream(bytes))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = offset;
                writer.Write(value);
            }
        }

        public static byte[] CreatePayload()
        {
            var bytes = Create("kernel32.dll");
            using (var stream = new MemoryStream(bytes))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 0x96;
                writer.Write((ushort)0x2000);
                stream.Position = 0x98 + 96;
                writer.Write(0x1080); // export directory
                writer.Write(0x60);
                stream.Position = 0x280 + 20;
                writer.Write(1); // functions
                writer.Write(1); // names
                writer.Write(0x10C0); // function RVA table
                writer.Write(0x10C4); // name RVA table
                writer.Write(0x10C8); // ordinal table
                stream.Position = 0x2C0;
                writer.Write(0x1180); // function address outside export range
                writer.Write(0x10D0); // function name
                writer.Write((ushort)0);
                stream.Position = 0x2D0;
                writer.Write(Encoding.ASCII.GetBytes("Direct3DCreate9\0"));
                stream.Position = 0x380;
                writer.Write((byte)0xC3); // fixture only; never executed
            }
            return bytes;
        }
    }
}
