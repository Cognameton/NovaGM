// File: Services/Multiplayer/GameCoordinator.cs
using System;
using System.Collections.Generic; // IAsyncEnumerable
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NovaGM.Services.Multiplayer
{
    public sealed class GameCoordinator
    {
        // Singleton used by UI + LocalServer
        public static GameCoordinator Instance { get; } = new GameCoordinator();

        private readonly Channel<PlayerInput> _inputs;
        private readonly object _lock = new();
        private string _currentCode;
        private readonly CancellationTokenSource _cts = new();

        public string CurrentCode
        {
            get { lock (_lock) return _currentCode; }
        }

        private GameCoordinator()
        {
            _inputs = Channel.CreateUnbounded<PlayerInput>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

            _currentCode = GenerateCode();
        }

        /// <summary>
        /// Try to queue a player's input into the turn stream, validating the room code.
        /// </summary>
        public bool TryEnqueue(string code, string player, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            lock (_lock)
            {
                if (!string.Equals(code, _currentCode, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return _inputs.Writer.TryWrite(new PlayerInput(player ?? "Player", text));
        }

        /// <summary>
        /// Async stream of player inputs for the GM loop.
        /// </summary>
        public async IAsyncEnumerable<PlayerInput> ReadInputsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            var reader = _inputs.Reader.ReadAllAsync(linked.Token);
            await foreach (var item in reader)
                yield return item;
        }

        /// <summary>
        /// Regenerates a room code and clears the input queue (use when starting a new game).
        /// </summary>
        public void ResetRoom()
        {
            lock (_lock)
                _currentCode = GenerateCode();

            // Finish current stream and create a new channel for a new session.
            _inputs.Writer.TryComplete();
        }

        /// <summary>Graceful stop of the input stream (lets readers unwind).</summary>
        public void Complete() => _inputs.Writer.TryComplete();

        /// <summary>Immediate cancellation (used during hard shutdown).</summary>
        public void Cancel() => _cts.Cancel();

        private static string GenerateCode(int len = 4)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/1/0
            Span<byte> bytes = stackalloc byte[len];
            RandomNumberGenerator.Fill(bytes);
            char[] chars = new char[len];
            for (int i = 0; i < len; i++)
                chars[i] = alphabet[bytes[i] % alphabet.Length];
            return new string(chars);
        }
    }

    public readonly struct PlayerInput
    {
        public string Player { get; }
        public string Text { get; }

        // Alias to satisfy older code that looked for .Name
        public string Name => Player;

        public PlayerInput(string player, string text)
        {
            Player = player ?? "Player";
            Text = text ?? "";
        }

        public override string ToString() => $"{Player}: {Text}";
    }
}
