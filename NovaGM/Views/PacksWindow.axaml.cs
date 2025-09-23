using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services.Packs;

namespace NovaGM.Views
{
    public partial class PacksWindow : Window
    {
        private PackInfo[] _packs = Array.Empty<PackInfo>();

        public PacksWindow()
        {
            InitializeComponent();
            Reload();
        }

        private void Reload()
        {
            _packs = PackManager.Discover().ToArray();
            var active = PackManager.GetActiveId();
            var items = _packs.Select(p => $"{p.Manifest.Name}  —  v{p.Manifest.Version}" +
                                            (p.Manifest.Id == active ? "  [ACTIVE]" : "")).ToArray();
            List.ItemsSource = items;
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();

        private void SetActive_Click(object? sender, RoutedEventArgs e)
        {
            var idx = List.SelectedIndex;
            if (idx < 0 || idx >= _packs.Length) return;
            PackManager.SetActiveId(_packs[idx].Manifest.Id);
            Reload();
        }

        private void OpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "packs");
            Directory.CreateDirectory(dir);
            try
            {
                var psi = OperatingSystem.IsWindows()
                    ? new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
                    : new ProcessStartInfo("xdg-open", dir);
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
