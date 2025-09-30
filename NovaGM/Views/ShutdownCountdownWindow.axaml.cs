using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace NovaGM.Views
{
    public partial class ShutdownCountdownWindow : Window
    {
        private CancellationTokenSource? _countdownCts;
        private int _remainingSeconds = 60;
        public bool WasCancelled { get; private set; }

        public ShutdownCountdownWindow()
        {
            InitializeComponent();
            StartCountdown();
        }

        private async void StartCountdown()
        {
            _countdownCts = new CancellationTokenSource();
            
            try
            {
                while (_remainingSeconds > 0 && !_countdownCts.Token.IsCancellationRequested)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        CountdownText.Text = _remainingSeconds.ToString();
                        ShutdownProgress.Value = 60 - _remainingSeconds;
                    });

                    await Task.Delay(1000, _countdownCts.Token);
                    _remainingSeconds--;
                }

                if (!_countdownCts.Token.IsCancellationRequested)
                {
                    // Countdown completed, close the window
                    await Dispatcher.UIThread.InvokeAsync(() => Close(false));
                }
            }
            catch (OperationCanceledException)
            {
                // Countdown was cancelled
            }
        }

        public void AddStatusMessage(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var statusItem = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Avalonia.Thickness(0, 2) };
                statusItem.Children.Add(new TextBlock { Text = "✓", Foreground = Avalonia.Media.Brushes.Green, Margin = new Avalonia.Thickness(0, 0, 8, 0) });
                statusItem.Children.Add(new TextBlock { Text = message, FontSize = 12 });
                
                StatusPanel.Children.Add(statusItem);
            });
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            WasCancelled = true;
            _countdownCts?.Cancel();
            Close(true);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _countdownCts?.Cancel();
            base.OnClosing(e);
        }
    }
}