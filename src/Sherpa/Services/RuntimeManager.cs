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
            return prefs.PreferredPhpPath;

        foreach (var c in CandidatePhp())
            if (File.Exists(c)) return c;

        return Which("php") ?? Which("php.exe");
    }

    public string? FindComposer()
    {
        var prefs = _prefs.Load();
        if (!string.IsNullOrWhiteSpace(prefs.PreferredComposerPath) && File.Exists(prefs.PreferredComposerPath))
            return prefs.PreferredComposerPath;

        foreach (var c in CandidateComposer())
            if (File.Exists(c)) return c;

        return Which("composer") ?? Which("composer.bat") ?? Which("composer.phar");
    }

    public string? FindGit()
    {
        var prefs = _prefs.Load();
        if (!string.IsNullOrWhiteSpace(prefs.PreferredGitPath) && File.Exists(prefs.PreferredGitPath))
            return prefs.PreferredGitPath;

        return Which("git") ?? Which("git.exe");
    }

    public string? FindNpm() => Which("npm") ?? Which("npm.cmd");

    public string StatusSummary()
    {
        string Mark(string? p) => p is null ? "not found" : p;
        return $"PHP: {Mark(FindPhp())}\nComposer: {Mark(FindComposer())}\nGit: {Mark(FindGit())}\nnpm: {Mark(FindNpm())}";
    }

    private static IEnumerable<string> CandidatePhp()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(home, "AppData", "Local", "Programs", "Herd", "resources", "app.asar.unpacked", "resources", "bin", "php.bat");
        // Herd Windows often puts shims here:
        yield return Path.Combine(home, ".config", "herd", "bin", "php.bat");
        yield return Path.Combine(local, "Herd", "bin", "php.bat");
        yield return Path.Combine(local, "Programs", "Herd", "bin", "php.exe");
        yield return @"C:\laragon\bin\php\php-8.3.0-Win32-vs16-x64\php.exe";
        // Generic laragon glob-ish: scan one level
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
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
