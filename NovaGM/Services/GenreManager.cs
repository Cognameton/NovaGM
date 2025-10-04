using System;
using System.Collections.Generic;
using System.Linq;
using NovaGM.Services.Packs;

namespace NovaGM.Services
{
    public enum GameGenre
    {
        Fantasy,
        SciFi,
        Horror,
        Custom
    }

    public sealed class GenreConfig
    {
        public GameGenre Genre { get; set; } = GameGenre.Fantasy;
        public Dictionary<string, RaceDef> CustomRaces { get; set; } = new();
        public Dictionary<string, ClassDef> CustomClasses { get; set; } = new();
        public bool GameStarted { get; set; } = false;
    }

    public static class GenreManager
    {
        private static GenreConfig _current = new();
        private static readonly object _lock = new();

        public static event Action<GameGenre>? GenreChanged;
        public static event Action? ContentChanged;

        static GenreManager()
        {
            ContentChanged += PackLoader.RefreshData;
        }

        public static GenreConfig Current
        {
            get { lock (_lock) return _current; }
        }

        public static void SetGenre(GameGenre genre)
        {
            lock (_lock)
            {
                if (_current.GameStarted)
                    throw new InvalidOperationException("Cannot change genre after game has started");

                _current.Genre = genre;
            }
            GenreChanged?.Invoke(genre);
            ContentChanged?.Invoke();
        }

        public static void AddCustomRace(RaceDef race)
        {
            lock (_lock)
            {
                if (_current.GameStarted)
                    throw new InvalidOperationException("Cannot add custom content after game has started");

                _current.CustomRaces[race.Id] = race;
            }
            ContentChanged?.Invoke();
        }

        public static void AddCustomClass(ClassDef classDef)
        {
            lock (_lock)
            {
                if (_current.GameStarted)
                    throw new InvalidOperationException("Cannot add custom content after game has started");

                _current.CustomClasses[classDef.Id] = classDef;
            }
            ContentChanged?.Invoke();
        }

        public static void StartGame()
        {
            lock (_lock)
            {
                _current.GameStarted = true;
            }
        }

        public static void ResetForNewGame()
        {
            lock (_lock)
            {
                _current = new GenreConfig();
            }
            GenreChanged?.Invoke(_current.Genre);
            ContentChanged?.Invoke();
        }

        public static PackData GetCurrentGenreData()
        {
            var genre = Current.Genre;
            var baseData = GetGenreBaseData(genre);
            
            // Merge with custom content
            lock (_lock)
            {
                foreach (var customRace in _current.CustomRaces)
                {
                    baseData.Races[customRace.Key] = customRace.Value;
                }
                
                foreach (var customClass in _current.CustomClasses)
                {
                    baseData.Classes[customClass.Key] = customClass.Value;
                }
            }

            return baseData;
        }

        private static PackData GetGenreBaseData(GameGenre genre)
        {
            return genre switch
            {
                GameGenre.Fantasy => GetFantasyData(),
                GameGenre.SciFi => GetSciFiData(),
                GameGenre.Horror => GetHorrorData(),
                GameGenre.Custom => new PackData(),
                _ => GetFantasyData()
            };
        }

