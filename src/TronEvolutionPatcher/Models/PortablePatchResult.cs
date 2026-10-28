namespace TronEvolutionPatcher.Models
{
    public sealed class PortablePatchResult
    {
        public bool NoRegistryEnabled { get; internal set; }
        public bool UserDirectoryRedirected { get; internal set; }
        public bool ExecutableChanged { get; internal set; }
        public bool BackupCreated { get; internal set; }
        public int CopiedConfigurationFiles { get; internal set; }
        public int SkippedConfigurationFiles { get; internal set; }
        public int CopiedSaveFiles { get; internal set; }
        public bool SaveDataAlreadyPresent { get; internal set; }
        public string RecoveryDirectory { get; internal set; }
    }
}
