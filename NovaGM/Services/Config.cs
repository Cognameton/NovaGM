using System.IO;
using System.Text.Json;

namespace NovaGM.Services
{
    public sealed class AppConfig
    {
        public bool SingleRoom { get; set; } = true;

        // GPU
        public bool UseGpu     { get; set; } = false;
        public int  GpuLayers  { get; set; } = 0;

        // Multiplayer
        public int  MaxPlayers { get; set; } = 8;

        // Narration
        public int    NarratorMaxTokens    { get; set; } = 260;
        public double NarratorTemperature  { get; set; } = 0.75; // (not exposed in LLamaSharp 0.25.x)
        public double NarratorTopP         { get; set; } = 0.92; // (not exposed in LLamaSharp 0.25.x)

        // LAN hosting
        public bool AllowLan { get; set; } = false;
        public int  HttpPort { get; set; } = 5055;

        // Packs
        public string? ActivePackId { get; set; }

        // Model selections by role (filenames under llm/)
        public ModelSelection Models { get; set; } = new();

        public sealed class ModelSelection
        {
            public string? Controller { get; set; }
            public string? Narrator   { get; set; }
            public string? Memory     { get; set; }
        }
    }

    public static class Config
    {
        private static readonly string PathFile = System.IO.Path.Combine(Paths.AppDataDir, "config.json");
        public static AppConfig Current { get; }

        static Config()
        {
            try
            {
                Directory.CreateDirectory(Paths.AppDataDir);
                if (File.Exists(PathFile))
                {
                    var json = File.ReadAllText(PathFile);
                    Current = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    Current = new AppConfig();
                    Save();
                }
            }
            catch
            {
                Current = new AppConfig();
            }
        }

        public static void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PathFile, json);
            }
            catch { /* ignore */ }
        }
    }
}
