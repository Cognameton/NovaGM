using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NovaGM.Models;

namespace NovaGM.Views
{
    public partial class EquipmentSelectionDialog : Window
    {
        public InventoryEntry? SelectedItem { get; private set; }
        
        public EquipmentSelectionDialog()
        {
            InitializeComponent();
        }
        
        public EquipmentSelectionDialog(EquipmentSlot slot, List<InventoryEntry> items) : this()
        {
            // Set slot label
            var slotLabel = this.FindControl<TextBlock>("SlotLabel");
            if (slotLabel != null)
            {
                slotLabel.Text = $"Slot: {slot}";
            }
            
            // Populate item list
            var itemList = this.FindControl<ListBox>("ItemList");
            if (itemList != null)
            {
                itemList.ItemsSource = items;
                if (items.Any())
                {
                    itemList.SelectedIndex = 0;
                }
            }
        }
        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        private void Equip_Click(object? sender, RoutedEventArgs e)
        {
            var itemList = this.FindControl<ListBox>("ItemList");
            if (itemList?.SelectedItem is InventoryEntry entry)
            {
                SelectedItem = entry;
                Close(entry);
            }
        }
        
        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            SelectedItem = null;
            Close(null);
        }
    }
}
