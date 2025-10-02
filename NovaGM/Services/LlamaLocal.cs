using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace NovaGM.Services
{
    /// Thin wrapper for LLamaSharp 0.25.0 that streams tokens and stops on "<EOT>"
    public sealed class LlamaLocal : IDisposable
    {
        private LLamaWeights? _weights;
        private LLamaContext? _ctx;
        private ChatSession? _chat;
        public bool IsLoaded => _chat is not null;

        public async Task LoadAsync(string ggufPath, int ctxSize = 2048, int gpuLayers = 0, int? threads = null)
        {
            Dispose();

            var parms = new ModelParams(ggufPath)
            {
                ContextSize = (uint)ctxSize,
                GpuLayerCount = gpuLayers > 0 ? gpuLayers : 0,
            };
            _weights = LLamaWeights.LoadFromFile(parms);
            _ctx = _weights.CreateContext(parms);
            _chat = new ChatSession(new InteractiveExecutor(_ctx));

            Console.WriteLine($"[NovaGM] LLAMA loaded: {System.IO.Path.GetFileName(ggufPath)} ctx={ctxSize} gpu={gpuLayers}");
            await Task.CompletedTask;
        }

        // Signature used by AgentOrchestrator: (sys, user, ct, onToken?)
        public async Task<string> AskAsync(string sys, string user, CancellationToken ct, Action<string>? onToken = null)
        {
            if (_chat is null) return "";

            var sb = new StringBuilder();
            var prompt = sys + "\n\nUser:\n" + user + "\nAssistant:\n";
            var infer = new InferenceParams
            {
                MaxTokens = Math.Max(512, 240),
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.6f,
                    TopP = 0.9f
                },
                AntiPrompts = new List<string> { "<EOT>" }
            };

            await foreach (var tok in _chat.ChatAsync(
                new ChatHistory.Message(AuthorRole.User, prompt),
                infer,
                ct))
            {
                if (ct.IsCancellationRequested) break;
                var s = tok;
                if (string.IsNullOrEmpty(s)) continue;

                if (s.Contains("<EOT>", StringComparison.Ordinal))
                {
                    sb.Append(s);
                    onToken?.Invoke(s);
                    break;
                }

                sb.Append(s);
                onToken?.Invoke(s);
            }

            return sb.ToString().Trim();
        }

        public void Dispose()
        {
            try { _chat = null; } catch { }
            try { _ctx?.Dispose(); } catch { }
            try { _weights?.Dispose(); } catch { }
            _ctx = null; _weights = null;
        }
    }
}
