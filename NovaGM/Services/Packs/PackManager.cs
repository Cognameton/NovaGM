using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NovaGM.Services.Packs
{
    public static class PackManager
    {
        private static string PacksDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "packs"));

        public static IReadOnlyList<PackInfo> Discover()
        {
            if (!Directory.Exists(PacksDir)) return Array.Empty<PackInfo>();
            var list = new List<PackInfo>();

            foreach (var dir in Directory.EnumerateDirectories(PacksDir))
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var m = JsonSerializer.Deserialize<PackManifest>(json);
                    if (m == null || string.IsNullOrWhiteSpace(m.Id)) continue;
                    list.Add(new PackInfo { FolderPath = dir, Manifest = m });
                }
                catch { /* skip bad manifests */ }
            }
            return list.OrderBy(p => p.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string? GetActiveId() => Config.Current.ActivePackId;
        public static void SetActiveId(string? id) { Config.Current.ActivePackId = id; Config.Save(); }

        public static PackInfo? GetActivePack() => Discover().FirstOrDefault(p => p.Manifest.Id == Config.Current.ActivePackId);
    }
}
