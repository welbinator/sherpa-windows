using System.Diagnostics;
using Sherpa.Support;

namespace Sherpa.Services;

/// <summary>
/// Windows port of Mac HerdService — park/link sites, secure HTTPS, ensure Herd is running.
/// </summary>
public sealed class HerdService
{
    private readonly ProcessRunner _runner;

    public HerdService(ProcessRunner runner) => _runner = runner;

    public string? FindHerdCli()
    {
        foreach (var c in CandidateCliPaths())
            if (File.Exists(c)) return c;

        return Which("herd") ?? Which("herd.bat");
    }

    public string? FindHerdApp()
    {
        foreach (var c in CandidateAppPaths())
            if (File.Exists(c)) return c;
        return null;
    }

    public bool IsAvailable => FindHerdCli() is not null || FindHerdApp() is not null;

    public bool IsRunning()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var n = p.ProcessName;
                    if (n.Equals("Herd", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("Herd", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    // access denied on some processes
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    /// <summary>
    /// Launch Herd if it isn't running, then wait briefly for it to come up.
    /// Safe to call before Open site / link / secure.
    /// </summary>
    public async Task<(bool ok, string message)> EnsureRunningAsync(
        Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        if (IsRunning())
            return (true, "Herd is already running.");

        var app = FindHerdApp();
        var cli = FindHerdCli();

        if (app is null && cli is null)
            return (false, "Herd was not found. Install Laravel Herd, then try again.");

        onLine?.Invoke("Starting Herd…");

        try
        {
            if (app is not null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = app,
                    UseShellExecute = true,
                });
            }
            else if (cli is not null)
            {
                // Some builds expose `herd start` / opening via CLI
                var start = await _runner.RunAsync(cli, new[] { "start" }, null, null, onLine, ct);
                if (!start.Success)
                {
                    // last resort: start the bat with no args (may open GUI)
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = cli,
                        UseShellExecute = true,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            return (false, "Could not start Herd: " + ex.Message);
        }

        // Wait up to ~20s for process or CLI to become usable
        for (var i = 0; i < 40; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (IsRunning())
            {
                onLine?.Invoke("Herd is running.");
                // small grace period for nginx/php services
                await Task.Delay(800, ct);
                return (true, "Herd started.");
            }

            await Task.Delay(500, ct);
        }

        // Process name may differ; if CLI exists, assume start was attempted
        if (cli is not null)
        {
            onLine?.Invoke("Herd launch was requested. If the site doesn’t load, open Herd once from the Start menu.");
            return (true, "Herd launch was requested.");
        }

        return (false, "Timed out waiting for Herd to start. Open Herd manually, then try again.");
    }

    public string DefaultSitesDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var herd = Path.Combine(home, "Herd");
        if (Directory.Exists(herd)) return herd;
        var sites = Path.Combine(home, "Sites");
        return Directory.Exists(sites) ? sites : herd;
    }

    public static string Slug(string name)
    {
        var s = name.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return "site";
        var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    public string UrlPreview(string siteName, bool https)
    {
        var host = Slug(siteName);
        return https ? $"https://{host}.test" : $"http://{host}.test";
    }

    public string WillCreatePath(string folder, string siteName)
        => Path.GetFullPath(Path.Combine(
            string.IsNullOrWhiteSpace(folder) ? DefaultSitesDirectory() : folder,
            Slug(siteName)));

    public async Task<(bool ok, string message)> ParkAsync(
        string sitePath,
        string siteName,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        var ensure = await EnsureRunningAsync(onLine, ct);
        if (!ensure.ok) return ensure;

        var herd = FindHerdCli();
        if (herd is null)
            return (false, "Herd CLI was not found. Install Laravel Herd and open it once, then try again.");

        var slug = Slug(siteName);
        onLine?.Invoke($"Park in Herd and open as {slug}.test…");
        var link = await _runner.RunAsync(herd, new[] { "link", slug }, sitePath, null, onLine, ct);
        if (link.Success)
            return (true, $"Parked in Herd as {slug}.test");

        var parent = Directory.GetParent(sitePath)?.FullName ?? sitePath;
        onLine?.Invoke("Link failed; trying herd park on the parent folder…");
        var park = await _runner.RunAsync(herd, new[] { "park", parent }, parent, null, onLine, ct);
        if (!park.Success)
            return (false, $"Could not park in Herd.\n{link.Combined}\n{park.Combined}");

        return (true, $"Parked path {parent}. Herd should serve the folder as {slug}.test when the name matches.");
    }

    public async Task<(bool ok, string message)> SecureAsync(
        string siteName,
        bool enable,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        var ensure = await EnsureRunningAsync(onLine, ct);
        if (!ensure.ok) return ensure;

        var herd = FindHerdCli();
        if (herd is null)
            return (false, "Herd CLI was not found. Install Laravel Herd first.");

        var slug = Slug(siteName);
        var args = enable ? new[] { "secure", slug } : new[] { "unsecure", slug };
        onLine?.Invoke(enable ? "Secure with HTTPS (Herd)…" : "Removing HTTPS…");
        var r = await _runner.RunAsync(herd, args, null, null, onLine, ct);
        return r.Success
            ? (true, enable ? "HTTPS enabled via Herd." : "HTTPS removed.")
            : (false, r.Combined);
    }

    private static IEnumerable<string> CandidateCliPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Programs", "Herd", "resources", "app.asar.unpacked", "resources", "bin", "herd.bat");
        yield return Path.Combine(local, "Programs", "Herd", "bin", "herd.bat");
        yield return Path.Combine(local, "Programs", "Herd", "resources", "bin", "herd.bat");
        yield return Path.Combine(home, ".config", "herd", "bin", "herd.bat");
        yield return Path.Combine(home, "AppData", "Roaming", "Herd", "bin", "herd.bat");
        yield return Path.Combine(home, "AppData", "Local", "herd", "bin", "herd.bat");
    }

    private static IEnumerable<string> CandidateAppPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Programs", "Herd", "Herd.exe");
        yield return Path.Combine(local, "Programs", "herd", "Herd.exe");
        yield return Path.Combine(local, "Herd", "Herd.exe");
        yield return Path.Combine(home, "AppData", "Local", "Programs", "Herd", "Herd.exe");
        // Start Menu shortcut target often under Local\Programs
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        if (Directory.Exists(startMenu))
        {
            foreach (var lnk in Directory.EnumerateFiles(startMenu, "*Herd*.lnk", SearchOption.AllDirectories).Take(5))
            {
                // can't resolve .lnk without COM easily — skip
                _ = lnk;
            }
        }
    }

    private static string? Which(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim('"'), name);
            if (File.Exists(full)) return full;
            foreach (var ext in exts)
            {
                var with = full.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? full : full + ext;
                if (File.Exists(with)) return with;
            }
        }

        return null;
    }
}
