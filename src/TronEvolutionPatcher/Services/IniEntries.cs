using System;
using System.Collections.Generic;
using System.IO;

namespace TronEvolutionPatcher.Services
{
    internal sealed class IniEntry
    {
        public string Section { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
    }

    internal static class IniEntries
    {
        public static IEnumerable<IniEntry> Read(TextReader reader)
        {
            var section = string.Empty;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }
                var equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;
                var value = line.Substring(equals + 1);
                var comment = value.IndexOfAny(new[] { ';', '#' });
                if (comment >= 0) value = value.Substring(0, comment);
                yield return new IniEntry
                {
                    Section = section,
                    Key = line.Substring(0, equals).Trim().ToLowerInvariant(),
                    Value = value.Trim()
                };
            }
        }

        public static bool TryBoolean(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
                return true;
            if (value == "1" || value == "0")
            {
                result = value == "1";
                return true;
            }
            return false;
        }
    }
}
