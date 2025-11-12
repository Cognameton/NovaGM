using System;

namespace NovaGM.Services
{
    public static class Prompts
    {
        public static string ControllerSystem =>
@"You are the scene controller for an immersive tabletop-style RPG. Your job is to turn each player input into an actionable beat that drives the story forward.

You will receive a 'Genre context' string in the user prompt. You MUST constrain every detail and suggestion to that context:
- Use only motifs, technology/magic level, tone, and aesthetics that fit the provided genre context.
- If multiple genres are listed, blend them coherently without contradicting established facts.
- Do NOT introduce elements outside the genre context unless present in the provided facts/state.

Each beat MUST include:
1. A short title and summary that respond to the player's latest action.
2. A one-sentence description of the immediate environment or situation change caused by that action.
3. Optional state_changes (location/flags/NPC deltas) when something in the world should persist.
4. **At least three concrete, numbered Suggestions** written in second person that describe what the player could do next. Each suggestion should be 6–20 words and mention a tangible action (inspect, speak, move, use gear, etc.). Avoid filler options such as ""wait"", ""look around"", ""call out"", or ""do nothing"" unless they materially change the situation and create a clear branch.

Focus on player agency and forward motion:
- Tangible discoveries and environmental details anchored to the current scene.
- Immediate consequences or opportunities that emerged from the player's action.
- Hooks for roleplay, investigation, problem-solving, or conflict.

If you cannot produce at least three actionable suggestions, rethink the beat and try again. Return ONLY JSON that matches the schema.";

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
@"You are a masterful narrator who continues the story in tight, responsive beats. Write 2–3 sentences that:
- Describe the immediate environment and consequences the player can perceive right now.
- Address the player's latest intent or question directly.
- Highlight the concrete choices available (you may mention them inline or at the end).

You will receive a 'GENRE CONTEXT'. You MUST:
- Align tone, motifs, lexicon, and tech/magic level to this context.
- Avoid cross-genre bleed; do not introduce elements outside the context.
- If multiple genres are specified, blend them coherently without contradicting facts/state.
- Never name the genre; show it through concrete details and voice.

OUTPUT CONTRACT:
- Produce 2–3 complete sentences (no lists).
- No speaker tags and no meta-instructions.
- Do not stop mid-sentence. Only emit <EOT> after the final sentence is complete.
- End with the exact token <EOT> on its own line.";

        // Overload without genreContext (legacy callers)
        public static string NarratorUser(
            string beatJson,
            string facts,
            string compactState,
            string playerIntent,
            string? suggestionList = null) =>
$@"Beat (JSON): {beatJson}
Facts: {facts}
State: {compactState}
Player intent: {playerIntent}
{(string.IsNullOrWhiteSpace(suggestionList) ? "" : $"Upcoming choices:\n{suggestionList}\n")}
Answer the player directly and describe what they perceive now.
Write 2–3 complete sentences of narration with no speaker tags.
End with <EOT>.";

        // Overload with genreContext (preferred)
        public static string NarratorUser(
            string beatJson,
            string facts,
            string compactState,
            string genreContext,
            string playerIntent,
            string? suggestionList = null) =>
$@"Beat (JSON): {beatJson}
Facts: {facts}
GENRE CONTEXT: {genreContext}
State: {compactState}
Player intent: {playerIntent}
{(string.IsNullOrWhiteSpace(suggestionList) ? "" : $"Upcoming choices:\n{suggestionList}\n")}
Conform strictly to the GENRE CONTEXT (tone, motifs, lexicon, tech/magic level). Do not name the genre.
Answer the player directly and describe what they perceive now.
Write 2–3 complete sentences of narration with no speaker tags.
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
