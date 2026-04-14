// NovaGM/Services/Agent/AgentTool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NovaGM.Services.Multiplayer;
using NovaGM.Services.Retrieval;
using NovaGM.Services.State;

namespace NovaGM.Services.Agent
{
    /// Dispatches tool calls made by the ControllerAgent during its ReAct loop.
    public static class ToolDispatcher
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static string Dispatch(string toolName, string argsJson, IStateStore state, Retriever? retriever)
        {
            try
            {
                var args = string.IsNullOrWhiteSpace(argsJson)
                    ? new Dictionary<string, JsonElement>()
                    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson, _json)
                      ?? new Dictionary<string, JsonElement>();

                return toolName.ToLowerInvariant().Trim() switch
                {
                    "roll_dice"   => RollDice(args),
                    "get_player"  => GetPlayer(args),
                    "get_npc"     => GetNpc(args, state),
                    "get_flags"   => GetFlags(state),
                    "query_memory"=> QueryMemory(args, state, retriever),
                    "set_flag"    => SetFlag(args, state),
                    "update_npc"  => UpdateNpc(args, state),
                    _             => $"Unknown tool '{toolName}'. Valid tools: roll_dice, get_player, get_npc, get_flags, query_memory, set_flag, update_npc"
                };
            }
            catch (Exception ex)
            {
                return $"Tool error ({toolName}): {ex.Message}";
            }
        }

        // ── Tools ────────────────────────────────────────────────────────────

        private static string RollDice(Dictionary<string, JsonElement> args)
        {
            var expr = GetString(args, "expr") ?? "1d20";
            var result = DiceService.Roll(expr);
            return result.Description;
        }

        private static string GetPlayer(Dictionary<string, JsonElement> args)
        {
            var name = GetString(args, "name") ?? "";
            var pc = GameCoordinator.Instance.GetPlayerCharacter(name);
            if (pc == null)
                return $"Player '{name}' not found. Connected players: {string.Join(", ", GameCoordinator.Instance.GetConnectedPlayers())}";

            return $"{pc.Name} | Race: {pc.Race} | Class: {pc.Class} | Level: {pc.Level} " +
                   $"| STR:{pc.STR} DEX:{pc.DEX} CON:{pc.CON} INT:{pc.INT} WIS:{pc.WIS} CHA:{pc.CHA}";
        }

        private static string GetNpc(Dictionary<string, JsonElement> args, IStateStore state)
        {
            var name = (GetString(args, "name") ?? "").Trim().ToLowerInvariant();
            var npcs = state.Load().Npcs;
            foreach (var kv in npcs)
            {
                if (kv.Key.ToLowerInvariant().Contains(name))
                    return $"{kv.Key}: {kv.Value}";
            }
            return $"NPC '{name}' not found. Known NPCs: {(npcs.Count == 0 ? "none" : string.Join(", ", npcs.Keys))}";
        }

        private static string GetFlags(IStateStore state)
        {
            var flags = state.Load().Flags;
            return flags.Count == 0 ? "No active flags." : string.Join(", ", flags);
        }

        private static string QueryMemory(Dictionary<string, JsonElement> args, IStateStore state, Retriever? retriever)
        {
            var q = GetString(args, "q") ?? "";
            if (retriever != null)
            {
                var results = Task.Run(() => retriever.QueryTopKAsync(q, 5)).GetAwaiter().GetResult();
                if (results.Count > 0)
                    return string.Join("; ", results);
            }
            // Fallback: recent facts
            var facts = state.Load().Facts;
            return facts.Count == 0 ? "No facts recorded yet." : string.Join("; ", facts.TakeLast(6));
        }

        private static string SetFlag(Dictionary<string, JsonElement> args, IStateStore state)
        {
            var flag = GetString(args, "flag") ?? "";
            if (string.IsNullOrWhiteSpace(flag)) return "Error: 'flag' arg is required.";
            state.ApplyChanges(null, new[] { flag }, null);
            return $"Flag set: {flag}";
        }

        private static string UpdateNpc(Dictionary<string, JsonElement> args, IStateStore state)
        {
            var name   = GetString(args, "name") ?? "";
            var status = GetString(args, "status") ?? "";
            if (string.IsNullOrWhiteSpace(name)) return "Error: 'name' arg is required.";
            state.ApplyChanges(null, null, new Dictionary<string, string> { [name] = status });
            return $"NPC updated — {name}: {status}";
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string? GetString(Dictionary<string, JsonElement> args, string key)
        {
            return args.TryGetValue(key, out var v) ? v.GetString() : null;
        }
    }
}