        private static PackData GetFantasyData()
        {
            var data = new PackData();

            // Fantasy Races
            data.Races["human"] = new RaceDef { Id = "human", Name = "Human", Mods = new Dictionary<string, int> { ["str"] = 0, ["dex"] = 0, ["con"] = 0, ["int"] = 0, ["wis"] = 0, ["cha"] = 1 }, Traits = new[] { "Versatile", "Bonus Skill" } };
            data.Races["elf"] = new RaceDef { Id = "elf", Name = "Elf", Mods = new Dictionary<string, int> { ["str"] = 0, ["dex"] = 2, ["con"] = -1, ["int"] = 1, ["wis"] = 1, ["cha"] = 0 }, Traits = new[] { "Keen Senses", "Fey Ancestry" } };
            data.Races["dwarf"] = new RaceDef { Id = "dwarf", Name = "Dwarf", Mods = new Dictionary<string, int> { ["str"] = 1, ["dex"] = 0, ["con"] = 2, ["int"] = 0, ["wis"] = 1, ["cha"] = -1 }, Traits = new[] { "Stout", "Stone Cunning" } };
            data.Races["halfling"] = new RaceDef { Id = "halfling", Name = "Halfling", Mods = new Dictionary<string, int> { ["str"] = -1, ["dex"] = 2, ["con"] = 0, ["int"] = 0, ["wis"] = 1, ["cha"] = 1 }, Traits = new[] { "Lucky", "Small Size" } };
            data.Races["orc"] = new RaceDef { Id = "orc", Name = "Orc", Mods = new Dictionary<string, int> { ["str"] = 2, ["dex"] = 0, ["con"] = 1, ["int"] = -1, ["wis"] = 0, ["cha"] = -1 }, Traits = new[] { "Powerful Build", "Relentless" } };

            // Fantasy Classes
            data.Classes["fighter"] = new ClassDef { Id = "fighter", Name = "Fighter", HitDie = 10, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["rogue"] = new ClassDef { Id = "rogue", Name = "Rogue", HitDie = 8, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["wizard"] = new ClassDef { Id = "wizard", Name = "Wizard", HitDie = 6, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["cleric"] = new ClassDef { Id = "cleric", Name = "Cleric", HitDie = 8, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["ranger"] = new ClassDef { Id = "ranger", Name = "Ranger", HitDie = 10, ProficiencyBonusByLevel = GetStandardProgression() };

            // Fantasy Skills (Magic-based)
            data.Skills["arcana"] = new SkillDef { Id = "arcana", Name = "Arcana", GoverningAttr = "int" };
            data.Skills["athletics"] = new SkillDef { Id = "athletics", Name = "Athletics", GoverningAttr = "str" };
            data.Skills["stealth"] = new SkillDef { Id = "stealth", Name = "Stealth", GoverningAttr = "dex" };
            data.Skills["nature"] = new SkillDef { Id = "nature", Name = "Nature", GoverningAttr = "wis" };
            data.Skills["persuasion"] = new SkillDef { Id = "persuasion", Name = "Persuasion", GoverningAttr = "cha" };

            // Fantasy Items/Weapons
            data.Items["longsword"] = new ItemDef { Id = "longsword", Name = "Longsword", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 8, ["acc"] = 1 }, Weight = 3.0 };
            data.Items["bow"] = new ItemDef { Id = "bow", Name = "Longbow", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 8, ["acc"] = 2 }, Weight = 2.0 };
            data.Items["staff"] = new ItemDef { Id = "staff", Name = "Wizard's Staff", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 6, ["acc"] = 0, ["magic"] = 2 }, Weight = 4.0 };
            data.Items["leather_armor"] = new ItemDef { Id = "leather_armor", Name = "Leather Armor", Type = "armor", Stats = new Dictionary<string, int> { ["ac"] = 2 }, Weight = 10.0 };
            data.Items["shield"] = new ItemDef { Id = "shield", Name = "Shield", Type = "armor", Stats = new Dictionary<string, int> { ["shield"] = 2 }, Weight = 6.0 };
            data.Items["healing_potion"] = new ItemDef { Id = "healing_potion", Name = "Healing Potion", Type = "consumable", Stats = new Dictionary<string, int> { ["heal"] = 8 }, Weight = 1.0, Description = "Restores vigor when consumed." };
            data.Items["chain_mail"] = new ItemDef { Id = "chain_mail", Name = "Chain Mail", Type = "armor", Stats = new Dictionary<string, int> { ["ac"] = 4 }, Weight = 20.0 };
            data.Items["throwing_axe"] = new ItemDef { Id = "throwing_axe", Name = "Throwing Axe", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 6, ["acc"] = 1 }, Weight = 2.0 };
            data.Items["arcane_talisman"] = new ItemDef { Id = "arcane_talisman", Name = "Arcane Talisman", Type = "trinket", Stats = new Dictionary<string, int> { ["magic"] = 3 }, Weight = 0.2, Description = "A charm that amplifies spell power." };

            // Fantasy Rules with magic formulas
            data.Rules.Constants["BaseAC"] = 10;
            data.Rules.Constants["MaxLevel"] = 20;
            data.Rules.Functions["mod"] = "floor((x-10)/2)";
            data.Rules.Formulas["HP"] = "classHitDie + mod(con) * level";
            data.Rules.Formulas["AC"] = "BaseAC + armor + shield + mod(dex)";
            data.Rules.Formulas["AttackBonus"] = "prof + weaponAcc + mod(str)";
            data.Rules.Formulas["SpellPower"] = "prof + mod(int) + magic";

            return data;
        }

