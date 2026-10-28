using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TronEvolutionPatcher.Models;

namespace TronEvolutionPatcher.Services
{
    // Edits individual lines instead of serializing an INI dictionary, retaining unrelated content.
    internal sealed class GameIniDocument
    {
        private sealed class Line
        {
            public string Text;
            public string Ending;
        }

        private readonly List<Line> lines = new List<Line>();
        private readonly Encoding encoding;
        private readonly byte[] preamble;
        private readonly string newline;

        public GameIniDocument(byte[] bytes)
        {
            encoding = DetectEncoding(bytes);
            var mark = encoding.GetPreamble();
            preamble = mark.Length > 0 && bytes.Take(mark.Length).SequenceEqual(mark) ? mark : new byte[0];
            string text;
            try { text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length); }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("Кодировка INI не распознана. Ожидается UTF-8 или Unicode с BOM; файл не изменён.", error);
            }
            if (text.IndexOf('\0') >= 0) throw new InvalidDataException("INI содержит нулевые символы. Проверьте кодировку файла.");
            for (var position = 0; position < text.Length;)
            {
                var end = text.IndexOfAny(new[] { '\r', '\n' }, position);
                if (end < 0) { lines.Add(new Line { Text = text.Substring(position), Ending = "" }); break; }
                var ending = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? "\r\n" : text[end].ToString();
                lines.Add(new Line { Text = text.Substring(position, end - position), Ending = ending });
                position = end + ending.Length;
            }
            newline = lines.Select(line => line.Ending).FirstOrDefault(ending => ending.Length > 0) ?? "\r\n";
        }

        public byte[] Apply(GameSettings settings)
        {
            UpdateSection("SystemSettings", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ResX", settings.ResolutionWidth.ToString(CultureInfo.InvariantCulture) },
                { "ResY", settings.ResolutionHeight.ToString(CultureInfo.InvariantCulture) },
                { "MotionBlur", settings.MotionBlur ? "True" : "False" },
                { "UseVsync", settings.VSync ? "True" : "False" }
            });
            UpdateSection("Engine.Engine", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Language", settings.Language.Trim().ToUpperInvariant() },
                { "bSmoothFrameRate", settings.SmoothFrameRate ? "TRUE" : "FALSE" },
                { "MaxSmoothedFrameRate", settings.FpsLimit.ToString(CultureInfo.InvariantCulture) }
            });
            var body = encoding.GetBytes(string.Concat(lines.Select(line => line.Text + line.Ending)));
            return preamble.Concat(body).ToArray();
        }

        private void UpdateSection(string sectionName, Dictionary<string, string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var active = false;
            var lastSectionEnd = -1;
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                var trimmed = line.Text.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    if (active) lastSectionEnd = index;
                    active = string.Equals(trimmed.Substring(1, trimmed.Length - 2).Trim(), sectionName, StringComparison.OrdinalIgnoreCase);
                    if (active) lastSectionEnd = lines.Count;
                    continue;
                }
                if (!active || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;
                var equals = line.Text.IndexOf('=');
                if (equals < 0) continue;
                var key = line.Text.Substring(0, equals).Trim();
                string value;
                if (!values.TryGetValue(key, out value)) continue;
                seen.Add(key);
                var comment = line.Text.IndexOfAny(new[] { ';', '#' }, equals + 1);
                var end = comment < 0 ? line.Text.Length : comment;
                var start = equals + 1;
                while (start < end && char.IsWhiteSpace(line.Text[start])) start++;
                while (end > start && char.IsWhiteSpace(line.Text[end - 1])) end--;
                line.Text = line.Text.Substring(0, start) + value + line.Text.Substring(end);
            }
            if (active) lastSectionEnd = lines.Count;
            var missing = values.Where(entry => !seen.Contains(entry.Key)).ToList();
            if (lastSectionEnd < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Text.Length > 0)
                    Insert(lines.Count, new[] { "" });
                Insert(lines.Count, new[] { "[" + sectionName + "]" });
                lastSectionEnd = lines.Count;
            }
            Insert(lastSectionEnd, missing.Select(entry => entry.Key + "=" + entry.Value));
        }

        private void Insert(int index, IEnumerable<string> text)
        {
            var additions = text.Select(value => new Line { Text = value, Ending = newline }).ToList();
            if (additions.Count == 0) return;
            if (index > 0 && lines[index - 1].Ending.Length == 0) lines[index - 1].Ending = newline;
            lines.InsertRange(index, additions);
        }

        private static Encoding DetectEncoding(byte[] bytes)
        {
            if (StartsWith(bytes, new byte[] { 0xFF, 0xFE, 0x00, 0x00 })) return new UTF32Encoding(false, true, true);
            if (StartsWith(bytes, new byte[] { 0x00, 0x00, 0xFE, 0xFF })) return new UTF32Encoding(true, true, true);
            if (StartsWith(bytes, new byte[] { 0xFF, 0xFE })) return new UnicodeEncoding(false, true, true);
            if (StartsWith(bytes, new byte[] { 0xFE, 0xFF })) return new UnicodeEncoding(true, true, true);
            return new UTF8Encoding(StartsWith(bytes, new byte[] { 0xEF, 0xBB, 0xBF }), true);
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            return bytes.Length >= prefix.Length && bytes.Take(prefix.Length).SequenceEqual(prefix);
        }
    }
}
