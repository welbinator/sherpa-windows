using System.Text.Json;
using System.Text.RegularExpressions;
using Sherpa.Clients;
using Sherpa.Models;
using Sherpa.Support;

namespace Sherpa.Services;

public sealed class StaticPublishResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? ProductionUrl { get; init; }
    public string? ProjectName { get; init; }
    public string? StaticOutputPath { get; init; }
}

/// <summary>
/// Static site publish for Cloudflare Pages — Mac Sherpa energy:
/// build assets → php please ssg:generate → wrangler pages deploy.
/// </summary>
public sealed class StaticPublishService
{
    private readonly ProcessRunner _runner;
    private readonly RuntimeManager _runtime;
    private readonly CloudflarePagesClient _cloudflare;
    private readonly SecretStore _secrets;

    public StaticPublishService(
        ProcessRunner runner,
        RuntimeManager runtime,
        CloudflarePagesClient cloudflare,
        SecretStore secrets)
    {
        _runner = runner;
        _runtime = runtime;
        _cloudflare = cloudflare;
        _secrets = secrets;
    }

    public static string DefaultStaticOutput(string sitePath) =>
        Path.Combine(sitePath, "storage", "app", "static");

    public bool StaticOutputLooksReady(string sitePath)
    {
        var dir = ResolveStaticOutputDir(sitePath);
        if (dir is null || !Directory.Exists(dir)) return false;
        return Directory.EnumerateFileSystemEntries(dir).Any();
    }

    public string? ResolveStaticOutputDir(string sitePath)
    {
        // Statamic SSG default + common overrides
        foreach (var rel in new[]
                 {
                     Path.Combine("storage", "app", "static"),
                     "static",
                     Path.Combine("public", "static"),
                 })
        {
            var full = Path.Combine(sitePath, rel);
            if (Directory.Exists(full)) return full;
        }

        // config/statamic/ssg.php publish path
        try
        {
            var cfg = Path.Combine(sitePath, "config", "statamic", "ssg.php");
            if (File.Exists(cfg))
            {
                var text = File.ReadAllText(cfg);
                var m = Regex.Match(text, @"['""]destination['""]\s*=>\s*['""]([^'""]+)['""]");
                if (m.Success)
                {
                    var dest = m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
                    if (!Path.IsPathRooted(dest))
                        dest = Path.GetFullPath(Path.Combine(sitePath, dest));
                    if (Directory.Exists(dest)) return dest;
                }
            }
        }
        catch
        {
            // ignore
        }

        return DefaultStaticOutput(sitePath);
    }