        private static PackData GetSciFiData()
        {
            var data = new PackData();

            // Sci-Fi Races
            data.Races["human"] = new RaceDef { Id = "human", Name = "Human", Mods = new Dictionary<string, int> { ["str"] = 0, ["dex"] = 0, ["con"] = 0, ["int"] = 1, ["wis"] = 0, ["cha"] = 0 }, Traits = new[] { "Adaptable", "Tech Savvy" } };
            data.Races["android"] = new RaceDef { Id = "android", Name = "Android", Mods = new Dictionary<string, int> { ["str"] = 1, ["dex"] = 1, ["con"] = 2, ["int"] = 1, ["wis"] = 0, ["cha"] = -1 }, Traits = new[] { "Synthetic", "No Sleep", "EMP Vulnerability" } };
            data.Races["cyborg"] = new RaceDef { Id = "cyborg", Name = "Cyborg", Mods = new Dictionary<string, int> { ["str"] = 2, ["dex"] = 0, ["con"] = 1, ["int"] = 1, ["wis"] = 0, ["cha"] = -1 }, Traits = new[] { "Enhanced", "Tech Interface" } };
            data.Races["alien"] = new RaceDef { Id = "alien", Name = "Alien", Mods = new Dictionary<string, int> { ["str"] = 0, ["dex"] = 1, ["con"] = 0, ["int"] = 2, ["wis"] = 1, ["cha"] = 0 }, Traits = new[] { "Telepathic", "Alien Physiology" } };
            data.Races["mutant"] = new RaceDef { Id = "mutant", Name = "Mutant", Mods = new Dictionary<string, int> { ["str"] = 1, ["dex"] = 0, ["con"] = 1, ["int"] = 0, ["wis"] = 0, ["cha"] = -1 }, Traits = new[] { "Mutation", "Radiation Resistance" } };

            // Sci-Fi Classes
            data.Classes["pilot"] = new ClassDef { Id = "pilot", Name = "Pilot", HitDie = 8, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["engineer"] = new ClassDef { Id = "engineer", Name = "Engineer", HitDie = 8, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["scientist"] = new ClassDef { Id = "scientist", Name = "Scientist", HitDie = 6, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["soldier"] = new ClassDef { Id = "soldier", Name = "Soldier", HitDie = 10, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["hacker"] = new ClassDef { Id = "hacker", Name = "Hacker", HitDie = 6, ProficiencyBonusByLevel = GetStandardProgression() };

            // Sci-Fi Skills (Tech-based)
            data.Skills["computers"] = new SkillDef { Id = "computers", Name = "Computers", GoverningAttr = "int" };
            data.Skills["piloting"] = new SkillDef { Id = "piloting", Name = "Piloting", GoverningAttr = "dex" };
            data.Skills["engineering"] = new SkillDef { Id = "engineering", Name = "Engineering", GoverningAttr = "int" };
            data.Skills["stealth"] = new SkillDef { Id = "stealth", Name = "Stealth", GoverningAttr = "dex" };
            data.Skills["diplomacy"] = new SkillDef { Id = "diplomacy", Name = "Diplomacy", GoverningAttr = "cha" };

            // Sci-Fi Items/Weapons (no bows, swords)
            data.Items["laser_pistol"] = new ItemDef { Id = "laser_pistol", Name = "Laser Pistol", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 8, ["acc"] = 2, ["range"] = 50 }, Weight = 1.0 };
            data.Items["phaser"] = new ItemDef { Id = "phaser", Name = "Phaser Rifle", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 2, ["dmgMax"] = 10, ["acc"] = 1, ["range"] = 100 }, Weight = 4.0 };
            data.Items["plasma_sword"] = new ItemDef { Id = "plasma_sword", Name = "Plasma Sword", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 2, ["dmgMax"] = 12, ["acc"] = 0 }, Weight = 2.0 };
            data.Items["combat_suit"] = new ItemDef { Id = "combat_suit", Name = "Combat Suit", Type = "armor", Stats = new Dictionary<string, int> { ["ac"] = 4, ["shield"] = 1 }, Weight = 15.0 };
            data.Items["datapad"] = new ItemDef { Id = "datapad", Name = "Datapad", Type = "tool", Stats = new Dictionary<string, int> { ["tech"] = 2 }, Weight = 0.5 };
            data.Items["plasma_pistol"] = new ItemDef { Id = "plasma_pistol", Name = "Plasma Pistol", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 6, ["acc"] = 1 }, Weight = 3.0 };
            data.Items["shield_generator"] = new ItemDef { Id = "shield_generator", Name = "Shield Generator", Type = "device", Stats = new Dictionary<string, int> { ["shield"] = 3 }, Weight = 4.0, Description = "Creates a short-lived energy barrier." };
            data.Items["stealth_cloak"] = new ItemDef { Id = "stealth_cloak", Name = "Stealth Cloak", Type = "gear", Stats = new Dictionary<string, int> { ["stealth"] = 3 }, Weight = 2.5 };
            data.Items["hacking_rig"] = new ItemDef { Id = "hacking_rig", Name = "Hacking Rig", Type = "tool", Stats = new Dictionary<string, int> { ["tech"] = 3 }, Weight = 5.0 };

            // Sci-Fi Rules with tech formulas
            data.Rules.Constants["BaseAC"] = 10;
            data.Rules.Constants["MaxLevel"] = 20;
            data.Rules.Functions["mod"] = "floor((x-10)/2)";
            data.Rules.Formulas["HP"] = "classHitDie + mod(con) * level";
            data.Rules.Formulas["AC"] = "BaseAC + armor + shield + mod(dex)";
            data.Rules.Formulas["AttackBonus"] = "prof + weaponAcc + mod(dex)";
            data.Rules.Formulas["TechPower"] = "prof + mod(int) + tech";

            return data;
        }

