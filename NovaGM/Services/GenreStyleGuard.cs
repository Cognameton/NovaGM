using System;
using System.Collections.Generic;
using System.Linq;

namespace NovaGM.Services
{
    public static class GenreStyleGuard
    {
        private static readonly Dictionary<string, (string[] expect, string[] avoid)> Profiles = new()
        {
            ["sci-fi"] = (new[] { "hull", "airlock", "thruster", "burn", "vacuum", "comms", "orbital", "station", "mag-boots" },
                          new[] { "tavern", "wizard", "kingdom", "dragon", "spell" }),
            ["fantasy"] = (new[] { "tavern", "keep", "cloak", "sigil", "goblin", "bard", "squire" },
                           new[] { "airlock", "plasma", "module", "orbital", "thruster" }),
            ["modern"] = (new[] { "alley", "warehouse", "apartment", "subway", "server", "drone", "neon" },
                          Array.Empty<string>())
        };

        public static bool Violates(string genreContext, string narration, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(narration)) return false;

            var g = genreContext.ToLowerInvariant();
            var key = Profiles.Keys.FirstOrDefault(k => g.Contains(k)) ?? "modern";
            var (_, avoid) = Profiles[key];

            var lower = narration.ToLowerInvariant();
            var avoidHits = avoid.Count(w => lower.Contains(w));
            if (avoidHits >= 2)
            {
                reason = $"Out-of-genre terms for '{key}' detected.";
                return true;
            }
            return false;
        }
    }
}
