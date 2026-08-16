using Sherpa.Models;
using Sherpa.Support;

namespace Sherpa.Services;

/// <summary>
/// Multi-step new site flow — mirrors Mac InstallCoordinator / official `statamic new` pipeline.
/// </summary>
public sealed class InstallCoordinator
{
    private readonly ProcessRunner _runner;
    private readonly RuntimeManager _runtime;
    private readonly HerdService _herd;
    private readonly GitService _git;
    private readonly SiteCommandsService _commands;

    public InstallCoordinator(
        ProcessRunner runner,
        RuntimeManager runtime,
        HerdService herd,
        GitService git,
        SiteCommandsService commands)
    {
        _runner = runner;
        _runtime = runtime;
        _herd = herd;
        _git = git;
        _commands = commands;
    }

    public enum ContentStorageKind
    {
        FlatFiles,
        Sqlite,
        MySql,
    }

    public sealed class CreateRequest
    {
        public string ParentFolder { get; init; } = "";
        public string SiteName { get; init; } = "";
        /// <summary>Null/empty = blank Statamic (no starter kit).</summary>
        public string? StarterKitPackage { get; init; }
        public bool StarterKitIsPaid { get; init; }
        public ContentStorageKind Storage { get; init; } = ContentStorageKind.FlatFiles;
        public string? MySqlHost { get; init; }
        public string? MySqlDatabase { get; init; }
        public string? MySqlUser { get; init; }
        public string? MySqlPassword { get; init; }
        public bool EnablePro { get; init; }
        public bool InstallSsg { get; init; }
        public bool InitGit { get; init; }
        public bool CreateSuperUser { get; init; }
        public string? SuperUserName { get; init; }
        public string? SuperUserEmail { get; init; }
        public string? SuperUserPassword { get; init; }
        public bool ParkInHerd { get; init; } = true;
        public bool SecureHttps { get; init; } = true;
    }

