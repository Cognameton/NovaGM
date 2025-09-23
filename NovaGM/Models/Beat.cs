using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NovaGM.Models
{
    public sealed class Beat
    {
        [JsonPropertyName("scene")]           public string? Scene { get; set; }
        [JsonPropertyName("synopsis")]        public string? Synopsis { get; set; }
        [JsonPropertyName("choices")]         public List<string>? Choices { get; set; }
        [JsonPropertyName("state_changes")]   public StateChanges? State_Changes { get; set; }
    }

    public sealed class StateChanges
    {
        [JsonPropertyName("location")]   public string? Location { get; set; }
        [JsonPropertyName("flags_add")]  public List<string>? Flags_Add { get; set; }
        // npc name -> brief delta description (keep generic for now)
        [JsonPropertyName("npc_delta")]  public Dictionary<string, string>? Npc_Delta { get; set; }
    }
}
