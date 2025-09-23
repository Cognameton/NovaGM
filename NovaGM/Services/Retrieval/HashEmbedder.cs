using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NovaGM.Services
{
    public sealed class HashEmbedder : IEmbedder
    {
        private static readonly Regex Splitter = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);
        public int Dimension { get; }
        public HashEmbedder(int dim = 384) => Dimension = dim;

        public float[] Embed(string text)
        {
            var v = new float[Dimension];
            if (string.IsNullOrWhiteSpace(text)) return v;

            foreach (var tok in Splitter.Split(text.ToLowerInvariant()).Where(t => t.Length > 0))
            {
                uint h = Fnv1a(tok);
                int idx = (int)(h % (uint)Dimension);
                v[idx] += 1f;
            }
            // L2 normalize
            double norm = Math.Sqrt(v.Sum(f => (double)f * f));
            if (norm > 0) for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
            return v;
        }

        private static uint Fnv1a(string s)
        {
            const uint FNV_PRIME = 16777619;
            uint hash = 2166136261;
            foreach (var ch in Encoding.UTF8.GetBytes(s))
            {
                hash ^= ch;
                hash *= FNV_PRIME;
            }
            return hash;
        }
    }
}
