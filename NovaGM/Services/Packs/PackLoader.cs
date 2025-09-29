using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NovaGM.Services.Packs
{
    /// Loads the active pack’s data. Seeds a minimal "classic" pack if none exist.
    public static class PackLoader
    {
        private static readonly object _lock = new();
        private static PackData _data = new();
        public static PackData Data { get { lock (_lock) return _data; } }

        public static void LoadActiveOrDefault()
        {
            lock (_lock)
            {
                var baseDir = AppContext.BaseDirectory;
                var packsDir = Path.Combine(baseDir, "packs");
                Directory.CreateDirectory(packsDir);

                // Active pack id (via PackManager/Config). Fallback to "classic".
                var activeId = PackManager.GetActiveId() ?? "classic";
                var packDir  = Path.Combine(packsDir, activeId, "data");

                if (!Directory.Exists(packDir))
                {
                    // seed "classic" if missing
                    SeedClassicPack(Path.Combine(packsDir, "classic"));
                    packDir = Path.Combine(packsDir, "classic", "data");
                }

                _data = LoadFromFolder(packDir);
            }
        }

        public static PackData LoadFromFolder(string dataDir)
        {
            var pd = new PackData();

            T read<T>(string name, Func<T> fallback) where T : class
            {
                var path = Path.Combine(dataDir, name);
                if (!File.Exists(path)) return fallback();
                try
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<T>(json) ?? fallback();
                }
                catch { return fallback(); }
            }

            pd.Races        = read<Dictionary<string, RaceDef>>("races.json",        () => new());
            pd.Classes      = read<Dictionary<string, ClassDef>>("classes.json",     () => new());
            pd.Skills       = read<Dictionary<string, SkillDef>>("skills.json",      () => new());
            pd.Items        = read<Dictionary<string, ItemDef>>("items.json",        () => new());
            pd.Rules        = read<RulesDoc>("rules.json",                           () => new());
            pd.Questionnaire= read<Questionnaire>("questionnaire.json",              () => new());
            return pd;
        }

        private static void SeedClassicPack(string packRoot)
        {
            try
            {
                var dataDir = Path.Combine(packRoot, "data");
                Directory.CreateDirectory(dataDir);

                var manifest = new PackManifest { Id = "classic", Name = "Classic Fantasy", Version = "0.1.0" };
                Directory.CreateDirectory(packRoot);
                File.WriteAllText(Path.Combine(packRoot, "manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                void writeIfMissing(string file, string contents)
                {
                    var path = Path.Combine(dataDir, file);
                    if (!File.Exists(path)) File.WriteAllText(path, contents);
                }

                writeIfMissing("races.json", ClassicJson.Races);
                writeIfMissing("classes.json", ClassicJson.Classes);
                writeIfMissing("skills.json", ClassicJson.Skills);
                writeIfMissing("items.json", ClassicJson.Items);
                writeIfMissing("rules.json", ClassicJson.Rules);
                writeIfMissing("questionnaire.json", ClassicJson.Questionnaire);
            }
            catch
            {
                // ignore seeding errors
            }
        }

        private static class ClassicJson
        {
            public const string Races = @"{
  ""human"": { ""id"": ""human"", ""name"": ""Human"", ""mods"": {""str"":0,""dex"":0,""con"":0,""int"":0,""wis"":0,""cha"":0}, ""traits"": [""Versatile""] },
  ""elf"":   { ""id"": ""elf"",   ""name"": ""Elf"",   ""mods"": {""str"":0,""dex"":2,""con"":-1,""int"":1,""wis"":0,""cha"":0}, ""traits"": [""Keen Senses""] },
  ""dwarf"": { ""id"": ""dwarf"", ""name"": ""Dwarf"", ""mods"": {""str"":1,""dex"":0,""con"":2,""int"":0,""wis"":0,""cha"":-1}, ""traits"": [""Stout""] },
  ""android"": { ""id"": ""android"", ""name"": ""Android"", ""mods"": {""str"":1,""dex"":1,""con"":2,""int"":1,""wis"":0,""cha"":-1}, ""traits"": [""Synthetic"", ""No Sleep""] },
  ""cyborg"": { ""id"": ""cyborg"", ""name"": ""Cyborg"", ""mods"": {""str"":2,""dex"":0,""con"":1,""int"":1,""wis"":0,""cha"":-1}, ""traits"": [""Enhanced"", ""Tech Interface""] },
  ""alien"": { ""id"": ""alien"", ""name"": ""Alien"", ""mods"": {""str"":0,""dex"":1,""con"":0,""int"":2,""wis"":1,""cha"":0}, ""traits"": [""Telepathic"", ""Adaptable""] }
}";
            public const string Classes = @"{
  ""fighter"": { ""id"": ""fighter"", ""name"": ""Fighter"", ""hitDie"": 10, ""proficiencyBonusByLevel"": [2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6] },
  ""rogue"":   { ""id"": ""rogue"",   ""name"": ""Rogue"",   ""hitDie"": 8,  ""proficiencyBonusByLevel"": [2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6] },
  ""mage"":    { ""id"": ""mage"",    ""name"": ""Mage"",    ""hitDie"": 6,  ""proficiencyBonusByLevel"": [2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6] },
  ""pilot"": { ""id"": ""pilot"", ""name"": ""Pilot"", ""hitDie"": 8, ""proficiencyBonusByLevel"": [2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6] },
  ""engineer"": { ""id"": ""engineer"", ""name"": ""Engineer"", ""hitDie"": 8, ""proficiencyBonusByLevel"": [2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6] },
  ""scientist"": { ""id"": ""scientist"", ""name"": ""Scientist"", ""hitDie"": 6, ""proficiencyBonusByLevel"": [2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6] }
}";
            public const string Skills = @"{
  ""athletics"": { ""id"": ""athletics"", ""name"": ""Athletics"", ""attr"": ""str"" },
  ""stealth"":   { ""id"": ""stealth"",   ""name"": ""Stealth"",   ""attr"": ""dex"" },
  ""lore"":      { ""id"": ""lore"",      ""name"": ""Lore"",      ""attr"": ""int"" }
}";
            public const string Items = @"{
  ""leather_armor"": { ""id"": ""leather_armor"", ""name"": ""Leather Armor"", ""type"": ""armor"", ""stats"": { ""ac"": 2 }, ""weight"": 10.0 },
  ""shortsword"":    { ""id"": ""shortsword"",    ""name"": ""Shortsword"",    ""type"": ""weapon"", ""stats"": { ""dmgMin"":1, ""dmgMax"":6, ""acc"":1 }, ""weight"": 2.0 },
  ""shield"":        { ""id"": ""shield"",        ""name"": ""Shield"",        ""type"": ""armor"", ""stats"": { ""shield"": 2 }, ""weight"": 6.0 }
}";
            public const string Rules = @"{
  ""constants"": { ""BaseAC"": 10, ""MaxLevel"": 20 },
  ""functions"": { ""mod"": ""floor((x-10)/2)"" },
  ""formulas"":  {
    ""HP"": ""classHitDie + mod(con) * level"",
    ""AC"": ""BaseAC + armor + shield + mod(dex)"",
    ""AttackBonus"": ""prof + weaponAcc + mod(str)"",
    ""CarryCap"": ""15 * str""
  }
}";
            public const string Questionnaire = @"{
  ""questions"": [
    { ""id"": ""name"", ""prompt"": ""Character name"", ""type"": ""text"" },
    { ""id"": ""race"", ""prompt"": ""Choose a race"", ""type"": ""select"", ""options"": [""human"",""elf"",""dwarf""] },
    { ""id"": ""class"", ""prompt"": ""Choose a class"", ""type"": ""select"", ""options"": [""fighter"",""rogue"",""mage""] },
    { ""id"": ""str"", ""prompt"": ""Strength"", ""type"": ""slider"", ""min"": 8, ""max"": 18, ""default"": 15 },
    { ""id"": ""dex"", ""prompt"": ""Dexterity"", ""type"": ""slider"", ""min"": 8, ""max"": 18, ""default"": 14 },
    { ""id"": ""con"", ""prompt"": ""Constitution"", ""type"": ""slider"", ""min"": 8, ""max"": 18, ""default"": 14 },
    { ""id"": ""int"", ""prompt"": ""Intelligence"", ""type"": ""slider"", ""min"": 8, ""max"": 18, ""default"": 12 },
    { ""id"": ""wis"", ""prompt"": ""Wisdom"", ""type"": ""slider"", ""min"": 8, ""max"": 18, ""default"": 10 },
    { ""id"": ""cha"", ""prompt"": ""Charisma"", ""type"": ""slider"", ""min"": 8, ""max"": 18, ""default"": 10 }
  ]
}";
        }
    }
}
