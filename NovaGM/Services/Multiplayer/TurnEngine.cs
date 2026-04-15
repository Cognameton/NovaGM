// NovaGM/Services/Multiplayer/TurnEngine.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NovaGM.Models;
using NovaGM.Services.State;

namespace NovaGM.Services.Multiplayer
{
    /// <summary>
    /// Manages the multi-player turn cycle.
    ///
    /// Responsibilities:
    ///   - Track active players (max 6) and whose turn it is.
    ///   - Advance to the next player after each action or pass.
    ///   - Fire a GM-initiative turn after all players have acted or passed in a round.
    ///   - Raise InterruptEvent when the GM injects an unexpected narrative beat.
    /// </summary>
    public sealed class TurnEngine : IDisposable
    {
        public const int MaxActivePlayers = 6;

        // ── Events ────────────────────────────────────────────────────────────

        /// Raised when a player's turn begins. Argument is the player id.
        public event Action<string>? TurnStarted;

        /// Raised when a player's turn ends (acted or passed). Arg: player id.
        public event Action<string>? TurnEnded;

        /// Raised when all players have acted/passed and the GM should advance the world.
        public event Func<Task>? GmTurnRequired;

        /// Raised when the GM injects an interrupt event. Arg: reason description.
        public event Func<string, Task>? InterruptEventFired;

        // ── State ─────────────────────────────────────────────────────────────

        private readonly IStateStore _store;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private List<string> _activePlayers = new();
        private readonly HashSet<string> _acted  = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _passed = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _incapacitated = new(StringComparer.OrdinalIgnoreCase);

        private int  _currentIndex   = 0;
        private int  _roundNumber    = 0;
        private bool _gmTurnInProgress = false;

        public string? CurrentPlayerId =>
            (_activePlayers.Count > 0 && _currentIndex < _activePlayers.Count)
            ? _activePlayers[_currentIndex]
            : null;

        public int RoundNumber => _roundNumber;
        public IReadOnlyList<string> ActivePlayers => _activePlayers;

        public TurnEngine(IStateStore store)
        {
            _store = store;
            var ts = store.Load().TurnState;
            _activePlayers = new List<string>(ts.ActivePlayerIds);
            _roundNumber   = ts.RoundNumber;
            foreach (var p in ts.Incapacitated) _incapacitated.Add(p);
        }

        // ── Player management ─────────────────────────────────────────────────

        /// <summary>Register a player as active. Returns false if the cap is reached.</summary>
        public bool AddPlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return false;
            var key = Normalize(playerId);
            if (_activePlayers.Contains(key, StringComparer.OrdinalIgnoreCase)) return true;
            if (_activePlayers.Count >= MaxActivePlayers) return false;
            _activePlayers.Add(key);
            PersistTurnState();
            return true;
        }

        /// <summary>Remove a player from the active roster.</summary>
        public void RemovePlayer(string playerId)
        {
            var key = Normalize(playerId);
            _lock.Wait();
            try
            {
                var idx = _activePlayers.FindIndex(p => p.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) return;
                _activePlayers.RemoveAt(idx);
                _acted.Remove(key);
                _passed.Remove(key);
                _incapacitated.Remove(key);
                if (_currentIndex >= _activePlayers.Count)
                    _currentIndex = 0;
                PersistTurnState();
            }
            finally { _lock.Release(); }
        }

        /// <summary>Mark a player as incapacitated — their turns are auto-passed until revived.</summary>
        public void Incapacitate(string playerId)
        {
            _lock.Wait();
            try
            {
                _incapacitated.Add(Normalize(playerId));
                PersistTurnState();
            }
            finally { _lock.Release(); }
        }

        /// <summary>Revive a player from incapacitation.</summary>
        public void Revive(string playerId)
        {
            _lock.Wait();
            try
            {
                _incapacitated.Remove(Normalize(playerId));
                PersistTurnState();
            }
            finally { _lock.Release(); }
        }

