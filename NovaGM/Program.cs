using Avalonia;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NovaGM;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // LLamaSharp's Linux CUDA12 backend has a broken RUNPATH (baked CI path).
        // Pre-load the sibling .so files in dependency order using full paths so the
        // OS dynamic linker finds libggml-base.so when libggml-cuda.so needs it.
        PreloadCudaBackend();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void PreloadCudaBackend()
    {
        if (!OperatingSystem.IsLinux()) return;

        var cudaDir = Path.Combine(AppContext.BaseDirectory,
            "runtimes", "linux-x64", "native", "cuda12");
        if (!Directory.Exists(cudaDir)) return;

        // Load in dependency order: base first, then consumers
        string[] order = { "libggml-base.so", "libggml.so", "libggml-cuda.so", "libmtmd.so", "libllama.so" };
        foreach (var name in order)
        {
            var path = Path.Combine(cudaDir, name);
            if (File.Exists(path))
            {
                try { NativeLibrary.Load(path); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NovaGM] CUDA pre-load warning: {name} — {ex.Message}");
                }
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