    public async Task<StaticPublishResult> PublishToCloudflareAsync(
        Site site,
        HostAccount host,
        string projectName,
        bool regenerate,
        Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        if (host.Provider != HostProviderKind.CloudflarePages)
            return Fail("That host does not support static publish. Connect Cloudflare Pages under Hosts.");

        var token = _secrets.Get(host.SecretKey);
        if (string.IsNullOrWhiteSpace(token))
            return Fail("Cloudflare API token missing. Re-connect Cloudflare Pages under Hosts.");

        var accountId = host.Extra?.Trim();
        if (string.IsNullOrWhiteSpace(accountId))
            return Fail("Cloudflare account ID missing. Re-connect Cloudflare Pages and paste the account ID.");

        if (string.IsNullOrWhiteSpace(site.Path) || !Directory.Exists(site.Path))
            return Fail("Site folder is missing on disk.");

        var php = _runtime.FindPhp();
        if (php is null)
            return Fail("PHP not found. Install Laravel Herd (or set PHP under Settings) before generating static files.");

        var npx = _runtime.FindNpx() ?? FindNpx();
        if (npx is null)
            return Fail("Node/npx is required to publish to Cloudflare Pages. Install Node.js from nodejs.org (or enable Node in Herd), then try again.");

        var name = CloudflarePagesClient.SanitizeProjectName(
            string.IsNullOrWhiteSpace(projectName) ? site.Name : projectName);
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Could not build a valid Cloudflare project name from the site name.");

        onLine?.Invoke($"Cloudflare project: {name}");
        onLine?.Invoke("Account: " + accountId);

        // 1) Ensure Pages project exists
        onLine?.Invoke("Checking Cloudflare Pages project…");
        var (projOk, projMsg, project) = await _cloudflare
            .EnsureProjectAsync(token, accountId, name, ct).ConfigureAwait(false);
        onLine?.Invoke(projMsg);
        if (!projOk || project is null)
            return Fail(projMsg);

        // 2) Generate static files if needed
        var staticDir = ResolveStaticOutputDir(site.Path) ?? DefaultStaticOutput(site.Path);
        var needBuild = regenerate || !Directory.Exists(staticDir)
                        || !Directory.EnumerateFileSystemEntries(staticDir).Any();

        if (needBuild)
        {
            onLine?.Invoke(regenerate
                ? "Regenerating static site…"
                : "Static output missing — generating…");

            var gen = await GenerateStaticAsync(site.Path, php, onLine, ct).ConfigureAwait(false);
            if (!gen.ok)
                return Fail(gen.message);

            staticDir = ResolveStaticOutputDir(site.Path) ?? staticDir;
        }
        else
        {
            onLine?.Invoke("Using existing static output (toggle “Regenerate” to rebuild).");
        }

        if (!Directory.Exists(staticDir) || !Directory.EnumerateFileSystemEntries(staticDir).Any())
            return Fail("Static output folder is empty or missing: " + staticDir);

        onLine?.Invoke("Static output: " + staticDir);

        // 3) Optional wrangler.toml for Git-based deploys later
        try
        {
            WriteWranglerToml(site.Path, name, accountId);
            onLine?.Invoke("Wrote wrangler.toml for optional Git-based deploys.");
        }
        catch (Exception ex)
        {
            onLine?.Invoke("Could not write wrangler.toml (non-fatal): " + ex.Message);
        }

        // 4) Upload via Wrangler
        onLine?.Invoke("Uploading via Wrangler (production branch: main)…");
        var env = new Dictionary<string, string?>
        {
            ["CLOUDFLARE_API_TOKEN"] = token,
            ["CLOUDFLARE_ACCOUNT_ID"] = accountId,
            // Avoid interactive wrangler prompts
            ["CI"] = "1",
            ["WRANGLER_SEND_METRICS"] = "false",
        };

        // npx --yes wrangler@3 pages deploy <dir> --project-name=X --branch=main --commit-dirty=true
        var args = new List<string>
        {
            "--yes",
            "wrangler@3",
            "pages",
            "deploy",
            staticDir,
            "--project-name",
            project.Name,
            "--branch",
            "main",
            "--commit-dirty=true",
        };

        var deploy = await _runner.RunAsync(npx, args, site.Path, env, onLine, ct).ConfigureAwait(false);
        if (!deploy.Success)
        {
            var detail = string.IsNullOrWhiteSpace(deploy.StdErr) ? deploy.StdOut : deploy.StdErr;
            if (detail.Length > 500) detail = detail[^500..];
            return Fail("Wrangler failed (" + deploy.ExitCode + "). " + detail.Trim());
        }

        var url = ExtractUrl(deploy.StdOut + "\n" + deploy.StdErr)
                  ?? project.ProductionUrl;

        onLine?.Invoke("Published to " + url);
        return new StaticPublishResult
        {
            Ok = true,
            Message = "Published: " + url,
            ProductionUrl = url,
            ProjectName = project.Name,
            StaticOutputPath = staticDir,
        };
    }

    private async Task<(bool ok, string message)> GenerateStaticAsync(
        string sitePath, string php, Action<string>? onLine, CancellationToken ct)
    {
        // Frontend assets first when package.json exists (Vite / Mix)
        var packageJson = Path.Combine(sitePath, "package.json");
        if (File.Exists(packageJson))
        {
            var npm = _runtime.FindNpm();
            if (npm is null)
            {
                onLine?.Invoke("npm not found — skipping JS build. Install Node if the site needs compiled assets.");
            }
            else
            {
                var hasLock = File.Exists(Path.Combine(sitePath, "package-lock.json"))
                              || File.Exists(Path.Combine(sitePath, "npm-shrinkwrap.json"));
                if (hasLock || Directory.Exists(Path.Combine(sitePath, "node_modules")) is false)
                {
                    onLine?.Invoke(hasLock ? "npm ci…" : "npm install…");
                    var installArgs = hasLock
                        ? new[] { "ci", "--no-fund", "--no-audit" }
                        : new[] { "install", "--no-fund", "--no-audit" };
                    var install = await _runner.RunAsync(npm, installArgs, sitePath, null, onLine, ct)
                        .ConfigureAwait(false);
                    if (!install.Success)
                        onLine?.Invoke("npm install had issues — continuing to SSG anyway.");
                }

                if (PackageJsonHasScript(packageJson, "build"))
                {
                    onLine?.Invoke("npm run build…");
                    var build = await _runner.RunAsync(npm, new[] { "run", "build" }, sitePath, null, onLine, ct)
                        .ConfigureAwait(false);
                    if (!build.Success)
                        onLine?.Invoke("npm run build had issues — continuing to SSG anyway.");
                }
            }
        }

        // Ensure SSG package if missing
        if (!LooksLikeSsgInstalled(sitePath))
        {
            onLine?.Invoke("statamic/ssg not detected — installing…");
            var composer = _runtime.FindComposer();
            if (composer is not null)
            {
                var isPhar = composer.EndsWith(".phar", StringComparison.OrdinalIgnoreCase);
                var cArgs = isPhar
                    ? new[] { composer, "require", "statamic/ssg", "--no-interaction" }
                    : new[] { "require", "statamic/ssg", "--no-interaction" };
                var cFile = isPhar ? php : composer;
                var req = await _runner.RunAsync(cFile, cArgs, sitePath, null, onLine, ct).ConfigureAwait(false);
                if (!req.Success)
                    onLine?.Invoke("composer require statamic/ssg failed — trying please install:ssg…");
            }

            var installSsg = await _runner.RunAsync(php,
                new[] { "please", "install:ssg", "--no-interaction" },
                sitePath, null, onLine, ct).ConfigureAwait(false);
            if (!installSsg.Success)
                onLine?.Invoke("SSG install had issues — will still try ssg:generate.");
        }

        onLine?.Invoke("php please ssg:generate…");
        var gen = await _runner.RunAsync(php,
            new[] { "please", "ssg:generate" },
            sitePath, null, onLine, ct).ConfigureAwait(false);

        if (!gen.Success)
        {
            var detail = (gen.StdErr + "\n" + gen.StdOut).Trim();
            if (detail.Length > 400) detail = detail[^400..];
            return (false, "ssg:generate failed. " + detail);
        }

        var outDir = ResolveStaticOutputDir(sitePath);
        if (outDir is null || !Directory.Exists(outDir) || !Directory.EnumerateFileSystemEntries(outDir).Any())
            return (false, "ssg:generate finished but static output is empty. Expected something under storage/app/static.");

        return (true, "Static site generated.");
    }

