using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NovaGM.Models;
using NovaGM.Services.Packs;
using NovaGM.Services.State;
using NovaGM.ViewModels;

namespace NovaGM.Services
{
    /// <summary>
    /// Service for saving and loading missions/scenarios
    /// </summary>
    public static class MissionService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Save the current session as a reusable mission
        /// </summary>
        public static string SaveCurrentSessionAsMission(
            IStateStore stateStore,
            IEnumerable<NovaGM.ViewModels.Message> messages,
            string missionName,
            string description = "",
            string genre = "fantasy",
            string difficulty = "medium")
        {
            var gameState = stateStore.Load();
            var messageList = messages.ToList();

            // Generate unique mission ID
            var missionId = GenerateMissionId(missionName);
            
            // Extract narrative content from messages
            var narrative = ExtractNarrative(messageList);
            
            // Create mission object
            var mission = new Mission
            {
                Id = missionId,
                Name = missionName,
                Description = string.IsNullOrWhiteSpace(description) ? GenerateDescription(narrative, gameState) : description,
                Genre = genre,
                Difficulty = difficulty,
                EstimatedDuration = EstimateDuration(messageList.Count),
                Tags = GenerateTags(narrative, gameState, genre),
                CreatedAt = DateTime.UtcNow,
                InitialState = new MissionState
                {
                    Location = gameState.Location,
                    Premise = gameState.Premise,
                    Flags = gameState.Flags.ToList(),
                    Npcs = new Dictionary<string, string>(gameState.Npcs),
                    Facts = gameState.Facts.ToList(),
                    SuggestedLevel = 1,
                    PartySize = "3-5 players"
                },
                Narrative = narrative,
                Encounters = ExtractEncounters(messageList),
                Metadata = new Dictionary<string, object>
                {
                    ["originalSessionLength"] = messageList.Count,
                    ["creationMethod"] = "session_export",
                    ["sourcePack"] = PackManager.GetActiveId() ?? "classic"
                }
            };

            // Save to active pack or create new pack
            var savedPath = SaveMissionToPack(mission);
            
            return savedPath;
        }

