// NovaGM/App.axaml.cs
using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NovaGM.Services.Streaming;
using NovaGM.Services.Multiplayer;
using NovaGM.ViewModels;

namespace NovaGM
{
    public partial class App : Application
    {
        private LocalServer? _server;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Initialize GameCoordinator but don't auto-start server 
                // (let user control it manually via UI)
                var coord = GameCoordinator.Instance;
                _server = null; // Will be created when user clicks Start Server


                _server.Start(port, allowLan);

                var mw = new MainWindow
                {
                    DataContext = new MainWindowViewModel()
                };

                desktop.MainWindow = mw;
                desktop.Exit += (_, __) => SafeShutdownAndExit();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// Called from menu File→Exit and from window close.
        public void SafeShutdownAndExit()
        {
            try { _server?.Dispose(); } catch { }
            try { LocalBroadcaster.Instance.Complete(); } catch { }
            try { GameCoordinator.Instance.ResetRoom(); } catch { }

            // As a final guard, kill the process so nothing keeps the message loop alive.
            Environment.Exit(0);
        }
    }
}
