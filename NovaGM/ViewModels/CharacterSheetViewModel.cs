using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NovaGM.Models;
using NovaGM.Services;

namespace NovaGM.ViewModels
{
    public sealed class CharacterSheetViewModel : INotifyPropertyChanged
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

        // Stats with equipment modifiers
        private Dictionary<string, int> _statMods => EquipmentService.GetAllStatModifiers(Character);
        
        public int STR => EquipmentService.CalculateStatWithModifiers(Character, "STR", S.STR);
        public int STRBase => S.STR;
        public int STRBonus => _statMods["STR"];
        public string STRDisplay => STRBonus != 0 ? $"{STR} ({S.STR}{(STRBonus > 0 ? "+" : "")}{STRBonus})" : STR.ToString();
        
        public int DEX => EquipmentService.CalculateStatWithModifiers(Character, "DEX", S.DEX);
        public int DEXBase => S.DEX;
        public int DEXBonus => _statMods["DEX"];
        public string DEXDisplay => DEXBonus != 0 ? $"{DEX} ({S.DEX}{(DEXBonus > 0 ? "+" : "")}{DEXBonus})" : DEX.ToString();
        
        public int CON => EquipmentService.CalculateStatWithModifiers(Character, "CON", S.CON);
        public int CONBase => S.CON;
        public int CONBonus => _statMods["CON"];
        public string CONDisplay => CONBonus != 0 ? $"{CON} ({S.CON}{(CONBonus > 0 ? "+" : "")}{CONBonus})" : CON.ToString();
        
        public int INT => EquipmentService.CalculateStatWithModifiers(Character, "INT", S.INT);
        public int INTBase => S.INT;
        public int INTBonus => _statMods["INT"];
        public string INTDisplay => INTBonus != 0 ? $"{INT} ({S.INT}{(INTBonus > 0 ? "+" : "")}{INTBonus})" : INT.ToString();
        
        public int WIS => EquipmentService.CalculateStatWithModifiers(Character, "WIS", S.WIS);
        public int WISBase => S.WIS;
        public int WISBonus => _statMods["WIS"];
        public string WISDisplay => WISBonus != 0 ? $"{WIS} ({S.WIS}{(WISBonus > 0 ? "+" : "")}{WISBonus})" : WIS.ToString();
        
        public int CHA => EquipmentService.CalculateStatWithModifiers(Character, "CHA", S.CHA);
        public int CHABase => S.CHA;
        public int CHABonus => _statMods["CHA"];
        public string CHADisplay => CHABonus != 0 ? $"{CHA} ({S.CHA}{(CHABonus > 0 ? "+" : "")}{CHABonus})" : CHA.ToString();

        public int STRMod => Stats.Mod(STR);
        public int DEXMod => Stats.Mod(DEX);
        public int CONMod => Stats.Mod(CON);
        public int INTMod => Stats.Mod(INT);
        public int WISMod => Stats.Mod(WIS);
        public int CHAMod => Stats.Mod(CHA);

        public List<Item> EquippedList => Character.Equipment.Values.ToList();

        public IReadOnlyList<InventorySlotViewModel> InventorySlots
            => Character.Inventory.Slots.Select(slot => new InventorySlotViewModel(slot)).ToList();

        private string GetItemName(EquipmentSlot slot)
            => Character.Equipment.TryGetValue(slot, out var item) ? item.Name : "—";
        
        /// <summary>
        /// Refresh all bindings after equipment changes
        /// </summary>
        public void RefreshAll()
        {
            OnPropertyChanged(nameof(Head));
            OnPropertyChanged(nameof(Neck));
            OnPropertyChanged(nameof(Cloak));
            OnPropertyChanged(nameof(Chest));
            OnPropertyChanged(nameof(Hands));
            OnPropertyChanged(nameof(Belt));
            OnPropertyChanged(nameof(Legs));
            OnPropertyChanged(nameof(Feet));
            OnPropertyChanged(nameof(MainHand));
            OnPropertyChanged(nameof(OffHand));
            OnPropertyChanged(nameof(Ring1));
            OnPropertyChanged(nameof(Ring2));
            OnPropertyChanged(nameof(EquippedList));
            OnPropertyChanged(nameof(InventorySlots));
            OnPropertyChanged(nameof(STR));
            OnPropertyChanged(nameof(STRDisplay));
            OnPropertyChanged(nameof(DEX));
            OnPropertyChanged(nameof(DEXDisplay));
            OnPropertyChanged(nameof(CON));
            OnPropertyChanged(nameof(CONDisplay));
            OnPropertyChanged(nameof(INT));
            OnPropertyChanged(nameof(INTDisplay));
            OnPropertyChanged(nameof(WIS));
            OnPropertyChanged(nameof(WISDisplay));
            OnPropertyChanged(nameof(CHA));
            OnPropertyChanged(nameof(CHADisplay));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
