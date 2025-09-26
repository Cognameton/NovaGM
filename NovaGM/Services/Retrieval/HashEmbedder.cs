using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NovaGM.Services.Retrieval
{
    public interface IEmbedder
    {
        int Dim { get; }
        float[] Embed(string text);
    }

    /// <summary>
    /// Tiny, dependency-free, deterministic embedder:
    /// SHA256 over tokens → sign-projected bag-of-words → L2 normalized.
    /// Not semantic, but good enough for quick “similar-ish” matches offline.
    /// </summary>
    public sealed class HashEmbedder : IEmbedder
    {
        public int Dim { get; }

        public HashEmbedder(int dim = 384)
        {
            if (dim <= 0) throw new ArgumentOutOfRangeException(nameof(dim));
            Dim = dim;
        }

        public float[] Embed(string text)
        {
            var v = new float[Dim];
            if (string.IsNullOrWhiteSpace(text))
                return v;

            var tokens = Tokenize(text);
            using var sha = SHA256.Create();

            foreach (var t in tokens)
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(t));
                // Use bytes to set +/- 1 into each dimension (cycle if needed)
                for (int i = 0; i < Dim; i++)
                {
                    // pick a bit deterministically
                    var b = bytes[(i * 7) % bytes.Length];
                    v[i] += ((b & 1) == 0) ? 1f : -1f;
                }
            }

            // L2 normalize
            float norm = (float)Math.Sqrt(v.Sum(x => x * x)) + 1e-6f;
            for (int i = 0; i < Dim; i++) v[i] /= norm;
            return v;
        }

        private static string[] Tokenize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            return sb.ToString()
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
