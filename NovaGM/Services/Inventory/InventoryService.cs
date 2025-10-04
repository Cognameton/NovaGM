using System;
using NovaGM.Models;
using NovaGM.Services.Items;
using NovaGM.Services.State;

namespace NovaGM.Services.Inventory
{
    public sealed class InventoryService
    {
        private readonly IStateStore _stateStore;

        public InventoryService(IStateStore stateStore)
        {
            _stateStore = stateStore;
        }

        public InventoryGrid GetInventory(string key)
        {
            return _stateStore.LoadInventory(key);
        }

        public InventoryGrid GetInventoryForPlayer(string playerName)
        {
            return GetInventory(InventoryKeys.ForPlayer(playerName));
        }

        public InventoryGrid GetInventoryForHubCharacter(string characterName)
        {
            return GetInventory(InventoryKeys.ForHubCharacter(characterName));
        }

        public void SaveInventory(string key, InventoryGrid grid)
        {
            _stateStore.SaveInventory(key, grid);
        }

        public bool TryAddItem(string key, string itemId, int quantity = 1)
        {
            var grid = GetInventory(key);
            var entry = CreateEntry(itemId, quantity);
            if (entry is null) return false;
            if (!grid.TryAdd(entry)) return false;
            SaveInventory(key, grid);
            return true;
        }

        public bool TryRemoveItem(string key, string itemId, int quantity = 1)
        {
            var grid = GetInventory(key);
            if (!grid.Remove(itemId, quantity)) return false;
            SaveInventory(key, grid);
            return true;
        }

        public InventoryEntry? CreateEntry(string itemId, int quantity = 1)
        {
            var entry = ItemCatalog.TryGet(itemId);
            if (entry is null) return null;
            return new InventoryEntry(entry.Id, entry.Name, quantity, entry.IconPath, entry.Stats);
        }
    }
}
