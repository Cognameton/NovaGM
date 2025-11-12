using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NovaGM.Models;
using NovaGM.Services;
using NovaGM.ViewModels;

namespace NovaGM.Views
{
    public partial class CharacterSheetView : UserControl
    {
        public CharacterSheetView()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
        
        private async void EquipmentSlot_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            if (DataContext is not CharacterSheetViewModel viewModel) return;
            
            // Parse slot from button tag
            var slotName = button.Tag?.ToString();
            if (string.IsNullOrEmpty(slotName)) return;
            if (!Enum.TryParse<EquipmentSlot>(slotName, out var slot)) return;
            
            var character = viewModel.Character;
            
            // Check if slot is already occupied
            if (character.Equipment.ContainsKey(slot))
            {
                // Unequip
                if (EquipmentService.UnequipItem(character, slot))
                {
                    viewModel.RefreshAll();
                }
            }
            else
            {
                // Show equipment selection dialog
                var items = EquipmentService.GetEquippableItems(character.Inventory, slot);
                
                if (!items.Any())
                {
                    // No items to equip
                    return;
                }
                
                var dialog = new EquipmentSelectionDialog(slot, items);
                
                // Get owner window
                Window? owner = null;
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    owner = desktop.MainWindow;
                }
                
                var result = owner != null 
                    ? await dialog.ShowDialog<InventoryEntry?>(owner) 
                    : null;
                
                if (result != null)
                {
                    // Equip selected item
                    if (EquipmentService.TryEquipItem(character, result.ItemId, slot))
                    {
                        viewModel.RefreshAll();
                    }
                }
            }
        }
    }
}
