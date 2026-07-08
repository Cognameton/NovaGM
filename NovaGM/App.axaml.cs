// NovaGM/App.axaml.cs
using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NovaGM.Services;
using NovaGM.Services.Streaming;
using NovaGM.Services.Multiplayer;
using NovaGM.ViewModels;

namespace NovaGM
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Start the join/HUD web server using the user's saved settings.
                // Routed through ServicesHost so the exit sequence can stop it.
                ServicesHost.Start(Config.EffectivePort, Config.Current.AllowLan);

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
            try { ServicesHost.Stop(); } catch { }
            try { LocalBroadcaster.Instance.Complete(); } catch { }
            try { GameCoordinator.Instance.ResetRoom(); } catch { }

            // As a final guard, kill the process so nothing keeps the message loop alive.
            Environment.Exit(0);
        }
    }
}
