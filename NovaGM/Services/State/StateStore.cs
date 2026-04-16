// NovaGM/Services/State/StateStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NovaGM.Models;

namespace NovaGM.Services.State
{
    /// <summary>
    /// JSON-on-disk + in-memory state store.
    /// </summary>
    public sealed class StateStore : IStateStore
    {
        private readonly GameState _state;
        private readonly string _filePath;

        private string? _pendingTransition;
        public string? PendingTransition => _pendingTransition;

        public string[] LastSuggestions { get; set; } = Array.Empty<string>();

        private static string AppDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaGM");

        public StateStore()
        {
            Directory.CreateDirectory(AppDataDir);
            _filePath = Path.Combine(AppDataDir, "state.json");
            _state = TryLoadFromDisk(_filePath) ?? CreateEmpty();
            EnsureCollectionsExist();
            Save();
        }

        public GameState Load() => _state;

        public void ApplyChanges(
            string? location,
            IEnumerable<string>? flagsAdd,
            Dictionary<string, string>? npcDelta,
            string[]? itemsGive    = null,
            string[]? itemsRemove  = null,
            string?   transitionTo = null,
            string?   actingPlayerId = null)
        {
            if (!string.IsNullOrWhiteSpace(location))
            {
                _state.Location = location;
                _state.Scene.LocationName = location;
            }

            if (flagsAdd is not null)
            {
                foreach (var f in flagsAdd)
                {
                    if (string.IsNullOrWhiteSpace(f)) continue;
                    if (!_state.Facts.Contains(f) && !_state.Flags.Contains(f))
                    {
                        if (f.Length <= 32) _state.Flags.Add(f);
                        else _state.Facts.Add(f);
                    }
                }
            }

            if (npcDelta is not null)
            {
                foreach (var kv in npcDelta)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                    _state.Npcs[kv.Key] = kv.Value ?? "";
                    // Mirror into Scene NPC if it exists there
                    var sceneNpc = _state.Scene.Npcs.Find(n =>
                        n.Id.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                        n.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (sceneNpc is not null)
                        sceneNpc.Disposition = kv.Value ?? sceneNpc.Disposition;
                }
            }

            // Move scene items into acting player's inventory
            if (itemsGive is not null && !string.IsNullOrWhiteSpace(actingPlayerId))
            {
                foreach (var itemId in itemsGive)
                {
                    var sceneItem = _state.Scene.Items.Find(i =>
                        i.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                    if (sceneItem is null || sceneItem.IsCollected) continue;

                    sceneItem.IsCollected = true;
                    sceneItem.CollectedBy = actingPlayerId;

                    // Add to player's inventory grid
                    var inv = LoadInventory(actingPlayerId);
                    inv.TryAdd(new InventoryEntry(
                        sceneItem.Id,
                        sceneItem.Name,
                        quantity: 1,
                        iconPath: null,
                        modifiers: new Dictionary<string, int>()));
                    SaveInventory(actingPlayerId, inv);
                }
            }

            // Remove items from scene (consumed/destroyed)
            if (itemsRemove is not null)
            {
                foreach (var itemId in itemsRemove)
                    _state.Scene.Items.RemoveAll(i =>
                        i.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
            }

            // Queue a scene transition (player confirmation required)
            if (!string.IsNullOrWhiteSpace(transitionTo))
                _pendingTransition = transitionTo;

            Save();
        }

        public void AddFacts(IEnumerable<string> facts)
        {
            if (facts is null) return;
            foreach (var f in facts)
            {
                if (string.IsNullOrWhiteSpace(f)) continue;
                if (!_state.Facts.Contains(f)) _state.Facts.Add(f);
            }
            Save();
        }

        public void AddHooks(IEnumerable<string> hooks)
        {
            if (hooks is null) return;
            foreach (var h in hooks)
            {
                if (string.IsNullOrWhiteSpace(h)) continue;
                if (!_state.Hooks.Contains(h)) _state.Hooks.Add(h);
            }
            // Keep at most 20 hooks — oldest fall off first
            while (_state.Hooks.Count > 20)
                _state.Hooks.RemoveAt(0);
            Save();
        }

        public string CompactSlice(int maxFacts = 10, int maxNpcs = 8, int maxFlags = 12)
        {
            var parts = new System.Text.StringBuilder();

            // Location
            var loc = string.IsNullOrWhiteSpace(_state.Scene.LocationName)
                ? _state.Location
                : _state.Scene.LocationName;
            if (!string.IsNullOrWhiteSpace(loc))
                parts.Append($"location={loc}; ");

            // Scene NPCs (rich model preferred over legacy flat dict)
            if (_state.Scene.Npcs.Count > 0)
            {
                var npcSummaries = _state.Scene.Npcs
                    .Take(maxNpcs)
                    .Select(n => n.Tier == "narrative"
                        ? $"{n.Name}[{n.Disposition}|{n.Motivation ?? n.Role ?? ""}]"
                        : $"{n.Name}[{n.Disposition}]");
                parts.Append($"npcs=[{string.Join(',', npcSummaries)}]; ");
            }
            else if (_state.Npcs.Count > 0)
            {
                var npcs = _state.Npcs.Take(maxNpcs).Select(kv => $"{kv.Key}:{kv.Value}");
                parts.Append($"npcs=[{string.Join(',', npcs)}]; ");
            }

            // Scene items
            var availableItems = _state.Scene.Items
                .Where(i => !i.IsCollected)
                .Take(6)
                .Select(i => i.Tier == "narrative"
                    ? $"{i.Name}[narrative,id={i.Id}]"
                    : i.Name);
            var itemList = string.Join(',', availableItems);
            if (!string.IsNullOrWhiteSpace(itemList))
                parts.Append($"items=[{itemList}]; ");

            // Flags
            var flags = _state.Flags.TakeLast(maxFlags);
            parts.Append($"flags=[{string.Join(',', flags)}]; ");

            // Hooks (unresolved threads — top 5 most recent)
            var hooks = _state.Hooks.TakeLast(5).ToList();
            if (hooks.Count > 0)
                parts.Append($"hooks=[{string.Join(';', hooks)}]; ");

            // Facts
            var facts = _state.Facts.TakeLast(maxFacts);
            parts.Append($"facts=[{string.Join(';', facts)}]");

            return parts.ToString().TrimEnd(';', ' ');
        }

        public InventoryGrid LoadInventory(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return new InventoryGrid();
            if (_state.Inventories.TryGetValue(key, out var snapshot))
                return InventoryGrid.FromSnapshot(snapshot);
            return new InventoryGrid();
        }

        public void SaveInventory(string key, InventoryGrid inventory)
        {
            if (string.IsNullOrWhiteSpace(key) || inventory is null) return;
            _state.Inventories[key] = inventory.ToSnapshot();
            Save();
        }

        public void RemoveInventory(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (_state.Inventories.Remove(key))
                Save();
        }

        public void TransitionScene(string destinationName)
        {
            if (string.IsNullOrWhiteSpace(destinationName)) return;

            // Archive current scene
            var currentName = _state.Scene.LocationName;
            if (!string.IsNullOrWhiteSpace(currentName))
                _state.ArchivedScenes[currentName] = _state.Scene;

            // Load archived scene if it exists, otherwise create a blank one
            if (_state.ArchivedScenes.TryGetValue(destinationName, out var archived))
            {
                _state.Scene = archived;
                _state.ArchivedScenes.Remove(destinationName);
            }
            else
            {
                _state.Scene = new WorldScene { LocationName = destinationName };
            }

            _state.Location = _state.Scene.LocationName;
            _pendingTransition = null;
            Save();
        }

        public void ClearPendingTransition()
        {
            _pendingTransition = null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static GameState CreateEmpty() => new GameState { Location = "" };

        private void EnsureCollectionsExist()
        {
            _ = _state.Facts;
            _ = _state.Hooks;
            _ = _state.Flags;
            _ = _state.Npcs;
            _ = _state.Inventories;
            _ = _state.ArchivedScenes;
            _state.Location  ??= "";
            _state.Scene     ??= new WorldScene();
            _state.TurnState ??= new TurnState();
        }

        private static GameState? TryLoadFromDisk(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<GameState>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch { return null; }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { /* Non-fatal */ }
        }
    }
}

