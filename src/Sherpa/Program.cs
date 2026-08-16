using System;
using System.IO;
using Avalonia;
using Avalonia.WebView.Desktop;

namespace Sherpa;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Single-file publish extracts native DLLs (incl. WebView2Loader.dll) under
        // AppContext.BaseDirectory (a temp folder). WebView2's loader lookup starts next
        // to the .exe path, so without help it never finds the DLL → black preview.
        EnsureWebView2LoaderDiscoverable();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseDesktopWebView();

    private static void EnsureWebView2LoaderDiscoverable()
    {
        try
        {
            var extractDir = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var loaderName = "WebView2Loader.dll";
            var extracted = Path.Combine(extractDir, loaderName);

            // Always prefer a stable, writable copy under LocalAppData.
            var stableDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sherpa",
                "native");
            Directory.CreateDirectory(stableDir);
            var stableLoader = Path.Combine(stableDir, loaderName);

            if (File.Exists(extracted))
            {
                var needsCopy = !File.Exists(stableLoader)
                    || new FileInfo(extracted).Length != new FileInfo(stableLoader).Length;
                if (needsCopy)
                    File.Copy(extracted, stableLoader, overwrite: true);
            }

            // Prepend extract dir + stable dir to PATH so LoadLibrary finds the loader
            // even when the real Sherpa.exe lives somewhere else (Desktop, Downloads, etc.).
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var prefix = extractDir + Path.PathSeparator + stableDir + Path.PathSeparator;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", prefix + path);

            // Best-effort: also drop a copy next to the exe when the folder is writable
            // (Desktop / Downloads). Harmless if it fails (Program Files, etc.).
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(stableLoader))
            {
                var exeDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrWhiteSpace(exeDir))
                {
                    var besideExe = Path.Combine(exeDir, loaderName);
                    if (!File.Exists(besideExe))
                    {
                        try { File.Copy(stableLoader, besideExe, overwrite: false); }
                        catch { /* not writable — PATH fallback is enough */ }
                    }
                }
            }
        }
        catch
        {
            // Never block app start on loader bootstrap.
        }
    }
}
