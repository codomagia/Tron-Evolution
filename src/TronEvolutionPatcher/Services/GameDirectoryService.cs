using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace TronEvolutionPatcher.Services
{
    public sealed class GameDirectoryService
    {
        public string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                    return string.Equals(Path.GetFileName(fullPath), "GridGame.exe", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetDirectoryName(fullPath) : null;

                if (File.Exists(Path.Combine(fullPath, "GridGame.exe")))
                    return fullPath;

                var binaries = Path.Combine(fullPath, "Binaries", "Win32Live");
                return File.Exists(Path.Combine(binaries, "GridGame.exe")) ? binaries : null;
            }
            catch (Exception error) when (error is ArgumentException || error is IOException ||
                error is NotSupportedException || error is UnauthorizedAccessException || error is SecurityException)
            {
                return null;
            }
        }

        // The caller controls search roots; no full-drive scans or machine-specific drive letters.
        public string Discover(IEnumerable<string> candidates)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            foreach (var candidate in candidates)
            {
                var resolved = Resolve(candidate);
                if (resolved != null)
                    return resolved;
            }
            return null;
        }
    }
}
