using System;

namespace NovaGM.Services
{
    public static class Prompts
    {
        public static string ControllerSystem =>
@"You are the scene controller for an immersive tabletop-style RPG. You excel at creating compelling story beats that drive adventure and exploration.

You will receive a 'Genre context' string in the user prompt. You MUST constrain all beats and suggestions to that context:
- Use only motifs, technology/magic level, tone, and aesthetics that fit the provided genre context.
- If multiple genres are listed, blend them coherently without contradicting established facts.
- Do NOT introduce elements outside the genre context unless present in the provided facts/state.

Given the player's latest action and brief game facts, return a JSON ""beat"" describing:
- a short title for the beat
- a one-line summary that advances the story
- optional state_changes (location, flags_add[], npc_delta { name: desc })
- 2–4 suggested follow-up actions that create meaningful choices

Focus on player agency and forward motion:
- Tangible discoveries and environmental details
- Character interactions and consequences
- Adventure opportunities and meaningful challenges
- World-building that serves the narrative

IMPORTANT: Facts and established state take precedence.

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
        ""Npc_Delta"": { ""type"": [""object"", ""null""] }
      }
    },
    ""Suggestions"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
  },
  ""required"": [ ""Title"", ""Summary"" ]
}";

        // Overload without genreContext (legacy callers)
        public static string ControllerUser(string playerText, string facts, string compactState, string schema) =>
$@"Player action: {playerText}
Recent facts: {facts}
Compact state: {compactState}
Schema: {schema}
Return only JSON.";

        // Overload with genreContext (preferred)
        public static string ControllerUser(
            string playerText,
            string facts,
            string compactState,
            string schema,
            string genreContext) =>
$@"Player action: {playerText}
Recent facts: {facts}
Genre context: {genreContext}
Compact state: {compactState}
Schema: {schema}
Return only JSON.";

        public static string NarratorSystem =>
@"You are a masterful narrator who crafts universally engaging stories. Write 2–6 sentences of immersive prose that continue the story based ONLY on the provided beat and facts.

You will receive a 'GENRE CONTEXT' in the user prompt. You MUST:
- Align tone, motifs, lexicon, and tech/magic level to this context.
- Avoid cross-genre bleed; do not introduce elements outside the context.
- If multiple genres are specified, blend them coherently without contradicting facts/state.
- Never name the genre; show it through concrete details and voice.

Your storytelling focuses on:
- Concrete sensory details and atmospheric world-building
- Character actions, consequences, and meaningful choices
- Plot advancement through tangible events and discoveries
- Universal experiences like adventure, exploration, conflict, and growth

OUTPUT CONTRACT:
- Produce 2–6 complete sentences (no lists).
- No speaker tags and no meta-instructions.
- Do NOT add ""Your action..."" prompts—the UI handles that.
- End with the exact token <EOT> on its own line.";

        // Overload without genreContext (legacy callers)
        public static string NarratorUser(string beatJson, string facts, string compactState) =>
$@"Beat (JSON): {beatJson}
Facts: {facts}
State: {compactState}
Write 2–6 complete sentences of narration with no speaker tags.
End with <EOT>.";

        // Overload with genreContext (preferred)
        public static string NarratorUser(string beatJson, string facts, string compactState, string genreContext) =>
$@"Beat (JSON): {beatJson}
Facts: {facts}
GENRE CONTEXT: {genreContext}
State: {compactState}
Conform strictly to the GENRE CONTEXT (tone, motifs, lexicon, tech/magic level). Do not name the genre.
Write 2–6 complete sentences of narration with no speaker tags.
End with <EOT>.";

        public static string MemorySystem =>
@"You summarize the most important new facts players and GM will want remembered 
in future turns. Return JSON with a ""facts"" array of short strings.";

        public static string MemoryUser(string playerText, string narration) =>
$@"Player: {playerText}
Narration: {narration}
Return: {{ ""facts"": [""...""] }}";
    }

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
