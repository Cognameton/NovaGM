// File: Models/GameState.cs
using System.Collections.Generic;

namespace NovaGM.Models
{
    public sealed class GameState
    {
        /// <summary>Single sentence or short paragraph captured from the very first player input.</summary>
        public string Premise { get; set; } = "";

        /// <summary>Optional current location label (can be filled later by the GM or rules).</summary>
        public string Location { get; set; } = "";

        /// <summary>Freeform flags like "night", "raining", "low-supplies".</summary>
        public HashSet<string> Flags { get; } = new();

        /// <summary>NPC name → short state e.g., "Irena: wary quartermaster".</summary>
        public Dictionary<string, string> Npcs { get; } = new();

        /// <summary>Lightweight “facts” the narrator must honor (loot found, clues, injuries…).</summary>
        public List<string> Facts { get; } = new();

        public bool HasPremise => !string.IsNullOrWhiteSpace(Premise);
    }
}
