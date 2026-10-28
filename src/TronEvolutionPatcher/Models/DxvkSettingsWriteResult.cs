namespace TronEvolutionPatcher.Models
{
    public sealed class DxvkSettingsWriteResult
    {
        public string ConfigurationPath { get; internal set; }
        public bool ConfigurationExists { get; internal set; }
        public bool ContentChanged { get; internal set; }
        public string RecoveryDirectory { get; internal set; }
    }
}
