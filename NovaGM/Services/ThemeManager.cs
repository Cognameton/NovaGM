using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;                  // ResourceDictionary (if you ever need it)
using Avalonia.Markup.Xaml;               // AvaloniaXamlLoader (not used here, but fine to keep)
using Avalonia.Markup.Xaml.Styling;       // StyleInclude
using Avalonia.Styling;                   // IStyle

namespace NovaGM.Services
{
    public sealed class ThemeManager
    {
        private IStyle? _active;

        public void ApplyBaseTheme()
        {
            var style = new StyleInclude(new Uri("avares://NovaGM/"))
            {
                Source = new Uri("avares://NovaGM/Themes/NovaClean.axaml")
            };
            Swap(style);
        }

        /// <summary>
        /// Load a theme dictionary from a file on disk (e.g., from a lore pack).
        /// </summary>
        public void ApplyLoreThemeFromFile(string axamlPath)
        {
            var full = Path.GetFullPath(axamlPath);
            if (!File.Exists(full))
                throw new FileNotFoundException("Theme file not found.", full);

            // Build a file:// URI that works on Windows & Linux
            // Linux absolute path -> file:///home/... ; Windows -> file:///C:/...
            var uri = BuildFileUri(full);

            var style = new StyleInclude(uri)
            {
                Source = uri
            };
            Swap(style);
        }

        public void ClearLoreTheme() => Swap(null);

        private void Swap(IStyle? style)
        {
            var styles = Application.Current!.Styles;
            if (_active is not null) styles.Remove(_active);
            if (style is not null) styles.Add(style);
            _active = style;
        }

        private static Uri BuildFileUri(string fullPath)
        {
            // Normalize slashes and ensure absolute file URI
            if (OperatingSystem.IsWindows())
            {
                var normalized = fullPath.Replace('\\', '/');
                if (!normalized.StartsWith("/"))
                    normalized = "/" + normalized; // file:///C:/...
                return new Uri($"file://{normalized}");
            }
            else
            {
                // On Linux/macOS, absolute paths already start with '/'
                return new Uri($"file://{fullPath}");
            }
        }
    }
}
