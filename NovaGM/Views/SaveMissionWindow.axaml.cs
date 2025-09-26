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
    public partial class SaveMissionWindow : Window, INotifyPropertyChanged
    {
        private readonly IStateStore _stateStore;
        private readonly IEnumerable<Message> _messages;
        
        private string _missionName = "";
        private string _description = "";
        private string _genre = "Fantasy";
        private string _difficulty = "Medium";

        public SaveMissionWindow() : this(null!, Enumerable.Empty<Message>())
        {
            // Design-time constructor
        }

        public SaveMissionWindow(IStateStore stateStore, IEnumerable<Message> messages)
        {
            _stateStore = stateStore;
            _messages = messages;
            
            InitializeComponent();
            DataContext = this;
            
            // Initialize with suggested name
            MissionName = GenerateSuggestedName();
            
            // Set defaults
            var genreCombo = this.FindControl<ComboBox>("CmbGenre");
            var difficultyCombo = this.FindControl<ComboBox>("CmbDifficulty");
            
            if (genreCombo != null) genreCombo.SelectedIndex = 0; // Fantasy
            if (difficultyCombo != null) difficultyCombo.SelectedIndex = 1; // Medium
        }

        public string MissionName
        {
            get => _missionName;
            set
            {
                if (_missionName != value)
                {
                    _missionName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MissionPreview));
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MissionPreview));
                }
            }
        }

        public string Genre
        {
            get => _genre;
            set
            {
                if (_genre != value)
                {
                    _genre = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MissionPreview));
                }
            }
        }

        public string Difficulty
        {
            get => _difficulty;
            set
            {
                if (_difficulty != value)
                {
                    _difficulty = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MissionPreview));
                }
            }
        }

        public string SessionStats
        {
            get
            {
                if (_messages == null) return "No session data";
                
                var messageList = _messages.ToList();
                var gmMessages = messageList.Count(m => m.Role == "GM");
                var playerMessages = messageList.Count(m => m.Role != "GM");
                
                return $"Session: {messageList.Count} total messages ({gmMessages} GM, {playerMessages} player)";
            }
        }

        public string GameStateSummary
        {
            get
            {
                if (_stateStore == null) return "No game state";
                
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
                
                return parts.Count > 0 ? string.Join(", ", parts) : "Empty game state";
            }
        }

        public string MissionPreview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_missionName))
                    return "Enter a mission name to see preview...";
                
                var preview = $"Mission: {_missionName}\n";
                preview += $"Genre: {_genre} | Difficulty: {_difficulty}\n\n";
                
                if (!string.IsNullOrWhiteSpace(_description))
                {
                    preview += _description;
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
                
                return preview;
            }
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private void SaveMission_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_missionName))
            {
                // Show error
                var messageBox = new Window
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
                
                stackPanel.Children.Add(new TextBlock { Text = "Mission name is required." });
                
                var okButton = new Button
                {
                    Content = "OK",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
                okButton.Click += (s, args) => messageBox.Close();
                stackPanel.Children.Add(okButton);
                
                messageBox.Content = stackPanel;
                messageBox.ShowDialog(this);
                return;
            }

            try
            {
                var savedPath = MissionService.SaveCurrentSessionAsMission(
                    _stateStore,
                    _messages,
                    _missionName,
                    _description,
                    _genre.ToLowerInvariant(),
                    _difficulty.ToLowerInvariant()
                );

                Close(savedPath);
            }
            catch (Exception ex)
            {
                // Show error dialog
                var errorBox = new Window
                {
                    Title = "Error Saving Mission",
                    Width = 400,
                    Height = 200,
                    Content = new StackPanel
                    {
                        Margin = new Avalonia.Thickness(20),
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock { Text = $"Failed to save mission: {ex.Message}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                            new Button
                            {
                                Content = "OK",
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                Click = (s, args) => ((Window)((Button)s!).Parent!.Parent!).Close()
                            }
                        }
                    }
                };
                errorBox.ShowDialog(this);
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

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}