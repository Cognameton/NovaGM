using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NovaGM.Models
{
    public sealed class MemoryDelta
    {
        [JsonPropertyName("facts")] public List<string>? Facts { get; set; }
    }
}
