using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services.Packs;

namespace NovaGM.Views
{
    public partial class CustomClassDialog : Window
    {
        public CustomClassDialog()
        {
            InitializeComponent();
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
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

        private void OnCreateClick(object? sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text?.Trim();
            var id = IdTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError("Please enter a class name.");
                return;
            }

            if (string.IsNullOrEmpty(id))
            {
                ShowError("Please enter a class ID.");
                return;
            }

            var hitDie = HitDieComboBox.SelectedIndex switch
            {
                0 => 6,
                1 => 8,
                2 => 10,
                3 => 12,
                _ => 8
            };

            var classDef = new ClassDef
            {
                Id = id,
                Name = name,
                HitDie = hitDie,
                ProficiencyBonusByLevel = GetStandardProgression()
            };

            Close(classDef);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private static int[] GetStandardProgression()
        {
            return new[] { 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6 };
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