using System.Collections.Generic;

namespace NovaGM.Models
{
    public sealed class Item
    {
        public string Name { get; set; } = "";
        public EquipmentSlot Slot { get; set; }
        public Dictionary<string,int> StatMods { get; set; } = new(); // e.g. { "STR": 1 }
        public string? Description { get; set; }
    }
}
