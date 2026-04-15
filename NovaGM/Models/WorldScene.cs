// NovaGM/Models/WorldScene.cs
using System.Collections.Generic;

namespace NovaGM.Models
{
    // ── NPC models ────────────────────────────────────────────────────────────

    /// <summary>
    /// Two-tier NPC model.
    /// Tier "ambient": background characters — fills the scene, no persistence required.
    /// Tier "narrative": story-locked characters — persists with full state across scenes/sessions.
    /// </summary>
    public sealed class SceneNpc
    {
        /// <summary>Unique key within this scene (e.g. "innkeeper_maren").</summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>Appearance, mannerisms, what makes them visually distinct.</summary>
        public string Description { get; set; } = "";

        /// <summary>"ambient" or "narrative".</summary>
        public string Tier { get; set; } = "ambient";

        /// <summary>
        /// Overall disposition toward the party.
        /// Values: friendly | neutral | suspicious | hostile | fearful | allied | dismissive | wary
        /// </summary>
        public string Disposition { get; set; } = "neutral";

        // ── Narrative tier fields ─────────────────────────────────────────────

        /// <summary>Functional role: quest_giver | antagonist | ally | informant | merchant | neutral_power</summary>
        public string? Role { get; set; }

        /// <summary>What this NPC wants — natural language, interpreted by the Controller.</summary>
        public string? Motivation { get; set; }

        /// <summary>Per-player disposition overrides (player id → disposition value).</summary>
        public Dictionary<string, string>? DispositionToward { get; set; }

        /// <summary>Facts this NPC is aware of — used when Controller reasons about NPC behaviour.</summary>
        public List<string>? Knows { get; set; }

        /// <summary>Arc state: introduced | active | resolved | absent | deceased</summary>
        public string? ArcState { get; set; }
    }

    // ── Item models ───────────────────────────────────────────────────────────

    /// <summary>
    /// Two-tier item model.
    /// Tier "ambient": scene dressing — collectible or not, no mechanical weight.
    /// Tier "narrative": story-relevant item — has explicit properties the Controller reasons about.
    /// </summary>
    public sealed class SceneItem
    {
        /// <summary>Unique key within this scene (e.g. "warden_seal_01").</summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Description { get; set; } = "";

        /// <summary>"ambient" or "narrative".</summary>
        public string Tier { get; set; } = "ambient";

        /// <summary>Whether this item can be picked up.</summary>
        public bool Collectible { get; set; } = true;

        /// <summary>True once a player has taken this item.</summary>
        public bool IsCollected { get; set; } = false;

        /// <summary>Player id of whoever collected this item.</summary>
        public string? CollectedBy { get; set; }

        // ── Narrative tier fields ─────────────────────────────────────────────

        /// <summary>Action this item grants the bearer (e.g. "impersonate_authority").</summary>
        public string? GrantsAction { get; set; }

        /// <summary>Scene or gate this item unlocks (e.g. "north_gate").</summary>
        public string? Unlocks { get; set; }

        /// <summary>NPC group → reaction when player carries this item (e.g. "guards" → "deferential").</summary>
        public Dictionary<string, string>? NpcReaction { get; set; }
    }

    // ── Scene ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The active scene — one location, its NPC roster, and its item list.
    /// Only one scene is "live" at a time; others are archived in GameState.
    /// </summary>
    public sealed class WorldScene
    {
        public string LocationName { get; set; } = "";

        /// <summary>Short evocative description of the location used to seed narration.</summary>
        public string Description { get; set; } = "";

        public List<SceneNpc> Npcs { get; set; } = new();

        public List<SceneItem> Items { get; set; } = new();

        /// <summary>True while the party is traveling between locations.</summary>
        public bool InTransit { get; set; } = false;

        /// <summary>Destination pending player confirmation during a scene transition.</summary>
        public string? TransitDestination { get; set; }
    }

    // ── Turn state ────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-round turn tracking used by TurnEngine.
    /// Persisted in GameState so it survives session saves.
    /// </summary>
    public sealed class TurnState
    {
        /// <summary>Ordered list of active player ids (max 6).</summary>
        public List<string> ActivePlayerIds { get; set; } = new();

        /// <summary>Players who have acted this round.</summary>
        public HashSet<string> ActedThisRound { get; set; } = new();

        /// <summary>Players who have passed this round (incapacitated or skipped).</summary>
        public HashSet<string> PassedThisRound { get; set; } = new();

        /// <summary>Players currently marked as incapacitated (pass forced until revived).</summary>
        public HashSet<string> Incapacitated { get; set; } = new();

        public int RoundNumber { get; set; } = 0;
    }
}
