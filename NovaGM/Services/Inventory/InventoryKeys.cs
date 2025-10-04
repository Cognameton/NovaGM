using System;

namespace NovaGM.Services.Inventory
{
    public static class InventoryKeys
    {
        public static string ForHubCharacter(string characterName)
            => $"hub:{Normalize(characterName)}";

        public static string ForPlayer(string playerName)
            => $"player:{Normalize(playerName)}";

        private static string Normalize(string value)
            => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
    }
}
