// NovaGM/Services/Paths.cs
using System;
using System.IO;

namespace NovaGM.Services
{
    public static class Paths
    {
        public static string AppDataDir { get; }

        static Paths()
        {
            // Cross-platform app data directory (~/.local/share/NovaGM on Linux)
            string baseDir;
            if (OperatingSystem.IsLinux())
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                baseDir = string.IsNullOrWhiteSpace(xdg)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                    : xdg;
            }
            else if (OperatingSystem.IsMacOS())
            {
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support");
            }
            else
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            AppDataDir = Path.Combine(baseDir, "NovaGM");
            Directory.CreateDirectory(AppDataDir);
        }
    }
}
