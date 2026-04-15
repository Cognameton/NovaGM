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
    ///   - Enforce a 60-second idle timer per player; auto-pass on expiry.
    ///   - Fire a GM-initiative turn after all players have acted or passed in a round.
    ///   - After two consecutive fully-idle rounds, halt the game and raise ContinueRequired.
    ///   - Raise InterruptEvent when the GM injects an unexpected narrative event.
    ///   - Auto-save (via IStateStore) when the app is closed.
    /// </summary>
    public sealed class TurnEngine : IDisposable
    {
        public const int MaxActivePlayers = 6;
        public const int IdleTimeoutSeconds = 60;

        // ── Events ────────────────────────────────────────────────────────────

        /// Raised when a player's turn begins. Argument is the player id.
        public event Action<string>? TurnStarted;

        /// Raised when a player's turn ends (acted, passed, or timed out). Arg: player id.
        public event Action<string>? TurnEnded;

        /// Raised when all players have acted/passed and the GM should advance the world.
        public event Func<Task>? GmTurnRequired;

        /// Raised when the GM injects an interrupt event. Arg: reason description.
        public event Func<string, Task>? InterruptEventFired;

        /// Raised when two consecutive idle rounds occur — game halts, awaiting CONTINUE.
        public event Action? ContinueRequired;

        /// Raised when the game is unhalted after a CONTINUE input.
        public event Action? GameResumed;

        // ── State ─────────────────────────────────────────────────────────────

        private readonly IStateStore _store;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private CancellationTokenSource? _idleCts;

        private List<string> _activePlayers = new();
        private readonly HashSet<string> _acted  = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _passed = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _incapacitated = new(StringComparer.OrdinalIgnoreCase);

        private int  _currentIndex = 0;
        private int  _roundNumber  = 0;
        private int  _consecutiveIdleRounds = 0;
        private bool _isHalted = false;
        private bool _gmTurnInProgress = false;

        public string? CurrentPlayerId =>
            (_activePlayers.Count > 0 && _currentIndex < _activePlayers.Count)
            ? _activePlayers[_currentIndex]
            : null;

        public bool IsHalted    => _isHalted;
        public int  RoundNumber => _roundNumber;
        public IReadOnlyList<string> ActivePlayers => _activePlayers;

        public TurnEngine(IStateStore store)
        {
            _store = store;
            // Restore state from disk if available
            var ts = store.Load().TurnState;
            _activePlayers        = new List<string>(ts.ActivePlayerIds);
            _consecutiveIdleRounds = ts.ConsecutiveIdleRounds;
            _isHalted             = ts.IsHalted;
            _roundNumber          = ts.RoundNumber;
            foreach (var p in ts.Incapacitated) _incapacitated.Add(p);
        }

        // ── Player management ─────────────────────────────────────────────────

        /// <summary>
        /// Register a player as active. Returns false if the cap is reached.
        /// </summary>
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

        /// <summary>Remove a player from the active roster (disconnect / kick).</summary>
        public void RemovePlayer(string playerId)
        {
            var key = Normalize(playerId);
            var idx = _activePlayers.FindIndex(p => p.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;

            _activePlayers.RemoveAt(idx);
            _acted.Remove(key);
            _passed.Remove(key);
            _incapacitated.Remove(key);

            // Keep current index in bounds
            if (_currentIndex >= _activePlayers.Count)
                _currentIndex = 0;

            PersistTurnState();
        }

        /// <summary>Mark a player as incapacitated — their turns are auto-passed until revived.</summary>
        public void Incapacitate(string playerId)
        {
            _incapacitated.Add(Normalize(playerId));
            PersistTurnState();
        }

        /// <summary>Revive a player from incapacitation.</summary>
        public void Revive(string playerId)
        {
            _incapacitated.Remove(Normalize(playerId));
            PersistTurnState();
        }

        // ── Turn flow ─────────────────────────────────────────────────────────

        /// <summary>
        /// Begin the turn cycle. Call once after all initial players are registered.
        /// </summary>
        public Task StartAsync(CancellationToken ct = default)
        {
            if (_activePlayers.Count == 0) return Task.CompletedTask;
            if (_isHalted) return Task.CompletedTask;
            BeginCurrentPlayerTurn(ct);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Record that the current player has taken their turn with the given input text.
        /// Advances to the next player (or GM turn if round is complete).
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
                CancelIdleTimer();
                _acted.Add(key);
                TurnEnded?.Invoke(key);
                await AdvanceAsync(ct);
            }
            finally { _lock.Release(); }

            return true;
        }

        /// <summary>
        /// Force-pass the current player's turn (incapacitation, explicit pass, or idle timeout).
        /// </summary>
        public async Task PassCurrentPlayerAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var key = CurrentPlayerId;
                if (key is null) return;

                CancelIdleTimer();
                _passed.Add(key);
                TurnEnded?.Invoke(key);
                await AdvanceAsync(ct);
            }
            finally { _lock.Release(); }
        }

        /// <summary>
        /// Inject a GM interrupt event mid-round (ambush, distress call, unexpected encounter).
        /// Pauses the turn cycle, fires the event, then resumes.
        /// </summary>
        public async Task FireInterruptEventAsync(string reason, CancellationToken ct = default)
        {
            CancelIdleTimer();
            if (InterruptEventFired is not null)
                await InterruptEventFired.Invoke(reason);
            // Resume the current player's turn
            BeginCurrentPlayerTurn(ct);
        }

        /// <summary>
        /// Called when a halted game receives a CONTINUE input.
        /// Resets idle tracking and resumes the turn cycle.
        /// </summary>
        public async Task ContinueAsync(CancellationToken ct = default)
        {
            _isHalted = false;
            _consecutiveIdleRounds = 0;
            PersistTurnState();
            GameResumed?.Invoke();
            await StartAsync(ct);
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private async Task AdvanceAsync(CancellationToken ct)
        {
            if (_activePlayers.Count == 0) return;

            // Move to next player
            _currentIndex = (_currentIndex + 1) % _activePlayers.Count;

            // If we've looped back to the start, the round is complete
            if (_currentIndex == 0)
                await EndRoundAsync(ct);
            else
                BeginCurrentPlayerTurn(ct);
        }

        private async Task EndRoundAsync(CancellationToken ct)
        {
            _roundNumber++;

            // Check if the entire round was idle
            var allIdle = _activePlayers.All(p =>
                _passed.Contains(p) &&
                !_acted.Contains(p));

            if (allIdle)
            {
                _consecutiveIdleRounds++;
                if (_consecutiveIdleRounds >= 2)
                {
                    _isHalted = true;
                    PersistTurnState();
                    ContinueRequired?.Invoke();
                    return;
                }
            }
            else
            {
                _consecutiveIdleRounds = 0;
            }

            // Clear round tracking
            _acted.Clear();
            _passed.Clear();

            PersistTurnState();

            // GM takes a turn to advance the world
            if (!_gmTurnInProgress && GmTurnRequired is not null)
            {
                _gmTurnInProgress = true;
                try   { await GmTurnRequired.Invoke(); }
                finally { _gmTurnInProgress = false; }
            }

            // Begin next round
            BeginCurrentPlayerTurn(ct);
        }

        private void BeginCurrentPlayerTurn(CancellationToken ct)
        {
            if (_isHalted || _activePlayers.Count == 0) return;

            var current = CurrentPlayerId;
            if (current is null) return;

            // Auto-pass incapacitated players without waiting
            if (_incapacitated.Contains(current))
            {
                _ = PassCurrentPlayerAsync(ct);
                return;
            }

            TurnStarted?.Invoke(current);
            StartIdleTimer(ct);
        }

        private void StartIdleTimer(CancellationToken outerCt)
        {
            CancelIdleTimer();
            _idleCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            var token = _idleCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(IdleTimeoutSeconds), token);
                    if (!token.IsCancellationRequested)
                        await PassCurrentPlayerAsync(outerCt);
                }
                catch (OperationCanceledException) { /* Normal cancellation */ }
            }, token);
        }

        private void CancelIdleTimer()
        {
            try { _idleCts?.Cancel(); } catch { }
            _idleCts = null;
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
            ts.RoundNumber            = _roundNumber;
            ts.ConsecutiveIdleRounds  = _consecutiveIdleRounds;
            ts.IsHalted               = _isHalted;
        }

        private static string Normalize(string id) => (id ?? "").Trim().ToUpperInvariant();

        public void Dispose()
        {
            CancelIdleTimer();
            _lock.Dispose();
        }
    }
}
