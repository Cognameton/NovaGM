using NovaGM.Models;

namespace NovaGM.ViewModels
{
    public sealed class InventorySlotViewModel
    {
        public InventorySlotViewModel(InventoryEntry? entry)
        {
            if (entry is null)
            {
                IsEmpty = true;
                Name = "";
                Quantity = 0;
                IconPath = string.Empty;
            }
            else
            {
                IsEmpty = false;
                Name = entry.Name;
                Quantity = entry.Quantity;
                IconPath = entry.IconPath ?? string.Empty;
            }
        }

        public bool IsEmpty { get; }
        public string Name { get; }
        public int Quantity { get; }
        public string IconPath { get; }

        public string QuantityDisplay => Quantity > 1 ? Quantity.ToString() : string.Empty;
        public string Tooltip => IsEmpty ? "Empty" : Quantity > 1 ? $"{Name} x{Quantity}" : Name;
    }
}
