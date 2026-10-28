namespace TronEvolutionPatcher.Models
{
    public sealed class PatcherSnapshot
    {
        public GameProfileStatus DocumentsProfile { get; internal set; }
        public GameProfileStatus PortableProfile { get; internal set; }
        public GameStatus Status { get; internal set; }
        public GameSettings Settings { get; internal set; }
        public string IniPath { get; internal set; }
        public bool IniExists { get; internal set; }
        public string SettingsError { get; internal set; }
    }
}
