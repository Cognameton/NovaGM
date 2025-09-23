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
            var p = Path.Combine(LlmDir, fileName);
            return File.Exists(p) ? p : null;
        }
    }
}
