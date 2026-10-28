using System.Text;

namespace TronEvolutionPatcher.Services
{
    internal static class DxvkConfigurationFile
    {
        public static byte[] Create(bool hud, bool anisotropy)
        {
            if (!hud && !anisotropy) return null;
            var text = new StringBuilder();
            if (hud) text.Append("dxvk.hud = fps,devinfo,api\n");
            if (anisotropy) text.Append("d3d9.samplerAnisotropy = 16\nd3d9.forceSamplerFilter = true\n");
            return Encoding.UTF8.GetBytes(text.ToString());
        }
    }
}
