using System.Collections.Generic;
using System.Linq;
using NovaGM.Models;

namespace NovaGM.ViewModels
{
    public sealed class CharacterSheetViewModel
    {
        public CharacterSheetViewModel(Character c) => Character = c;
        public Character Character { get; }

        public string Header => $"{Character.Name} — {Character.Race} {Character.Class} (Lv {Character.Level})";

        // Expose convenient read-only strings for slot labels
        public string Head     => GetItemName(EquipmentSlot.Head);
        public string Neck     => GetItemName(EquipmentSlot.Neck);
        public string Cloak    => GetItemName(EquipmentSlot.Cloak);
        public string Chest    => GetItemName(EquipmentSlot.Chest);
        public string Hands    => GetItemName(EquipmentSlot.Hands);
        public string Belt     => GetItemName(EquipmentSlot.Belt);
        public string Legs     => GetItemName(EquipmentSlot.Legs);
        public string Feet     => GetItemName(EquipmentSlot.Feet);
        public string MainHand => GetItemName(EquipmentSlot.MainHand);
        public string OffHand  => GetItemName(EquipmentSlot.OffHand);
        public string Ring1    => GetItemName(EquipmentSlot.Ring1);
        public string Ring2    => GetItemName(EquipmentSlot.Ring2);

        public Stats S => Character.Stats;

        public int STR => S.STR; public int STRMod => Stats.Mod(S.STR);
        public int DEX => S.DEX; public int DEXMod => Stats.Mod(S.DEX);
        public int CON => S.CON; public int CONMod => Stats.Mod(S.CON);
        public int INT => S.INT; public int INTMod => Stats.Mod(S.INT);
        public int WIS => S.WIS; public int WISMod => Stats.Mod(S.WIS);
        public int CHA => S.CHA; public int CHAMod => Stats.Mod(S.CHA);

        public List<Item> EquippedList => Character.Equipment.Values.ToList();

        private string GetItemName(EquipmentSlot slot)
            => Character.Equipment.TryGetValue(slot, out var item) ? item.Name : "—";
    }
}
