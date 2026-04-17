using System;
using System.Collections.Generic;
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
using NovaGM.Services.Inventory;     // Inventory service
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
        private string _input = string.Empty;
        public string Input
        {
            get => _input;
            set { if (_input != value) { _input = value; OnPropertyChanged(); } }
        }
        public ICommand SendCommand { get; }

        public CharacterSheetViewModel CharacterSheet { get; }
        public ObservableCollection<CharacterSheetViewModel> HubCharacters { get; } = new();

        private CharacterSheetViewModel? _selectedHubCharacter;
        public CharacterSheetViewModel? SelectedHubCharacter
        {
            get => _selectedHubCharacter;
            set
            {
                if (_selectedHubCharacter != value)
                {
                    _selectedHubCharacter = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<RemotePlayerViewModel> RemotePlayers { get; } = new();

        private RemotePlayerViewModel? _activeRemotePlayer;
        public RemotePlayerViewModel? ActiveRemotePlayer
        {
            get => _activeRemotePlayer;
            private set
            {
                if (_activeRemotePlayer != value)
                {
                    _activeRemotePlayer = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRemotePlayerDetailVisible));
                    OnPropertyChanged(nameof(IsRemotePlayerListVisible));
                }
            }
        }

        public bool IsRemotePlayerDetailVisible => ActiveRemotePlayer is not null;
        public bool IsRemotePlayerListVisible => !IsRemotePlayerDetailVisible;

        public ICommand ShowRemotePlayerCommand { get; }
        public ICommand BackToRemotePlayerListCommand { get; }
        public ICommand CreateHubCharacterCommand { get; }
        public ICommand DeleteHubCharacterCommand { get; }

        public ObservableCollection<string> JournalEntries { get; } = new();
        private string _newJournalText = "";
        public string NewJournalText
        {
            get => _newJournalText;
            set { if (_newJournalText != value) { _newJournalText = value; OnPropertyChanged(); } }
        }
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
        private readonly InventoryService _inventoryService;
        private readonly TurnEngine _turnEngine;
        private readonly CancellationTokenSource _sessionCts = new();
        // Tracks players already welcomed so the join message fires once on character save, not on first message.
        private readonly HashSet<string> _welcomedPlayers = new(StringComparer.OrdinalIgnoreCase);
        // Guards against starting the TurnEngine more than once.
        private bool _turnEngineStarted;

        private string _currentTurnPlayer = "";
        public string CurrentTurnPlayer
        {
            get => _currentTurnPlayer;
            private set
            {
                if (_currentTurnPlayer != value)
                {
                    _currentTurnPlayer = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasActiveTurn));
                }
            }
        }
        public bool HasActiveTurn => !string.IsNullOrEmpty(_currentTurnPlayer);

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
            _inventoryService = new InventoryService(_agent.StateStore);
            CharacterSheet.Character.Inventory = _inventoryService.GetInventoryForHubCharacter(CharacterSheet.Character.Name);
            _inventoryService.SaveInventory(InventoryKeys.ForHubCharacter(CharacterSheet.Character.Name), CharacterSheet.Character.Inventory);
            HubCharacters.Add(CharacterSheet);
            SelectedHubCharacter = CharacterSheet;

            // TurnEngine — manages multi-player round cycle, idle timers, GM turns.
            // NOT started here — starts on first player action so players have time to connect
            // and create characters before the idle timer runs.
            _turnEngine = new TurnEngine(_agent.StateStore);
            WireTurnEngine();
            // Register hub player; remote players register when they fully join (character saved)
            _turnEngine.AddPlayer(CharacterSheet.Character.Name);

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

            ShowRemotePlayerCommand = new RelayCommand(param =>
            {
                if (param is RemotePlayerViewModel remote)
                {
                    ShowRemotePlayer(remote);
                }
                return Task.CompletedTask;
            });

            BackToRemotePlayerListCommand = new RelayCommand(_ =>
            {
                ActiveRemotePlayer = null;
                return Task.CompletedTask;
            });

            CreateHubCharacterCommand = new RelayCommand(async _ =>
            {
                await CreateHubCharacterAsync();
            });

            DeleteHubCharacterCommand = new RelayCommand(async _ =>
            {
                await DeleteHubCharacterAsync();
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
                // Reset genre manager and turn engine for new game
                GenreManager.ResetForNewGame();
                _turnEngineStarted = false;
                _welcomedPlayers.Clear();

                Messages.Clear();
                Messages.Add(new Message("GM", "New game started. Select a genre from the Genre menu, then type an action or describe the scene to begin."));

                // Notify connected players so their Table view resets
                LocalBroadcaster.Instance.PublishEvent("new_game", "", "");

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
                    var ownerWindow = GetMainWindow()
                        ?? new Window { Width = 1, Height = 1, WindowStartupLocation = WindowStartupLocation.CenterScreen };
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
                    ShowToolWindow(() => new PlayerManagementWindow());
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
                    var ownerWindow = GetMainWindow()
                        ?? new Window { Width = 1, Height = 1, WindowStartupLocation = WindowStartupLocation.CenterScreen };
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
                    var ownerWindow = GetMainWindow()
                        ?? new Window { Width = 1, Height = 1, WindowStartupLocation = WindowStartupLocation.CenterScreen };
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
                try
                {
                    await foreach (var inp in coordinator.ReadInputsAsync(_sessionCts.Token))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            // Track presence (connected but may not have saved character yet)
                            if (!ConnectedPlayers.Contains(inp.Player))
                                ConnectedPlayers.Add(inp.Player);

                            if (!RemotePlayers.Any(p => string.Equals(p.Name, inp.Player, StringComparison.OrdinalIgnoreCase)))
                                RemotePlayers.Add(new RemotePlayerViewModel(inp.Player));

                            // Show join message only when character is saved — not on first message
                            if (coordinator.IsJoined(inp.Player) && _welcomedPlayers.Add(inp.Player))
                            {
                                Messages.Add(new Message("System", $"'{inp.Player}' has joined the session."));

                                // Register with TurnEngine now that the player is fully joined.
                                // Use the locked IsPlayerActive accessor — AddPlayer is idempotent
                                // but calling it without a prior check would log a spurious full-session
                                // message for already-registered players.
                                if (!_turnEngine.IsPlayerActive(inp.Player))
                                {
                                    if (!_turnEngine.AddPlayer(inp.Player))
                                        Messages.Add(new Message("System", $"Session is full ({TurnEngine.MaxActivePlayers} players). '{inp.Player}' may spectate."));
                                }
                            }

                            UpdateRemotePlayerFromCoordinator(inp.Player);
                        });

                        await HandleTurnAsync(inp.Player, inp.Text, broadcaster);
                    }
                }
                catch (OperationCanceledException) { /* normal shutdown */ }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[NovaGM] Player input loop failed: {ex}");
                    Dispatcher.UIThread.Post(() =>
                        Messages.Add(new Message("System", "Lost connection to player network. Restart the session to reconnect.")));
                }
            });
        }

        private static void ShowToolWindow(Func<Window> factory)
        {
            var owner = GetMainWindow();
            if (owner is not null)
                factory().Show(owner);
            else
                factory().Show();
        }

        /// Returns the app's main window, or null if running headless.
        private static Window? GetMainWindow()
            => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life
                ? life.MainWindow
                : null;

        private void WireTurnEngine()
        {
            var broadcaster = LocalBroadcaster.Instance;

            // Announce whose turn it is; always update the turn indicator
            _turnEngine.TurnStarted += playerId =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    CurrentTurnPlayer = playerId;
                    var isHub = HubCharacters.Any(c =>
                        c.Character.Name.Equals(playerId, StringComparison.OrdinalIgnoreCase));
                    if (!isHub)
                        Messages.Add(new Message("System", $"It is {playerId}'s turn."));
                });
            };

            // GM-initiative turn: world advances after a full round
            _turnEngine.GmTurnRequired += async () =>
            {
                Dispatcher.UIThread.Post(() => CurrentTurnPlayer = "GM");
                await RunGmNarrativeAsync(broadcaster);
            };

            // Interrupt event: GM injects an unexpected beat (ambush, distress call, etc.)
            _turnEngine.InterruptEventFired += async reason =>
                await RunGmNarrativeAsync(broadcaster, interruptReason: reason);
        }

        /// Runs a GM-authored narrative turn (world-advance or interrupt) and streams it to the UI and LAN.
        private async Task RunGmNarrativeAsync(LocalBroadcaster broadcaster, string? interruptReason = null)
        {
            var gm = new Message("GM", "");
            Dispatcher.UIThread.Post(() => Messages.Add(gm));
            broadcaster.PublishEvent("gm_start", "", "");

            var final = await _agent.RunGmTurnAsync(
                _sessionCts.Token,
                onNarratorToken: chunk =>
                {
                    Dispatcher.UIThread.Post(() => gm.Append(chunk));
                    broadcaster.Publish(chunk);
                },
                interruptReason: interruptReason);

            Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrEmpty(final) && !gm.Content.Equals(final))
                    gm.Content = final;
                broadcaster.PublishEvent("gm_end", "", "");
                if (!string.IsNullOrWhiteSpace(final))
                    MessageHistoryService.AddMessage(new Models.Message("GM", final));
            });
        }

        private async Task HandleTurnAsync(string playerName, string text, LocalBroadcaster broadcaster)
        {
            // Empty text is a silent join notification enqueued by the /character POST handler.
            // The Dispatcher.Post in the input loop already updated the UI; nothing more to do.
            if (string.IsNullOrWhiteSpace(text)) return;

            await _turnLock.WaitAsync();
            try
            {
                if (!GenreManager.Current.GameStarted)
                {
                    GenreManager.StartGame();
                    // First action — start the turn engine now that the game is live
                    if (!_turnEngineStarted)
                    {
                        _turnEngineStarted = true;
                        _ = _turnEngine.StartAsync(_sessionCts.Token);
                    }
                }

                var isHubPlayer = playerName == "GM" ||
                    HubCharacters.Any(c => c.Character.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

                // Remote player: must be fully joined and registered with TurnEngine
                if (!isHubPlayer)
                {
                    if (!GameCoordinator.Instance.IsJoined(playerName))
                        return; // Character not saved yet — ignore silently

                    // Register with TurnEngine if not already (handles late joiners)
                    if (!_turnEngine.IsPlayerActive(playerName))
                    {
                        if (!_turnEngine.AddPlayer(playerName))
                        {
                            Dispatcher.UIThread.Post(() =>
                                Messages.Add(new Message("System",
                                    $"Session is full. {playerName} may spectate only.")));
                            return;
                        }
                    }
                }

                // In multi-player, enforce whose turn it is
                var isSolo = _turnEngine.ActivePlayerCount <= 1;
                if (!isSolo && !isHubPlayer)
                {
                    var current = _turnEngine.CurrentPlayerId;
                    if (!string.Equals(current, playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.UIThread.Post(() =>
                            Messages.Add(new Message("System",
                                $"Waiting for {current}'s turn.")));
                        return;
                    }
                }

                // Display player input
                var displayName = playerName == "GM"
                    ? HubCharacters.FirstOrDefault()?.Character.Name ?? "GM"
                    : playerName;
                var inputText = playerName == "GM" ? $"GM instruction: {text}" : text;
                var actingId  = playerName == "GM"
                    ? (HubCharacters.FirstOrDefault()?.Character.Name ?? "GM")
                    : playerName;

                var playerMessage = new Message(displayName, text);
                Dispatcher.UIThread.Post(() =>
                {
                    Messages.Add(playerMessage);
                    MessageHistoryService.AddMessage(new Models.Message(displayName, text));
                });
                broadcaster.PublishEvent("player", displayName, text);

                // Stream GM response
                var gm = new Message("GM", "");
                Dispatcher.UIThread.Post(() => Messages.Add(gm));
                broadcaster.PublishEvent("gm_start", "", "");

                var final = await _agent.RunTurnAsync(
                    inputText,
                    _sessionCts.Token,
                    onNarratorToken: chunk =>
                    {
                        Dispatcher.UIThread.Post(() => gm.Append(chunk));
                        broadcaster.Publish(chunk);
                    },
                    actingPlayerId: actingId);

                Dispatcher.UIThread.Post(() =>
                {
                    if (!string.IsNullOrEmpty(final) && !gm.Content.Equals(final))
                        gm.Content = final;
                    broadcaster.PublishEvent("gm_end", "", "");
                    MessageHistoryService.AddMessage(new Models.Message("GM", gm.Content));
                });

                // Advance the turn engine
                await _turnEngine.RecordActionAsync(actingId, _sessionCts.Token);
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

        private void ShowRemotePlayer(RemotePlayerViewModel remote)
        {
            UpdateRemotePlayerFromCoordinator(remote.Name);
            ActiveRemotePlayer = remote;
        }

        private void UpdateRemotePlayerFromCoordinator(string playerName)
        {
            var remote = RemotePlayers.FirstOrDefault(p => string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase));
            if (remote is null) return;

            var character = GameCoordinator.Instance.GetPlayerCharacter(playerName);
            if (character is not null)
            {
                character.Inventory = _inventoryService.GetInventoryForPlayer(playerName);
            }
            remote.UpdateFrom(character);

            if (ActiveRemotePlayer == remote)
            {
                OnPropertyChanged(nameof(ActiveRemotePlayer));
            }
        }

        private async Task CreateHubCharacterAsync()
        {
            CharacterDraft? draft = null;

            var creator = new CharacterCreatorWindow();
            Window? tempOwner = null;
            var ownerWindow = GetMainWindow();
            if (ownerWindow is null)
            {
                tempOwner = new Window { Width = 1, Height = 1, Opacity = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                tempOwner.Show();
                ownerWindow = tempOwner;
            }

            try
            {
                draft = await creator.ShowDialog<CharacterDraft?>(ownerWindow);
            }
            catch (Exception ex)
            {
                Messages.Add(new Message("GM", $"Failed to create character: {ex.Message}"));
            }
            finally
            {
                tempOwner?.Close();
            }

            if (draft is null)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                ApplyCharacterDraft(draft);
            });
        }

        private void ApplyCharacterDraft(CharacterDraft draft)
        {
            var character = CreateCharacterFromDraft(draft);
            var key = InventoryKeys.ForHubCharacter(character.Name);
            character.Inventory = _inventoryService.GetInventory(key);
            var sheet = new CharacterSheetViewModel(character);

            // Remove old hub character from TurnEngine and register the new one
            if (HubCharacters.Count > 0 && HubCharacters[0].Character.Name != character.Name)
            {
                _turnEngine.RemovePlayer(HubCharacters[0].Character.Name);
                _turnEngine.AddPlayer(character.Name);
            }

            if (HubCharacters.Count == 1 && ReferenceEquals(HubCharacters[0], CharacterSheet))
            {
                HubCharacters.Clear();
            }

            HubCharacters.Add(sheet);
            SelectedHubCharacter = sheet;
            _inventoryService.SaveInventory(key, character.Inventory);
        }

        private Task DeleteHubCharacterAsync()
        {
            var target = SelectedHubCharacter;
            if (target is null) return Task.CompletedTask;

            var name = target.Character.Name;

            // Unregister from TurnEngine so their slot doesn't block turn advancement
            _turnEngine.RemovePlayer(name);

            // Remove from UI list and select next available character
            HubCharacters.Remove(target);
            SelectedHubCharacter = HubCharacters.Count > 0 ? HubCharacters[^1] : null;

            // Clean up persisted inventory snapshot
            _inventoryService.RemoveInventory(InventoryKeys.ForHubCharacter(name));

            Messages.Add(new Message("System", $"{name} has been removed."));
            return Task.CompletedTask;
        }

        private static Character CreateCharacterFromDraft(CharacterDraft draft)
        {
            var stats = draft.Stats ?? new Stats();

            var character = new Character
            {
                Name = string.IsNullOrWhiteSpace(draft.Name) ? "Player Character" : draft.Name.Trim(),
                Race = string.IsNullOrWhiteSpace(draft.Race) ? "Unknown" : draft.Race.Trim(),
                Class = string.IsNullOrWhiteSpace(draft.Class) ? "Adventurer" : draft.Class.Trim(),
                Level = draft.Level > 0 ? draft.Level : 1,
                Stats = new Stats
                {
                    STR = NormalizeStat(stats.STR),
                    DEX = NormalizeStat(stats.DEX),
                    CON = NormalizeStat(stats.CON),
                    INT = NormalizeStat(stats.INT),
                    WIS = NormalizeStat(stats.WIS),
                    CHA = NormalizeStat(stats.CHA)
                },
                Equipment = BuildStarterEquipment(draft)
            };

            return character;
        }

        private static Dictionary<EquipmentSlot, Item> BuildStarterEquipment(CharacterDraft draft)
        {
            var equipment = new Dictionary<EquipmentSlot, Item>();
            var genre = GenreManager.Current.Genre;
            var classId = (draft.Class ?? string.Empty).ToLowerInvariant();

            void Add(EquipmentSlot slot, string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                equipment[slot] = new Item { Slot = slot, Name = name };
            }

            void AddCommon(string boots = "", string hands = "", string belt = "")
            {
                if (!string.IsNullOrWhiteSpace(boots)) Add(EquipmentSlot.Feet, boots);
                if (!string.IsNullOrWhiteSpace(hands)) Add(EquipmentSlot.Hands, hands);
                if (!string.IsNullOrWhiteSpace(belt)) Add(EquipmentSlot.Belt, belt);
            }

            switch (genre)
            {
                case GameGenre.Fantasy:
                    if (classId.Contains("wizard") || classId.Contains("mage"))
                    {
                        Add(EquipmentSlot.MainHand, "Wizard's Staff");
                        Add(EquipmentSlot.Cloak, "Spellweave Cloak");
                        Add(EquipmentSlot.Chest, "Apprentice Robes");
                    }
                    else if (classId.Contains("cleric"))
                    {
                        Add(EquipmentSlot.MainHand, "Warhammer");
                        Add(EquipmentSlot.OffHand, "Polished Shield");
                        Add(EquipmentSlot.Chest, "Scale Mail");
                    }
                    else if (classId.Contains("rogue"))
                    {
                        Add(EquipmentSlot.MainHand, "Twin Daggers");
                        Add(EquipmentSlot.Cloak, "Shadow Cloak");
                        Add(EquipmentSlot.Chest, "Soft Leather Armor");
                    }
                    else
                    {
                        Add(EquipmentSlot.MainHand, "Longsword");
                        Add(EquipmentSlot.OffHand, "Wooden Shield");
                        Add(EquipmentSlot.Chest, "Chain Shirt");
                    }
                    AddCommon("Traveler's Boots", "Leather Gloves", "Adventurer's Belt");
                    break;

                case GameGenre.SciFi:
                    Add(EquipmentSlot.Head, "Tactical Visor");
                    if (classId.Contains("engineer") || classId.Contains("hacker"))
                    {
                        Add(EquipmentSlot.MainHand, "Smart Toolkit");
                        Add(EquipmentSlot.Chest, "Utility Jumpsuit");
                        Add(EquipmentSlot.Hands, "Interface Gloves");
                    }
                    else if (classId.Contains("scientist"))
                    {
                        Add(EquipmentSlot.MainHand, "Research Scanner");
                        Add(EquipmentSlot.Chest, "Nano-Fabric Lab Coat");
                    }
                    else
                    {
                        Add(EquipmentSlot.MainHand, "Pulse Carbine");
                        Add(EquipmentSlot.Chest, "Composite Armor Vest");
                        Add(EquipmentSlot.OffHand, "Deployable Shield");
                    }
                    AddCommon("Mag-Boots", equipment.TryGetValue(EquipmentSlot.Hands, out _) ? "" : "Carbon Gloves", "Utility Harness");
                    break;

                case GameGenre.Horror:
                    Add(EquipmentSlot.MainHand, "Crowbar");
                    Add(EquipmentSlot.OffHand, "Flashlight");
                    Add(EquipmentSlot.Chest, "Weathered Jacket");
                    AddCommon("Sturdy Boots", "Work Gloves", "Survival Satchel");
                    break;

                default:
                    Add(EquipmentSlot.MainHand, "Reliable Blade");
                    Add(EquipmentSlot.OffHand, "Sturdy Shield");
                    Add(EquipmentSlot.Chest, "Traveler's Vest");
                    AddCommon("Trail Boots", "Ropebound Gloves", "Utility Belt");
                    break;
            }

            return equipment;
        }

        private static int NormalizeStat(int value) => value <= 0 ? 10 : value;

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

                    // Persist the loaded mission state so it survives a restart.
                    _agent.StateStore.Save();
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
                var ownerWindow = GetMainWindow();
                
                if (ownerWindow != null)
                    await exitDialog.ShowDialog(ownerWindow);
                else
                {
                    exitDialog.Show();
                    while (exitDialog.IsVisible) await Task.Delay(50);
                }

                if (exitDialog.Result != true)
                {
                    return; // User cancelled exit
                }

                // Step 2: Save session dialog
                var saveDialog = new SaveSessionDialog();
                if (ownerWindow != null)
                    await saveDialog.ShowDialog(ownerWindow);
                else
                {
                    saveDialog.Show();
                    while (saveDialog.IsVisible) await Task.Delay(50);
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
            var ownerWindow = GetMainWindow();
            
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
                await countdownWindow.ShowDialog(ownerWindow);
            else
            {
                countdownWindow.Show();
                while (countdownWindow.IsVisible) await Task.Delay(50);
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

        /// Left-border accent colour for this message type in the session chat.
        public string RoleAccent => Role switch
        {
            "GM"     => "#5A66FF",  // primary blue — AI narration
            "System" => "#D4961A",  // amber — engine events
            _        => "#4A9E6A"   // green — player actions
        };

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
        public async void Execute(object? parameter)
        {
            if (_async is null) return;
            try { await _async(parameter); }
            catch (Exception ex) { Console.Error.WriteLine($"[NovaGM] Command error: {ex}"); }
        }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