        private static PackData GetHorrorData()
        {
            var data = new PackData();

            // Horror Races (mostly humans with variations)
            data.Races["human"] = new RaceDef { Id = "human", Name = "Human", Mods = new Dictionary<string, int> { ["str"] = 0, ["dex"] = 0, ["con"] = 0, ["int"] = 0, ["wis"] = 0, ["cha"] = 0 }, Traits = new[] { "Adaptable", "Sanity Points" } };
            data.Races["survivor"] = new RaceDef { Id = "survivor", Name = "Survivor", Mods = new Dictionary<string, int> { ["str"] = 1, ["dex"] = 1, ["con"] = 2, ["int"] = 0, ["wis"] = 1, ["cha"] = -1 }, Traits = new[] { "Hardy", "Paranoid" } };
            data.Races["occultist"] = new RaceDef { Id = "occultist", Name = "Occultist", Mods = new Dictionary<string, int> { ["str"] = -1, ["dex"] = 0, ["con"] = -1, ["int"] = 2, ["wis"] = 2, ["cha"] = 0 }, Traits = new[] { "Arcane Knowledge", "Fragile Mind" } };
            data.Races["infected"] = new RaceDef { Id = "infected", Name = "Infected", Mods = new Dictionary<string, int> { ["str"] = 2, ["dex"] = -1, ["con"] = 2, ["int"] = -1, ["wis"] = -1, ["cha"] = -2 }, Traits = new[] { "Disease Immunity", "Unnatural" } };

            // Horror Classes
            data.Classes["investigator"] = new ClassDef { Id = "investigator", Name = "Investigator", HitDie = 8, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["medic"] = new ClassDef { Id = "medic", Name = "Medic", HitDie = 8, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["occultist"] = new ClassDef { Id = "occultist", Name = "Occultist", HitDie = 6, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["survivor"] = new ClassDef { Id = "survivor", Name = "Survivor", HitDie = 10, ProficiencyBonusByLevel = GetStandardProgression() };
            data.Classes["scholar"] = new ClassDef { Id = "scholar", Name = "Scholar", HitDie = 6, ProficiencyBonusByLevel = GetStandardProgression() };

            // Horror Skills (Sanity-based)
            data.Skills["investigation"] = new SkillDef { Id = "investigation", Name = "Investigation", GoverningAttr = "int" };
            data.Skills["medicine"] = new SkillDef { Id = "medicine", Name = "Medicine", GoverningAttr = "wis" };
            data.Skills["occult"] = new SkillDef { Id = "occult", Name = "Occult", GoverningAttr = "int" };
            data.Skills["stealth"] = new SkillDef { Id = "stealth", Name = "Stealth", GoverningAttr = "dex" };
            data.Skills["psychology"] = new SkillDef { Id = "psychology", Name = "Psychology", GoverningAttr = "wis" };

            // Horror Items/Weapons (mix of modern and primitive)
            data.Items["shotgun"] = new ItemDef { Id = "shotgun", Name = "Shotgun", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 2, ["dmgMax"] = 12, ["acc"] = 0, ["spread"] = 1 }, Weight = 7.0 };
            data.Items["pistol"] = new ItemDef { Id = "pistol", Name = "Pistol", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 8, ["acc"] = 1 }, Weight = 2.0 };
            data.Items["knife"] = new ItemDef { Id = "knife", Name = "Combat Knife", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 4, ["acc"] = 2 }, Weight = 0.5 };
            data.Items["bow"] = new ItemDef { Id = "bow", Name = "Crossbow", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 10, ["acc"] = 1 }, Weight = 5.0 };
            data.Items["vest"] = new ItemDef { Id = "vest", Name = "Tactical Vest", Type = "armor", Stats = new Dictionary<string, int> { ["ac"] = 3 }, Weight = 8.0 };
            data.Items["flashlight"] = new ItemDef { Id = "flashlight", Name = "Flashlight", Type = "tool", Stats = new Dictionary<string, int> { ["light"] = 1 }, Weight = 1.0 };
            data.Items["revolver"] = new ItemDef { Id = "revolver", Name = "Revolver", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 2, ["dmgMax"] = 6, ["acc"] = 1 }, Weight = 4.0 };
            data.Items["holy_water"] = new ItemDef { Id = "holy_water", Name = "Holy Water", Type = "consumable", Stats = new Dictionary<string, int> { ["banish"] = 2 }, Weight = 0.5 };
            data.Items["first_aid"] = new ItemDef { Id = "first_aid", Name = "First Aid Kit", Type = "consumable", Stats = new Dictionary<string, int> { ["heal"] = 6 }, Weight = 2.5 };
            data.Items["silver_knife"] = new ItemDef { Id = "silver_knife", Name = "Silver Knife", Type = "weapon", Stats = new Dictionary<string, int> { ["dmgMin"] = 1, ["dmgMax"] = 4, ["acc"] = 1, ["banish"] = 1 }, Weight = 1.5 };

