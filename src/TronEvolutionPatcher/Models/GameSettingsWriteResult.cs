namespace TronEvolutionPatcher.Models
{
    public sealed class GameSettingsWriteResult
    {
        public string IniPath { get; internal set; }
        public bool CreatedFromDefault { get; internal set; }
        public bool ContentChanged { get; internal set; }
        public string RecoveryDirectory { get; internal set; }
    }
}
