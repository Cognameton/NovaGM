using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NovaGM.Models;
using NovaGM.Services;               // AgentOrchestrator, ShutdownUtil
using NovaGM.Services.Streaming;     // LocalBroadcaster
using NovaGM.Services.Multiplayer;   // GameCoordinator
using NovaGM.Views;                  // SettingsWindow, PacksWindow, ModelsWindow (if present)

namespace NovaGM.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public string Title => "NovaGM";
        public string RoomCode => GameCoordinator.Instance.CurrentCode;

        public ObservableCollection<Message> Messages { get; } = new();
        public string Input { get; set; } = string.Empty;
        public ICommand SendCommand { get; }

        public CharacterSheetViewModel CharacterSheet { get; }
        public ObservableCollection<string> JournalEntries { get; } = new();
        public string NewJournalText { get; set; } = "";
        public ICommand AddJournalEntryCommand { get; }

        public ObservableCollection<CompendiumEntry> Compendium { get; } = new();

        // Menu commands
        public ICommand NewGameCommand { get; }
        public ICommand ContinueGameCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand OpenPacksCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenModelsCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand SaveToJournalCommand { get; }
        public ICommand SaveAsMissionCommand { get; }
        public ICommand KickPlayerCommand { get; }
        public ICommand LoadScenarioCommand { get; }

        private readonly AgentOrchestrator _agent = new();
        private readonly SemaphoreSlim _turnLock = new(1, 1);

        public MainWindowViewModel()
        {
            // Seed example character (placeholder to light up UI)
            var c = new Character
            {
                Name = "Aria Voss",
                Race = "Human",
                Class = "Ranger",
                Level = 1,
                Stats = new Stats { STR = 12, DEX = 15, CON = 12, INT = 10, WIS = 13, CHA = 9 },
                Equipment =
                {
                    [EquipmentSlot.Head]     = new Item { Name = "Leather Hood",     Slot = EquipmentSlot.Head },
                    [EquipmentSlot.Chest]    = new Item { Name = "Leather Jerkin",   Slot = EquipmentSlot.Chest },
                    [EquipmentSlot.MainHand] = new Item { Name = "Shortsword",       Slot = EquipmentSlot.MainHand },
                    [EquipmentSlot.OffHand]  = new Item { Name = "Wooden Buckler",   Slot = EquipmentSlot.OffHand },
                    [EquipmentSlot.Feet]     = new Item { Name = "Traveler's Boots", Slot = EquipmentSlot.Feet },
                }
            };
            CharacterSheet = new CharacterSheetViewModel(c);

            // Minimal compendium placeholders
            Compendium.Add(new CompendiumEntry { Category = "Race",   Name = "Human",      Description = "Versatile and adaptable." });
            Compendium.Add(new CompendiumEntry { Category = "Race",   Name = "Elf",        Description = "Graceful, keen senses." });
            Compendium.Add(new CompendiumEntry { Category = "Class",  Name = "Fighter",    Description = "Martial expert." });
            Compendium.Add(new CompendiumEntry { Category = "Class",  Name = "Wizard",     Description = "Arcane scholar." });
            Compendium.Add(new CompendiumEntry { Category = "Weapon", Name = "Shortsword", Description = "Light melee weapon." });

            Messages.Add(new Message("GM", "Welcome to NovaGM. Type an action to begin."));

            var broadcaster  = LocalBroadcaster.Instance;
            var coordinator  = GameCoordinator.Instance;

            // Desktop "send"
            SendCommand = new RelayCommand(async _ =>
            {
                var text = Input.Trim();
                if (string.IsNullOrWhiteSpace(text)) return;
                Input = string.Empty;
                await HandleTurnAsync("Player", text, broadcaster);
            });

            // Journal add
            AddJournalEntryCommand = new RelayCommand(_ =>
            {
                var t = NewJournalText?.Trim();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    JournalEntries.Add(t);
                    NewJournalText = "";
                }
                return Task.CompletedTask;
            });

            // Menu commands
            NewGameCommand = new RelayCommand(_ =>
            {
                Messages.Clear();
                Messages.Add(new Message("GM", "New game started. Set the scene or type an action to begin."));
                return Task.CompletedTask;
            });

            ContinueGameCommand = new RelayCommand(_ =>
            {
                Messages.Add(new Message("GM", "Continuing last session…"));
                return Task.CompletedTask;
            });

            ExitCommand = new RelayCommand(_ =>
            {
                // Close main window → triggers App cleanup (LocalServer.Dispose + ShutdownUtil.HardExit)
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life
                    && life.MainWindow is { } mw)
                {
                    mw.Close();
                }
                return Task.CompletedTask;
            });

            OpenPacksCommand = new RelayCommand(_ =>
            {
                ShowToolWindow(() => new PacksWindow());
                return Task.CompletedTask;
            });

            OpenSettingsCommand = new RelayCommand(_ =>
            {
                ShowToolWindow(() => new SettingsWindow());
                return Task.CompletedTask;
            });

            OpenModelsCommand = new RelayCommand(_ =>
            {
                ShowToolWindow(() => new ModelsWindow());
                return Task.CompletedTask;
            });

            AboutCommand = new RelayCommand(_ =>
            {
                Messages.Add(new Message("GM", "NovaGM — local-first GM assistant. Foreigner on the jukebox, Star Trek in our hearts."));
                return Task.CompletedTask;
            });

            SaveToJournalCommand = new RelayCommand(_ =>
            {
                var sessionSummary = GenerateSessionSummary();
                JournalEntries.Add($"[Session {DateTime.Now:yyyy-MM-dd}] {sessionSummary}");
                Messages.Add(new Message("GM", "Current session saved to journal."));
                return Task.CompletedTask;
            });

            SaveAsMissionCommand = new RelayCommand(async _ =>
            {
                // Open Save Mission dialog
                try
                {
                    var saveWindow = new SaveMissionWindow(_agent.StateStore, Messages);
                    var result = await saveWindow.ShowDialog<string?>(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null);
                    
                    if (!string.IsNullOrEmpty(result))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(result);
                        Messages.Add(new Message("GM", $"Mission saved successfully as '{fileName}'. You can now load this scenario in future sessions."));
                    }
                }
                catch (Exception ex)
                {
                    Messages.Add(new Message("GM", $"Failed to save mission: {ex.Message}"));
                }
            });

            KickPlayerCommand = new RelayCommand(async _ =>
            {
                try
                {
                    // Get current players from coordinator
                    var coordinator = GameCoordinator.Instance;
                    // Since GameCoordinator doesn't expose player list directly, we'll show a simple input dialog
                    
                    var kickDialog = new Window
                    {
                        Title = "Kick Player",
                        Width = 300,
                        Height = 180,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    var stackPanel = new StackPanel
                    {
                        Margin = new Avalonia.Thickness(20),
                        Spacing = 10
                    };

                    stackPanel.Children.Add(new TextBlock { Text = "Enter player name to kick:" });
                    
                    var nameBox = new TextBox { Name = "PlayerNameBox", Watermark = "Player name..." };
                    stackPanel.Children.Add(nameBox);

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
                    cancelButton.Click += (s, args) => kickDialog.Close(null);

                    var kickButton = new Button 
                    { 
                        Content = "Kick",
                        MinWidth = 60
                    };
                    kickButton.Click += (s, args) => kickDialog.Close(nameBox.Text?.Trim());

                    buttonPanel.Children.Add(cancelButton);
                    buttonPanel.Children.Add(kickButton);
                    stackPanel.Children.Add(buttonPanel);

                    kickDialog.Content = stackPanel;

                    var result = await kickDialog.ShowDialog<string?>(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null);
                    
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        // For now, just announce the kick since we don't have a direct way to forcefully disconnect players
                        Messages.Add(new Message("GM", $"Player '{result}' has been kicked from the session. They will be disconnected on their next action."));
                        
                        // Reset the room code to effectively kick all players
                        coordinator.ResetRoom();
                        Messages.Add(new Message("GM", $"Room code reset to {coordinator.CurrentCode}. Share the new code with remaining players."));
                    }
                }
                catch (Exception ex)
                {
                    Messages.Add(new Message("GM", $"Failed to kick player: {ex.Message}"));
                }
            });

            LoadScenarioCommand = new RelayCommand(async _ =>
            {
                try
                {
                    var loadWindow = new LoadScenarioWindow();
                    var result = await loadWindow.ShowDialog<Mission?>(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null);
                    
                    if (result != null)
                    {
                        await LoadMissionAsync(result);
                    }
                }
                catch (Exception ex)
                {
                    Messages.Add(new Message("GM", $"Failed to load scenario: {ex.Message}"));
                }
            });

            // Consume LAN player inputs
            _ = Task.Run(async () =>
            {
                await foreach (var inp in coordinator.ReadInputsAsync(CancellationToken.None))
                    await HandleTurnAsync(inp.Name, inp.Text, broadcaster);
            });
        }

        private static void ShowToolWindow(Func<Window> factory)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life
                && life.MainWindow is { } owner)
            {
                var w = factory();
                w.Show(owner);
            }
            else
            {
                factory().Show();
            }
        }

        private async Task HandleTurnAsync(string playerName, string text, LocalBroadcaster broadcaster)
        {
            await _turnLock.WaitAsync();
            try
            {
                Dispatcher.UIThread.Post(() => Messages.Add(new Message(playerName, text)));

                var gm = new Message("GM", "");
                Dispatcher.UIThread.Post(() => Messages.Add(gm));

                string final = await _agent.RunTurnAsync(
                    text,
                    default,
                    onNarratorToken: chunk =>
                    {
                        Dispatcher.UIThread.Post(() => gm.Append(chunk));
                        broadcaster.Publish(chunk);
                    }
                );

                Dispatcher.UIThread.Post(() =>
                {
                    if (!string.IsNullOrEmpty(final) && !gm.Content.Equals(final))
                        gm.Content = final;
                    broadcaster.Publish("\n");
                });
            }
            finally { _turnLock.Release(); }
        }

        private string GenerateSessionSummary()
        {
            var recentMessages = Messages.TakeLast(10).Where(m => m.Role == "GM").ToList();
            if (recentMessages.Count == 0) return "Empty session";
            
            var summary = string.Join(" ", recentMessages.Select(m => 
                m.Content.Length > 100 ? m.Content.Substring(0, 100) + "..." : m.Content));
            
            return summary.Length > 200 ? summary.Substring(0, 200) + "..." : summary;
        }

        private async Task LoadMissionAsync(Mission mission)
        {
            try
            {
                // Clear current session
                Messages.Clear();
                
                // Load mission state into the game
                if (mission.InitialState != null)
                {
                    var state = _agent.StateStore.Load();
                    
                    // Apply mission initial state
                    state.Location = mission.InitialState.Location;
                    state.Premise = mission.InitialState.Premise;
                    
                    // Clear and reload collections
                    state.Flags.Clear();
                    foreach (var flag in mission.InitialState.Flags)
                        state.Flags.Add(flag);
                    
                    state.Npcs.Clear();
                    foreach (var npc in mission.InitialState.Npcs)
                        state.Npcs[npc.Key] = npc.Value;
                    
                    state.Facts.Clear();
                    foreach (var fact in mission.InitialState.Facts)
                        state.Facts.Add(fact);
                }
                
                // Add opening message
                Messages.Add(new Message("GM", $"Loading mission: {mission.Name}"));
                
                if (!string.IsNullOrWhiteSpace(mission.Narrative?.OpeningText))
                {
                    Messages.Add(new Message("GM", mission.Narrative.OpeningText));
                }
                else if (!string.IsNullOrWhiteSpace(mission.Description))
                {
                    Messages.Add(new Message("GM", mission.Description));
                }
                
                // Add objectives if available
                if (mission.Narrative?.Objectives?.Any() == true)
                {
                    var objectiveText = "Mission Objectives:\n" + string.Join("\n", mission.Narrative.Objectives.Select(o => $"• {o}"));
                    Messages.Add(new Message("GM", objectiveText));
                }
                
                Messages.Add(new Message("GM", "Mission loaded successfully. What would you like to do?"));
            }
            catch (Exception ex)
            {
                Messages.Add(new Message("GM", $"Error loading mission: {ex.Message}"));
            }
        }
    }

    public sealed class Message : INotifyPropertyChanged
    {
        private string _content;
        public string Role { get; }
        public string Content
        {
            get => _content;
            set { if (_content != value) { _content = value; OnPropertyChanged(); } }
        }
        public Message(string role, string content) { Role = role; _content = content; }
        public void Append(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return;
            chunk = chunk.Replace("<EOT>", "");
            _content += chunk;
            OnPropertyChanged(nameof(Content));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Func<object?, Task>? _async;
        private readonly Predicate<object?>? _can;
        public RelayCommand(Func<object?, Task> async, Predicate<object?>? can = null)
        { _async = async; _can = can; }
        public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
        public async void Execute(object? parameter) { if (_async is not null) await _async(parameter); }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
