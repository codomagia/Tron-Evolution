namespace TronEvolutionPatcher.Models
{
    public sealed class GameProfileStatus
    {
        public string DirectoryPath { get; internal set; }
        public bool HasEngineIni { get; internal set; }
        public bool HasGameIni { get; internal set; }
        public bool HasInputIni { get; internal set; }
        public bool HasCheckpoint { get; internal set; }
        public bool ContainsFiles { get; internal set; }
        public bool IsInitialized => HasEngineIni && HasGameIni && HasInputIni;
    }
}