        /// <summary>
        /// Load a mission by ID from the active pack
        /// </summary>
        public static Mission? LoadMission(string missionId)
        {
            var activePackDir = GetActivePackMissionsDir();
            var missionFile = Path.Combine(activePackDir, $"{missionId}.json");

            if (!File.Exists(missionFile))
                return null;

            try
            {
                var json = File.ReadAllText(missionFile);
                return JsonSerializer.Deserialize<Mission>(json, _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// List all available missions in the active pack
        /// </summary>
        public static List<Mission> ListAvailableMissions()
        {
            var missions = new List<Mission>();
            var activePackDir = GetActivePackMissionsDir();

            if (!Directory.Exists(activePackDir))
                return missions;

            foreach (var file in Directory.GetFiles(activePackDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var mission = JsonSerializer.Deserialize<Mission>(json, _jsonOptions);
                    if (mission != null)
                        missions.Add(mission);
                }
                catch
                {
                    // Skip invalid mission files
                }
            }

            return missions.OrderBy(m => m.Name).ToList();
        }

        /// <summary>
        /// Delete a mission from the active pack
        /// </summary>
        public static bool DeleteMission(string missionId)
        {
            var activePackDir = GetActivePackMissionsDir();
            var missionFile = Path.Combine(activePackDir, $"{missionId}.json");

            if (!File.Exists(missionFile))
                return false;

            try
            {
                File.Delete(missionFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SaveMissionToPack(Mission mission)
        {
            var activePackDir = GetActivePackMissionsDir();
            Directory.CreateDirectory(activePackDir);

            var missionFile = Path.Combine(activePackDir, $"{mission.Id}.json");
            var json = JsonSerializer.Serialize(mission, _jsonOptions);
            
            File.WriteAllText(missionFile, json);
            return missionFile;
        }

        private static string GetActivePackMissionsDir()
        {
            var packId = PackManager.GetActiveId() ?? "classic";
            var baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "packs", packId, "missions");
        }

        private static string GenerateMissionId(string name)
        {
            // Convert name to safe filename
            var safeName = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9\s\-]", "")
                               .Trim()
                               .Replace(' ', '-');
            
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "mission";

            // Add timestamp to ensure uniqueness
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            return $"{safeName}-{timestamp}";
        }

        private static MissionNarrative ExtractNarrative(List<NovaGM.ViewModels.Message> messages)
        {
            var gmMessages = messages.Where(m => m.Role == "GM").ToList();
            var playerMessages = messages.Where(m => m.Role != "GM").ToList();

            var narrative = new MissionNarrative();

            if (gmMessages.Count > 0)
            {
                // Use first GM message as opening text
                narrative.OpeningText = gmMessages.First().Content;
                
                // Extract key events from GM messages
                narrative.KeyEvents = gmMessages
                    .Skip(1)
                    .Select(m => m.Content)
                    .Where(content => !string.IsNullOrWhiteSpace(content))
                    .Take(10)
                    .ToList();

                // Use last GM message as potential conclusion
                if (gmMessages.Count > 1)
                    narrative.Conclusion = gmMessages.Last().Content;
            }

            // Extract objectives from player actions
            narrative.Objectives = ExtractObjectivesFromMessages(playerMessages);
            
            // Generate hooks based on the session content
            narrative.Hooks = GenerateHooks(gmMessages);

            return narrative;
        }

        private static List<string> ExtractObjectivesFromMessages(List<NovaGM.ViewModels.Message> playerMessages)
        {
            var objectives = new List<string>();
            
            // Look for common action patterns that suggest objectives
            var actionPatterns = new[]
            {
                @"find\s+(\w+)",
                @"rescue\s+(\w+)",
                @"defeat\s+(\w+)",
                @"explore\s+(\w+)",
                @"investigate\s+(\w+)",
                @"retrieve\s+(\w+)",
                @"protect\s+(\w+)"
            };

            foreach (var message in playerMessages.Take(10)) // Analyze first 10 player messages
            {
                foreach (var pattern in actionPatterns)
                {
                    var matches = Regex.Matches(message.Content, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        var objective = $"Player sought to {match.Value}";
                        if (!objectives.Contains(objective))
                            objectives.Add(objective);
                    }
                }
            }

            // Add generic objectives if none found
            if (objectives.Count == 0)
            {
                objectives.Add("Complete the adventure successfully");
                objectives.Add("Work together as a team");
            }

            return objectives;
        }

        private static List<string> GenerateHooks(List<NovaGM.ViewModels.Message> gmMessages)
        {
            var hooks = new List<string>();
            
            if (gmMessages.Count > 0)
            {
                // Extract potential hooks from first few GM messages
                var earlyMessages = gmMessages.Take(3);
                foreach (var message in earlyMessages)
                {
                    var sentences = message.Content.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var sentence in sentences.Take(2))
                    {
                        var cleaned = sentence.Trim();
                        if (cleaned.Length > 20 && cleaned.Length < 150)
                        {
                            hooks.Add(cleaned);
                        }
                    }
                }
            }

            // Default hooks if none extracted
            if (hooks.Count == 0)
            {
                hooks.Add("A mysterious opportunity presents itself");
                hooks.Add("Local authorities seek capable adventurers");
                hooks.Add("An urgent situation requires immediate attention");
            }

            return hooks.Take(5).ToList();
        }

        private static List<Encounter> ExtractEncounters(List<Message> messages)
        {
            var encounters = new List<Encounter>();
            
            // Simple heuristic: look for dramatic moments in GM responses
            var gmMessages = messages.Where(m => m.Role == "GM").ToList();
            
            for (int i = 0; i < gmMessages.Count; i++)
            {
                var message = gmMessages[i];
                
                // Look for encounter keywords
                var content = message.Content.ToLowerInvariant();
                var isEncounter = content.Contains("attack") || 
                                 content.Contains("combat") || 
                                 content.Contains("enemy") || 
                                 content.Contains("challenge") ||
                                 content.Contains("roll") ||
                                 content.Contains("defend");

                if (isEncounter)
                {
                    var encounterType = DetermineEncounterType(content);
                    encounters.Add(new Encounter
                    {
                        Id = $"encounter_{i + 1}",
                        Name = $"Encounter {i + 1}",
                        Type = encounterType,
                        Description = message.Content,
                        Trigger = "During the adventure progression",
                        Difficulty = "medium",
                        Rewards = new List<string> { "Experience and story progression" }
                    });
                }
            }

            return encounters;
        }

        private static string DetermineEncounterType(string content)
        {
            if (content.Contains("attack") || content.Contains("combat") || content.Contains("enemy"))
                return "combat";
            if (content.Contains("puzzle") || content.Contains("riddle") || content.Contains("solve"))
                return "puzzle";
            if (content.Contains("talk") || content.Contains("negotiate") || content.Contains("persuade"))
                return "social";
            if (content.Contains("explore") || content.Contains("search") || content.Contains("investigate"))
                return "exploration";
                
            return "general";
        }

        private static string GenerateDescription(MissionNarrative narrative, GameState gameState)
        {
            var parts = new List<string>();
            
            if (!string.IsNullOrWhiteSpace(gameState.Premise))
                parts.Add(gameState.Premise);
                
            if (!string.IsNullOrWhiteSpace(narrative.OpeningText))
                parts.Add(narrative.OpeningText);
                
            if (parts.Count == 0)
                return "A custom mission created from a gameplay session.";
                
            var description = string.Join(" ", parts);
            return description.Length > 300 ? description.Substring(0, 300) + "..." : description;
        }

        private static List<string> GenerateTags(MissionNarrative narrative, GameState gameState, string genre)
        {
            var tags = new List<string> { genre, "custom", "session-generated" };
            
            // Add location-based tags
            if (!string.IsNullOrWhiteSpace(gameState.Location))
            {
                var location = gameState.Location.ToLowerInvariant();
                if (location.Contains("dungeon") || location.Contains("cave"))
                    tags.Add("dungeon");
                if (location.Contains("forest") || location.Contains("wood"))
                    tags.Add("wilderness");
                if (location.Contains("city") || location.Contains("town"))
                    tags.Add("urban");
            }

            // Add content-based tags
            var allText = (narrative.OpeningText + " " + string.Join(" ", narrative.KeyEvents)).ToLowerInvariant();
            
            if (allText.Contains("dragon") || allText.Contains("monster"))
                tags.Add("monsters");
            if (allText.Contains("treasure") || allText.Contains("gold"))
                tags.Add("treasure");
            if (allText.Contains("magic") || allText.Contains("spell"))
                tags.Add("magic");
            if (allText.Contains("mystery") || allText.Contains("investigate"))
                tags.Add("mystery");

            return tags.Distinct().ToList();
        }

        private static string EstimateDuration(int messageCount)
        {
            return messageCount switch
            {
                < 20 => "1-2 hours",
                < 50 => "2-3 hours", 
                < 100 => "3-4 hours",
                _ => "4+ hours"
            };
        }
    }
}