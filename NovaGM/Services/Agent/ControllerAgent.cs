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
        // a FINAL_ANSWER. Raise this for larger/smarter models.
        private const int MaxIterations = 6;

        // Tokens the LLM may emit per reasoning step.
        private const int TokensPerStep = 320;

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
            CancellationToken ct)
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
                    var observation = ToolDispatcher.Dispatch(toolName, toolArgs ?? "{}", _state, _retriever);
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
$@"You are the Controller for a tabletop RPG. Genre context: {genreContext}

You reason step by step and call tools to ground decisions in actual game state.
Do NOT invent dice results or player stats — use tools.

RESPONSE FORMAT:
THOUGHT: <your reasoning>
ACTION: <tool_name> {{""arg"": ""value""}}

The app executes the tool and replies:
OBSERVATION: <result>

When you have enough information, end with:
THOUGHT: <final reasoning>
FINAL_ANSWER: <JSON matching schema below>

AVAILABLE TOOLS:
  roll_dice    {{""expr"": ""2d6""}}                     — roll dice; returns individual rolls + total
  get_player   {{""name"": ""PlayerName""}}              — stats (STR/DEX/CON/INT/WIS/CHA, class, race, level)
  get_npc      {{""name"": ""NpcName""}}                 — NPC status from world state
  get_flags                                              — active world flags and conditions
  query_memory {{""q"": ""what happened at the docks""}} — search session memory for relevant facts
  set_flag     {{""flag"": ""bridge_destroyed""}}        — record a persistent world state flag
  update_npc   {{""name"": ""Guard"", ""status"": ""..."" }} — update NPC disposition or status

SCHEMA (FINAL_ANSWER must match):
{schema}

Rules:
- Roll dice for any outcome that depends on chance or player stats
- FINAL_ANSWER.Suggestions must contain at least 3 numbered, concrete player options
- Do not name the genre — show it through details and tone
- Max {MaxIterations} tool calls per turn; produce FINAL_ANSWER before that limit";

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
