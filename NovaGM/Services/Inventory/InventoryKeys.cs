using System;

namespace NovaGM.Services.Inventory
{
    public static class InventoryKeys
    {
        public static string ForHubCharacter(string characterName)
            => $"hub:{Normalize(characterName)}";

        public static string ForPlayer(string playerName)
            => $"player:{Normalize(playerName)}";

        /// <summary>
        /// Resolves the persisted-inventory key for an acting player id coming out of
        /// the turn pipeline. LAN players registered with the GameCoordinator get a
        /// player: key; anyone else (hub characters) gets a hub: key.
        /// </summary>
        public static string ForActor(string actorName)
            => Multiplayer.GameCoordinator.Instance.GetPlayerCharacter(actorName) is not null
                ? ForPlayer(actorName)
                : ForHubCharacter(actorName);

        private static string Normalize(string value)
            => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
    }
}
