using System;
using System.Collections.Generic;
using System.Linq;
using NovaGM.Services;
using NovaGM.Services.Packs;

namespace NovaGM.Services.Items
{
    public sealed class ItemCatalogEntry
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public Dictionary<string, int> Stats { get; init; } = new();
        public string? Description { get; init; }
        public string IconPath { get; init; } = string.Empty;
    }

    public static class ItemCatalog
    {
        public const string IconDirectory = "Assets/Items";

        public static IReadOnlyList<ItemCatalogEntry> GetItemsForCurrentGenre()
        {
            var data = GenreManager.GetCurrentGenreData();
            return data.Items.Values
                .Select(ToCatalogEntry)
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static ItemCatalogEntry? TryGet(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;
            var data = GenreManager.GetCurrentGenreData();
            if (!data.Items.TryGetValue(itemId, out var def))
            {
                // fallback: try case-insensitive match
                def = data.Items.FirstOrDefault(kv => kv.Key.Equals(itemId, StringComparison.OrdinalIgnoreCase)).Value;
                if (def is null) return null;
            }
            return ToCatalogEntry(def);
        }

        private static ItemCatalogEntry ToCatalogEntry(ItemDef def)
        {
            return new ItemCatalogEntry
            {
                Id = def.Id,
                Name = string.IsNullOrWhiteSpace(def.Name) ? def.Id : def.Name,
                Type = def.Type,
                Stats = def.Stats?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, int>(),
                Description = def.Description,
                IconPath = BuildIconPath(def.Id)
            };
        }

        private static string BuildIconPath(string itemId)
        {
            return System.IO.Path.Combine(IconDirectory, itemId + ".png");
        }
    }
}
