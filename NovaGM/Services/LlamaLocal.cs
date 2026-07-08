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
        private ModelParams? _parms;
        private StatelessExecutor? _executor; // Shared by AskAsync and CompleteAsync
        public bool IsLoaded => _executor is not null;

        public async Task LoadAsync(string ggufPath, int ctxSize = 2048, int gpuLayers = 0, int? threads = null)
        {
            Dispose();

            _parms = new ModelParams(ggufPath)
            {
                ContextSize = (uint)ctxSize,
                GpuLayerCount = gpuLayers > 0 ? gpuLayers : 0,
            };
            _weights = LLamaWeights.LoadFromFile(_parms);

            // Stateless on purpose: the orchestrator re-sends all continuity (facts,
            // compact world state, rolling context) in every prompt, so persistent
            // chat history only fills the fixed context window until narration
            // degrades a few turns in. The controller made this switch earlier;
            // narrator and memory now execute the same way.
            _executor = new StatelessExecutor(_weights, _parms);

            Console.WriteLine($"[NovaGM] LLAMA loaded: {System.IO.Path.GetFileName(ggufPath)} ctx={ctxSize} gpu={gpuLayers}");
            await Task.CompletedTask;
        }

        // Signature used by AgentOrchestrator: (sys, user, ct, onToken?, maxTokens?)
        // maxTokens=0 uses Config.NarratorMaxTokens; pass an explicit value for non-narrator roles.
        public async Task<string> AskAsync(string sys, string user, CancellationToken ct, Action<string>? onToken = null, int maxTokens = 0)
        {
            if (_executor is null) return "";

            var sb = new StringBuilder();
            var prompt = sys + "\n\nUser:\n" + user + "\nAssistant:\n";
            var resolvedTokens = maxTokens > 0 ? maxTokens : Math.Max(Config.Current.NarratorMaxTokens, 200);
            var infer = new InferenceParams
            {
                MaxTokens = resolvedTokens,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.6f,
                    TopP = 0.9f
                },
                AntiPrompts = new List<string> { "<EOT>" }
            };

            await foreach (var tok in _executor.InferAsync(prompt, infer, ct))
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

        /// Raw completion for agent ReAct loops. Manages its own full prompt string;
        /// stops on OBSERVATION: (so the caller can inject the tool result) or on
        /// FINAL_ANSWER closing brace. Uses StatelessExecutor so no chat history
        /// accumulates — the caller is responsible for building the prompt.
        public async Task<string> CompleteAsync(
            string fullPrompt,
            int maxTokens,
            CancellationToken ct,
            Action<string>? onToken = null)
        {
            if (_executor is null) return "";

            var executor = _executor;
            var infer = new InferenceParams
            {
                MaxTokens = maxTokens,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.5f,  // Lower = more deterministic reasoning
                    TopP = 0.9f
                },
                // Stop when the model hands back to the app (tool boundary or user turn)
                AntiPrompts = new List<string> { "OBSERVATION:", "\nUser:", "\nHuman:" }
            };

            var sb = new StringBuilder();
            var finalAnswerStart = -1; // index in sb where FINAL_ANSWER: was found
            var braceDepth = 0;

            await foreach (var tok in executor.InferAsync(fullPrompt, infer, ct))
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrEmpty(tok)) continue;

                sb.Append(tok);
                onToken?.Invoke(tok);

                var current = sb.ToString();

                // Locate the start of the FINAL_ANSWER JSON object once
                if (finalAnswerStart < 0)
                {
                    var faIdx = current.IndexOf("FINAL_ANSWER:", StringComparison.OrdinalIgnoreCase);
                    if (faIdx >= 0)
                    {
                        var braceIdx = current.IndexOf('{', faIdx);
                        if (braceIdx >= 0)
                        {
                            finalAnswerStart = braceIdx;
                            braceDepth = 0;
                        }
                    }
                }

                // Once we're inside the FINAL_ANSWER JSON, count braces to find
                // the true closing } of the outermost object
                if (finalAnswerStart >= 0)
                {
                    // Recount from finalAnswerStart on each token (sb only grows)
                    braceDepth = 0;
                    bool inString = false;
                    bool escape = false;
                    for (int ci = finalAnswerStart; ci < current.Length; ci++)
                    {
                        char c = current[ci];
                        if (escape) { escape = false; continue; }
                        if (c == '\\' && inString) { escape = true; continue; }
                        if (c == '"') { inString = !inString; continue; }
                        if (inString) continue;
                        if (c == '{') braceDepth++;
                        else if (c == '}')
                        {
                            braceDepth--;
                            if (braceDepth == 0)
                                goto done; // outermost object closed — we're done
                        }
                    }
                }
            }
            done:

            return sb.ToString().Trim();
        }

        public void Dispose()
        {
            _executor = null;
            try { _weights?.Dispose(); } catch { }
            _weights = null; _parms = null;
        }
    }
}