        // ── Turn flow ─────────────────────────────────────────────────────────

        /// <summary>Begin the turn cycle. Call once after all initial players are registered.</summary>
        public Task StartAsync(CancellationToken ct = default)
        {
            if (_activePlayers.Count == 0) return Task.CompletedTask;
            BeginCurrentPlayerTurn();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Record that a player has taken their turn.
        /// Advances to the next player (or fires a GM turn if the round is complete).
        /// Returns false if it is not this player's turn.
        /// </summary>
        public async Task<bool> RecordActionAsync(string playerId, CancellationToken ct = default)
        {
            var key = Normalize(playerId);
            if (!key.Equals(Normalize(CurrentPlayerId ?? ""), StringComparison.OrdinalIgnoreCase))
                return false;

            await _lock.WaitAsync(ct);
            try
            {
                _acted.Add(key);
                TurnEnded?.Invoke(key);
                await AdvanceAsync(ct);
            }
            finally { _lock.Release(); }

            return true;
        }

        /// <summary>Force-pass the current player's turn (incapacitation or explicit pass).</summary>
        public async Task PassCurrentPlayerAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var key = CurrentPlayerId;
                if (key is null) return;
                _passed.Add(key);
                TurnEnded?.Invoke(key);
                await AdvanceAsync(ct);
            }
            finally { _lock.Release(); }
        }

        /// <summary>
        /// Inject a GM interrupt event mid-round (ambush, distress call, unexpected encounter).
        /// </summary>
        public async Task FireInterruptEventAsync(string reason, CancellationToken ct = default)
        {
            if (InterruptEventFired is not null)
                await InterruptEventFired.Invoke(reason);
            BeginCurrentPlayerTurn();
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private async Task AdvanceAsync(CancellationToken ct)
        {
            if (_activePlayers.Count == 0) return;

            _currentIndex = (_currentIndex + 1) % _activePlayers.Count;

            if (_currentIndex == 0)
                await EndRoundAsync(ct);
            else
                BeginCurrentPlayerTurn();
        }

        private async Task EndRoundAsync(CancellationToken ct)
        {
            _roundNumber++;
            _acted.Clear();
            _passed.Clear();
            PersistTurnState();

            // GM advances the world after every complete round
            if (!_gmTurnInProgress && GmTurnRequired is not null)
            {
                _gmTurnInProgress = true;
                try
                {
                    await GmTurnRequired.Invoke();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NovaGM] GM turn error: {ex.Message}");
                }
                finally
                {
                    _gmTurnInProgress = false;
                }
            }

            BeginCurrentPlayerTurn();
        }

        private void BeginCurrentPlayerTurn()
        {
            if (_activePlayers.Count == 0) return;
            var current = CurrentPlayerId;
            if (current is null) return;

            // Auto-pass incapacitated players — fire-and-forget, but log any failure
            if (_incapacitated.Contains(current))
            {
                _ = PassCurrentPlayerAsync().ContinueWith(
                    t => Console.Error.WriteLine($"[TurnEngine] Auto-pass failed: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
                return;
            }

            TurnStarted?.Invoke(current);
        }

        private void PersistTurnState()
        {
            var ts = _store.Load().TurnState;
            ts.ActivePlayerIds.Clear();
            ts.ActivePlayerIds.AddRange(_activePlayers);
            ts.ActedThisRound.Clear();
            foreach (var p in _acted)  ts.ActedThisRound.Add(p);
            ts.PassedThisRound.Clear();
            foreach (var p in _passed) ts.PassedThisRound.Add(p);
            ts.Incapacitated.Clear();
            foreach (var p in _incapacitated) ts.Incapacitated.Add(p);
            ts.RoundNumber = _roundNumber;
        }

        private static string Normalize(string id) => (id ?? "").Trim().ToUpperInvariant();

        public void Dispose() => _lock.Dispose();
    }
}
