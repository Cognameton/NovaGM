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
- Fantasy elements (magic, dragons, kingdoms) → respond with fantasy elements
- Sci-Fi elements (space, starships, planets, technology) → respond with sci-fi elements  
- Horror elements (suspense, supernatural, dark themes) → respond with horror/contemporary elements
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

CRITICAL: Match the established setting and genre:
- If in Fantasy → use magic, medieval elements, mythical creatures, ancient kingdoms
- If in Sci-Fi → use technology, starships, alien worlds, futuristic elements, cyberpunk aesthetics
- If in Horror → use suspense, psychological tension, supernatural elements, modern/contemporary settings with dark atmosphere
Stay consistent with the established tone and setting throughout.

TOPIC BOUNDARY — HARD BAN:
Do not include social/ideological/identity commentary or slogans of any kind.
Do not mention or imply concepts such as: diversity, equity, inclusion, representation,
identity politics, social justice, ally/allyship, marginalized groups, oppression, patriarchy,
colonialism, privilege, flags as symbols of identity or movements, or similar themes.
Do not add or infer demographic attributes (race, gender, orientation, etc.) for any character
unless they are explicitly provided in the beat/facts; if not provided, remain silent about them.

The narration must stay universal and scene-focused: tangible setting details,
actions, consequences, stakes. No moralizing, advocacy, or critique.

PHYSICS/SANITY CHECK:
Avoid physically impossible imagery unless the context justifies it (e.g., flags don't wave
in vacuum; if flags appear, they are in a pressurized environment).

OUTPUT CONTRACT:
- Write 2–6 complete sentences of narration, no speaker tags, no lists.
- Do NOT include 'GM:', 'Player:', or 'Your action...' in your output.
- Do NOT break mid-sentence. If near the token limit, finish the sentence concisely.
- Write complete, flowing narrative prose only.

BEFORE YOU ANSWER, SELF-CHECK:
1) Did I include any social/ideological/identity commentary or slogans? → If yes, remove it.
2) Did I assign identity traits not in the facts? → If yes, remove them.  
3) Is my output 2–6 complete sentences, concrete, sensory, and focused on the scene/action?
4) Did I avoid speaker tags and incomplete sentences? → If no, fix it.

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

// Runtime content guardrails to prevent unwanted commentary
public static class NarrationGuards
{
    // Keep list short & surgical; match whole words/phrases case-insensitively
    private static readonly string[] Banned = new[]
    {
        "diversity", "equity", "inclusion", "dei",
        "representation", "identity politics", "social justice",
        "ally", "allyship", "marginalized", "oppression",
        "patriarchy", "colonialism", "privilege",
        "pride flag", "flags of identity", "waving flags of",
        "diverse crew", "representation of", "pride in representing"
    };

    public static bool ViolatesPolicy(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var span = text.AsSpan();
        foreach (var term in Banned)
        {
            if (span.IndexOf(term.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    public static bool IsIncomplete(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        
        // Remove <EOT> token for checking
        var cleaned = text.Replace("<EOT>", "").Trim();
        if (string.IsNullOrEmpty(cleaned)) return true;
        
        // Check if it ends with proper punctuation
        var lastChar = cleaned[^1];
        if (!"!?.".Contains(lastChar)) return true;
        
        // Check for common incomplete patterns
        if (cleaned.EndsWith(" with", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" their", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" the", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" a", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith(" an", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("etched with", StringComparison.OrdinalIgnoreCase) && !cleaned.Contains(".", cleaned.LastIndexOf("etched with", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        
        return false;
    }

    public static string CompleteText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        
        var cleaned = text.Replace("<EOT>", "").Trim();
        if (string.IsNullOrEmpty(cleaned)) return text;
        
        // If doesn't end with punctuation, add period
        var lastChar = cleaned[^1];
        if (!"!?.".Contains(lastChar))
        {
            cleaned += ".";
        }
        
        return cleaned + "<EOT>";
    }

    public static string GetNeutralFallback(string setting = "")
    {
        return setting.ToLowerInvariant() switch
        {
            var s when s.Contains("space") || s.Contains("sci") || s.Contains("tech") || s.Contains("future") => 
                "The scene holds a quiet tension. Control panels hum with energy, displays casting their glow across the surfaces. What do you do next?",
            var s when s.Contains("fantasy") || s.Contains("magic") || s.Contains("medieval") || s.Contains("kingdom") => 
                "The moment stretches, filled with possibility. Shadows dance in the flickering light as the scene awaits your next move.",
            var s when s.Contains("horror") || s.Contains("dark") || s.Contains("supernatural") || s.Contains("suspense") => 
                "An unsettling silence hangs in the air. The atmosphere grows heavy with unspoken dread as something stirs in the shadows.",
            _ => "The scene holds a quiet tension. The environment awaits your next action."
        };
    }
}
