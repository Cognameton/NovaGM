// NovaGM/Services/Prompts.cs
using System;

namespace NovaGM.Services
{
    /// Centralized prompt text. These are intentionally neutral—no canned scenes.
    public static class Prompts
    {
        public static string ControllerSystem =>
@"You are the game controller. You DO NOT narrate prose.
You plan a single beat of gameplay based on the player's action, current facts, and compact state.
Return strict JSON matching the 'Beat' shape. No explanations, no markdown—JSON only.

The Beat JSON shape:
{
  ""state_changes"": {
    ""location"": string|null,
    ""flags_add"": string[]|null,
    ""npc_delta"": { string: string }|null
  },
  ""narrator_cues"": string   // 1–2 sentences of guidance for the narrator, not prose.
}";

        public static string ControllerSchema =>
@"{
  ""type"": ""object"",
  ""properties"": {
    ""state_changes"": {
      ""type"": ""object"",
      ""properties"": {
        ""location"": { ""type"": [""string"", ""null""] },
        ""flags_add"": { ""type"": [""array"", ""null""], ""items"": { ""type"": ""string"" } },
        ""npc_delta"": { ""type"": [""object"", ""null""] }
      },
      ""additionalProperties"": false
    },
    ""narrator_cues"": { ""type"": ""string"" }
  },
  ""required"": [ ""narrator_cues"" ],
  ""additionalProperties"": false
}";

        public static string ControllerUser(string playerText, string facts, string compact, string schemaJson) =>
$@"Player action: {playerText}

Known facts (recent & retrieved):
{facts}

Compact state:
{compact}

Return ONLY JSON (no markdown) that validates against this schema:
{schemaJson}";

        public static string NarratorSystem =>
@"You are the narrator. Write concise, vivid prose responding only to the latest action
and the controller's beat. Do NOT introduce prewritten locations or canned scenes.
Keep 2–5 sentences. Conclude with short options in one line (e.g., ""1) … 2) … 3) …"").
End your output with <EOT> once the thought is complete.";

        public static string NarratorUser(string beatJson, string facts, string compact) =>
$@"Beat (from controller as JSON):
{beatJson}

Facts:
{facts}

Compact state:
{compact}

Write the narrated response as instructed. End with <EOT>.";

        public static string MemorySystem =>
@"You identify new durable facts from (player text + narrator output).
Return JSON: { ""facts"": [string, ...] }.
Facts should be short, reusable, and setting-agnostic when possible.";

        public static string MemoryUser(string playerText, string prose) =>
$@"Player: {playerText}
Narrator: {prose}

Return only JSON with key ""facts"" (may be empty).";
    }
}
