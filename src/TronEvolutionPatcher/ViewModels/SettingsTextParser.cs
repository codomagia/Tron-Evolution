using System;
using System.Globalization;
using System.Text.RegularExpressions;
using TronEvolutionPatcher.Models;

namespace TronEvolutionPatcher.ViewModels
{
    internal static class SettingsTextParser
    {
        public static GameSettings Parse(string resolution, string fpsText, string language, bool blur, bool vsync)
        {
            var match = Regex.Match(resolution ?? "", @"^\s*(\d+)\s*[xх×]\s*(\d+)(?:\s+.*)?$", RegexOptions.IgnoreCase);
            int width, height;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out width) || !int.TryParse(match.Groups[2].Value, out height) || width <= 0 || height <= 0)
                throw new ArgumentException("Введите разрешение в формате 2560x1440.");
            var unlimited = string.Equals(fpsText?.Trim(), "Без лимита", StringComparison.OrdinalIgnoreCase);
            var fps = 200;
            if (!unlimited)
            {
                var fpsMatch = Regex.Match(fpsText ?? "", @"^\s*(\d+)\s*(?:FPS)?\s*$", RegexOptions.IgnoreCase);
                if (!fpsMatch.Success || !int.TryParse(fpsMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out fps))
                    throw new ArgumentException("Введите положительное число FPS или выберите «Без лимита».");
                if (fps == 0) { unlimited = true; fps = 200; }
            }
            language = language?.Trim().ToUpperInvariant();
            if (!Regex.IsMatch(language ?? "", "^[A-Z]{3}$")) throw new ArgumentException("Выберите язык RUS / INT или трёхбуквенный код языка.");
            return new GameSettings
            {
                ResolutionWidth = width, ResolutionHeight = height, FpsLimit = fps, SmoothFrameRate = !unlimited,
                Language = language, MotionBlur = blur, VSync = vsync
            };
        }
    }
}
