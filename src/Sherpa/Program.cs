using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.WebView.Desktop;
using Velopack;

namespace Sherpa;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run before any other startup work (handles install/update hooks).
        try
        {
            VelopackApp.Build()
                .OnFirstRun(_ => { /* reserved — first-run tips later */ })
                .Run();
        }
        catch (Exception ex)
        {
            CrashLog.Write("VelopackApp.Run", ex, showDialog: false);
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashLog.Write("UnhandledException", ex, showDialog: true);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("UnobservedTaskException", e.Exception, showDialog: false);
            e.SetObserved();
        };

        try
        {
            EnsureWebView2LoaderDiscoverable();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Main", ex, showDialog: true);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        try
        {
            builder = builder.UseDesktopWebView();
        }
        catch (Exception ex)
        {
            CrashLog.Write("UseDesktopWebView", ex, showDialog: false);
        }

        return builder;
    }

    private static void EnsureWebView2LoaderDiscoverable()
    {
        try
        {
            var dirs = new List<string>();

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

            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                var coreSrc = Path.Combine(exeDir, "Microsoft.Web.WebView2.Core.dll");
                var coreDest = Path.Combine(stableDir, "Microsoft.Web.WebView2.Core.dll");
                try
                {
                    if (File.Exists(coreSrc)
                        && (!File.Exists(coreDest) || new FileInfo(coreSrc).Length != new FileInfo(coreDest).Length))
                        File.Copy(coreSrc, coreDest, overwrite: true);
                }
                catch { /* ignore */ }
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var prefix = string.Join(Path.PathSeparator, dirs) + Path.PathSeparator;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", prefix + path);
        }
        catch (Exception ex)
        {
            CrashLog.Write("EnsureWebView2LoaderDiscoverable", ex, showDialog: false);
        }
    }
}

internal static class CrashLog
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    public static void Write(string where, Exception ex, bool showDialog)
    {
        var text = new StringBuilder()
            .AppendLine($"Sherpa crash @ {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Where: {where}")
            .AppendLine($"Exe: {Environment.ProcessPath}")
            .AppendLine($"BaseDir: {AppContext.BaseDirectory}")
            .AppendLine(ex.ToString())
            .AppendLine(new string('-', 60))
            .ToString();

        foreach (var path in CandidateLogPaths())
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(path, text);
            }
            catch { /* try next */ }
        }

        if (!showDialog) return;
        try
        {
            MessageBoxW(IntPtr.Zero,
                "Sherpa could not start.\n\n" +
                ex.Message + "\n\n" +
                "A log was written to Desktop / %LocalAppData%\\Sherpa\\sherpa-crash.log\n\n" +
                "Prefer the installer from GitHub Releases (Setup.exe), not running from inside a zip.",
                "Sherpa",
                0x00000010);
        }
        catch { /* ignore */ }
    }

    private static IEnumerable<string> CandidateLogPaths()
    {
        var name = "sherpa-crash.log";
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(exeDir))
            yield return Path.Combine(exeDir, name);

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sherpa",
            name);

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            name);
    }
}
