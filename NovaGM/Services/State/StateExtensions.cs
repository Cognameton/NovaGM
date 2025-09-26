using System;
using System.Linq;
using System.Text;
using NovaGM.Services.State;

namespace NovaGM.Services
{
    public static class StateExtensions
    {
        /// <summary>
        /// Produce a compact, single-line summary of the current state, capped at maxChars.
        /// Safe to pass into prompts.
        /// </summary>
        public static string CompactSlice(this IStateStore store, int maxChars)
        {
            var s = store.Load();
            var sb = new StringBuilder(256);

            if (!string.IsNullOrWhiteSpace(s.Location))
                sb.Append("location:").Append(s.Location).Append(" ; ");

            if (s.Flags.Count > 0)
                sb.Append("flags:").Append(string.Join(',', s.Flags)).Append(" ; ");

            if (s.Npcs.Count > 0)
                sb.Append("npcs:")
                  .Append(string.Join(',', s.Npcs.Select(kv => $"{kv.Key}={kv.Value}")))
                  .Append(" ; ");

            if (s.Facts.Count > 0)
                sb.Append("facts:")
                  .Append(string.Join(" | ", s.Facts));

            var text = sb.ToString().Trim();
            if (text.Length <= maxChars) return text;
            return text.Substring(0, Math.Max(0, maxChars));
        }
    }
}
