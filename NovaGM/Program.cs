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

        var nativeDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native");
        var cudaDir   = Path.Combine(nativeDir, "cuda12");
        if (!Directory.Exists(cudaDir)) return;

        // libggml.so (in cuda12) depends on libggml-cpu.so which lives in the CPU folder.
        // Detect the best available AVX level and load it first.
        var cpuDir = BestCpuDir(nativeDir);
        if (cpuDir != null)
            LoadLib(Path.Combine(cpuDir, "libggml-base.so")); // cpu folder also has base

        // Full dependency order for the CUDA backend:
        //   libggml-base.so  <- no local deps
        //   libggml-cpu.so   <- needs libggml-base.so
        //   libggml-cuda.so  <- needs libggml-base.so
        //   libggml.so       <- needs libggml-base.so + libggml-cpu.so + libggml-cuda.so
        //   libllama.so      <- needs libggml.so + libggml-base.so
        //   libmtmd.so       <- needs libllama.so + libggml.so + libggml-base.so
        LoadLib(Path.Combine(cudaDir, "libggml-base.so"));
        if (cpuDir != null)
            LoadLib(Path.Combine(cpuDir, "libggml-cpu.so"));
        LoadLib(Path.Combine(cudaDir, "libggml-cuda.so"));
        LoadLib(Path.Combine(cudaDir, "libggml.so"));
        LoadLib(Path.Combine(cudaDir, "libllama.so"));
        LoadLib(Path.Combine(cudaDir, "libmtmd.so"));
    }

    /// Returns the path to the best CPU-level native subfolder available on this machine.
    private static string? BestCpuDir(string nativeDir)
    {
        try
        {
            var flags = File.ReadAllText("/proc/cpuinfo");
            string[] candidates = flags.Contains("avx512") ? new[] { "avx512", "avx2", "avx", "noavx" }
                                : flags.Contains("avx2")   ? new[] { "avx2",   "avx",  "noavx" }
                                : flags.Contains(" avx ")  ? new[] { "avx",    "noavx" }
                                :                            new[] { "noavx" };
            foreach (var c in candidates)
            {
                var dir = Path.Combine(nativeDir, c);
                if (Directory.Exists(dir)) return dir;
            }
        }
        catch { }
        return null;
    }

    private static void LoadLib(string path)
    {
        if (!File.Exists(path)) return;
        try { NativeLibrary.Load(path); }
        catch (Exception ex)
        {
            Console.WriteLine($"[NovaGM] CUDA pre-load warning: {Path.GetFileName(path)} — {ex.Message}");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
