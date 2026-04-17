// NovaGM/Services/Agent/ControllerAgent.cs
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NovaGM.Services.Retrieval;
using NovaGM.Services.State;

namespace NovaGM.Services.Agent
{
    /// Controller implemented as a local ReAct agent.
    ///
    /// Each turn the agent reasons step by step, calls tools (dice rolls, player
    /// stats, NPC lookups, world-state queries, memory search) and produces a Beat
    /// JSON only when it has enough information. A rolling context window gives it
    /// continuity across turns without blowing the model's context budget.
    public sealed class ControllerAgent
    {
        // How many THOUGHT→ACTION→OBSERVATION cycles are allowed before forcing
        // a FINAL_ANSWER. Keep low for small models — more iterations waste context.
        private const int MaxIterations = 4;

        // Tokens the LLM may emit per reasoning step.
        // Must be large enough for THOUGHT (1 sentence ~20 tokens) + full FINAL_ANSWER JSON (~600-800 tokens).
        private const int TokensPerStep = 1200;

        // How many past-turn summaries to keep in the rolling context.
        private const int MaxContextEntries = 8;

        private readonly LlamaLocal _llm;
        private readonly IStateStore _state;
        private Retriever? _retriever;

        // Summaries of recent turns injected at the top of each new turn prompt.
        private readonly List<string> _rollingContext = new();

        public ControllerAgent(LlamaLocal llm, IStateStore state, Retriever? retriever = null)
        {
            _llm = llm;
            _state = state;
            _retriever = retriever;
        }

        public void SetRetriever(Retriever retriever) => _retriever = retriever;

        // Keywords that must not appear in user-controlled fields to prevent prompt injection.
        private static readonly string[] InjectionKeywords = { "FINAL_ANSWER:", "ACTION:", "OBSERVATION:" };

        /// Strip prompt-injection keywords from a player-supplied string.
        private static string SanitizeUserInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            foreach (var kw in InjectionKeywords)
                input = input.Replace(kw, string.Empty, StringComparison.OrdinalIgnoreCase);
            return input;
        }

