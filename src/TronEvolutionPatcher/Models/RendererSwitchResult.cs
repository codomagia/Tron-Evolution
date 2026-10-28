namespace TronEvolutionPatcher.Models
{
    public sealed class RendererSwitchResult
    {
        public RendererState Renderer { get; internal set; }
        public bool BackupCreated { get; internal set; }
        public bool ExecutableChanged { get; internal set; }
        // Non-null when an otherwise successful operation could not remove its recovery folder.
        public string RecoveryDirectory { get; internal set; }
    }
}
