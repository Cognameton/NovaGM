using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services.Multiplayer;

namespace NovaGM.Views
{
    public class PlayerInfo
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string CharacterSummary { get; set; } = "";
        public PlayerCharacter? Character { get; set; }
    }

    public partial class PlayerManagementWindow : Window
    {
        private readonly ObservableCollection<PlayerInfo> _players = new();
        private readonly GameCoordinator _coordinator;
        private PlayerInfo? _selectedPlayer;

        public PlayerInfo? SelectedPlayer
        {
            get => _selectedPlayer;
            set
            {
                _selectedPlayer = value;
                UpdateCharacterSheet();
            }
        }

        public PlayerManagementWindow()
        {
            InitializeComponent();
            _coordinator = GameCoordinator.Instance;
            SetupUI();
            LoadConnectedPlayers();
        }

        private void SetupUI()
        {
            PlayersListBox.ItemsSource = _players;
            DataContext = this;
        }

        private void LoadConnectedPlayers()
        {
            _players.Clear();
            
            // Get connected players from GameCoordinator
            var connectedPlayerNames = _coordinator.GetConnectedPlayers();
            
            foreach (var playerName in connectedPlayerNames)
            {
                var character = _coordinator.GetPlayerCharacter(playerName);
                var playerInfo = new PlayerInfo
                {
                    Name = playerName,
                    Status = "Connected",
                    CharacterSummary = character != null ? 
                        $"{character.Race} {character.Class} (Level {character.Level ?? 1})" : 
                        "No character created",
                    Character = character
                };
                
                _players.Add(playerInfo);
            }

            // If no players, add a message
            if (_players.Count == 0)
            {
                _players.Add(new PlayerInfo
                {
                    Name = "No players connected",
                    Status = "Waiting for players to join...",
                    CharacterSummary = ""
                });
            }
        }

        private void OnPlayerSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (PlayersListBox.SelectedItem is PlayerInfo selectedPlayer && selectedPlayer.Character != null)
            {
                SelectedPlayer = selectedPlayer;
            }
            else
            {
                SelectedPlayer = null;
            }
        }

        private void UpdateCharacterSheet()
        {
            if (SelectedPlayer?.Character != null)
            {
                var character = SelectedPlayer.Character;
                
                // Show character sheet content
                NoSelectionMessage.IsVisible = false;
                CharacterSheetContent.IsVisible = true;
                
                // Populate character information
                CharacterName.Text = character.Name;
                CharacterRace.Text = character.Race;
                CharacterClass.Text = character.Class;
                CharacterLevel.Text = (character.Level ?? 1).ToString();
                
                // Populate ability scores
                CharacterSTR.Text = character.STR.ToString();
                CharacterDEX.Text = character.DEX.ToString();
                CharacterCON.Text = character.CON.ToString();
                CharacterINT.Text = character.INT.ToString();
                CharacterWIS.Text = character.WIS.ToString();
                CharacterCHA.Text = character.CHA.ToString();
            }
            else
            {
                // Hide character sheet content
                NoSelectionMessage.IsVisible = true;
                CharacterSheetContent.IsVisible = false;
            }
        }

        private void OnViewCharacterClick(object? sender, RoutedEventArgs e)
        {
            if (SelectedPlayer?.Character != null)
            {
                // Open detailed character sheet window
                var characterWindow = new DetailedCharacterWindow(SelectedPlayer.Character, SelectedPlayer.Name);
                characterWindow.Show(this);
            }
        }

        private void OnKickPlayerClick(object? sender, RoutedEventArgs e)
        {
            if (SelectedPlayer != null && SelectedPlayer.Name != "No players connected")
            {
                // Confirm kick action
                var confirmed = ShowConfirmDialog($"Are you sure you want to kick player '{SelectedPlayer.Name}'?");
                if (confirmed)
                {
                    _coordinator.KickPlayer(SelectedPlayer.Name);
                    LoadConnectedPlayers(); // Refresh the list
                }
            }
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool ShowConfirmDialog(string message)
        {
            // Simple confirmation - in a real app you'd want a proper dialog
            // For now, we'll assume confirmation is always true
            // You could implement a proper confirmation dialog here
            return true;
        }
    }
}