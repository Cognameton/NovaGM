// NovaGM/Services/State/IStateStore.cs
using System.Collections.Generic;
using NovaGM.Models;

namespace NovaGM.Services.State
{
    /// <summary>
    /// Simple game-state facade used by AgentOrchestrator.
    /// </summary>
    public interface IStateStore
    {
        /// <summary>Returns the in-memory state object.</summary>
        GameState Load();

        /// <summary>Applies incremental changes to the state and persists them.</summary>
        void ApplyChanges(
            string? location,
            IEnumerable<string>? flagsAdd,
            Dictionary<string, string>? npcDelta);

        /// <summary>Returns a compact, human-readable slice of the current state for prompts.</summary>
        string CompactSlice(int maxFacts = 10, int maxNpcs = 8, int maxFlags = 12);

        /// <summary>Adds new facts (deduplicated) and persists them.</summary>
        void AddFacts(IEnumerable<string> facts);

        /// <summary>Loads the inventory for the supplied key (player name).</summary>
        InventoryGrid LoadInventory(string key);

        /// <summary>Persists the inventory for the supplied key.</summary>
        void SaveInventory(string key, InventoryGrid inventory);

        /// <summary>Removes a stored inventory snapshot.</summary>
        void RemoveInventory(string key);
    }
}
