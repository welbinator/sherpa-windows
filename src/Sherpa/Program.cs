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
        // WebView2 Core+Loader are published beside the exe (excluded from the
        // single-file bundle). Make sure that directory is on PATH early.
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
            var dirs = new System.Collections.Generic.List<string>();

            var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(exeDir))
                dirs.Add(exeDir);

            var baseDir = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(baseDir))
                dirs.Add(baseDir);

            var stableDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sherpa",
                "native");
            Directory.CreateDirectory(stableDir);
            dirs.Add(stableDir);

            // If loader sits next to the exe, also keep a stable copy under LocalAppData.
            foreach (var dir in dirs)
            {
                var src = Path.Combine(dir, "WebView2Loader.dll");
                if (!File.Exists(src)) continue;
                var dest = Path.Combine(stableDir, "WebView2Loader.dll");
                try
                {
                    if (!File.Exists(dest) || new FileInfo(src).Length != new FileInfo(dest).Length)
                        File.Copy(src, dest, overwrite: true);
                }
                catch { /* ignore */ }
                break;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var prefix = string.Join(Path.PathSeparator, dirs) + Path.PathSeparator;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", prefix + path);
        }
        catch
        {
            // Never block app start on loader bootstrap.
        }
    }
}
