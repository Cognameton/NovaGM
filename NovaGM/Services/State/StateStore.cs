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
    /// JSON-on-disk + in-memory state store. Works with read-only collection
    /// properties on GameState (Facts/Flags/Npcs are get-only).
    /// </summary>
    public sealed class StateStore : IStateStore
    {
        private readonly GameState _state;
        private readonly string _filePath;

        private static string AppDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaGM");

        public StateStore()
        {
            Directory.CreateDirectory(AppDataDir);
            _filePath = Path.Combine(AppDataDir, "state.json");
            _state = TryLoadFromDisk(_filePath) ?? CreateEmpty();
            EnsureCollectionsExist();
            Save(); // ensure file exists for debugging/inspection
        }

        public GameState Load() => _state;

        public void ApplyChanges(string? location, IEnumerable<string>? flagsAdd, Dictionary<string, string>? npcDelta)
        {
            if (!string.IsNullOrWhiteSpace(location))
                _state.Location = location;

            if (flagsAdd is not null)
            {
                foreach (var f in flagsAdd)
                {
                    if (string.IsNullOrWhiteSpace(f)) continue;
                    if (!_state.Facts.Contains(f) && !_state.Flags.Contains(f))
                    {
                        // Heuristic: route short strings to flags, longer to facts.
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
                }
            }

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

        public string CompactSlice(int maxFacts = 10, int maxNpcs = 8, int maxFlags = 12)
        {
            var loc   = string.IsNullOrWhiteSpace(_state.Location) ? "" : $"location={_state.Location}; ";
            var flags = _state.Flags.TakeLast(maxFlags);
            var facts = _state.Facts.TakeLast(maxFacts);
            var npcs  = _state.Npcs.Take(maxNpcs).Select(kv => $"{kv.Key}:{kv.Value}");

            return $"{loc}flags=[{string.Join(',', flags)}]; npcs=[{string.Join(',', npcs)}]; facts=[{string.Join(';', facts)}]";
        }

        public InventoryGrid LoadInventory(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return new InventoryGrid();
            if (_state.Inventories.TryGetValue(key, out var snapshot))
            {
                return InventoryGrid.FromSnapshot(snapshot);
            }
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
            {
                Save();
            }
        }

        // ---------- helpers ----------

        private static GameState CreateEmpty() => new GameState
        {
            // With get-only collections on GameState, only set Location here.
            Location = ""
        };

        private void EnsureCollectionsExist()
        {
            // Do NOT reassign read-only props; just ensure non-null by touching them.
            _ = _state.Facts;
            _ = _state.Flags;
            _ = _state.Npcs;
            _ = _state.Inventories;
            _state.Location ??= "";
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
            catch
            {
                return null;
            }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Non-fatal; keep running even if disk write fails.
            }
        }
    }
}
