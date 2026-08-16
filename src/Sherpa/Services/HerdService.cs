using Sherpa.Support;

namespace Sherpa.Services;

/// <summary>
/// Windows port of Mac HerdService — park/link sites and secure HTTPS via Herd CLI.
/// </summary>
public sealed class HerdService
{
    private readonly ProcessRunner _runner;

    public HerdService(ProcessRunner runner) => _runner = runner;

    public string? FindHerdCli()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var c in new[]
                 {
                     Path.Combine(local, "Programs", "Herd", "resources", "app.asar.unpacked", "resources", "bin", "herd.bat"),
                     Path.Combine(local, "Programs", "Herd", "bin", "herd.bat"),
                     Path.Combine(home, ".config", "herd", "bin", "herd.bat"),
                     Path.Combine(home, "AppData", "Roaming", "Herd", "bin", "herd.bat"),
                 })
        {
            if (File.Exists(c)) return c;
        }

        return Which("herd") ?? Which("herd.bat");
    }

    public bool IsAvailable => FindHerdCli() is not null;

    public string DefaultSitesDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // User reported Windows Herd chose: profile\Herd
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
