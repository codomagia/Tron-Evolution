using System;
using System.Linq;

namespace TronEvolutionPatcher.Patching
{
    // Ported from TronEvolutionPatcher 1.0.1 core.py (MIT, CodoMagia).
    internal static class PortablePatchDefinition
    {
        public const int RegistryOffset = 0x11383E0;
        public const int VirtualSizeOffset = 0x240;
        public const int CaveOffset = 0x17B0A50;
        public const int SiteAOffset = 0xC249;
        public const int SiteBOffset = 0x1139623;
        public static readonly byte[] OriginalRegistry = { 0x81, 0xEC, 0x90, 0x04, 0, 0 };
        public static readonly byte[] PatchedRegistry = { 0xB8, 0x01, 0, 0, 0, 0xC3 };
        public static readonly byte[] OriginalSize = { 0x41, 0x06, 0x7B, 0x01 };
        public static readonly byte[] PatchedSize = { 0, 0x08, 0x7B, 0x01 };
        public static readonly byte[] OriginalCall = { 0xFF, 0x15, 0xA4, 0x28, 0xBB, 0x01 };
        public static readonly byte[] PatchedCallA = { 0xE8, 0x02, 0x48, 0x7A, 0x01, 0x90 };
        public static readonly byte[] PatchedCallB = { 0xE8, 0x28, 0x74, 0x67, 0, 0x90 };
        public static readonly byte[] Cave = FromHex(
            "565753558b7c24246804010000576a00ff15ec21bb0185c00f849a0000008d3447" +
            "b90300000039fe0f868a00000083ee0266833e5c75ef4975ec89f3be1817bb0189" +
            "dde83d000000c7838a0000004c006f00c7838e0000006700730066c78392000000" +
            "00006a0057ff15e820bb01bebc17bb018d6b66e80a00000066c74312000031c0eb" +
            "38668b066689450083c60283c5026685c074066683f85c75e85066c745fe00006a" +
            "0057ff15e820bb0158668945fe6685c075cec3b8054000805d5b5f5ec214009090" +
            "90905c00550073006500720044006100740061005c004400690073006e00650079" +
            "00200049006e007400650072006100630074006900760065002000530074007500" +
            "640069006f0073005c00540072006f006e002000450076006f006c007500740069" +
            "006f006e005c0055006e007200650061006c0045006e00670069006e0065003300" +
            "5c004700720069006400470061006d0065005c0043006f006e0066006900670000" +
            "005c00530061007600650044006100740061005c00300078003200310035003400" +
            "3400430034003600320031003500340034004300340036005c0053006100760065" +
            "000000");

        private static byte[] FromHex(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2).Select(index => Convert.ToByte(hex.Substring(index * 2, 2), 16)).ToArray();
        }
    }
}
