using System.Text.Json.Serialization;

namespace NovaGM.Services.Packs
{
    public sealed class PackManifest
    {
        [JsonPropertyName("id")]          public string Id { get; set; } = "";
        [JsonPropertyName("name")]        public string Name { get; set; } = "";
        [JsonPropertyName("version")]     public string Version { get; set; } = "1.0.0";
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("theme")]       public string? Theme { get; set; } // optional path within the pack (future)
    }

    public sealed class PackInfo
    {
        public string FolderPath { get; set; } = "";
        public PackManifest Manifest { get; set; } = new();
    }
}
