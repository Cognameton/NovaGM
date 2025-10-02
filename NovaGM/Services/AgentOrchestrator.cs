// NovaGM/Services/AgentOrchestrator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NovaGM.Models;
using NovaGM.Services.State;
using NovaGM.Services.Retrieval; // Retriever, VectorStoreSqlite, HashEmbedder
using NovaGM.Services.Multiplayer;

namespace NovaGM.Services
{
    /// Controller → State → Narrator (stream) → Memory + simple retrieval + per-role model selection.
    public sealed class AgentOrchestrator
    {
        private readonly LlamaLocal _controller = new();
        private readonly LlamaLocal _narrator  = new();
        private readonly LlamaLocal _memory    = new();
        private readonly IStateStore _state    = new StateStore();

        public IStateStore StateStore => _state; // Expose for mission saving

        private Retriever? _retriever;
        private bool _loadAttempted;

        private static string LlmDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "llm"));

        private static string? FindFirstModelPath()
        {
            if (!Directory.Exists(LlmDir)) return null;
            return Directory.EnumerateFiles(LlmDir, "*.gguf", SearchOption.TopDirectoryOnly)
                            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
        }

        private static string? FindModelByPrefix(string prefix)
        {
            if (!Directory.Exists(LlmDir)) return null;
            return Directory.EnumerateFiles(LlmDir, $"{prefix}*.gguf", SearchOption.TopDirectoryOnly)
                            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
        }

        private readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// Run a full turn. If onNarratorToken is provided, narration text streams to UI and LAN clients.
        public async Task<string> RunTurnAsync(
            string playerText,
            CancellationToken outerCt,
            Action<string>? onNarratorToken = null)
        {
            await EnsureLoadedAsync();

            // Build facts using retriever + recent facts
            var state = _state.Load();
            var recentFacts = state.Facts.TakeLast(8).ToList();
            string facts;

            if (_retriever is not null)
            {
                await _retriever.UpsertFacts(state.Facts);
                var retrieved = await _retriever.QueryTopKAsync(playerText, 6);
                var combined = retrieved.Concat(recentFacts).Distinct().Take(12).ToList();
                facts = string.Join("; ", combined);
            }
            else
            {
                facts = string.Join("; ", recentFacts);
            }

            var compact = _state.CompactSlice();
            var genreContext = BuildGenreContext();
            var controllerSystem = AppendGenreContext(Prompts.ControllerSystem, genreContext);

            // PLAN → Beat JSON (Controller)
            using var planCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            planCts.CancelAfter(TimeSpan.FromSeconds(12));
            var beatJson = await AskSafeAsync(
                _controller,
                controllerSystem,
                Prompts.ControllerUser(playerText, facts, compact, Prompts.ControllerSchema, genreContext),
                planCts.Token,
                expectJson: true
            );

            var beat = TryDeserialize<Beat>(beatJson) 
                       ?? await TryRepairBeatAsync(playerText, facts, compact, beatJson, genreContext, controllerSystem, planCts.Token);
            if (beat is null)
                return "The scene hesitates, waiting for clarity. 1) Look around 2) Call out 3) Wait.<EOT>";

            // Apply state changes
            _state.ApplyChanges(
                beat.State_Changes?.Location,
                beat.State_Changes?.Flags_Add,
                beat.State_Changes?.Npc_Delta
            );

            // NARRATE → prose (stream tokens to UI if delegate is provided)
            using var narrCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            narrCts.CancelAfter(TimeSpan.FromSeconds(20));
            var narratorSystem = AppendGenreContext(Prompts.NarratorSystem, genreContext);
            var narratorUser = Prompts.NarratorUser(JsonSerializer.Serialize(beat, _json), facts, compact, genreContext);
            var narrationRaw = await AskSafeAsync(
                _narrator,
                narratorSystem,
                narratorUser,
                narrCts.Token,
                expectJson: false,
                onToken: onNarratorToken
            );

            var finalProse = TruncateAtEot(narrationRaw).Trim();
            if (string.IsNullOrWhiteSpace(finalProse))
                finalProse = "Silence lingers. 1) Call out 2) Advance 3) Wait.";

            if (GenreStyleGuard.Violates(genreContext, finalProse, out var reason))
            {
                var repairUser = narratorUser +
                    $"\nNote: You drifted off-genre ({reason}). Strictly align to the GENRE CONTEXT. Keep the same facts/outcomes. End with <EOT>.";

                var repairRaw = await AskSafeAsync(
                    _narrator,
                    narratorSystem,
                    repairUser,
                    narrCts.Token,
                    expectJson: false,
                    onToken: null
                );

                var repaired = TruncateAtEot(repairRaw).Trim();
                if (!string.IsNullOrWhiteSpace(repaired))
                {
                    finalProse = repaired;
                }
            }

            // MEMORY → new facts
            using var memoCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            memoCts.CancelAfter(TimeSpan.FromSeconds(6));
            var memJson = await AskSafeAsync(
                _memory,
                Prompts.MemorySystem,
                Prompts.MemoryUser(playerText, finalProse),
                memoCts.Token,
                expectJson: true
            );

            var delta = TryDeserialize<MemoryDelta>(memJson);
            if (delta?.Facts?.Count > 0)
                _state.AddFacts(delta.Facts);

            return finalProse;
        }

        private async Task EnsureLoadedAsync()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            // GPU layers: parse env; negative (e.g., -1) means "max offload".
            int gpuLayers = 0;
            var env = Environment.GetEnvironmentVariable("NOVAGM_GPU_LAYERS");
            if (int.TryParse(env, out var g))
                gpuLayers = g < 0 ? 999 : g;

            // Choose per-role models (prefix match), else fall back to first gguf
            var ctrlPath = FindModelByPrefix("controller") ?? FindFirstModelPath();

            if (ctrlPath is null)
            {
                Console.WriteLine("[NovaGM] No .gguf models found under llm/. Running in stub mode.");
                return; // no models → stub mode (the app still runs)
            }

            var memPath  = FindModelByPrefix("memory")   ?? ctrlPath;
            var narrPath = FindModelByPrefix("narrator") ?? ctrlPath;

            // Load with role-appropriate context sizes
            await _controller.LoadAsync(ctrlPath, ctxSize: 1536, gpuLayers: gpuLayers);
            await _memory.LoadAsync(memPath,    ctxSize: 1024, gpuLayers: gpuLayers);
            await _narrator.LoadAsync(narrPath, ctxSize: 2048, gpuLayers: gpuLayers);

            // Initialize the retriever (SQLite + hash-embeddings for now)
            var dbPath = Path.Combine(Paths.AppDataDir, "vec.db");
            _retriever = new Retriever(new VectorStoreSqlite(dbPath, dim: 384), new HashEmbedder(384));

            Console.WriteLine($"[NovaGM] Models loaded:");
            Console.WriteLine($"  Controller: {ctrlPath}");
            Console.WriteLine($"  Memory    : {memPath}");
            Console.WriteLine($"  Narrator  : {narrPath}");
            Console.WriteLine($"[NovaGM] GPU layers request: {gpuLayers}");
        }

        private async Task<string> AskSafeAsync(
            LlamaLocal m,
            string sys,
            string user,
            CancellationToken ct,
            bool expectJson,
            Action<string>? onToken = null)
        {
            if (!m.IsLoaded)
            {
                if (!expectJson)
                {
                    const string fallback = "The lights flicker as the air hums.";
                    onToken?.Invoke(fallback);
                    return fallback;
                }
                return "{}";
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            StringBuilder? streamBuffer = onToken is not null ? new StringBuilder() : null;
            var lastSent = 0;

            Action<string>? forwarder = null;
            if (onToken is not null)
            {
                forwarder = chunk =>
                {
                    if (string.IsNullOrEmpty(chunk) || streamBuffer is null) return;

                    streamBuffer.Append(chunk);
                    var current = streamBuffer.ToString();
                    var truncated = TruncateAtEot(current);

                    if (truncated.Length > lastSent)
                    {
                        var delta = truncated.Substring(lastSent);
                        if (!string.IsNullOrEmpty(delta))
                        {
                            onToken(delta);
                        }
                        lastSent = truncated.Length;
                    }

                    if (current.IndexOf("<EOT>", StringComparison.Ordinal) >= 0)
                    {
                        linked.Cancel();
                    }
                };
            }

            string raw = string.Empty;

            try
            {
                raw = await m.AskAsync(sys, user, linked.Token, forwarder);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                raw = streamBuffer?.ToString() ?? string.Empty;
            }
            catch
            {
                if (!expectJson)
                {
                    const string fallback = "You hear a relay crackle to life.";
                    onToken?.Invoke(fallback);
                    return fallback;
                }
                return "{}";
            }

            if (streamBuffer is not null && streamBuffer.Length > 0)
            {
                raw = streamBuffer.ToString();
            }

            if (expectJson)
            {
                var start = raw.IndexOf('{');
                var end = raw.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    raw = raw.Substring(start, end - start + 1);
                }
                return raw.Trim();
            }

            var final = TruncateAtEot(raw).Trim();
            if (string.IsNullOrEmpty(final))
            {
                final = raw.Trim();
            }
            if (string.IsNullOrEmpty(final))
            {
                final = "The lights flicker as the air hums.";
            }

            return final;
        }

        private T? TryDeserialize<T>(string json)
        {
            try { return JsonSerializer.Deserialize<T>(json, _json); }
            catch { return default; }
        }

        private async Task<Beat?> TryRepairBeatAsync(
            string player,
            string facts,
            string compact,
            string badJson,
            string genreContext,
            string controllerSystem,
            CancellationToken ct)
        {
            var repairUser = Prompts.ControllerUser(player, facts, compact, Prompts.ControllerSchema, genreContext) +
                             $"\nThe previous JSON was malformed: ```{badJson}```\nReturn ONLY corrected JSON.";
            var repaired = await AskSafeAsync(_controller, controllerSystem, repairUser, ct, expectJson: true);
            return TryDeserialize<Beat>(repaired);
        }

        private static string TruncateAtEot(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var idx = text.IndexOf("<EOT>", StringComparison.Ordinal);
            return idx >= 0 ? text.Substring(0, idx) : text;
        }

        private static string AppendGenreContext(string basePrompt, string genreContext)
        {
            if (string.IsNullOrWhiteSpace(genreContext)) return basePrompt;
            return $"{basePrompt}\n\nUI-selected genre: {genreContext}";
        }

        private static string BuildGenreContext()
        {
            var sessionGenre = GameCoordinator.Instance.Session.GenreContext;
            if (!string.IsNullOrWhiteSpace(sessionGenre))
            {
                return sessionGenre;
            }

            try
            {
                var config = GenreManager.Current;
                var display = GenreManager.GetGenreDisplayName(config.Genre);
                var segments = new List<string> { $"Genre: {display}" };

                var raceData = GenreManager.GetAvailableRaces();
                var availableRaces = SafeCollectNames(raceData.Values.Select(r => r.Name), raceData.Keys);
                if (availableRaces.Length > 0)
                {
                    segments.Add($"Races: {string.Join(", ", availableRaces)}");
                }

                var classData = GenreManager.GetAvailableClasses();
                var availableClasses = SafeCollectNames(classData.Values.Select(c => c.Name), classData.Keys);
                if (availableClasses.Length > 0)
                {
                    segments.Add($"Classes: {string.Join(", ", availableClasses)}");
                }

                var availableItems = GenreManager.GetAvailableItems().Values
                    .Select(i => string.IsNullOrWhiteSpace(i.Name) ? i.Id : i.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToArray();
                if (availableItems.Length > 0)
                {
                    segments.Add($"Equipment: {string.Join(", ", availableItems)}");
                }

                if (config.Genre == GameGenre.Custom)
                {
                    var customRaces = SafeCollectNames(config.CustomRaces.Values.Select(r => r.Name),
                                                        config.CustomRaces.Keys);
                    if (customRaces.Length > 0)
                    {
                        segments.Add($"Custom races: {string.Join(", ", customRaces)}");
                    }

                    var customClasses = SafeCollectNames(config.CustomClasses.Values.Select(c => c.Name),
                                                          config.CustomClasses.Keys);
                    if (customClasses.Length > 0)
                    {
                        segments.Add($"Custom classes: {string.Join(", ", customClasses)}");
                    }

                    if (customRaces.Length == 0 && customClasses.Length == 0)
                    {
                        segments.Add("Custom genre relies on GM-defined tone and content.");
                    }
                }
                else
                {
                    var tone = config.Genre switch
                    {
                        GameGenre.Fantasy => "Tone: magic, medieval adventure, mythical lore.",
                        GameGenre.SciFi   => "Tone: futuristic technology, starships, alien worlds.",
                        GameGenre.Horror  => "Tone: dread, suspense, supernatural danger.",
                        _                 => string.Empty
                    };
                    if (!string.IsNullOrWhiteSpace(tone))
                    {
                        segments.Add(tone);
                    }
                }

                var combined = string.Join("; ", segments);
                GameCoordinator.Instance.SetGenreContext(combined);
                return combined;
            }
            catch
            {
                var existing = GameCoordinator.Instance.Session.GenreContext;
                return string.IsNullOrWhiteSpace(existing)
                    ? "Genre information unavailable"
                    : existing;
            }
        }

        private static string[] SafeCollectNames(IEnumerable<string?> primary, IEnumerable<string> fallbackIds)
        {
            var fallbackArray = fallbackIds.ToArray();
            var names = primary
                .Select((n, idx) => string.IsNullOrWhiteSpace(n) ? fallbackArray.ElementAtOrDefault(idx) : n)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();
            return names;
        }
    }
}
