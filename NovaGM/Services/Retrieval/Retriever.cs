using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NovaGM.Services.Retrieval
{
    /// <summary>
    /// Thin orchestrator over an embedder + a store. All similarity is computed in-process.
    /// </summary>
    public sealed class Retriever
    {
        private readonly VectorStoreSqlite _store;
        private readonly IEmbedder _embedder;

        public Retriever(VectorStoreSqlite store, IEmbedder embedder)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        }

        public Task UpsertFacts(IEnumerable<string> facts)
        {
            facts ??= Array.Empty<string>();
            var rows = facts
                .Select(f => (text: (f ?? "").Trim(), vec: _embedder.Embed(f ?? "")))
                .Where(t => t.text.Length > 0)
                .ToList();

            _store.UpsertMany(rows);
            return Task.CompletedTask;
        }

        public Task<List<string>> QueryTopKAsync(string query, int k = 6)
        {
            k = Math.Max(1, k);
            var q = _embedder.Embed(query ?? "");
            var all = _store.LoadAll();

            var scored = all.Select(a => (text: a.text, score: CosSim(q, a.vec)))
                            .OrderByDescending(t => t.score)
                            .Take(k)
                            .Select(t => t.text)
                            .ToList();

            return Task.FromResult(scored);
        }

        private static float CosSim(float[] a, float[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < n; i++)
            {
                dot += a[i] * b[i];
                na  += a[i] * a[i];
                nb  += b[i] * b[i];
            }
            return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-6));
        }
    }
}
