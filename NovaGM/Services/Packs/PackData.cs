using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NovaGM.Services.Packs
{
    // ----- Core pack data types -----

    public sealed class RaceDef
    {
        [JsonPropertyName("id")]     public string Id { get; set; } = "";
        [JsonPropertyName("name")]   public string Name { get; set; } = "";
        [JsonPropertyName("mods")]   public Dictionary<string, int> Mods { get; set; } = new(); // e.g., { "str": 2, "dex": 0 }
        [JsonPropertyName("traits")] public string[] Traits { get; set; } = System.Array.Empty<string>();
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    public sealed class ClassDef
    {
        [JsonPropertyName("id")]     public string Id { get; set; } = "";
        [JsonPropertyName("name")]   public string Name { get; set; } = "";
        [JsonPropertyName("hitDie")] public int HitDie { get; set; } = 8;

        // Index: level-1 → proficiency bonus
        [JsonPropertyName("proficiencyBonusByLevel")]
        public int[] ProficiencyBonusByLevel { get; set; } = new[] { 2 };
        
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    public sealed class SkillDef
    {
        [JsonPropertyName("id")]   public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        // str/dex/con/int/wis/cha
        [JsonPropertyName("attr")] public string GoverningAttr { get; set; } = "int";
    }

    public sealed class ItemDef
    {
        [JsonPropertyName("id")]     public string Id { get; set; } = "";
        [JsonPropertyName("name")]   public string Name { get; set; } = "";
        // weapon/armor/tool/consumable/vehicle/ship/…
        [JsonPropertyName("type")]   public string Type { get; set; } = "weapon";
        // e.g., { "ac": 2, "dmgMin":1, "dmgMax":6, "acc":1 }
        [JsonPropertyName("stats")]  public Dictionary<string, int> Stats { get; set; } = new();
        [JsonPropertyName("weight")] public double Weight { get; set; } = 0.0;
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    // Rules/Formula document that StatCalculator uses
    public sealed class RulesDoc
    {
        // Named constants available to formulas (e.g., "BaseAC": 10)
        [JsonPropertyName("constants")]
        public Dictionary<string, double> Constants { get; set; } = new();

        // Custom simple functions (kept for future use; not required by our tiny evaluator)
        [JsonPropertyName("functions")]
        public Dictionary<string, string> Functions { get; set; } = new();

        // Named expressions, e.g., "HP": "classHitDie + mod(con) * level"
        [JsonPropertyName("formulas")]
        public Dictionary<string, string> Formulas { get; set; } = new();
    }

    public sealed class Questionnaire
    {
        public sealed class Question
        {
            [JsonPropertyName("id")]      public string Id { get; set; } = "";
            [JsonPropertyName("prompt")]  public string Prompt { get; set; } = "";
            // "text" | "select" | "slider"
            [JsonPropertyName("type")]    public string Type { get; set; } = "text";
            [JsonPropertyName("options")] public string[] Options { get; set; } = System.Array.Empty<string>();
            [JsonPropertyName("min")]     public int Min { get; set; } = 8;
            [JsonPropertyName("max")]     public int Max { get; set; } = 18;
            [JsonPropertyName("default")] public int Default { get; set; } = 10;
        }

        [JsonPropertyName("questions")]
        public Question[] Questions { get; set; } = System.Array.Empty<Question>();
    }

    public sealed class PackData
    {
        [JsonPropertyName("races")]         public Dictionary<string, RaceDef>  Races   { get; set; } = new();
        [JsonPropertyName("classes")]       public Dictionary<string, ClassDef> Classes { get; set; } = new();
        [JsonPropertyName("skills")]        public Dictionary<string, SkillDef> Skills  { get; set; } = new();
        [JsonPropertyName("items")]         public Dictionary<string, ItemDef>  Items   { get; set; } = new();
        [JsonPropertyName("rules")]         public RulesDoc Rules { get; set; } = new();
        [JsonPropertyName("questionnaire")] public Questionnaire Questionnaire { get; set; } = new();
    }
}