    public async Task<(Site? site, ProcessResult? result, string? error)> CreateAsync(
        CreateRequest req,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.SiteName))
            return (null, null, "Site name is required.");

        if (req.StarterKitIsPaid && !string.IsNullOrWhiteSpace(req.StarterKitPackage))
            return (null, null, "This is a paid starter kit. License checkout isn’t wired up in Sherpa for Windows yet — pick Blank site or a Free kit for now.");

        if (req.CreateSuperUser)
        {
            if (string.IsNullOrWhiteSpace(req.SuperUserEmail) || string.IsNullOrWhiteSpace(req.SuperUserPassword))
                return (null, null, "Super user needs an email and password.");
        }

        var slug = HerdService.Slug(req.SiteName);
        var parent = string.IsNullOrWhiteSpace(req.ParentFolder)
            ? _herd.DefaultSitesDirectory()
            : req.ParentFolder;
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, slug);

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            return (null, null, $"That folder already has files: {path}");

        var php = _runtime.FindPhp();
        var composer = _runtime.FindComposer();
        if (php is null || composer is null)
            return (null, null, "PHP and Composer are required. Install Laravel Herd or set paths under Settings.");

        // 1) Base Statamic project
        onLine?.Invoke("Creating Statamic project…");
        ProcessResult result;
        if (composer.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
        {
            result = await _runner.RunAsync(php,
                new[] { composer, "create-project", "statamic/statamic", slug, "--no-interaction" },
                parent, null, onLine, ct);
        }
        else
        {
            result = await _runner.RunAsync(composer,
                new[] { "create-project", "statamic/statamic", slug, "--no-interaction" },
                parent, null, onLine, ct);
        }

        if (!result.Success || !Directory.Exists(path))
            return (null, result, "There was a problem installing Statamic. See the log below.");

        // APP_URL like official CLI
        TrySetEnv(path, "APP_URL", req.SecureHttps ? $"https://{slug}.test" : $"http://{slug}.test");

        // 2) Starter kit
        if (!string.IsNullOrWhiteSpace(req.StarterKitPackage))
        {
            onLine?.Invoke($"Installing starter kit {req.StarterKitPackage}…");
            var kit = await Please(path, php, new[]
            {
                "starter-kit:install", req.StarterKitPackage!,
                "--cli-install", "--clear-site", "--no-interaction",
            }, onLine, ct);
            if (!kit.Success)
                return (null, kit, "There was a problem installing the starter kit. See the log below.");
            result = kit;
        }
        else
        {
            onLine?.Invoke("Blank site — fresh Statamic, no starter kit.");
        }

        // 3) Content storage
        if (req.Storage == ContentStorageKind.Sqlite)
        {
            onLine?.Invoke("Configuring SQLite database…");
            var dbDir = Path.Combine(path, "database");
            Directory.CreateDirectory(dbDir);
            var sqlite = Path.Combine(dbDir, "database.sqlite");
            if (!File.Exists(sqlite)) File.WriteAllBytes(sqlite, Array.Empty<byte>());
            TrySetEnv(path, "DB_CONNECTION", "sqlite");
            // Clear mysql-ish vars if present
            TrySetEnv(path, "DB_DATABASE", sqlite.Replace('\\', '/'));
            onLine?.Invoke("Running install:eloquent-driver…");
            var eloq = await Please(path, php, new[] { "install:eloquent-driver", "--no-interaction" }, onLine, ct);
            if (!eloq.Success)
                onLine?.Invoke("Eloquent driver install reported issues — you can finish with: php please install:eloquent-driver");
            result = eloq.Success ? eloq : result;
        }
        else if (req.Storage == ContentStorageKind.MySql)
        {
            onLine?.Invoke("Configuring MySQL…");
            TrySetEnv(path, "DB_CONNECTION", "mysql");
            TrySetEnv(path, "DB_HOST", string.IsNullOrWhiteSpace(req.MySqlHost) ? "127.0.0.1" : req.MySqlHost!);
            TrySetEnv(path, "DB_DATABASE", req.MySqlDatabase ?? slug);
            TrySetEnv(path, "DB_USERNAME", req.MySqlUser ?? "root");
            TrySetEnv(path, "DB_PASSWORD", req.MySqlPassword ?? "");
            onLine?.Invoke("Running install:eloquent-driver…");
            var eloq = await Please(path, php, new[] { "install:eloquent-driver", "--no-interaction" }, onLine, ct);
            if (!eloq.Success)
                onLine?.Invoke("Eloquent driver install reported issues — check MySQL credentials and run: php please install:eloquent-driver");
            result = eloq.Success ? eloq : result;
        }
        else
        {
            onLine?.Invoke("Content storage: Flat Files (typical).");
        }

        // 4) Pro — disabled in UI for now; keep hook
        if (req.EnablePro)
        {
            onLine?.Invoke("Enabling Statamic Pro…");
            var pro = await Please(path, php, new[] { "pro:enable", "--no-interaction" }, onLine, ct);
            if (!pro.Success)
                onLine?.Invoke("Could not enable Pro automatically. You can run: php please pro:enable");
        }

        // 5) SSG
        if (req.InstallSsg)
        {
            onLine?.Invoke("Installing Static Site Generator…");
            var ssg = await Please(path, php, new[] { "install:ssg", "--no-interaction" }, onLine, ct);
            if (!ssg.Success)
            {
                // Fallback package require
                onLine?.Invoke("please install:ssg failed — trying composer require statamic/ssg…");
                var composerArgs = composer.EndsWith(".phar", StringComparison.OrdinalIgnoreCase)
                    ? new[] { composer, "require", "statamic/ssg", "--no-interaction" }
                    : new[] { "require", "statamic/ssg", "--no-interaction" };
                var file = composer.EndsWith(".phar", StringComparison.OrdinalIgnoreCase) ? php : composer;
                ssg = await _runner.RunAsync(file, composerArgs, path, null, onLine, ct);
            }
            if (!ssg.Success)
                onLine?.Invoke("SSG install had issues — you can run: php please install:ssg");
            else
                result = ssg;
        }

        // 6) Super user
        if (req.CreateSuperUser)
        {
            onLine?.Invoke("Creating super user…");
            var user = await _commands.MakeUserAsync(
                path,
                req.SuperUserEmail!.Trim(),
                req.SuperUserPassword!,
                super: true,
                onLine,
                ct);
            if (!user.Success)
                onLine?.Invoke("User create had issues — you can run Create User from Overview later.");
            else
                result = user;
        }

        // 7) Git
        if (req.InitGit)
        {
            onLine?.Invoke("Initialize Git…");
            var git = await _git.InitAsync(path, ct);
            onLine?.Invoke(git.Combined);
            if (git.Success)
            {
                // optional initial commit if identity available later — skip if no identity
            }
        }

        // 8) Herd
        var site = SiteDetector.FromPath(path);
        site.Name = req.SiteName.Trim();
        site.StartingPoint = string.IsNullOrWhiteSpace(req.StarterKitPackage) ? "blank" : "kit:" + req.StarterKitPackage;
        site.Url = _herd.UrlPreview(req.SiteName, req.SecureHttps);
        site.Https = req.SecureHttps;

        if (req.ParkInHerd)
        {
            var (ok, msg) = await _herd.ParkAsync(path, req.SiteName, onLine, ct);
            onLine?.Invoke(msg);
            site.ParkedInHerd = ok;
            if (ok && req.SecureHttps)
            {
                var (sok, smsg) = await _herd.SecureAsync(req.SiteName, true, onLine, ct);
                onLine?.Invoke(smsg);
                site.Https = sok;
                if (sok) site.Url = _herd.UrlPreview(req.SiteName, true);
            }
        }

        onLine?.Invoke("Created project");
        return (site, result, null);
    }

    private async Task<ProcessResult> Please(
        string sitePath,
        string php,
        IEnumerable<string> args,
        Action<string>? onLine,
        CancellationToken ct)
    {
        var please = Path.Combine(sitePath, "please");
        if (!File.Exists(please))
            return new ProcessResult { ExitCode = 1, StdErr = "No please CLI in site." };
        return await _runner.RunAsync(php, new[] { please }.Concat(args), sitePath, null, onLine, ct);
    }

    private static void TrySetEnv(string sitePath, string key, string value)
    {
        var envPath = Path.Combine(sitePath, ".env");
        if (!File.Exists(envPath)) return;
        var lines = File.ReadAllLines(envPath).ToList();
        var prefix = key + "=";
        var found = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal) ||
                lines[i].StartsWith("# " + prefix, StringComparison.Ordinal))
            {
                lines[i] = prefix + value;
                found = true;
                break;
            }
        }
        if (!found) lines.Add(prefix + value);
        File.WriteAllLines(envPath, lines);
    }
}