    private static bool LooksLikeSsgInstalled(string sitePath)
    {
        var vendor = Path.Combine(sitePath, "vendor", "statamic", "ssg");
        if (Directory.Exists(vendor)) return true;
        var lockPath = Path.Combine(sitePath, "composer.lock");
        if (!File.Exists(lockPath)) return false;
        try
        {
            return File.ReadAllText(lockPath).Contains("statamic/ssg", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PackageJsonHasScript(string packageJsonPath, string script)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            return doc.RootElement.TryGetProperty("scripts", out var scripts)
                   && scripts.TryGetProperty(script, out _);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteWranglerToml(string sitePath, string projectName, string accountId)
    {
        var path = Path.Combine(sitePath, "wrangler.toml");
        var body =
            $"""
             # Written by Sherpa for optional Cloudflare Pages / Wrangler deploys.
             name = "{projectName}"
             compatibility_date = "2024-01-01"
             pages_build_output_dir = "storage/app/static"

             [vars]
             # Account is also passed via CLOUDFLARE_ACCOUNT_ID when publishing from Sherpa.
             """;
        // Don't clobber a custom wrangler the user already tuned heavily — only write if missing or Sherpa-marked
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (!existing.Contains("Written by Sherpa", StringComparison.Ordinal))
                return;
        }

        File.WriteAllText(path, body);
        _ = accountId; // reserved for future account_id field if wrangler schema wants it
    }

    /// <summary>Fallback locator when <see cref="RuntimeManager.FindNpx"/> is null.</summary>
    private string? FindNpx()
    {
        // Windows: never pick the extensionless Node shim (not a PE binary).
        if (OperatingSystem.IsWindows())
        {
            var cmd = Which("npx.cmd");
            if (cmd is not null) return cmd;
            if (_runtime.FindNpm() is { } npm)
            {
                var sibling = Path.Combine(Path.GetDirectoryName(npm) ?? "", "npx.cmd");
                if (File.Exists(sibling)) return sibling;
            }
            return null;
        }

        return Which("npx")
               ?? (_runtime.FindNpm() is { } npmUnix
                   ? Path.Combine(Path.GetDirectoryName(npmUnix) ?? "", "npx")
                   : null);
    }

    private static string? Which(string name)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (OperatingSystem.IsWindows()
                    && !name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(candidate + ".cmd")) return candidate + ".cmd";
                    if (File.Exists(candidate + ".exe")) return candidate + ".exe";
                    if (File.Exists(candidate + ".bat")) return candidate + ".bat";
                    // Skip extensionless non-PE shims on Windows.
                    continue;
                }

                if (File.Exists(candidate)) return candidate;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? ExtractUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // https://xxx.pages.dev or deployment-specific *.pages.dev
        var m = Regex.Match(text, @"https://[a-zA-Z0-9.-]+\.pages\.dev[^\s]*");
        if (m.Success)
        {
            var url = m.Value.TrimEnd('.', ',', ')', ']');
            return url;
        }

        return null;
    }

    private static StaticPublishResult Fail(string message) => new()
    {
        Ok = false,
        Message = message,
    };
}
