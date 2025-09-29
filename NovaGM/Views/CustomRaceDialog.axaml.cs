using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services.Packs;

namespace NovaGM.Views
{
    public partial class CustomRaceDialog : Window
    {
        private readonly ObservableCollection<string> _traits = new();

        public CustomRaceDialog()
        {
            InitializeComponent();
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            TraitsList.ItemsSource = _traits;
            
            AddTraitButton.Click += OnAddTraitClick;
            CreateButton.Click += OnCreateClick;
            CancelButton.Click += OnCancelClick;

            // Auto-generate ID from name
            NameTextBox.TextChanged += (s, e) =>
            {
                if (NameTextBox.Text != null)
                {
                    var id = NameTextBox.Text.ToLower().Replace(" ", "_").Replace("-", "_");
                    // Remove any non-alphanumeric characters except underscore
                    id = new string(id.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                    IdTextBox.Text = id;
                }
            };
        }

        private void OnAddTraitClick(object? sender, RoutedEventArgs e)
        {
            var trait = TraitTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(trait) && !_traits.Contains(trait))
            {
                _traits.Add(trait);
                TraitTextBox.Text = "";
            }
        }

        private void OnCreateClick(object? sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text?.Trim();
            var id = IdTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError("Please enter a race name.");
                return;
            }

            if (string.IsNullOrEmpty(id))
            {
                ShowError("Please enter a race ID.");
                return;
            }

            var race = new RaceDef
            {
                Id = id,
                Name = name,
                Mods = new Dictionary<string, int>
                {
                    ["str"] = (int)StrModifier.Value,
                    ["dex"] = (int)DexModifier.Value,
                    ["con"] = (int)ConModifier.Value,
                    ["int"] = (int)IntModifier.Value,
                    ["wis"] = (int)WisModifier.Value,
                    ["cha"] = (int)ChaModifier.Value
                },
                Traits = _traits.ToArray()
            };

            Close(race);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private async void ShowError(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                PrimaryButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
    }
}