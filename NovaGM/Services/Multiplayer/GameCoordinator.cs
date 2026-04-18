// NovaGM/Services/Multiplayer/GameCoordinator.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using NovaGM.Models;
using NovaGM.Services.State;

namespace NovaGM.Services.Multiplayer
{
    public sealed class SessionSettings
    {
        public string GenreContext { get; set; } = string.Empty;
    }

    public sealed class PlayerInput
    {
        public string Player { get; }
        public string Text { get; }
        public PlayerInput(string player, string text) { Player = player; Text = text; }
        public override string ToString() => $"{Player}: {Text}";
    }

    public sealed class PlayerCharacter
    {
        public string Name { get; set; } = "";
        public string Race { get; set; } = "";
        public string Class { get; set; } = "";
        public int? Level { get; set; } = 1;
        public int STR { get; set; }
        public int DEX { get; set; }
        public int CON { get; set; }
        public int INT { get; set; }
        public int WIS { get; set; }
        public int CHA { get; set; }
        public Dictionary<EquipmentSlot, Item> Equipment { get; set; } = new();
        public InventoryGrid Inventory { get; set; } = new();
    }

    /// Coordinates join code, inputs from LAN, and player character data.
    public sealed class GameCoordinator
    {
        private static readonly Lazy<GameCoordinator> _instance = new(() => new GameCoordinator());
        public static GameCoordinator Instance => _instance.Value;

        private readonly ConcurrentQueue<PlayerInput> _queue = new();
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, PlayerCharacter> _players = new();

        /// <summary>
        /// Tracks players who have completed character creation and are fully joined.
        /// A player is added here when they save their character — NOT on first message.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _joinedPlayers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Fired after a player's character is saved. Provides the normalized player id
        /// and the character. Subscribers (e.g. MainWindowViewModel) use this to persist
        /// the snapshot to disk so it survives session restarts.
        /// </summary>
        public event Action<string, PlayerCharacter>? CharacterSaved;

        public string CurrentCode { get; private set; }
        public SessionSettings Session { get; } = new SessionSettings();

        private GameCoordinator()
        {
            CurrentCode = GenerateCode();
            // Clear the cached genre context whenever the GM changes genre so
            // BuildGenreContext() re-derives it from GenreManager.Current.
            GenreManager.GenreChanged += _ => Session.GenreContext = string.Empty;
        }

        public void ResetRoom() => CurrentCode = GenerateCode();

        public bool TryEnqueue(string code, string name, string text)
        {
            if (!string.Equals(code, CurrentCode, StringComparison.OrdinalIgnoreCase)) return false;
            var player = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
            _queue.Enqueue(new PlayerInput(player, text));
            _signal.Release();
            return true;
        }

        public void SetCharacter(string code, string name, PlayerCharacter pc)
        {
            if (!string.Equals(code, CurrentCode, StringComparison.OrdinalIgnoreCase)) return;
            var key = NormalizeKey(name);
            if (_players.TryGetValue(key, out var existing) && existing.Inventory is not null)
            {
                pc.Inventory = existing.Inventory;
            }
            _players[key] = pc;

            // Mark the player as fully joined now that their character is saved.
            // This is the authoritative join moment — not when they send their first message.
            _joinedPlayers[key] = true;

            // Notify subscribers (e.g. ViewModel) so they can persist the snapshot to disk.
            CharacterSaved?.Invoke(key, pc);
        }

        /// <summary>Returns true if the player has completed character creation and is fully joined.</summary>
        public bool IsJoined(string name) =>
            _joinedPlayers.ContainsKey(NormalizeKey(name));

        /// <summary>Returns the names of all fully-joined players (character saved).</summary>
        public string[] GetJoinedPlayers() =>
            _joinedPlayers.Keys
                .Where(k => _players.ContainsKey(k))
                .ToArray();

        public bool TryGetCharacter(string code, string name, out PlayerCharacter pc)
        {
            pc = new PlayerCharacter();
            if (!string.Equals(code, CurrentCode, StringComparison.OrdinalIgnoreCase)) return false;
            return _players.TryGetValue(NormalizeKey(name), out pc!);
        }

        public async IAsyncEnumerable<PlayerInput> ReadInputsAsync([EnumeratorCancellation] CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            var token = linked.Token;
            while (!token.IsCancellationRequested)
            {
                while (_queue.TryDequeue(out var v))
                {
                    yield return v;
                    if (token.IsCancellationRequested) yield break;
                }
                try { await _signal.WaitAsync(token).ConfigureAwait(false); }
                catch (OperationCanceledException) { yield break; }
            }
        }

        public void Cancel()
        {
            try { _cts.Cancel(); } catch { }
            try { _signal.Release(); } catch { }
        }

        private static string GenerateCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var r = Random.Shared;
            return new string(new[] {
                chars[r.Next(chars.Length)],
                chars[r.Next(chars.Length)],
                chars[r.Next(chars.Length)],
                chars[r.Next(chars.Length)],
                chars[r.Next(chars.Length)],
                chars[r.Next(chars.Length)]
            });
        }

        private static string NormalizeKey(string name) => (name ?? "").Trim().ToUpperInvariant();

        // Additional methods for player management
        public string[] GetConnectedPlayers()
        {
            return _players.Keys.ToArray();
        }

        public PlayerCharacter? GetPlayerCharacter(string playerName)
        {
            var key = NormalizeKey(playerName);
            return _players.TryGetValue(key, out var character) ? character : null;
        }

        public bool KickPlayer(string playerName)
        {
            var key = NormalizeKey(playerName);
            _joinedPlayers.TryRemove(key, out _);
            return _players.TryRemove(key, out _);
        }

        public int GetConnectedPlayerCount()
        {
            return _players.Count;
        }

        public void SetGenreContext(string genre)
        {
            Session.GenreContext = genre?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Clears all in-memory player data. Call on New Game so no ghost characters
        /// from the previous session linger in the coordinator.
        /// </summary>
        public void ResetPlayers()
        {
            _players.Clear();
            _joinedPlayers.Clear();
        }

        /// <summary>
        /// Re-hydrates player character data from persisted snapshots so that
        /// <see cref="GetPlayerCharacter"/> returns useful data for previously-known
        /// players even before they reconnect this session.
        /// Loaded players are NOT marked as joined — they won't receive turns until
        /// they reconnect and call <see cref="SetCharacter"/>.
        /// </summary>
        public void LoadKnownPlayers(IStateStore store)
        {
            foreach (var id in store.GetKnownPlayerIds())
            {
                var snap = store.LoadPlayerCharacter(id);
                if (snap is null) continue;
                var key = NormalizeKey(id);
                // Only restore if not already connected this session
                if (!_players.ContainsKey(key))
                {
                    _players[key] = new PlayerCharacter
                    {
                        Name  = snap.Name,
                        Race  = snap.Race,
                        Class = snap.Class,
                        Level = snap.Level,
                        STR   = snap.STR,
                        DEX   = snap.DEX,
                        CON   = snap.CON,
                        INT   = snap.INT,
                        WIS   = snap.WIS,
                        CHA   = snap.CHA
                    };
                }
            }
        }
    }
}
