using System;
using System.Collections.Generic;
using System.Linq;
using NovaGM.Models;

namespace NovaGM.Services
{
    /// <summary>
    /// Service for managing character equipment operations
    /// </summary>
    public static class EquipmentService
    {
        // ── Starter equipment ─────────────────────────────────────────────────

        /// <summary>
        /// Returns a genre- and class-appropriate starting loadout.
        /// Covers the five most visible slots: MainHand, OffHand, Chest, Cloak, Feet.
        /// Callers may override individual slots before saving.
        /// </summary>
        public static Dictionary<EquipmentSlot, Item> BuildStarterEquipment(string? classId, GameGenre genre)
        {
            var equipment = new Dictionary<EquipmentSlot, Item>();
            var cls = (classId ?? string.Empty).ToLowerInvariant();

            void Add(EquipmentSlot slot, string name)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    equipment[slot] = new Item { Slot = slot, Name = name };
            }

            switch (genre)
            {
                case GameGenre.Fantasy:
                    if (cls.Contains("wizard") || cls.Contains("mage"))
                    {
                        Add(EquipmentSlot.MainHand, "Wizard's Staff");
                        Add(EquipmentSlot.Cloak,    "Spellweave Cloak");
                        Add(EquipmentSlot.Chest,    "Apprentice Robes");
                    }
                    else if (cls.Contains("cleric"))
                    {
                        Add(EquipmentSlot.MainHand, "Warhammer");
                        Add(EquipmentSlot.OffHand,  "Polished Shield");
                        Add(EquipmentSlot.Chest,    "Scale Mail");
                    }
                    else if (cls.Contains("rogue"))
                    {
                        Add(EquipmentSlot.MainHand, "Twin Daggers");
                        Add(EquipmentSlot.Cloak,    "Shadow Cloak");
                        Add(EquipmentSlot.Chest,    "Soft Leather Armor");
                    }
                    else
                    {
                        Add(EquipmentSlot.MainHand, "Longsword");
                        Add(EquipmentSlot.OffHand,  "Wooden Shield");
                        Add(EquipmentSlot.Chest,    "Chain Shirt");
                    }
                    Add(EquipmentSlot.Feet,  "Traveler's Boots");
                    if (!equipment.ContainsKey(EquipmentSlot.Cloak))
                        Add(EquipmentSlot.Cloak, "Weathered Cloak");
                    break;

                case GameGenre.SciFi:
                    Add(EquipmentSlot.Head, "Tactical Visor");
                    if (cls.Contains("engineer") || cls.Contains("hacker"))
                    {
                        Add(EquipmentSlot.MainHand, "Smart Toolkit");
                        Add(EquipmentSlot.Chest,    "Utility Jumpsuit");
                    }
                    else if (cls.Contains("scientist"))
                    {
                        Add(EquipmentSlot.MainHand, "Research Scanner");
                        Add(EquipmentSlot.Chest,    "Nano-Fabric Lab Coat");
                    }
                    else
                    {
                        Add(EquipmentSlot.MainHand, "Pulse Carbine");
                        Add(EquipmentSlot.OffHand,  "Deployable Shield");
                        Add(EquipmentSlot.Chest,    "Composite Armor Vest");
                    }
                    Add(EquipmentSlot.Feet, "Mag-Boots");
                    break;

                case GameGenre.Horror:
                    Add(EquipmentSlot.MainHand, "Crowbar");
                    Add(EquipmentSlot.OffHand,  "Flashlight");
                    Add(EquipmentSlot.Chest,    "Weathered Jacket");
                    Add(EquipmentSlot.Feet,     "Sturdy Boots");
                    break;

                default:
                    Add(EquipmentSlot.MainHand, "Reliable Blade");
                    Add(EquipmentSlot.OffHand,  "Sturdy Shield");
                    Add(EquipmentSlot.Chest,    "Traveler's Vest");
                    Add(EquipmentSlot.Feet,     "Trail Boots");
                    break;
            }

            return equipment;
        }

