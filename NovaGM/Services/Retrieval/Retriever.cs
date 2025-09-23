using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NovaGM.Services
{
    public sealed class Retriever
    {
        private readonly VectorStoreSqlite _store;
        private readonly IEmbedder _embedder;

        public Retriever(VectorStoreSqlite store, IEmbedder embedder)
        {
            _store = store;
            _embedder = embedder;
        }

        public Task UpsertFacts(IEnumerable<string> facts) => _store.UpsertAsync(facts, _embedder);

        public async Task<List<string>> QueryTopKAsync(string query, int k = 6)
        {
            var q = _embedder.Embed(query);
            var hits = await _store.TopKAsync(q, k);
            return hits.Select(h => h.Text).ToList();
        }
    }
}
