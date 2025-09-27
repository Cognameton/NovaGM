using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services;
using NovaGM.Services.State;
using NovaGM.ViewModels;

namespace NovaGM.Views
{
    public partial class SaveMissionWindow : Window
    {
        private readonly IStateStore _stateStore;
        private readonly IEnumerable<Message> _messages;

        public SaveMissionWindow() : this(null!, Enumerable.Empty<Message>())
        {
            // Design-time constructor
        }

        public SaveMissionWindow(IStateStore stateStore, IEnumerable<Message> messages)
        {
            _stateStore = stateStore;
            _messages = messages;
            
            InitializeComponent();
            
            // Initialize with suggested name
            var nameBox = this.FindControl<TextBox>("TxtMissionName");
            if (nameBox != null)
            {
                nameBox.Text = GenerateSuggestedName();
            }

            // Update preview initially
            UpdatePreview();
            
            // Wire up events for live preview
            if (nameBox != null)
                nameBox.TextChanged += (s, e) => UpdatePreview();
            
            var descBox = this.FindControl<TextBox>("TxtDescription");
            if (descBox != null)
                descBox.TextChanged += (s, e) => UpdatePreview();

            var genreCombo = this.FindControl<ComboBox>("CmbGenre");
            if (genreCombo != null)
                genreCombo.SelectionChanged += (s, e) => UpdatePreview();

            var diffCombo = this.FindControl<ComboBox>("CmbDifficulty");
            if (diffCombo != null)
                diffCombo.SelectionChanged += (s, e) => UpdatePreview();

            // Set session info
            UpdateSessionInfo();
        }

        private void UpdateSessionInfo()
        {
            var statsLabel = this.FindControl<TextBlock>("LblSessionStats");
            var gameStateLabel = this.FindControl<TextBlock>("LblGameState");

            if (statsLabel != null)
            {
                if (_messages == null)
                {
                    statsLabel.Text = "No session data";
                }
                else
                {
                    var messageList = _messages.ToList();
                    var gmMessages = messageList.Count(m => m.Role == "GM");
                    var playerMessages = messageList.Count(m => m.Role != "GM");
                    statsLabel.Text = $"Session: {messageList.Count} total messages ({gmMessages} GM, {playerMessages} player)";
                }
            }

            if (gameStateLabel != null)
            {
                if (_stateStore == null)
                {
                    gameStateLabel.Text = "No game state";
                }
                else
                {
                    var state = _stateStore.Load();
                    var parts = new List<string>();
                    
                    if (!string.IsNullOrWhiteSpace(state.Location))
                        parts.Add($"Location: {state.Location}");
                        
                    if (state.Npcs.Count > 0)
                        parts.Add($"NPCs: {state.Npcs.Count}");
                        
                    if (state.Facts.Count > 0)
                        parts.Add($"Facts: {state.Facts.Count}");
                        
                    if (state.Flags.Count > 0)
                        parts.Add($"Flags: {state.Flags.Count}");
                    
                    gameStateLabel.Text = parts.Count > 0 ? string.Join(", ", parts) : "Empty game state";
                }
            }
        }

        private void UpdatePreview()
        {
            var nameBox = this.FindControl<TextBox>("TxtMissionName");
            var descBox = this.FindControl<TextBox>("TxtDescription");
            var genreCombo = this.FindControl<ComboBox>("CmbGenre");
            var diffCombo = this.FindControl<ComboBox>("CmbDifficulty");
            var previewLabel = this.FindControl<TextBlock>("LblPreview");

            if (previewLabel == null) return;

            var missionName = nameBox?.Text?.Trim() ?? "";
            var description = descBox?.Text?.Trim() ?? "";
            var genre = (genreCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Fantasy";
            var difficulty = (diffCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Medium";

            if (string.IsNullOrWhiteSpace(missionName))
            {
                previewLabel.Text = "Enter a mission name to see preview...";
                return;
            }
            
            var preview = $"Mission: {missionName}\n";
            preview += $"Genre: {genre} | Difficulty: {difficulty}\n\n";
            
            if (!string.IsNullOrWhiteSpace(description))
            {
                preview += description;
            }
            else if (_stateStore != null)
            {
                var state = _stateStore.Load();
                if (!string.IsNullOrWhiteSpace(state.Premise))
                {
                    preview += $"Based on: {state.Premise}";
                }
                else if (_messages?.Any() == true)
                {
                    var firstGM = _messages.FirstOrDefault(m => m.Role == "GM");
                    if (firstGM != null)
                    {
                        var excerpt = firstGM.Content.Length > 100 
                            ? firstGM.Content.Substring(0, 100) + "..."
                            : firstGM.Content;
                        preview += $"Opening: {excerpt}";
                    }
                }
            }
            
            previewLabel.Text = preview;
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private void SaveMission_Click(object? sender, RoutedEventArgs e)
        {
            var nameBox = this.FindControl<TextBox>("TxtMissionName");
            var missionName = nameBox?.Text?.Trim();

            if (string.IsNullOrWhiteSpace(missionName))
            {
                ShowErrorDialog("Mission name is required.");
                return;
            }

            try
            {
                var descBox = this.FindControl<TextBox>("TxtDescription");
                var genreCombo = this.FindControl<ComboBox>("CmbGenre");
                var diffCombo = this.FindControl<ComboBox>("CmbDifficulty");

                var description = descBox?.Text?.Trim() ?? "";
                var genre = (genreCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "fantasy";
                var difficulty = (diffCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "medium";

                var savedPath = MissionService.SaveCurrentSessionAsMission(
                    _stateStore,
                    _messages,
                    missionName,
                    description,
                    genre,
                    difficulty
                );

                Close(savedPath);
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"Failed to save mission: {ex.Message}");
            }
        }

        private string GenerateSuggestedName()
        {
            if (_stateStore == null) return "Custom Mission";
            
            var state = _stateStore.Load();
            
            // Try to extract name from premise or location
            if (!string.IsNullOrWhiteSpace(state.Premise))
            {
                var words = state.Premise.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 2)
                {
                    return string.Join(" ", words.Take(3));
                }
            }
            
            if (!string.IsNullOrWhiteSpace(state.Location))
            {
                return $"Adventure in {state.Location}";
            }
            
            // Use date as fallback
            return $"Mission {DateTime.Now:yyyy-MM-dd}";
        }

        private void ShowErrorDialog(string message)
        {
            var errorBox = new Window
            {
                Title = "Error",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            
            var stackPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 10
            };
            
            stackPanel.Children.Add(new TextBlock 
            { 
                Text = message, 
                TextWrapping = Avalonia.Media.TextWrapping.Wrap 
            });
            
            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            okButton.Click += (s, args) => errorBox.Close();
            stackPanel.Children.Add(okButton);
            
            errorBox.Content = stackPanel;
            errorBox.ShowDialog(this);
        }
    }
}