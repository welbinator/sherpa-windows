namespace Sherpa.Services;

/// <summary>
/// Locates PHP, Composer, Git, Node — prefer Herd/Laragon/system PATH.
/// </summary>
public sealed class RuntimeManager
{
    private readonly PreferencesStore _prefs;

    public RuntimeManager(PreferencesStore prefs) => _prefs = prefs;

    public string? FindPhp()
    {
        var prefs = _prefs.Load();
        if (!string.IsNullOrWhiteSpace(prefs.PreferredPhpPath) && File.Exists(prefs.PreferredPhpPath))
            return PreferRunnableWindows(prefs.PreferredPhpPath);

        foreach (var c in CandidatePhp())
            if (File.Exists(c)) return PreferRunnableWindows(c);

        return PreferRunnableWindows(Which("php.exe") ?? Which("php"));
    }

    public string? FindComposer()
    {
        var prefs = _prefs.Load();
        if (!string.IsNullOrWhiteSpace(prefs.PreferredComposerPath) && File.Exists(prefs.PreferredComposerPath))
            return PreferRunnableWindows(prefs.PreferredComposerPath);

        foreach (var c in CandidateComposer())
            if (File.Exists(c)) return PreferRunnableWindows(c);

        return PreferRunnableWindows(
            Which("composer.bat") ?? Which("composer.cmd") ?? Which("composer") ?? Which("composer.phar"));
    }

    public string? FindGit()
    {
        var prefs = _prefs.Load();
        if (!string.IsNullOrWhiteSpace(prefs.PreferredGitPath) && File.Exists(prefs.PreferredGitPath))
            return prefs.PreferredGitPath;

        return Which("git") ?? Which("git.exe");
    }

    /// <summary>
    /// Windows Node installs ship a non-PE <c>npm</c> shim next to <c>npm.cmd</c>.
    /// Prefer .cmd so Process.Start (UseShellExecute=false) can launch it.
    /// </summary>
    public string? FindNpm() =>
        OperatingSystem.IsWindows()
            ? Which("npm.cmd") ?? Which("npm")
            : Which("npm");

    public string? FindNpx() =>
        OperatingSystem.IsWindows()
            ? Which("npx.cmd") ?? Which("npx")
            : Which("npx");

    public string? FindHerd()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var c in new[]
                 {
                     Path.Combine(local, "Programs", "Herd", "bin", "herd.bat"),
                     Path.Combine(home, ".config", "herd", "bin", "herd.bat"),
                 })
            if (File.Exists(c)) return c;
        return OperatingSystem.IsWindows()
            ? Which("herd.bat") ?? Which("herd.cmd") ?? Which("herd")
            : Which("herd");
    }

    public string StatusSummary()
    {
        string Mark(string? p) => p is null ? "not found" : p;
        return $"PHP: {Mark(FindPhp())}\nComposer: {Mark(FindComposer())}\nGit: {Mark(FindGit())}\nnpm: {Mark(FindNpm())}\nHerd: {Mark(FindHerd())}";
    }

    /// <summary>Best-effort short version string from `php -v` (e.g. "8.3.12").</summary>
    public string? TryGetPhpVersion()
    {
        var php = FindPhp();
        if (php is null) return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = php,
                ArgumentList = { "-r", "echo PHP_VERSION;" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            var v = output.Trim();
            return string.IsNullOrWhiteSpace(v) ? null : v.Split('-')[0].Trim();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> CandidatePhp()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Prefer real php.exe over .bat shims (Process.Start + redirected IO hates batch files).
        yield return Path.Combine(local, "Programs", "Herd", "bin", "php.exe");
        yield return Path.Combine(local, "Programs", "Herd", "resources", "app.asar.unpacked", "resources", "bin", "php.exe");
        yield return Path.Combine(home, "AppData", "Local", "Programs", "Herd", "resources", "app.asar.unpacked", "resources", "bin", "php.exe");
        yield return Path.Combine(home, ".config", "herd", "bin", "php.exe");
        yield return Path.Combine(local, "Herd", "bin", "php.exe");

        // Herd versioned PHP homes
        var herdPhpRoot = Path.Combine(home, ".config", "herd", "bin", "php");
        if (Directory.Exists(herdPhpRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(herdPhpRoot).OrderByDescending(d => d))
            {
                var exe = Path.Combine(dir, "php.exe");
                if (File.Exists(exe)) yield return exe;
            }
        }

        // Fall back to bat shims (ProcessRunner unwraps when possible).
        yield return Path.Combine(home, ".config", "herd", "bin", "php.bat");
        yield return Path.Combine(local, "Herd", "bin", "php.bat");
        yield return Path.Combine(home, "AppData", "Local", "Programs", "Herd", "resources", "app.asar.unpacked", "resources", "bin", "php.bat");

        yield return @"C:\laragon\bin\php\php-8.3.0-Win32-vs16-x64\php.exe";
        var laragonPhp = @"C:\laragon\bin\php";
        if (Directory.Exists(laragonPhp))
        {
            foreach (var dir in Directory.EnumerateDirectories(laragonPhp).OrderByDescending(d => d))
            {
                var exe = Path.Combine(dir, "php.exe");
                if (File.Exists(exe)) yield return exe;
            }
        }
    }

    private static IEnumerable<string> CandidateComposer()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Herd", "bin", "composer.bat");
        yield return Path.Combine(home, ".config", "herd", "bin", "composer.bat");
        yield return @"C:\ProgramData\ComposerSetup\bin\composer.bat";
        yield return @"C:\laragon\bin\composer\composer.bat";
    }

    private static string? Which(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nameHasExt = name.Contains('.', StringComparison.Ordinal);
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim('"'), name);

            // On Windows, prefer PATHEXT matches (.cmd/.exe) before extensionless files.
            // Node ships a Unix shell script named "npm"/"npx" that is not a PE binary —
            // Process.Start then fails with "not a valid application for this OS platform".
            if (OperatingSystem.IsWindows() && !nameHasExt)
            {
                foreach (var ext in exts)
                {
                    var with = full + ext;
                    if (File.Exists(with)) return with;
                }

                // Only accept extensionless if it looks like a real Windows PE (MZ header).
                if (File.Exists(full) && LooksLikeWindowsPe(full)) return full;
                continue;
            }

            if (File.Exists(full)) return full;
            if (!nameHasExt)
            {
                foreach (var ext in exts)
                {
                    var with = full + ext;
                    if (File.Exists(with)) return with;
                }
            }
        }
        return null;
    }

    private static bool LooksLikeWindowsPe(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// If we were handed a .bat/.cmd, prefer a real PE resolved beside it or via ProcessRunner unwrap.
    /// </summary>
    private static string? PreferRunnableWindows(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (!OperatingSystem.IsWindows()) return path;
        if (!path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            return path;

        if (ProcessRunner.TryResolveWindowsBatchTarget(path, Array.Empty<string>(), out var exe, out _))
            return exe;

        return path;
    }
}
