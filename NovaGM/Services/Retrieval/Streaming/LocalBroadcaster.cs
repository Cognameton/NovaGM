using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace NovaGM.Services.Streaming
{
    /// Very small pub/sub for narration tokens.
    /// Each subscriber gets its own channel; Publish fan-outs to all.
    public sealed class LocalBroadcaster
    {
        private static readonly Lazy<LocalBroadcaster> _lazy = new(() => new LocalBroadcaster());
        public static LocalBroadcaster Instance => _lazy.Value;

        private readonly ConcurrentDictionary<Guid, Channel<string>> _subs = new();

        private LocalBroadcaster() { }

        /// Publish a chunk to all subscribers (best-effort).
        public void Publish(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return;

            foreach (var kv in _subs)
                kv.Value.Writer.TryWrite(chunk); // best-effort
        }

        /// Publish a structured typed event. Encoded as "§{type}§{name}§{jsonData}" so
        /// the SSE endpoint can emit a named event instead of a raw data line.
        /// <paramref name="name"/> identifies the speaker (player name or empty for GM events).
        /// <paramref name="data"/> is the text payload (will be JSON-escaped server-side).
        public void PublishEvent(string type, string name, string data)
            => Publish($"§{type}§{name}§{data}");

        /// Subscribe to a stream of chunks. Disposes automatically on cancellation.
        public async IAsyncEnumerable<string> Subscribe([EnumeratorCancellation] CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var ch = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _subs.TryAdd(id, ch);

            try
            {
                await foreach (string s in ch.Reader.ReadAllAsync(ct))
                    yield return s;
            }
            finally
            {
                _subs.TryRemove(id, out var removed);
                removed?.Writer.TryComplete();
            }
        }

        /// Completes all subscriber channels so SSE loops exit.
        public void Complete()
        {
            foreach (var kv in _subs)
            {
                try { kv.Value.Writer.TryComplete(); } catch { }
            }
            _subs.Clear();
        }
    }
}