        /// <summary>
        /// Merges manual overrides on top of a starter loadout.
        /// Any non-blank override replaces the auto-assigned starter for that slot.
        /// </summary>
        public static Dictionary<EquipmentSlot, Item> MergeOverrides(
            Dictionary<EquipmentSlot, Item> starters,
            Dictionary<EquipmentSlot, string> overrides)
        {
            var result = new Dictionary<EquipmentSlot, Item>(starters);
            foreach (var (slot, name) in overrides)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                result[slot] = new Item { Slot = slot, Name = name.Trim() };
            }
            return result;
        }
        /// <summary>
        /// Equip an item from inventory to an equipment slot
        /// </summary>
        public static bool TryEquipItem(Character character, string itemId, EquipmentSlot slot)
        {
            if (character == null) return false;
            
            // Find item in inventory
            var inventorySlot = character.Inventory.Slots.FirstOrDefault(s => 
                s?.ItemId?.Equals(itemId, StringComparison.OrdinalIgnoreCase) == true);
            
            if (inventorySlot == null) return false;
            
            // Check if slot is already occupied
            if (character.Equipment.ContainsKey(slot))
            {
                // Unequip existing item first
                if (!UnequipItem(character, slot)) return false;
            }
            
            // Create Item from InventoryEntry
            var item = new Item
            {
                Name = inventorySlot.Name,
                Slot = slot,
                StatMods = new Dictionary<string, int>(inventorySlot.Modifiers),
                Description = $"{inventorySlot.Name} equipped to {slot}"
            };
            
            // Add to equipment
            character.Equipment[slot] = item;
            
            // Remove from inventory (1 quantity)
            character.Inventory.Remove(itemId, 1);
            
            return true;
        }
        
        /// <summary>
        /// Unequip an item from equipment slot and return to inventory
        /// </summary>
        public static bool UnequipItem(Character character, EquipmentSlot slot)
        {
            if (character == null) return false;
            if (!character.Equipment.TryGetValue(slot, out var item)) return false;
            
            // Create inventory entry from equipped item
            var entry = new InventoryEntry(
                item.Name.ToLowerInvariant().Replace(" ", "_"),
                item.Name,
                1,
                null,
                item.StatMods
            );
            
            // Add back to inventory
            if (!character.Inventory.TryAdd(entry))
            {
                // Inventory full
                return false;
            }
            
            // Remove from equipment
            character.Equipment.Remove(slot);
            
            return true;
        }
        
        /// <summary>
        /// Calculate total stat with equipment modifiers
        /// </summary>
        public static int CalculateStatWithModifiers(Character character, string statName, int baseStat)
        {
            if (character?.Equipment == null) return baseStat;
            
            int modifier = 0;
            foreach (var item in character.Equipment.Values)
            {
                if (item.StatMods.TryGetValue(statName, out var mod))
                {
                    modifier += mod;
                }
            }
            
            return baseStat + modifier;
        }
        
        /// <summary>
        /// Get all stat modifiers from equipped items
        /// </summary>
        public static Dictionary<string, int> GetAllStatModifiers(Character character)
        {
            var mods = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["STR"] = 0,
                ["DEX"] = 0,
                ["CON"] = 0,
                ["INT"] = 0,
                ["WIS"] = 0,
                ["CHA"] = 0
            };
            
            if (character?.Equipment == null) return mods;
            
            foreach (var item in character.Equipment.Values)
            {
                foreach (var kvp in item.StatMods)
                {
                    if (mods.ContainsKey(kvp.Key))
                    {
                        mods[kvp.Key] += kvp.Value;
                    }
                }
            }
            
            return mods;
        }
        
        /// <summary>
        /// Get list of equippable items from inventory for a specific slot
        /// </summary>
        public static List<InventoryEntry> GetEquippableItems(InventoryGrid inventory, EquipmentSlot slot)
        {
            // For now, return all non-empty inventory slots
            // In future, could add slot validation logic
            return inventory.Slots
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Name))
                .ToList()!;
        }
    }
}
