// File: Models/GameState.cs
using System.Collections.Generic;

namespace NovaGM.Models
{
    public sealed class GameState
    {
        /// <summary>Single sentence or short paragraph captured from the very first player input.</summary>
        public string Premise { get; set; } = string.Empty;

        /// <summary>Current location label - mirrors Scene.LocationName for backward compat.</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>Freeform flags like night, raining, low-supplies.</summary>
        public HashSet<string> Flags { get; } = new();

        /// <summary>Legacy flat NPC map (name to state). Kept for backward compat with old saves.</summary>
        public Dictionary<string, string> Npcs { get; } = new();

        /// <summary>Established world truths the narrator must honour.</summary>
        public List<string> Facts { get; } = new();

        /// <summary>Unresolved narrative threads - questions raised, objects noticed but not examined,
        /// people met but not fully understood. Fed back to the Controller on future turns.</summary>
        public List<string> Hooks { get; } = new();

        /// <summary>Player/character identifier to inventory snapshot.</summary>
        public Dictionary<string, InventoryGridSnapshot> Inventories { get; } = new();

        // ── Rich world model ──────────────────────────────────────────────────

        /// <summary>The currently active scene (one location live at a time).</summary>
        public WorldScene Scene { get; set; } = new();

        /// <summary>Archived scenes keyed by location name. Loaded when party returns.</summary>
        public Dictionary<string, WorldScene> ArchivedScenes { get; } = new();

        /// <summary>Turn tracking for the multi-player round system.</summary>
        public TurnState TurnState { get; set; } = new();

        public bool HasPremise => !string.IsNullOrWhiteSpace(Premise);
    }
}
