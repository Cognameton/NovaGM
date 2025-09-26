using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using NovaGM.Models;
using NovaGM.Services;

namespace NovaGM.Views
{
    public partial class LoadScenarioWindow : Window
    {
        private readonly List<Mission> _missions = new();
        private Mission? _selectedMission;

        public LoadScenarioWindow()
        {
            InitializeComponent();
            LoadMissions();
        }

        private void LoadMissions()
        {
            try
            {
                _missions.Clear();
                _missions.AddRange(MissionService.ListAvailableMissions());

                var missionsList = this.FindControl<ListBox>("MissionsList")!;
                missionsList.ItemsSource = _missions;

                if (!_missions.Any())
                {
                    ShowNoMissionsMessage();
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Failed to load missions: {ex.Message}");
            }
        }

        private void MissionsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            _selectedMission = listBox?.SelectedItem as Mission;

            UpdateMissionDetails();
            UpdateButtonStates();
        }

        private void UpdateMissionDetails()
        {
            var detailsPanel = this.FindControl<StackPanel>("DetailsPanel")!;
            detailsPanel.Children.Clear();

            if (_selectedMission == null)
            {
                detailsPanel.Children.Add(new TextBlock
                {
                    Text = "Select a mission to see details",
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    Opacity = 0.6,
                    Margin = new Avalonia.Thickness(0, 20)
                });
                return;
            }

            var mission = _selectedMission;

            // Mission name and basic info
            detailsPanel.Children.Add(new TextBlock
            {
                Text = mission.Name,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                FontSize = 14,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            detailsPanel.Children.Add(new TextBlock
            {
                Text = $"{mission.Genre} • {mission.Difficulty}",
                FontSize = 11,
                Opacity = 0.7
            });

            detailsPanel.Children.Add(new TextBlock
            {
                Text = mission.EstimatedDuration,
                FontSize = 11,
                Opacity = 0.7
            });

            // Description
            if (!string.IsNullOrWhiteSpace(mission.Description))
            {
                detailsPanel.Children.Add(new Border { Height = 8 }); // Spacer
                detailsPanel.Children.Add(new TextBlock
                {
                    Text = "Description:",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    FontSize = 12
                });
                detailsPanel.Children.Add(new TextBlock
                {
                    Text = mission.Description,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = 11
                });
            }

            // Initial state info
            if (mission.InitialState != null)
            {
                detailsPanel.Children.Add(new Border { Height = 8 }); // Spacer
                detailsPanel.Children.Add(new TextBlock
                {
                    Text = "Game State:",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    FontSize = 12
                });

                if (!string.IsNullOrWhiteSpace(mission.InitialState.Location))
                {
                    detailsPanel.Children.Add(new TextBlock
                    {
                        Text = $"Location: {mission.InitialState.Location}",
                        FontSize = 11
                    });
                }

                detailsPanel.Children.Add(new TextBlock
                {
                    Text = $"Level: {mission.InitialState.SuggestedLevel}",
                    FontSize = 11
                });

                detailsPanel.Children.Add(new TextBlock
                {
                    Text = $"Party: {mission.InitialState.PartySize}",
                    FontSize = 11
                });

                if (mission.InitialState.Facts.Any())
                {
                    detailsPanel.Children.Add(new TextBlock
                    {
                        Text = $"Facts: {mission.InitialState.Facts.Count}",
                        FontSize = 11
                    });
                }

                if (mission.InitialState.Npcs.Any())
                {
                    detailsPanel.Children.Add(new TextBlock
                    {
                        Text = $"NPCs: {mission.InitialState.Npcs.Count}",
                        FontSize = 11
                    });
                }
            }

            // Tags
            if (mission.Tags?.Any() == true)
            {
                detailsPanel.Children.Add(new Border { Height = 8 }); // Spacer
                detailsPanel.Children.Add(new TextBlock
                {
                    Text = "Tags:",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    FontSize = 12
                });
                detailsPanel.Children.Add(new TextBlock
                {
                    Text = string.Join(", ", mission.Tags),
                    FontSize = 11,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            }

            // Creation info
            detailsPanel.Children.Add(new Border { Height = 8 }); // Spacer
            detailsPanel.Children.Add(new TextBlock
            {
                Text = $"Created: {mission.CreatedAt:yyyy-MM-dd HH:mm}",
                FontSize = 10,
                Opacity = 0.6
            });
        }

        private void UpdateButtonStates()
        {
            var loadButton = this.FindControl<Button>("LoadButton")!;
            var deleteButton = this.FindControl<Button>("DeleteButton")!;

            var hasSelection = _selectedMission != null;
            loadButton.IsEnabled = hasSelection;
            deleteButton.IsEnabled = hasSelection;
        }

        private void Refresh_Click(object? sender, RoutedEventArgs e)
        {
            LoadMissions();
        }

        private void Delete_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedMission == null) return;

            // Show confirmation dialog
            var confirmDialog = new Window
            {
                Title = "Confirm Delete",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var stackPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15
            };

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"Are you sure you want to delete '{_selectedMission.Name}'?",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Spacing = 10
            };

            var cancelButton = new Button 
            { 
                Content = "Cancel",
                MinWidth = 60
            };
            cancelButton.Click += (s, args) => confirmDialog.Close(false);

            var deleteButton = new Button 
            { 
                Content = "Delete",
                MinWidth = 60
            };
            deleteButton.Click += (s, args) => confirmDialog.Close(true);

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(deleteButton);
            stackPanel.Children.Add(buttonPanel);

            confirmDialog.Content = stackPanel;

            confirmDialog.ShowDialog<bool>(this).ContinueWith(task =>
            {
                if (task.Result)
                {
                    try
                    {
                        MissionService.DeleteMission(_selectedMission.Id);
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            LoadMissions(); // Refresh the list
                        });
                    }
                    catch (Exception ex)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ShowErrorMessage($"Failed to delete mission: {ex.Message}");
                        });
                    }
                }
            });
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private void LoadMission_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedMission == null) return;

            Close(_selectedMission);
        }

        private void ShowNoMissionsMessage()
        {
            var detailsPanel = this.FindControl<StackPanel>("DetailsPanel")!;
            detailsPanel.Children.Clear();
            detailsPanel.Children.Add(new TextBlock
            {
                Text = "No missions found.\n\nCreate missions by playing sessions and using 'Save as Mission' from the File menu.",
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Opacity = 0.6,
                Margin = new Avalonia.Thickness(0, 20)
            });
        }

        private void ShowErrorMessage(string message)
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