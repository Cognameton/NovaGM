// NovaGM/Services/Agent/AgentTool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NovaGM.Models;
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

        public static async Task<string> DispatchAsync(
            string toolName,
            string argsJson,
            IStateStore state,
            Retriever? retriever,
            string? actingPlayerId = null)
        {
            try
            {
                var args = string.IsNullOrWhiteSpace(argsJson)
                    ? new Dictionary<string, JsonElement>()
                    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson, _json)
                      ?? new Dictionary<string, JsonElement>();

                // query_memory is the only async tool
                if (toolName.Trim().Equals("query_memory", StringComparison.OrdinalIgnoreCase))
                    return await QueryMemoryAsync(args, state, retriever);

                return toolName.ToLowerInvariant().Trim() switch
                {
                    "roll_dice"        => RollDice(args),
                    "get_player"       => GetPlayer(args),
                    "get_npc"          => GetNpc(args, state),
                    "get_flags"        => GetFlags(state),
                    "set_flag"         => SetFlag(args, state),
                    "update_npc"       => UpdateNpc(args, state),
                    "get_scene"        => GetScene(state),
                    "give_item"        => GiveItem(args, state, actingPlayerId),
                    "add_scene_npc"    => AddSceneNpc(args, state),
                    "add_scene_item"   => AddSceneItem(args, state),
                    "scene_transition" => SceneTransition(args, state),
                    _ => $"Unknown tool '{toolName}'. Valid tools: roll_dice, get_player, get_npc, get_flags, query_memory, set_flag, update_npc, get_scene, give_item, add_scene_npc, add_scene_item, scene_transition"
                };
            }
            catch (Exception ex)
            {
                return $"Tool error ({toolName}): {ex.Message}";
            }
        }

        // ── Existing tools ────────────────────────────────────────────────────

        private static string RollDice(Dictionary<string, JsonElement> args)
        {
            var expr   = GetString(args, "expr") ?? "1d20";
            var result = DiceService.Roll(expr);
            return result.Description;
        }

        private static string GetPlayer(Dictionary<string, JsonElement> args)
        {
            var name = GetString(args, "name") ?? "";
            var pc   = GameCoordinator.Instance.GetPlayerCharacter(name);
            if (pc == null)
                return $"Player '{name}' not found. Connected players: {string.Join(", ", GameCoordinator.Instance.GetConnectedPlayers())}";

            return $"{pc.Name} | Race: {pc.Race} | Class: {pc.Class} | Level: {pc.Level} " +
                   $"| STR:{pc.STR} DEX:{pc.DEX} CON:{pc.CON} INT:{pc.INT} WIS:{pc.WIS} CHA:{pc.CHA}";
        }

        private static string GetNpc(Dictionary<string, JsonElement> args, IStateStore state)
        {
            var name = (GetString(args, "name") ?? "").Trim().ToLowerInvariant();
            var gs   = state.Load();

            // Check rich scene NPCs first
            var sceneNpc = gs.Scene.Npcs.Find(n =>
                n.Id.ToLowerInvariant().Contains(name) ||
                n.Name.ToLowerInvariant().Contains(name));
            if (sceneNpc is not null)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"{sceneNpc.Name} [{sceneNpc.Tier}] | Disposition: {sceneNpc.Disposition}");
                if (!string.IsNullOrWhiteSpace(sceneNpc.Motivation))
                    sb.Append($" | Motivation: {sceneNpc.Motivation}");
                if (!string.IsNullOrWhiteSpace(sceneNpc.ArcState))
                    sb.Append($" | Arc: {sceneNpc.ArcState}");
                if (sceneNpc.Knows?.Count > 0)
                    sb.Append($" | Knows: {string.Join(", ", sceneNpc.Knows)}");
                return sb.ToString();
            }

            // Fall back to legacy flat dict
            foreach (var kv in gs.Npcs)
            {
                if (kv.Key.ToLowerInvariant().Contains(name))
                    return $"{kv.Key}: {kv.Value}";
            }
            return $"NPC '{name}' not found. Known NPCs: {(gs.Npcs.Count == 0 ? "none" : string.Join(", ", gs.Npcs.Keys))}";
        }

        private static string GetFlags(IStateStore state)
        {
            var flags = state.Load().Flags;
            return flags.Count == 0 ? "No active flags." : string.Join(", ", flags);
        }

        private static async Task<string> QueryMemoryAsync(Dictionary<string, JsonElement> args, IStateStore state, Retriever? retriever)
        {
            var q = GetString(args, "q") ?? "";
            if (retriever != null)
            {
                try
                {
                    var results = await retriever.QueryTopKAsync(q, 5).ConfigureAwait(false);
                    if (results.Count > 0)
                        return string.Join("; ", results);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[NovaGM] QueryMemory retriever failed: {ex.Message}");
                }
            }
            var gs    = state.Load();
            var facts = gs.Facts.TakeLast(6).ToList();
            var hooks = gs.Hooks.TakeLast(4).ToList();
            if (facts.Count == 0 && hooks.Count == 0) return "No facts recorded yet.";
            var parts = new List<string>();
            if (facts.Count > 0) parts.Add("Facts: " + string.Join("; ", facts));
            if (hooks.Count > 0) parts.Add("Hooks: " + string.Join("; ", hooks));
            return string.Join(" | ", parts);
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

        // ── New scene tools ───────────────────────────────────────────────────

        /// <summary>Returns a summary of the current scene: location, NPCs, and available items.</summary>
        private static string GetScene(IStateStore state)
        {
            var scene = state.Load().Scene;
            var sb    = new System.Text.StringBuilder();
            sb.Append($"Location: {(string.IsNullOrWhiteSpace(scene.LocationName) ? "unknown" : scene.LocationName)}");

            if (scene.Npcs.Count > 0)
            {
                sb.Append(" | NPCs: ");
                sb.Append(string.Join(", ", scene.Npcs.Select(n =>
                    $"{n.Name}[{n.Tier},{n.Disposition}]")));
            }
            else
            {
                sb.Append(" | NPCs: none");
            }

            var available = scene.Items.Where(i => !i.IsCollected).ToList();
            if (available.Count > 0)
            {
                sb.Append(" | Items: ");
                sb.Append(string.Join(", ", available.Select(i =>
                    $"{i.Name}[id={i.Id},tier={i.Tier},collectible={i.Collectible}]")));
            }
            else
            {
                sb.Append(" | Items: none");
            }

            if (scene.InTransit)
                sb.Append($" | IN TRANSIT to: {scene.TransitDestination ?? "unknown"}");

            return sb.ToString();
        }

        /// <summary>Moves a scene item into the acting player's inventory.</summary>
        private static string GiveItem(Dictionary<string, JsonElement> args, IStateStore state, string? actingPlayerId)
        {
            var itemId   = GetString(args, "item_id")  ?? "";
            var playerId = GetString(args, "player_id") ?? actingPlayerId ?? "";

            if (string.IsNullOrWhiteSpace(itemId))   return "Error: 'item_id' is required.";
            if (string.IsNullOrWhiteSpace(playerId))  return "Error: 'player_id' is required (or must be inferred from acting player).";

            var scene = state.Load().Scene;
            var item  = scene.Items.Find(i => i.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
            if (item is null)      return $"Item '{itemId}' not found in scene.";
            if (!item.Collectible) return $"Item '{item.Name}' is not collectible.";
            if (item.IsCollected)  return $"Item '{item.Name}' has already been collected by {item.CollectedBy}.";

            state.ApplyChanges(null, null, null, itemsGive: new[] { itemId }, actingPlayerId: playerId);
            return $"'{item.Name}' added to {playerId}'s inventory.";
        }

        /// <summary>Adds a new NPC to the current scene.</summary>
        private static string AddSceneNpc(Dictionary<string, JsonElement> args, IStateStore state)
        {
            var id          = GetString(args, "id")          ?? "";
            var name        = GetString(args, "name")        ?? "";
            var description = GetString(args, "description") ?? "";
            var tier        = GetString(args, "tier")        ?? "ambient";
            var disposition = GetString(args, "disposition") ?? "neutral";
            var motivation  = GetString(args, "motivation");
            var role        = GetString(args, "role");

            if (string.IsNullOrWhiteSpace(name)) return "Error: 'name' is required.";
            if (string.IsNullOrWhiteSpace(id))   id = name.ToLowerInvariant().Replace(' ', '_');

            var scene = state.Load().Scene;
            // Avoid duplicates
            if (scene.Npcs.Any(n => n.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                return $"NPC '{id}' already in scene.";

            scene.Npcs.Add(new SceneNpc
            {
                Id          = id,
                Name        = name,
                Description = description,
                Tier        = tier,
                Disposition = disposition,
                Motivation  = motivation,
                Role        = role,
                ArcState    = tier == "narrative" ? "introduced" : null
            });

            // Mirror into legacy Npcs dict for backward compat
            state.Load().Npcs[name] = $"{tier}; {disposition}" +
                (motivation is not null ? $"; wants: {motivation}" : "");

            return $"NPC '{name}' added to scene as {tier}.";
        }

        /// <summary>Adds a new item to the current scene.</summary>
        private static string AddSceneItem(Dictionary<string, JsonElement> args, IStateStore state)
        {
            var id          = GetString(args, "id")          ?? "";
            var name        = GetString(args, "name")        ?? "";
            var description = GetString(args, "description") ?? "";
            var tier        = GetString(args, "tier")        ?? "ambient";
            var collectible = true;

            if (args.TryGetValue("collectible", out var cVal) && cVal.ValueKind == JsonValueKind.False)
                collectible = false;

            if (string.IsNullOrWhiteSpace(name)) return "Error: 'name' is required.";
            if (string.IsNullOrWhiteSpace(id))   id = name.ToLowerInvariant().Replace(' ', '_');

            var scene = state.Load().Scene;
            if (scene.Items.Any(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                return $"Item '{id}' already in scene.";

            var item = new SceneItem
            {
                Id          = id,
                Name        = name,
                Description = description,
                Tier        = tier,
                Collectible = collectible
            };

            if (tier == "narrative")
            {
                item.GrantsAction = GetString(args, "grants_action");
                item.Unlocks      = GetString(args, "unlocks");
            }

            scene.Items.Add(item);
            return $"Item '{name}' added to scene as {tier}.";
        }

        /// <summary>Queues a scene transition — player confirmation required before it executes.</summary>
        private static string SceneTransition(Dictionary<string, JsonElement> args, IStateStore state)
        {
            var destination = GetString(args, "destination") ?? "";
            if (string.IsNullOrWhiteSpace(destination)) return "Error: 'destination' is required.";

            state.ApplyChanges(null, null, null, transitionTo: destination);
            return $"Scene transition queued to '{destination}'. Awaiting player confirmation.";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string? GetString(Dictionary<string, JsonElement> args, string key)
            => args.TryGetValue(key, out var v) ? v.GetString() : null;
    }
}
