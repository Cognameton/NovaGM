using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NovaGM.Models
{
    /// <summary>
    /// Represents a saved mission/scenario that can be stored in packs
    /// </summary>
    public sealed class Mission
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("genre")]
        public string Genre { get; set; } = "fantasy";

        [JsonPropertyName("difficulty")]
        public string Difficulty { get; set; } = "medium";

        [JsonPropertyName("estimatedDuration")]
        public string EstimatedDuration { get; set; } = "2-4 hours";

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        // Game state and narrative content
        [JsonPropertyName("initialState")]
        public MissionState InitialState { get; set; } = new();

        [JsonPropertyName("narrative")]
        public MissionNarrative Narrative { get; set; } = new();

        [JsonPropertyName("encounters")]
        public List<Encounter> Encounters { get; set; } = new();

        [JsonPropertyName("metadata")]
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Initial game state for the mission
    /// </summary>
    public sealed class MissionState
    {
        [JsonPropertyName("location")]
        public string Location { get; set; } = "";

        [JsonPropertyName("premise")]
        public string Premise { get; set; } = "";

        [JsonPropertyName("flags")]
        public List<string> Flags { get; set; } = new();

        [JsonPropertyName("npcs")]
        public Dictionary<string, string> Npcs { get; set; } = new();

        [JsonPropertyName("facts")]
        public List<string> Facts { get; set; } = new();

        [JsonPropertyName("suggestedLevel")]
        public int SuggestedLevel { get; set; } = 1;

        [JsonPropertyName("partySize")]
        public string PartySize { get; set; } = "3-5 players";
    }

    /// <summary>
    /// Narrative content for the mission
    /// </summary>
    public sealed class MissionNarrative
    {
        [JsonPropertyName("openingText")]
        public string OpeningText { get; set; } = "";

        [JsonPropertyName("backgroundInfo")]
        public string BackgroundInfo { get; set; } = "";

        [JsonPropertyName("objectives")]
        public List<string> Objectives { get; set; } = new();

        [JsonPropertyName("hooks")]
        public List<string> Hooks { get; set; } = new();

        [JsonPropertyName("keyEvents")]
        public List<string> KeyEvents { get; set; } = new();

        [JsonPropertyName("conclusion")]
        public string Conclusion { get; set; } = "";
    }

    /// <summary>
    /// Individual encounter within the mission
    /// </summary>
    public sealed class Encounter
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "combat"; // combat, social, puzzle, exploration

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("trigger")]
        public string Trigger { get; set; } = "";

        [JsonPropertyName("difficulty")]
        public string Difficulty { get; set; } = "medium";

        [JsonPropertyName("rewards")]
        public List<string> Rewards { get; set; } = new();

        [JsonPropertyName("consequences")]
        public List<string> Consequences { get; set; } = new();
    }
}