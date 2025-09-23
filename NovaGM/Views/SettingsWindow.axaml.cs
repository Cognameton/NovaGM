using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services;

namespace NovaGM.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // Load
            ChkSingleRoom.IsChecked = Config.Current.SingleRoom;
            ChkUseGpu.IsChecked     = Config.Current.UseGpu;
            SldGpuLayers.Value      = Config.Current.GpuLayers;
            LblGpuLayers.Text       = Config.Current.GpuLayers.ToString();

            SldNarrTokens.Value     = Config.Current.NarratorMaxTokens <= 0 ? 260 : Config.Current.NarratorMaxTokens;
            LblNarrTokens.Text      = ((int)SldNarrTokens.Value).ToString();

            ChkAllowLan.IsChecked   = Config.Current.AllowLan;
            TxtPort.Text            = (Config.Current.HttpPort <= 0 ? 5055 : Config.Current.HttpPort).ToString();

            ChkUseGpu.IsCheckedChanged += (_, __) =>
            {
                var on = ChkUseGpu.IsChecked == true;
                SldGpuLayers.IsEnabled = on;
                InfoText.Text = on
                    ? "Changes to GPU or LAN binding take effect after restart."
                    : "GPU disabled. Changes to LAN binding take effect after restart.";
            };
            SldGpuLayers.IsEnabled = ChkUseGpu.IsChecked == true;

            SldNarrTokens.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == "Value")
                    LblNarrTokens.Text = ((int)SldNarrTokens.Value).ToString();
            };
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            // Validate port
            int port = 5055;
            if (!string.IsNullOrWhiteSpace(TxtPort.Text) && int.TryParse(TxtPort.Text, out var p) && p >= 1024 && p <= 65535)
                port = p;

            Config.Current.SingleRoom        = ChkSingleRoom.IsChecked == true;
            Config.Current.UseGpu            = ChkUseGpu.IsChecked == true;
            Config.Current.GpuLayers         = (int)SldGpuLayers.Value;
            Config.Current.NarratorMaxTokens = (int)SldNarrTokens.Value;
            Config.Current.AllowLan          = ChkAllowLan.IsChecked == true;
            Config.Current.HttpPort          = port;

            Config.Save();
            Close();
        }
    }
}