            // Horror Rules with sanity system
            data.Rules.Constants["BaseAC"] = 10;
            data.Rules.Constants["MaxLevel"] = 20;
            data.Rules.Constants["BaseSanity"] = 50;
            data.Rules.Functions["mod"] = "floor((x-10)/2)";
            data.Rules.Formulas["HP"] = "classHitDie + mod(con) * level";
            data.Rules.Formulas["AC"] = "BaseAC + armor + mod(dex)";
            data.Rules.Formulas["AttackBonus"] = "prof + weaponAcc + mod(str)";
            data.Rules.Formulas["Sanity"] = "BaseSanity + mod(wis) * 5";

            return data;
        }

        private static int[] GetStandardProgression()
        {
            return new[] { 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6 };
        }

        public static Dictionary<string, RaceDef> GetAvailableRaces()
        {
            return GetCurrentGenreData().Races;
        }

        public static Dictionary<string, ClassDef> GetAvailableClasses()
        {
            return GetCurrentGenreData().Classes;
        }

        public static Dictionary<string, SkillDef> GetAvailableSkills()
        {
            return GetCurrentGenreData().Skills;
        }

        public static Dictionary<string, ItemDef> GetAvailableItems()
        {
            return GetCurrentGenreData().Items;
        }

        public static string GetGenreDisplayName(GameGenre genre)
        {
            return genre switch
            {
                GameGenre.Fantasy => "Fantasy",
                GameGenre.SciFi => "Sci-Fi",
                GameGenre.Horror => "Horror",
                GameGenre.Custom => "Custom",
                _ => "Unknown"
            };
        }
    }
}
