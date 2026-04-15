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
        // Must be large enough for THOUGHT + full FINAL_ANSWER JSON (~600-800 tokens).
        private const int TokensPerStep = 896;

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
            var prompt = BuildInitialPrompt(playerText, facts, compact, genreContext, schema);
            Beat? result = null;

            for (int i = 0; i < MaxIterations && !ct.IsCancellationRequested; i++)
            {
                var response = await _llm.CompleteAsync(prompt.ToString(), TokensPerStep, ct);
                if (string.IsNullOrWhiteSpace(response)) break;

                prompt.Append(response);

                // FINAL_ANSWER → done
                var beat = TryExtractFinalAnswer(response);
                if (beat != null)
                {
                    result = beat;
                    break;
                }

                // ACTION → dispatch tool, inject observation, continue
                var (toolName, toolArgs) = TryExtractAction(response);
                if (toolName != null)
                {
                    var observation = ToolDispatcher.Dispatch(toolName, toolArgs ?? "{}", _state, _retriever, actingPlayerId);
                    prompt.AppendLine($"\nOBSERVATION: {observation}");
                    prompt.AppendLine("Assistant:");
                    continue;
                }

                // No structured output — nudge model toward a conclusion
                prompt.AppendLine("\n[System: write THOUGHT then either ACTION <tool> {args} or FINAL_ANSWER {json}]");
                prompt.AppendLine("Assistant:");
            }

            // If we ran out of iterations, try one last forced extraction from the
            // accumulated prompt — the model may have produced partial JSON.
            if (result == null)
                result = TryExtractFinalAnswer(prompt.ToString());

            // Persist a one-line summary into the rolling context
            if (result != null)
            {
                var title   = result.Title   ?? "";
                var summary = result.Summary ?? "";
                var label   = string.IsNullOrWhiteSpace(title) ? summary : title;
                var snippet = playerText.Length > 50 ? playerText[..50] + "…" : playerText;
                var entry   = $"[Turn] \"{snippet}\" → {(label.Length > 80 ? label[..80] + "…" : label)}";
                _rollingContext.Add(entry);
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
$@"You are the Narrative Controller for a tabletop RPG — the mind that shapes the world.
Genre context: {genreContext}

Think like a game master and author. Every action has consequence, weight, and narrative potential.

DEFAULT PATH — use when you have enough context:
THOUGHT: <reason through consequence, consistency, narrative weight>
FINAL_ANSWER: {{""Title"":""..."",""Summary"":""..."",""Mood"":""tense"",""Stakes"":""..."",""NarrativeNote"":""..."",""Suggestions"":[""..."",""..."",""...""]}}

TOOL PATH — use ONLY when you need game state you don't have:
THOUGHT: <reasoning>
ACTION: get_scene {{}}
[app replies: OBSERVATION: <scene contents>]
THOUGHT: <reasoning>
FINAL_ANSWER: {{...}}

TOOLS (use sparingly — only what you need):
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
  add_scene_item   {{""id"":""key"",""name"":""Name"",""tier"":""ambient|narrative"",""collectible"":true,""description"":""...""}}
  scene_transition {{""destination"":""location name""}}

FINAL_ANSWER SCHEMA:
{schema}

REQUIREMENTS:
- Mood: tense|mysterious|triumphant|dread|wonder|melancholic|urgent|grim|hopeful
- Stakes: what is immediately at risk (life, secret, trust, time, opportunity)
- NarrativeNote: specific private instruction to narrator (what detail to land on, what to feel)
- Suggestions: 3+ distinct concrete actions hinting at different consequences
- Do NOT name the genre — show it through tone, detail, lexicon
- Plant a seed: name a person, object, or place that could return later
- Max {MaxIterations} steps total; always reach FINAL_ANSWER within that limit";

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
