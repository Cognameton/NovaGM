using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaGM.Services;

namespace NovaGM.Views
{
    public partial class ModelsWindow : Window
    {
        public ModelsWindow()
        {
            InitializeComponent();

            var list = ModelRegistry.ListGguf().ToList();
            list.Insert(0, "(auto: first .gguf)");

            CmbController.ItemsSource = list;
            CmbNarrator.ItemsSource   = list;
            CmbMemory.ItemsSource     = list;

            CmbController.SelectedItem = ModelRegistry.GetAssigned("controller") ?? "(auto: first .gguf)";
            CmbNarrator.SelectedItem   = ModelRegistry.GetAssigned("narrator")   ?? "(auto: first .gguf)";
            CmbMemory.SelectedItem     = ModelRegistry.GetAssigned("memory")     ?? "(auto: first .gguf)";
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            static string? Norm(object? s)
            {
                var str = s?.ToString();
                return (str == null || str.StartsWith("(auto")) ? null : str;
            }

            ModelRegistry.SetAssigned("controller", Norm(CmbController.SelectedItem));
            ModelRegistry.SetAssigned("narrator",   Norm(CmbNarrator.SelectedItem));
            ModelRegistry.SetAssigned("memory",     Norm(CmbMemory.SelectedItem));

            Close();
        }

        private void OpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "llm");
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
