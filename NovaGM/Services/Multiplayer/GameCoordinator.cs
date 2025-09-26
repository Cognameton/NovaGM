// NovaGM/Services/Multiplayer/GameCoordinator.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NovaGM.Services.Multiplayer
{
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
        public int STR { get; set; }
        public int DEX { get; set; }
        public int CON { get; set; }
        public int INT { get; set; }
        public int WIS { get; set; }
        public int CHA { get; set; }
    }

    /// Coordinates join code, inputs from LAN, and player character data.
    public sealed class GameCoordinator
    {
        private readonly ConcurrentQueue<PlayerInput> _queue = new();
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, PlayerCharacter> _players = new();

        public string CurrentCode { get; private set; }

        public GameCoordinator()
        {
            CurrentCode = GenerateCode();
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
            _players[key] = pc;
        }

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
            var r = Random.Shared;
            return $"{(char)('A' + r.Next(26))}{(char)('A' + r.Next(26))}{r.Next(0, 10)}{r.Next(0, 10)}";
        }

        private static string NormalizeKey(string name) => (name ?? "").Trim().ToUpperInvariant();
    }
}
