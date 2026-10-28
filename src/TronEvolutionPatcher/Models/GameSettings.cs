namespace TronEvolutionPatcher.Models
{
    public sealed class GameSettings
    {
        public int ResolutionWidth { get; set; } = 1920;
        public int ResolutionHeight { get; set; } = 1080;
        public bool MotionBlur { get; set; }
        public bool VSync { get; set; } = true;
        public string Language { get; set; } = "RUS";
        public int FpsLimit { get; set; } = 62;
        public bool SmoothFrameRate { get; set; } = true;
    }
}