        /// Run a full agent turn and return a Beat, or null if the model failed.
        public async Task<Beat?> RunAsync(
            string playerText,
            string facts,
            string compact,
            string genreContext,
            string schema,
            CancellationToken ct,
            string? actingPlayerId = null)
        {
            // Sanitize only the player-supplied string — facts and compact are server-generated.
            playerText = SanitizeUserInput(playerText);

            var prompt = BuildInitialPrompt(playerText, facts, compact, genreContext, schema);
            // Record where the system portion ends so we only search model output for markers.
            int modelOutputStart = prompt.Length;
            Beat? result = null;

            Console.WriteLine($"[Controller] Starting ReAct loop (max {MaxIterations} iterations) for: \"{(playerText.Length > 60 ? playerText[..60] + "…" : playerText)}\"");

            for (int i = 0; i < MaxIterations && !ct.IsCancellationRequested; i++)
            {
                Console.WriteLine($"[Controller] Iteration {i + 1}/{MaxIterations} — calling LLM ({TokensPerStep} tokens)");
                var response = await _llm.CompleteAsync(prompt.ToString(), TokensPerStep, ct);
                if (string.IsNullOrWhiteSpace(response))
                {
                    Console.WriteLine($"[Controller] Iteration {i + 1}: LLM returned empty/null — breaking");
                    break;
                }

                // Log first 200 chars of raw response for diagnostics
                var preview = response.Length > 200 ? response[..200].Replace('\n', '↵') + "…" : response.Replace('\n', '↵');
                Console.WriteLine($"[Controller] Iteration {i + 1} raw: {preview}");

                prompt.Append(response);

                // FINAL_ANSWER → done (search only the model's own response slice)
                var beat = TryExtractFinalAnswer(response);
                if (beat != null)
                {
                    Console.WriteLine($"[Controller] Iteration {i + 1}: FINAL_ANSWER found — beat title=\"{beat.Title ?? "(none)"}\"");
                    result = beat;
                    break;
                }

                // ACTION → dispatch tool, inject observation, continue
                var (toolName, toolArgs) = TryExtractAction(response);
                if (toolName != null)
                {
                    Console.WriteLine($"[Controller] Iteration {i + 1}: ACTION={toolName} args={toolArgs}");
                    var observation = await ToolDispatcher.DispatchAsync(toolName, toolArgs ?? "{}", _state, _retriever, actingPlayerId);
                    Console.WriteLine($"[Controller] Iteration {i + 1}: OBSERVATION={observation}");
                    prompt.AppendLine($"\nOBSERVATION: {observation}");
                    prompt.AppendLine("Assistant:");
                    continue;
                }

                // No structured output — push directly to FINAL_ANSWER, skip re-asking for THOUGHT
                Console.WriteLine($"[Controller] Iteration {i + 1}: no ACTION or FINAL_ANSWER — nudging model");
                prompt.AppendLine("\n[System: output FINAL_ANSWER JSON now. No more THOUGHT.]");
                prompt.AppendLine("FINAL_ANSWER:");
            }

            // If we ran out of iterations, try one last forced extraction — restrict
            // the search to only the model output portion (after modelOutputStart) so
            // that player-injected text in the prompt prefix cannot be matched.
            if (result == null)
            {
                Console.WriteLine("[Controller] Loop exhausted — attempting forced extraction from full model output");
                var modelOutput = prompt.Length > modelOutputStart
                    ? prompt.ToString(modelOutputStart, prompt.Length - modelOutputStart)
                    : string.Empty;
                result = TryExtractFinalAnswer(modelOutput);
                if (result != null)
                    Console.WriteLine($"[Controller] Forced extraction succeeded — beat title=\"{result.Title ?? "(none)"}\"");
                else
                    Console.WriteLine("[Controller] Forced extraction failed — returning null (fallback will fire)");
            }

            // Persist a summary + suggestions into the rolling context so the next turn
            // can understand references like "I choose option 2".
            if (result != null)
            {
                var title   = result.Title   ?? "";
                var summary = result.Summary ?? "";
                var label   = string.IsNullOrWhiteSpace(title) ? summary : title;
                var snippet = playerText.Length > 50 ? playerText[..50] + "…" : playerText;
                var entry   = new System.Text.StringBuilder();
                entry.Append($"[Turn] \"{snippet}\" → {(label.Length > 80 ? label[..80] + "…" : label)}");
                if (result.Suggestions is { Length: > 0 })
                {
                    entry.Append(" | options offered:");
                    for (int s = 0; s < result.Suggestions.Length; s++)
                        entry.Append($" {s + 1}) {result.Suggestions[s]}");
                }
                _rollingContext.Add(entry.ToString());
                if (_rollingContext.Count > MaxContextEntries)
                    _rollingContext.RemoveAt(0);
            }

            return result;
        }

        // ── Prompt construction ───────────────────────────────────────────────

        private StringBuilder BuildInitialPrompt(
            string playerText, string facts, string compact,
            string genreContext, string schema)
        {
            var sb = new StringBuilder();

            // System instruction
            sb.AppendLine(BuildSystemPrompt(genreContext, schema));

            // Rolling context from prior turns (continuity without full history)
            if (_rollingContext.Count > 0)
            {
                sb.AppendLine("\n=== Session so far ===");
                foreach (var entry in _rollingContext)
                    sb.AppendLine(entry);
                sb.AppendLine("=== End session ===\n");
            }

            // Current turn input
            sb.AppendLine($"Player action: {playerText}");
            sb.AppendLine($"Recent facts: {facts}");
            sb.AppendLine($"World state: {compact}");
            sb.AppendLine("\nThink step by step. Use tools as needed, then produce FINAL_ANSWER.");
            sb.AppendLine("Assistant:");

            return sb;
        }

