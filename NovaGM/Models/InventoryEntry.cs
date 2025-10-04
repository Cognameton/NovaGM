using System.Collections.Generic;

namespace NovaGM.Models
{
    public sealed class InventoryEntry
    {
        public InventoryEntry(string itemId, string name, int quantity = 1, string? iconPath = null, Dictionary<string, int>? modifiers = null)
        {
            ItemId = itemId;
            Name = name;
            Quantity = quantity < 1 ? 1 : quantity;
            IconPath = iconPath;
            Modifiers = modifiers ?? new Dictionary<string, int>();
        }

        public string ItemId { get; }
        public string Name { get; }
        public int Quantity { get; private set; }
        public string? IconPath { get; }
        public Dictionary<string, int> Modifiers { get; }

        public void AddQuantity(int amount)
        {
            if (amount <= 0) return;
            Quantity += amount;
        }

        public bool RemoveQuantity(int amount)
        {
            if (amount <= 0) return false;
            if (amount > Quantity) return false;
            Quantity -= amount;
            return true;
        }
    }
}
