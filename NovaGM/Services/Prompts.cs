// NovaGM/Services/Prompts.cs
using System;

namespace NovaGM.Services
{
    public static class Prompts
    {
        public static string ControllerSystem =>
@"You are the scene controller for a tabletop-style RPG. 
Given the player's latest action and brief game facts, you return a JSON ""beat"" describing:
- a short title for the beat
- a one-line summary  
- optional state_changes (location, flags_add[], npc_delta{{name:desc}})
- 2–4 suggested follow-up actions the player might try next.

IMPORTANT: Adapt to the genre and setting the player establishes. If they mention:
- Space, starships, planets, moons → respond with sci-fi elements
- Magic, dragons, kingdoms → respond with fantasy elements  
- Modern cities, technology → respond with contemporary elements
Always maintain consistency with the established setting and tone.

Return ONLY JSON that matches the schema.";

        public static string ControllerSchema =>
@"{
  ""type"": ""object"",
  ""properties"": {
    ""Title"": { ""type"": ""string"" },
    ""Summary"": { ""type"": ""string"" },
    ""State_Changes"": {
      ""type"": ""object"",
      ""properties"": {
        ""Location"": { ""type"": [""string"", ""null""] },
        ""Flags_Add"": { ""type"": [""array"", ""null""], ""items"": { ""type"": ""string"" } },
        ""Npc_Delta"": { ""type"": [""object"", ""null"" ] }
      }
    },
    ""Suggestions"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
  },
  ""required"": [ ""Title"", ""Summary"" ]
}";

        public static string ControllerUser(string playerText, string facts, string compactState, string schema) =>
$@"Player action: {playerText}
Recent facts: {facts}
Compact state: {compactState}
Schema: {schema}
Return only JSON.";

        public static string NarratorSystem =>
@"You are a vivid but concise narrator. Write 2–6 sentences of immersive prose 
that continue the story based ONLY on the provided beat and facts. 
Do not invent separate quests or new settings beyond what the beat implies.
End the final output with the token <EOT> to signal end-of-turn.";

        public static string NarratorUser(string beatJson, string facts, string compactState) =>
$@"Beat (JSON): {beatJson}
Facts: {facts}
State: {compactState}
Write narration now. End with <EOT>.";

        public static string MemorySystem =>
@"You summarize the most important new facts players and GM will want remembered 
in future turns. Return JSON with a ""facts"" array of short strings.";

        public static string MemoryUser(string playerText, string narration) =>
$@"Player: {playerText}
Narration: {narration}
Return: {{ ""facts"": [""...""] }}";
    }

    // Shared small DTOs used by the orchestrator
    public sealed class Beat
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public StateChange? State_Changes { get; set; }
        public string[]? Suggestions { get; set; }
    }

    public sealed class StateChange
    {
        public string? Location { get; set; }
        public string[]? Flags_Add { get; set; }
        public System.Collections.Generic.Dictionary<string, string>? Npc_Delta { get; set; }
    }

    public sealed class MemoryDelta
    {
        public System.Collections.Generic.List<string>? Facts { get; set; }
    }
}