        private static string BuildSystemPrompt(string genreContext, string schema) =>
$@"You are a game state processor for a tabletop RPG. Your job is mechanical: read input, call tools if needed, output a Beat JSON directive for the narrator. You do NOT write prose or narrate — that is the narrator's exclusive role.

Genre: {genreContext}

DEFAULT — when you have enough context:
THOUGHT: [one sentence: what state changes and why]
FINAL_ANSWER: {{""Title"":""..."",""Summary"":""..."",""Mood"":""tense"",""Stakes"":""..."",""NarrativeNote"":""..."",""Suggestions"":[""..."",""..."",""...""]}}

TOOL PATH — only when you need state you don't have:
THOUGHT: [one sentence: what you need]
ACTION: tool_name {{args}}
OBSERVATION: <result>
THOUGHT: [one sentence: conclusion]
FINAL_ANSWER: {{...}}

TOOLS:
  roll_dice        {{""expr"":""2d6""}}
  get_player       {{""name"":""PlayerName""}}
  get_npc          {{""name"":""NpcName""}}
  get_flags        {{}}
  get_scene        {{}}
  query_memory     {{""q"":""search terms""}}
  set_flag         {{""flag"":""flag_name""}}
  update_npc       {{""name"":""NpcName"",""status"":""new status""}}
  give_item        {{""item_id"":""id"",""player_id"":""name""}}
  add_scene_npc    {{""id"":""key"",""name"":""Name"",""tier"":""ambient|narrative"",""disposition"":""neutral"",""motivation"":""optional""}}
  add_scene_item   {{""id"":""key"",""name"":""Name"",""tier"":""ambient|narrative"",""collectible"":true,""description"":""..."",""level_required"":0}}
  scene_transition {{""destination"":""location name""}}

BEAT OUTPUT — keep all fields brief and factual:
- Title: 2-4 word label (e.g. ""Gate Confrontation"")
- Summary: one factual sentence — what happened and what changed (no prose)
- Mood: tense|mysterious|triumphant|dread|wonder|melancholic|urgent|grim|hopeful
- Stakes: one short phrase (e.g. ""losing the informant"", ""being caught"")
- NarrativeNote: one terse cue to the narrator (e.g. ""land on the wax seal; player should feel too late"")
- Suggestions: 3 brief action phrases — not prose (e.g. ""bribe the guard"", ""take the side passage"")

OPENING SEQUENCE — when rolling context is empty and no location is set:
1. get_player for each connected player — record name, class, level.
2. add_scene_npc — outfitter appropriate to genre. tier=""narrative"", disposition=""friendly"".
3. add_scene_item x4-6 — level_required=1 for accessible items, level_required=player_level+3 for locked items. collectible=true for all.
4. FINAL_ANSWER: Title=""Opening"", Mood=""hopeful"", Summary=""[players] arrive at [outfitter name]"", NarrativeNote=""name each player and class; describe outfitter and wares; call out locked items as visible but out of reach"", Suggestions=[""examine the wares"",""speak with [outfitter]"",""check your gear""]

ITEM LOCKS: if give_item returns ""Level lock..."" — put the lock context in NarrativeNote.

SCHEMA:
{schema}

Max {MaxIterations} steps. Always produce FINAL_ANSWER.";

        // ── Parsing ───────────────────────────────────────────────────────────

        private static Beat? TryExtractFinalAnswer(string text)
        {
            var idx = text.IndexOf("FINAL_ANSWER:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var after = text[(idx + "FINAL_ANSWER:".Length)..].Trim();
            var start = after.IndexOf('{');
            var end   = after.LastIndexOf('}');
            if (start < 0 || end <= start) return null;

            var json = after[start..(end + 1)];
            try
            {
                return JsonSerializer.Deserialize<Beat>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });
            }
            catch { return null; }
        }

        private static (string? toolName, string? toolArgs) TryExtractAction(string text)
        {
            // Find the last ACTION: line (so we handle multi-step output correctly)
            var idx = text.LastIndexOf("ACTION:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return (null, null);

            // Grab the rest of that line
            var rest = text[(idx + "ACTION:".Length)..];
            var lineEnd = rest.IndexOf('\n');
            var line = (lineEnd >= 0 ? rest[..lineEnd] : rest).Trim();

            if (string.IsNullOrWhiteSpace(line)) return (null, null);

            // Split on first space: "tool_name {...}"
            var space = line.IndexOf(' ');
            if (space < 0) return (line, "{}");

            var toolName = line[..space].Trim();
            var argsPart = line[space..].Trim();

            var jsonStart = argsPart.IndexOf('{');
            var jsonEnd   = argsPart.LastIndexOf('}');
            var args = (jsonStart >= 0 && jsonEnd > jsonStart)
                ? argsPart[jsonStart..(jsonEnd + 1)]
                : "{}";

            return (toolName, args);
        }
    }
}
