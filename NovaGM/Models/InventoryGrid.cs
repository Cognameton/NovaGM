using System;
using System.Collections.Generic;
using System.Linq;

namespace NovaGM.Models
{
    public sealed class InventoryGrid
    {
        public const int Width = 7;
        public const int Height = 7;
        private readonly InventoryEntry?[] _slots;

        public InventoryGrid()
        {
            _slots = new InventoryEntry?[Width * Height];
        }

        private InventoryGrid(InventoryEntry?[] slots)
        {
            _slots = slots;
        }

        public IReadOnlyList<InventoryEntry?> Slots => _slots;

        public bool TryAdd(InventoryEntry entry)
        {
            if (entry is null) return false;

            // Try stacking with existing entry of same item id
            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] is { } existing && existing.ItemId.Equals(entry.ItemId, StringComparison.OrdinalIgnoreCase))
                {
                    existing.AddQuantity(entry.Quantity);
                    return true;
                }
            }

            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] is null)
                {
                    _slots[i] = entry;
                    return true;
                }
            }
            return false;
        }

        public bool Remove(string itemId, int quantity = 1)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return false;

            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] is { } existing && existing.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!existing.RemoveQuantity(quantity)) return false;
                    if (existing.Quantity <= 0)
                    {
                        _slots[i] = null;
                    }
                    return true;
                }
            }
            return false;
        }

        public void Clear()
        {
            Array.Fill(_slots, null);
        }

        public InventoryGridSnapshot ToSnapshot()
        {
            var cells = new List<InventoryCellSnapshot>(_slots.Length);
            foreach (var slot in _slots)
            {
                if (slot is null)
                {
                    cells.Add(new InventoryCellSnapshot());
                }
                else
                {
                    cells.Add(new InventoryCellSnapshot
                    {
                        ItemId = slot.ItemId,
                        Name = slot.Name,
                        Quantity = slot.Quantity,
                        IconPath = slot.IconPath,
                        Modifiers = slot.Modifiers.ToDictionary(k => k.Key, v => v.Value)
                    });
                }
            }
            return new InventoryGridSnapshot { Cells = cells };
        }

        public static InventoryGrid FromSnapshot(InventoryGridSnapshot? snapshot)
        {
            if (snapshot?.Cells is null || snapshot.Cells.Count == 0)
            {
                return new InventoryGrid();
            }

            var slots = new InventoryEntry?[Width * Height];
            var count = Math.Min(slots.Length, snapshot.Cells.Count);
            for (var i = 0; i < count; i++)
            {
                var cell = snapshot.Cells[i];
                if (string.IsNullOrWhiteSpace(cell.ItemId))
                {
                    slots[i] = null;
                    continue;
                }
                slots[i] = new InventoryEntry(
                    cell.ItemId,
                    cell.Name ?? cell.ItemId,
                    cell.Quantity <= 0 ? 1 : cell.Quantity,
                    cell.IconPath,
                    cell.Modifiers ?? new Dictionary<string, int>());
            }
            return new InventoryGrid(slots);
        }
    }

    public sealed class InventoryGridSnapshot
    {
        public List<InventoryCellSnapshot> Cells { get; set; } = new();
    }

    public sealed class InventoryCellSnapshot
    {
        public string? ItemId { get; set; }
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public string? IconPath { get; set; }
        public Dictionary<string, int>? Modifiers { get; set; }
    }
}
