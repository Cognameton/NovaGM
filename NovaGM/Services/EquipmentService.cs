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
