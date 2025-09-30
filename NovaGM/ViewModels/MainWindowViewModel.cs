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
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        public string Title => "NovaGM";
        public string RoomCode => GameCoordinator.Instance.CurrentCode;
        public string ServerUrl => $"http://{GetLocalIp()}:5055";

        public ObservableCollection<Message> Messages { get; } = new();
        public ObservableCollection<string> ConnectedPlayers { get; } = new();
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
        public ICommand SelectGenreCommand { get; }
        public ICommand ViewPlayersCommand { get; }

        private readonly AgentOrchestrator _agent = new();
        private readonly SemaphoreSlim _turnLock = new(1, 1);

        public MainWindowViewModel()
        {
            // Clear any existing history when starting new session
            MessageHistoryService.ClearHistory();
            
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

            AddMessage("GM", "Welcome to NovaGM. Type an action to begin.");

            var broadcaster  = LocalBroadcaster.Instance;
            var coordinator  = GameCoordinator.Instance;

            // Desktop "send"
            SendCommand = new RelayCommand(async _ =>
            {
                var text = Input.Trim();
                if (string.IsNullOrWhiteSpace(text)) return;
                Input = string.Empty;
                await HandleTurnAsync("GM", text, broadcaster);
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
                // Reset genre manager for new game
                GenreManager.ResetForNewGame();
                
                Messages.Clear();
                Messages.Add(new Message("GM", "New game started. Select a genre from the Genre menu, then set the scene or type an action to begin."));
                
                // Update compendium with default content
                UpdateCompendiumForGenre();
                
                return Task.CompletedTask;
            });

            ContinueGameCommand = new RelayCommand(_ =>
            {
                Messages.Add(new Message("GM", "Continuing last session…"));
                return Task.CompletedTask;
            });

            ExitCommand = new RelayCommand(async _ =>
            {
                await HandleExitSequenceAsync();
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
                    var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null;
                    // ShowDialog requires an owner, so we'll provide a fallback
                    if (ownerWindow == null)
                    {
                        // Create a temporary invisible window as owner if needed
                        ownerWindow = new Window { Width = 1, Height = 1, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                        ownerWindow.Show();
                    }
                    var result = await saveWindow.ShowDialog<string?>(ownerWindow);
                    
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

            KickPlayerCommand = new RelayCommand(_ =>
            {
                Messages.Add(new Message("GM", "Player kick functionality available via room code regeneration."));
                return Task.CompletedTask;
            });

            ViewPlayersCommand = new RelayCommand(_ =>
            {
                try
                {
                    var playerWindow = new PlayerManagementWindow();
                    var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null;
                    if (ownerWindow != null)
                    {
                        playerWindow.Show(ownerWindow);
                    }
                    else
                    {
                        playerWindow.Show();
                    }
                }
                catch (Exception ex)
                {
                    Messages.Add(new Message("GM", $"Failed to open player management: {ex.Message}"));
                }
                return Task.CompletedTask;
            });

            LoadScenarioCommand = new RelayCommand(async _ =>
            {
                try
                {
                    var loadWindow = new LoadScenarioWindow();
                    var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null;
                    // ShowDialog requires an owner, so we'll provide a fallback
                    if (ownerWindow == null)
                    {
                        // Create a temporary invisible window as owner if needed
                        ownerWindow = new Window { Width = 1, Height = 1, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                        ownerWindow.Show();
                    }
                    var result = await loadWindow.ShowDialog<Mission?>(ownerWindow);
                    
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

            SelectGenreCommand = new RelayCommand(async _ =>
            {
                try
                {
                    if (GenreManager.Current.GameStarted)
                    {
                        Messages.Add(new Message("GM", "Cannot change genre after game has started. Start a new game to select a different genre."));
                        return;
                    }

                    var genreWindow = new GenreSelectionWindow();
                    var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null;
                    // ShowDialog requires an owner, so we'll provide a fallback
                    if (ownerWindow == null)
                    {
                        // Create a temporary invisible window as owner if needed
                        ownerWindow = new Window { Width = 1, Height = 1, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                        ownerWindow.Show();
                    }
                    var result = await genreWindow.ShowDialog<GameGenre?>(ownerWindow);
                    
                    if (result.HasValue)
                    {
                        var genreName = GenreManager.GetGenreDisplayName(result.Value);
                        Messages.Add(new Message("GM", $"Genre set to {genreName}. The available races, classes, and equipment have been updated accordingly."));
                        
                        // Update compendium with new genre content
                        UpdateCompendiumForGenre();
                    }
                }
                catch (Exception ex)
                {
                    Messages.Add(new Message("GM", $"Failed to change genre: {ex.Message}"));
                }
            });

            // Consume LAN player inputs and track connected players
            _ = Task.Run(async () =>
            {
                await foreach (var inp in coordinator.ReadInputsAsync(CancellationToken.None))
                {
                    // Track connected players
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!ConnectedPlayers.Contains(inp.Player))
                        {
                            ConnectedPlayers.Add(inp.Player);
                            Messages.Add(new Message("System", $"Player '{inp.Player}' joined the session."));
                        }
                    });
                    
                    await HandleTurnAsync(inp.Player, inp.Text, broadcaster);
                }
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
                // Start the game if this is the first turn
                if (!GenreManager.Current.GameStarted)
                {
                    GenreManager.StartGame();
                }

                // Handle GM input differently than player input
                if (playerName == "GM")
                {
                    // GM input: This is a narrative prompt/instruction to the AI
                    // Add GM prompt to local display
                    var gmPrompt = new Message("GM", text);
                    Dispatcher.UIThread.Post(() => {
                        Messages.Add(gmPrompt);
                        MessageHistoryService.AddMessage(new Models.Message("GM", text));
                    });

                    // Broadcast GM prompt to all connected players
                    broadcaster.Publish($"GM: {text}\n");

                    // Process the GM's prompt through the AI as a narrative instruction
                    var aiGmResponse = new Message("GM", "");
                    Dispatcher.UIThread.Post(() => Messages.Add(aiGmResponse));

                    // Broadcast AI-GM response indicator
                    broadcaster.Publish("GM: ");

                    string final = await _agent.RunTurnAsync(
                        $"GM instruction: {text}",
                        default,
                        onNarratorToken: chunk =>
                        {
                            Dispatcher.UIThread.Post(() => aiGmResponse.Append(chunk));
                            broadcaster.Publish(chunk);
                        }
                    );

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!string.IsNullOrEmpty(final) && !aiGmResponse.Content.Equals(final))
                            aiGmResponse.Content = final;
                        broadcaster.Publish("\n");
                        // Add completed AI response to history
                        MessageHistoryService.AddMessage(new Models.Message("GM", aiGmResponse.Content));
                    });
                }
                else
                {
                    // Player input: Standard player action processing
                    // Add player message to GM display
                    var playerMessage = new Message(playerName, text);
                    Dispatcher.UIThread.Post(() => {
                        Messages.Add(playerMessage);
                        MessageHistoryService.AddMessage(new Models.Message(playerName, text));
                    });

                    // Broadcast player message to all connected players
                    broadcaster.Publish($"{playerName}: {text}\n");

                    var gm = new Message("GM", "");
                    Dispatcher.UIThread.Post(() => Messages.Add(gm));

                    // Broadcast GM response indicator to all players
                    broadcaster.Publish("GM: ");

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

        private Task LoadMissionAsync(Mission mission)
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
            
            return Task.CompletedTask;
        }

        private void UpdateCompendiumForGenre()
        {
            try
            {
                // Clear existing compendium entries
                Compendium.Clear();
                
                // Get current genre content
                var availableRaces = GenreManager.GetAvailableRaces();
                var availableClasses = GenreManager.GetAvailableClasses();
                var availableItems = GenreManager.GetAvailableItems();
                
                // Add races
                foreach (var race in availableRaces)
                {
                    Compendium.Add(new CompendiumEntry 
                    { 
                        Category = "Race", 
                        Name = race.Key, 
                        Description = race.Value.Description ?? "No description available." 
                    });
                }
                
                // Add classes
                foreach (var characterClass in availableClasses)
                {
                    Compendium.Add(new CompendiumEntry 
                    { 
                        Category = "Class", 
                        Name = characterClass.Key, 
                        Description = characterClass.Value.Description ?? "No description available." 
                    });
                }
                
                // Add equipment/items
                foreach (var item in availableItems)
                {
                    Compendium.Add(new CompendiumEntry 
                    { 
                        Category = "Equipment", 
                        Name = item.Key, 
                        Description = item.Value.Description ?? "No description available." 
                    });
                }
            }
            catch (Exception ex)
            {
                Messages.Add(new Message("GM", $"Failed to update compendium: {ex.Message}"));
            }
        }

        private async Task HandleExitSequenceAsync()
        {
            try
            {
                // Step 1: Exit confirmation dialog
                var exitDialog = new ExitConfirmationDialog();
                var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null;
                
                if (ownerWindow != null)
                {
#pragma warning disable CS4014
                    exitDialog.ShowDialog(ownerWindow);
#pragma warning restore CS4014
                    // Wait for dialog to complete
                    await Task.Run(() =>
                    {
                        while (exitDialog.IsVisible)
                        {
                            Thread.Sleep(50);
                        }
                    });
                }
                else
                {
                    exitDialog.Show();
                    await Task.Run(() =>
                    {
                        while (exitDialog.IsVisible)
                        {
                            Thread.Sleep(50);
                        }
                    });
                }

                if (exitDialog.Result != true)
                {
                    return; // User cancelled exit
                }

                // Step 2: Save session dialog
                var saveDialog = new SaveSessionDialog();
                if (ownerWindow != null)
                {
#pragma warning disable CS4014
                    saveDialog.ShowDialog(ownerWindow);
#pragma warning restore CS4014
                    await Task.Run(() =>
                    {
                        while (saveDialog.IsVisible)
                        {
                            Thread.Sleep(50);
                        }
                    });
                }
                else
                {
                    saveDialog.Show();
                    await Task.Run(() =>
                    {
                        while (saveDialog.IsVisible)
                        {
                            Thread.Sleep(50);
                        }
                    });
                }

                if (saveDialog.Result == true)
                {
                    // Save the session
                    await SaveCurrentSessionAsync();
                }

                // Step 3: Show shutdown countdown and perform safe shutdown
                await PerformSafeShutdownAsync();
            }
            catch (Exception ex)
            {
                Messages.Add(new Message("GM", $"Error during exit sequence: {ex.Message}"));
                // Fall back to hard exit if something goes wrong
                ShutdownUtil.HardExit();
            }
        }

        private async Task SaveCurrentSessionAsync()
        {
            try
            {
                var sessionSummary = GenerateSessionSummary();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var sessionData = $"[Session {timestamp}] {sessionSummary}";
                
                JournalEntries.Add(sessionData);
                Messages.Add(new Message("GM", "Session saved to journal successfully."));
                
                // Give UI time to update
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Messages.Add(new Message("GM", $"Failed to save session: {ex.Message}"));
            }
        }

        private async Task PerformSafeShutdownAsync()
        {
            var countdownWindow = new ShutdownCountdownWindow();
            var ownerWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life && life.MainWindow is { } mw ? mw : null;
            
            // Start the shutdown process in the background
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000); // Give UI time to show
                
                try
                {
                    // Step 1: Stop accepting new connections
                    countdownWindow.AddStatusMessage("Stopping new player connections...");
                    GameCoordinator.Instance.Cancel();
                    await Task.Delay(2000);

                    // Step 2: Notify connected players
                    countdownWindow.AddStatusMessage("Notifying connected players...");
                    var broadcaster = LocalBroadcaster.Instance;
                    broadcaster.Publish("\n\n🔴 GM: The game session is ending. Thank you for playing!\n\n");
                    await Task.Delay(3000);

                    // Step 3: Stop local server
                    countdownWindow.AddStatusMessage("Shutting down local server...");
                    ServicesHost.Stop();
                    await Task.Delay(2000);

                    // Step 4: Complete broadcasting
                    countdownWindow.AddStatusMessage("Closing player connections...");
                    broadcaster.Complete();
                    await Task.Delay(2000);

                    // Step 5: Clean up LLM and other resources
                    countdownWindow.AddStatusMessage("Releasing LLM resources...");
                    await Task.Delay(3000); // Give time for LLM cleanup

                    // Step 6: Final cleanup
                    countdownWindow.AddStatusMessage("Performing final cleanup...");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    await Task.Delay(2000);

                    countdownWindow.AddStatusMessage("Shutdown complete. NovaGM will now exit.");
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    countdownWindow.AddStatusMessage($"Error during shutdown: {ex.Message}");
                }
            });

            // Show countdown window
            if (ownerWindow != null)
            {
#pragma warning disable CS4014
                countdownWindow.ShowDialog(ownerWindow);
#pragma warning restore CS4014
                await Task.Run(() =>
                {
                    while (countdownWindow.IsVisible)
                    {
                        Thread.Sleep(50);
                    }
                });
            }
            else
            {
                countdownWindow.Show();
                await Task.Run(() =>
                {
                    while (countdownWindow.IsVisible)
                    {
                        Thread.Sleep(50);
                    }
                });
            }

            if (countdownWindow.WasCancelled)
            {
                // User cancelled shutdown
                Messages.Add(new Message("GM", "Shutdown cancelled. Game session resumed."));
                return;
            }

            // Proceed with final shutdown
            await ShutdownUtil.RequestAsync();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void AddMessage(string role, string content)
        {
            var message = new Message(role, content);
            Messages.Add(message);
            MessageHistoryService.AddMessage(new Models.Message(role, content));
        }
            
        private static string GetLocalIp()
        {
            try
            {
                using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as System.Net.IPEndPoint;
                return endPoint?.Address?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
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
