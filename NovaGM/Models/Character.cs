using System.Collections.Generic;

namespace NovaGM.Models
{
    public sealed class Character
    {
        public string Name { get; set; } = "Adventurer";
        public string Race { get; set; } = "Human";
        public string Class { get; set; } = "Fighter";
        public int Level { get; set; } = 1;
        public Stats Stats { get; set; } = new();
        public Dictionary<EquipmentSlot, Item> Equipment { get; set; } = new();
    }
}
