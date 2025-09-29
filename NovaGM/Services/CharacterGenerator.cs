using System;
using System.Collections.Generic;
using System.Linq;
using NovaGM.Services.Packs;

namespace NovaGM.Services
{
    /// <summary>
    /// Service for generating random characters based on pack data
    /// </summary>
    public static class CharacterGenerator
    {
        private static readonly Random _random = new();

        /// <summary>
        /// Generate a completely random character
        /// </summary>
        public static GeneratedCharacter GenerateRandom()
        {
            PackLoader.LoadActiveOrDefault();
            var data = PackLoader.Data;

            // Pick random race and class
            var races = data.Races.Values.ToArray();
            var classes = data.Classes.Values.ToArray();
            
            if (races.Length == 0 || classes.Length == 0)
            {
                return GenerateFallbackCharacter();
            }

            var race = races[_random.Next(races.Length)];
            var @class = classes[_random.Next(classes.Length)];

            // Generate random name
            var name = GenerateRandomName();

            // Roll stats (3d6 method)
            var baseStats = new Dictionary<string, int>();
            foreach (var stat in new[] { "str", "dex", "con", "int", "wis", "cha" })
            {
                baseStats[stat] = RollStat();
            }

            // Apply racial modifiers
            var finalStats = new Dictionary<string, int>();
            foreach (var stat in baseStats.Keys)
            {
                var racialMod = race.Mods.TryGetValue(stat, out var mod) ? mod : 0;
                finalStats[stat] = Math.Max(3, Math.Min(20, baseStats[stat] + racialMod));
            }

            return new GeneratedCharacter
            {
                Name = name,
                Race = race.Id,
                Class = @class.Id,
                BaseStats = baseStats,
                FinalStats = finalStats,
                RacialMods = race.Mods.ToDictionary(kv => kv.Key, kv => kv.Value)
            };
        }

        /// <summary>
        /// Generate a character optimized for a specific class
        /// </summary>
        public static GeneratedCharacter GenerateForClass(string classId)
        {
            PackLoader.LoadActiveOrDefault();
            var data = PackLoader.Data;

            if (!data.Classes.TryGetValue(classId, out var @class))
            {
                return GenerateRandom();
            }

            // Pick best race for this class
            var race = PickOptimalRace(classId);
            var name = GenerateRandomName();

            // Generate optimized stats based on class
            var baseStats = GenerateOptimizedStats(classId);
            
            // Apply racial modifiers
            var finalStats = new Dictionary<string, int>();
            foreach (var stat in baseStats.Keys)
            {
                var racialMod = race.Mods.TryGetValue(stat, out var mod) ? mod : 0;
                finalStats[stat] = Math.Max(3, Math.Min(20, baseStats[stat] + racialMod));
            }

            return new GeneratedCharacter
            {
                Name = name,
                Race = race.Id,
                Class = classId,
                BaseStats = baseStats,
                FinalStats = finalStats,
                RacialMods = race.Mods.ToDictionary(kv => kv.Key, kv => kv.Value)
            };
        }

        private static RaceDef PickOptimalRace(string classId)
        {
            var data = PackLoader.Data;
            var races = data.Races.Values.ToArray();
            
            if (races.Length == 0)
            {
                return new RaceDef { Id = "human", Name = "Human" };
            }

            // Simple optimization based on class preferences
            var primaryStats = GetPrimaryStatsForClass(classId);
            
            RaceDef bestRace = races[0];
            int bestScore = CalculateRaceScore(bestRace, primaryStats);

            foreach (var race in races)
            {
                int score = CalculateRaceScore(race, primaryStats);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRace = race;
                }
            }

            return bestRace;
        }

        private static string[] GetPrimaryStatsForClass(string classId)
        {
            return classId.ToLowerInvariant() switch
            {
                "fighter" => new[] { "str", "con", "dex" },
                "rogue" => new[] { "dex", "int", "con" },
                "mage" => new[] { "int", "wis", "dex" },
                "wizard" => new[] { "int", "wis", "dex" },
                "ranger" => new[] { "dex", "wis", "con" },
                "cleric" => new[] { "wis", "con", "str" },
                "pilot" => new[] { "dex", "int", "con" },
                "engineer" => new[] { "int", "dex", "con" },
                "scientist" => new[] { "int", "wis", "dex" },
                _ => new[] { "str", "dex", "con" } // default
            };
        }

        private static int CalculateRaceScore(RaceDef race, string[] primaryStats)
        {
            int score = 0;
            foreach (var stat in primaryStats)
            {
                if (race.Mods.TryGetValue(stat, out var mod))
                {
                    score += mod;
                }
            }
            return score;
        }

