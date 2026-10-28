using System.IO;

namespace TronEvolutionPatcher.Patching
{
    // These signatures describe the Python patcher's supported Win32Live binary.
    // Detection alone does not establish that the entire EXE is safe to modify.
    public static class PortablePatchInspector
    {
        public static bool IsNoRegistryPatched(Stream executable)
        {
            return Matches(executable, PortablePatchDefinition.RegistryOffset, PortablePatchDefinition.PatchedRegistry);
        }

        public static bool IsUserDirectoryRedirected(Stream executable)
        {
            return Matches(executable, PortablePatchDefinition.SiteAOffset, PortablePatchDefinition.PatchedCallA) &&
                Matches(executable, PortablePatchDefinition.SiteBOffset, PortablePatchDefinition.PatchedCallB);
        }

        private static bool Matches(Stream stream, long offset, byte[] signature)
        {
            if (offset > stream.Length - signature.Length)
                return false;

            stream.Position = offset;
            foreach (var value in signature)
                if (stream.ReadByte() != value)
                    return false;
            return true;
        }
    }
}
