// NovaGM/Services/State/IStateStore.cs
using System.Collections.Generic;
using NovaGM.Models;

namespace NovaGM.Services.State
{
    /// <summary>
    /// Game-state facade used by AgentOrchestrator and TurnEngine.
    /// </summary>
    public interface IStateStore
    {
        /// <summary>Returns the in-memory state object.</summary>
        GameState Load();

        /// <summary>Applies incremental changes from a Beat and persists them.</summary>
        void ApplyChanges(
            string? location,
            IEnumerable<string>? flagsAdd,
            Dictionary<string, string>? npcDelta,
            string[]? itemsGive   = null,
            string[]? itemsRemove = null,
            string? transitionTo  = null,
            string? actingPlayerId = null);

        /// <summary>Returns a compact, human-readable slice of the current state for prompts.</summary>
        string CompactSlice(int maxFacts = 10, int maxNpcs = 8, int maxFlags = 12);

        /// <summary>Adds new facts (deduplicated) and persists them.</summary>
        void AddFacts(IEnumerable<string> facts);

        /// <summary>Adds narrative hooks (unresolved threads) and persists them.</summary>
        void AddHooks(IEnumerable<string> hooks);

        /// <summary>Loads the inventory for the supplied key (player name).</summary>
        InventoryGrid LoadInventory(string key);

        /// <summary>Persists the inventory for the supplied key.</summary>
        void SaveInventory(string key, InventoryGrid inventory);

        /// <summary>Removes a stored inventory snapshot.</summary>
        void RemoveInventory(string key);

        /// <summary>Archives the current scene and loads (or generates) the destination scene.</summary>
        void TransitionScene(string destinationName);

        /// <summary>Exposes a pending scene transition destination if one is queued, else null.</summary>
        string? PendingTransition { get; }

        /// <summary>Clears the pending transition (called after player confirms or cancels).</summary>
        void ClearPendingTransition();
    }
}
