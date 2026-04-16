using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NovaGM.Services
{
    public static class ModelRegistry
    {
        private static string LlmDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "llm"));

        public static IReadOnlyList<string> ListGguf()
        {
            if (!Directory.Exists(LlmDir)) return Array.Empty<string>();
            var list = Directory.EnumerateFiles(LlmDir, "*.gguf", SearchOption.TopDirectoryOnly)
                                .Select(Path.GetFileName)
                                .Where(n => !string.IsNullOrWhiteSpace(n))
                                .Select(n => n!)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            return list;
        }

        public static string? GetAssigned(string role)
        {
            var m = Config.Current.Models;
            return role switch
            {
                "controller" => m.Controller,
                "narrator"   => m.Narrator,
                "memory"     => m.Memory,
                _            => null
            };
        }

        public static void SetAssigned(string role, string? fileName)
        {
            var m = Config.Current.Models;
            switch (role)
            {
                case "controller": m.Controller = fileName; break;
                case "narrator":   m.Narrator   = fileName; break;
                case "memory":     m.Memory     = fileName; break;
            }
            Config.Save();
        }

        public static string? ResolvePath(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            // Reject any filename that contains directory separators or .. components.
            if (fileName.Contains(Path.DirectorySeparatorChar) ||
                fileName.Contains(Path.AltDirectorySeparatorChar) ||
                fileName.Contains(".."))
                return null;

            var p = Path.GetFullPath(Path.Combine(LlmDir, fileName));

            // Confirm the resolved path is still inside LlmDir.
            var baseDir = LlmDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            if (!p.StartsWith(baseDir, StringComparison.Ordinal))
                return null;

            return File.Exists(p) ? p : null;
        }
    }
}
