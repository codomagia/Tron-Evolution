namespace TronEvolutionPatcher.Models
{
    public sealed class GameLaunchResult
    {
        public string ExecutablePath { get; internal set; }
        public string WorkingDirectory { get; internal set; }
        public int ProcessId { get; internal set; }
    }
}
