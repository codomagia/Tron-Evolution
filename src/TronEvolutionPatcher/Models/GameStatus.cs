namespace TronEvolutionPatcher.Models
{
    public enum RendererState
    {
        NotFound,
        Unknown,
        DirectX9,
        Vulkan
    }

    public sealed class GameStatus
    {
        public RendererState Renderer { get; internal set; }
        public bool ExeExists { get; internal set; }
        public bool BackupExists { get; internal set; }
        public bool VulkanDllExists { get; internal set; }
        public bool DxvkConfigExists { get; internal set; }
        public string ImportedLibrary { get; internal set; }
        public bool HudEnabled { get; internal set; }
        public bool AnisotropyEnabled { get; internal set; }
        public bool NoRegistryPatched { get; internal set; }
        public bool UserDirectoryRedirected { get; internal set; }
        public string Error { get; internal set; }
    }
}