        private static Dictionary<string, int> GenerateOptimizedStats(string classId)
        {
            var primaryStats = GetPrimaryStatsForClass(classId);
            var stats = new Dictionary<string, int>();

            // Start with decent base stats (point buy style)
            var basePool = new[] { 15, 14, 13, 12, 10, 8 };
            var statNames = new[] { "str", "dex", "con", "int", "wis", "cha" };

            // Assign highest stats to primary stats for this class
            var assignments = new Dictionary<string, int>();
            
            // Prioritize primary stats
            for (int i = 0; i < Math.Min(primaryStats.Length, 3); i++)
            {
                assignments[primaryStats[i]] = basePool[i];
            }

            // Fill remaining stats
            var remainingStats = statNames.Except(assignments.Keys).ToArray();
            var remainingValues = basePool.Skip(assignments.Count).ToArray();
            
            for (int i = 0; i < remainingStats.Length && i < remainingValues.Length; i++)
            {
                assignments[remainingStats[i]] = remainingValues[i];
            }

            // Ensure all six stats are assigned
            foreach (var stat in statNames)
            {
                if (!assignments.ContainsKey(stat))
                {
                    assignments[stat] = 10; // default value
                }
            }

            return assignments;
        }

        private static int RollStat()
        {
            // Roll 4d6, drop lowest (classic D&D method)
            var rolls = new int[4];
            for (int i = 0; i < 4; i++)
            {
                rolls[i] = _random.Next(1, 7);
            }
            Array.Sort(rolls);
            return rolls[1] + rolls[2] + rolls[3]; // Sum of highest 3
        }

        private static string GenerateRandomName()
        {
            var firstNames = new[]
            {
                "Aiden", "Aria", "Bjorn", "Cora", "Darian", "Elena", "Finn", "Gaia",
                "Hector", "Iris", "Jaxon", "Kira", "Leon", "Maya", "Nolan", "Ora",
                "Pavel", "Quinn", "Raven", "Sage", "Thane", "Uma", "Victor", "Wren",
                "Xara", "Yorick", "Zara", "Aldric", "Brynn", "Cedric", "Delara", "Ewan",
                "Freya", "Gareth", "Hilda", "Ivan", "Juno", "Kaelen", "Lyra", "Magnus",
                "Naia", "Orion", "Petra", "Quill", "Rhea", "Silas", "Tessa", "Ulric"
            };

            var lastNames = new[]
            {
                "Ashford", "Blackwood", "Crane", "Dorne", "Ember", "Frost", "Grey",
                "Hawk", "Iron", "Vale", "Stone", "Reed", "Storm", "Swift", "Thorn",
                "Wolf", "Rivers", "Snow", "Moon", "Sun", "Star", "Dawn", "Dusk",
                "Silver", "Gold", "Steel", "Bronze", "Copper", "Rose", "Sage",
                "Pine", "Oak", "Ash", "Elm", "Birch", "Willow", "Cedar", "Maple"
            };

            var firstName = firstNames[_random.Next(firstNames.Length)];
            var lastName = lastNames[_random.Next(lastNames.Length)];
            
            return $"{firstName} {lastName}";
        }

        private static GeneratedCharacter GenerateFallbackCharacter()
        {
            var name = GenerateRandomName();
            var baseStats = new Dictionary<string, int>();
            var finalStats = new Dictionary<string, int>();

            foreach (var stat in new[] { "str", "dex", "con", "int", "wis", "cha" })
            {
                var value = RollStat();
                baseStats[stat] = value;
                finalStats[stat] = value;
            }

            return new GeneratedCharacter
            {
                Name = name,
                Race = "human",
                Class = "fighter",
                BaseStats = baseStats,
                FinalStats = finalStats,
                RacialMods = new Dictionary<string, int>()
            };
        }
    }

    /// <summary>
    /// Represents a generated character with all necessary data
    /// </summary>
    public sealed class GeneratedCharacter
    {
        public string Name { get; set; } = "";
        public string Race { get; set; } = "";
        public string Class { get; set; } = "";
        public Dictionary<string, int> BaseStats { get; set; } = new();
        public Dictionary<string, int> FinalStats { get; set; } = new();
        public Dictionary<string, int> RacialMods { get; set; } = new();

        public int GetStat(string stat) => FinalStats.TryGetValue(stat, out var value) ? value : 10;
        public int GetBaseStat(string stat) => BaseStats.TryGetValue(stat, out var value) ? value : 10;
        public int GetRacialMod(string stat) => RacialMods.TryGetValue(stat, out var value) ? value : 0;

        public override string ToString()
        {
            return $"{Name} ({Race} {Class}) - STR:{GetStat("str")} DEX:{GetStat("dex")} CON:{GetStat("con")} INT:{GetStat("int")} WIS:{GetStat("wis")} CHA:{GetStat("cha")}";
        }
    }
}