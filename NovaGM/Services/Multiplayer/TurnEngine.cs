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
    ///
    /// Thread-safety contract:
    ///   _lock guards all mutations of _activePlayers, _acted, _passed, _incapacitated,
    ///   _currentIndex, _roundNumber, and _gmTurnInProgress.  External calls (GM turn,
    ///   TurnStarted/TurnEnded events, PassCurrentPlayerAsync fire-and-forget) always
    ///   happen AFTER the lock is released to prevent deadlocks.
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

        private int  _currentIndex    = 0;
        private int  _roundNumber     = 0;
        private bool _gmTurnInProgress = false;

        public string? CurrentPlayerId =>
            (_activePlayers.Count > 0 && _currentIndex < _activePlayers.Count)
            ? _activePlayers[_currentIndex]
            : null;

        public int RoundNumber => _roundNumber;

        /// <summary>
        /// Returns a live reference to the active-player list.
        /// Use only from code that is already holding _lock, or where a
        /// slightly-stale read is acceptable (e.g. logging, UI binding).
        /// For concurrent callers use <see cref="IsPlayerActive"/> or
        /// <see cref="ActivePlayerCount"/> instead.
        /// </summary>
        public IReadOnlyList<string> ActivePlayers => _activePlayers;

        /// <summary>Thread-safe check for whether a player is on the active roster.</summary>
        public bool IsPlayerActive(string playerId)
        {
            var key = Normalize(playerId);
            _lock.Wait();
            try { return _activePlayers.Contains(key); }
            finally { _lock.Release(); }
        }

        /// <summary>Thread-safe count of active players.</summary>
        public int ActivePlayerCount
        {
            get
            {
                _lock.Wait();
                try { return _activePlayers.Count; }
                finally { _lock.Release(); }
            }
        }

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
            _lock.Wait();
            try
            {
                if (_activePlayers.Contains(key)) return true;
                if (_activePlayers.Count >= MaxActivePlayers) return false;
                _activePlayers.Add(key);
                PersistTurnState();
                return true;
            }
            finally { _lock.Release(); }
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

            // Acquire the lock BEFORE reading CurrentPlayerId to avoid a TOCTOU race:
            // another thread could advance the turn between our check and our state mutation.
            await _lock.WaitAsync(ct);
            try
            {
                if (!key.Equals(Normalize(CurrentPlayerId ?? ""), StringComparison.OrdinalIgnoreCase))
                    return false;
                _acted.Add(key);
            }
            finally { _lock.Release(); }

            // Events and advance run outside the lock to prevent deadlock
            // (AdvanceAsync → RunGmTurnAsync → BeginCurrentPlayerTurn → PassCurrentPlayerAsync → _lock).
            TurnEnded?.Invoke(key);
            await AdvanceAsync(ct);
            return true;
        }

        /// <summary>Force-pass the current player's turn (incapacitation or explicit pass).</summary>
        public async Task PassCurrentPlayerAsync(CancellationToken ct = default)
        {
            string? key;
            await _lock.WaitAsync(ct);
            try
            {
                key = CurrentPlayerId;
                if (key is null) return;
                _passed.Add(key);
            }
            finally { _lock.Release(); }

            TurnEnded?.Invoke(key);
            await AdvanceAsync(ct);
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

        // Called WITHOUT holding _lock. Acquires the lock only for state mutations,
        // then releases before any external calls.
        private async Task AdvanceAsync(CancellationToken ct)
        {
            bool endOfRound = false;
            await _lock.WaitAsync(ct);
            try
            {
                if (_activePlayers.Count == 0) return;
                _currentIndex = (_currentIndex + 1) % _activePlayers.Count;
                endOfRound = _currentIndex == 0;
                if (endOfRound)
                {
                    _roundNumber++;
                    _acted.Clear();
                    _passed.Clear();
                    PersistTurnState();
                }
            }
            finally { _lock.Release(); }

            if (endOfRound)
                await RunGmTurnAsync(ct);

            BeginCurrentPlayerTurn();
        }

        // Runs the GM turn outside the lock to allow concurrent RemovePlayer/Incapacitate
        // calls and to prevent PassCurrentPlayerAsync from deadlocking on re-entry.
        private async Task RunGmTurnAsync(CancellationToken ct)
        {
            // Check-and-set under the lock so only one GM turn runs at a time.
            await _lock.WaitAsync(ct);
            bool shouldRun = !_gmTurnInProgress && GmTurnRequired is not null;
            if (shouldRun) _gmTurnInProgress = true;
            _lock.Release();

            if (!shouldRun) return;

            try
            {
                await GmTurnRequired!.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NovaGM] GM turn error: {ex.Message}");
            }
            finally
            {
                // Use synchronous Wait here so cancellation cannot prevent cleanup.
                _lock.Wait();
                _gmTurnInProgress = false;
                _lock.Release();
            }
        }

        // Reads the state snapshot under the lock, then acts on it without holding it.
        // This prevents BeginCurrentPlayerTurn from deadlocking when it kicks off
        // PassCurrentPlayerAsync as fire-and-forget for incapacitated players.
        private void BeginCurrentPlayerTurn()
        {
            string? current = null;
            bool isIncapacitated = false;

            _lock.Wait();
            try
            {
                if (_activePlayers.Count == 0) return;
                current = CurrentPlayerId;
                if (current is null) return;
                isIncapacitated = _incapacitated.Contains(current);
            }
            finally { _lock.Release(); }

            if (isIncapacitated)
            {
                _ = PassCurrentPlayerAsync().ContinueWith(
                    t => Console.Error.WriteLine($"[TurnEngine] Auto-pass failed: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
                return;
            }

            TurnStarted?.Invoke(current!);
        }

        private void PersistTurnState()
        {
            var ts = _store.Load().TurnState;
            ts.ActivePlayerIds.Clear();  ts.ActivePlayerIds.AddRange(_activePlayers);
            ts.ActedThisRound.Clear();   ts.ActedThisRound.UnionWith(_acted);
            ts.PassedThisRound.Clear();  ts.PassedThisRound.UnionWith(_passed);
            ts.Incapacitated.Clear();    ts.Incapacitated.UnionWith(_incapacitated);
            ts.RoundNumber = _roundNumber;
            _store.Save();
        }

        private static string Normalize(string id) => (id ?? "").Trim().ToUpperInvariant();

        public void Dispose() => _lock.Dispose();
    }
}
