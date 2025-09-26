// NovaGM/Services/ShutdownUtil.cs
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NovaGM.Services
{
    /// Centralized, deterministic shutdown for the whole app.
    public static class ShutdownUtil
    {
        private static readonly CancellationTokenSource _cts = new();
        private static volatile bool _requested;
        private static Func<Task>? _onDisposeAsync;

        /// Exposed cancellation flow for services (LocalServer SSE, etc.)
        public static CancellationToken Token => _cts.Token;

        /// Register async disposer (e.g., stop Kestrel, dispose models).
        public static void RegisterAsyncDisposer(Func<Task> disposer) => _onDisposeAsync = disposer;

        /// Ask everything to stop, then exit.
        public static async Task RequestAsync()
        {
            if (_requested) return;
            _requested = true;

            try { _cts.Cancel(); } catch { /* no-op */ }

            // Let registered services stop
            if (_onDisposeAsync != null)
            {
                try { await _onDisposeAsync().ConfigureAwait(false); } catch { /* swallow */ }
            }

            // Encourage native releases (llama buffers, file mmaps, etc.)
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { /* no-op */ }

            // Final, deterministic exit
            Environment.Exit(0);
        }

        /// Synchronous hard exit (menu click fallback).
        public static void HardExit()
        {
            if (_requested)
            {
                Environment.Exit(0);
                return;
            }

            _requested = true;
            try { _cts.Cancel(); } catch { }

            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }
            catch { }

            Environment.Exit(0);
        }
    }
}
