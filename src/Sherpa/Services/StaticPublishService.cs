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

        // Public URL for this deploy. Prefer root-relative asset URLs in HTML so a
        // wrong APP_URL can never break CSS on other devices.
        var publicBaseUrl = (project.ProductionUrl ?? $"https://{CloudflarePagesProject.NormalizePagesHost(null, project.Name)}").TrimEnd('/');
        // Guard against any legacy double-suffix still floating around
        publicBaseUrl = publicBaseUrl.Replace(".pages.dev.pages.dev", ".pages.dev", StringComparison.OrdinalIgnoreCase);

        // Always rebuild for Cloudflare publish. Reusing a prior SSG that baked
        // https://site.test into @vite tags is the #1 cause of “works on my PC only”.
        var staticDir = ResolveStaticOutputDir(site.Path) ?? DefaultStaticOutput(site.Path);
        var localOrigins = CollectLocalOrigins(site);

        onLine?.Invoke("Generating static site for public URL (always rebuild on publish)…");
        var gen = await GenerateStaticAsync(site.Path, php, publicBaseUrl, onLine, ct).ConfigureAwait(false);
        if (!gen.ok)
            return Fail(gen.message);

        staticDir = ResolveStaticOutputDir(site.Path) ?? staticDir;

        if (!Directory.Exists(staticDir) || !Directory.EnumerateFileSystemEntries(staticDir).Any())
            return Fail("Static output folder is empty or missing: " + staticDir);

        // Ensure Vite build assets are inside the static folder (SSG usually copies them).
        EnsurePublicBuildCopied(site.Path, staticDir, onLine);

        // Safety net: rewrite absolute URLs → root-relative so CSS/JS always load
        // from the same host as the page (works on every device + custom domains).
        // 1) local Herd *.test  2) whatever public base we generated with  3) doubled pages.dev
        var originsToStrip = new List<string>(localOrigins)
        {
            publicBaseUrl,
            publicBaseUrl.Replace("https://", "http://", StringComparison.OrdinalIgnoreCase),
        };
        // Also strip any accidental *.pages.dev.pages.dev
        if (publicBaseUrl.Contains(".pages.dev", StringComparison.OrdinalIgnoreCase))
        {
            originsToStrip.Add(publicBaseUrl.Replace(".pages.dev", ".pages.dev.pages.dev", StringComparison.OrdinalIgnoreCase));
        }

        var rewritten = RewriteLocalAbsoluteUrls(staticDir, originsToStrip, publicBase: null);
        onLine?.Invoke(rewritten > 0
            ? $"Rewrote absolute asset URLs in {rewritten} file(s) to root-relative paths."
            : "Checked static files for absolute local/public asset URLs.");

        // Hard gate — never upload HTML that still points at Herd/local or broken hosts.
        var leftover = FindBrokenAssetUrlSamples(staticDir, max: 5);
        if (leftover.Count > 0)
        {
            return Fail(
                "Refusing to publish: static HTML still has bad asset URLs (" +
                string.Join(", ", leftover) +
                "). CSS would break off this PC. Try Publish again; if it keeps happening, tell Hermes.");
        }

        // Confirm /build exists in what we upload
        var buildDir = Path.Combine(staticDir, "build");
        if (!Directory.Exists(buildDir)
            || !Directory.EnumerateFiles(buildDir, "*.css", SearchOption.AllDirectories).Any())
        {
            onLine?.Invoke("Warning: no CSS under static/build — copying public/build again…");
            EnsurePublicBuildCopied(site.Path, staticDir, onLine, force: true);
        }

        onLine?.Invoke("Static output: " + staticDir);
        onLine?.Invoke("Public URL base: " + publicBaseUrl);
        _ = regenerate; // UI toggle kept; publish always rebuilds for correctness

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
            // Avoid interactive wrangler prompts + noisy git HEAD errors on non-git folders
            ["CI"] = "1",
            ["WRANGLER_SEND_METRICS"] = "false",
            ["GIT_DIR"] = null, // don't let wrangler poke parent git oddly
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
            "--commit-message",
            "Sherpa publish",
        };

        var deploy = await _runner.RunAsync(npx, args, site.Path, env, onLine, ct).ConfigureAwait(false);
        if (!deploy.Success)
        {
            var detail = string.IsNullOrWhiteSpace(deploy.StdErr) ? deploy.StdOut : deploy.StdErr;
            if (detail.Length > 500) detail = detail[^500..];
            return Fail("Wrangler failed (" + deploy.ExitCode + "). " + detail.Trim());
        }

        // Prefer the stable production host (my-site.pages.dev), not Wrangler's
        // per-deploy preview (abc123.my-site.pages.dev) which changes every publish.
        var deployPreview = ExtractUrl(deploy.StdOut + "\n" + deploy.StdErr);
        var url = PreferStablePagesUrl(publicBaseUrl, project.ProductionUrl, deployPreview);

        if (!string.IsNullOrWhiteSpace(deployPreview)
            && !string.Equals(deployPreview.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            onLine?.Invoke("Deploy preview: " + deployPreview.TrimEnd('/'));
        }

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
        string sitePath,
        string php,
        string publicBaseUrl,
        Action<string>? onLine,
        CancellationToken ct)
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

        // Critical: generate with the PUBLIC base URL so @vite / asset() don't bake in
        // https://site.test (local Herd). Process env only — does not edit .env on disk.
        var appUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? "http://localhost"
            : publicBaseUrl.TrimEnd('/');

        // Drop config cache so a stale APP_URL isn't baked in.
        onLine?.Invoke("php artisan config:clear…");
        await _runner.RunAsync(php, new[] { "artisan", "config:clear" }, sitePath, null, onLine, ct)
            .ConfigureAwait(false);

        // If public/hot exists, @vite points at the Vite dev server (localhost) — kill it.
        try
        {
            var hot = Path.Combine(sitePath, "public", "hot");
            if (File.Exists(hot))
            {
                File.Delete(hot);
                onLine?.Invoke("Removed public/hot (Vite dev server file) so assets use built files.");
            }
        }
        catch (Exception ex)
        {
            onLine?.Invoke("Could not remove public/hot: " + ex.Message);
        }

        onLine?.Invoke("php please ssg:generate (APP_URL=" + appUrl + ")…");
        var genEnv = new Dictionary<string, string?>
        {
            ["APP_URL"] = appUrl,
            // Same origin for compiled assets; root-relative rewrite still runs after.
            ["ASSET_URL"] = appUrl,
            // Prevent Vite dev server / hot file from leaking into the static build.
            ["VITE_DEV_SERVER_URL"] = null,
        };
        var gen = await _runner.RunAsync(php,
            new[] { "please", "ssg:generate", "--no-interaction" },
            sitePath, genEnv, onLine, ct).ConfigureAwait(false);

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

    /// <summary>
    /// Local origins that must never appear in a public static deploy (Herd *.test, APP_URL, site URL).
    /// </summary>
    internal static List<string> CollectLocalOrigins(Site site)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            raw = raw.Trim().TrimEnd('/');
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                set.Add(raw);
                // also the other scheme
                if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    set.Add("http://" + raw["https://".Length..]);
                else
                    set.Add("https://" + raw["http://".Length..]);
            }
        }

        Add(site.Url);
        try
        {
            var envPath = Path.Combine(site.Path, ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadLines(envPath))
                {
                    var t = line.Trim();
                    if (t.StartsWith("APP_URL=", StringComparison.OrdinalIgnoreCase)
                        || t.StartsWith("ASSET_URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = t[(t.IndexOf('=') + 1)..].Trim().Trim('"').Trim('\'');
                        Add(v);
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        // Herd convention from site folder / name
        var slug = slugify(site.Name);
        if (!string.IsNullOrWhiteSpace(slug))
        {
            Add($"http://{slug}.test");
            Add($"https://{slug}.test");
        }

        try
        {
            var folder = Path.GetFileName(site.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Add($"http://{folder}.test");
                Add($"https://{folder}.test");
            }
        }
        catch
        {
            // ignore
        }

        return set.ToList();

        static string slugify(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"[^a-z0-9-]+", "-");
            return Regex.Replace(s, @"-+", "-").Trim('-');
        }
    }

    internal static bool StaticHtmlReferencesLocalOrigins(string staticDir, IReadOnlyList<string> localOrigins)
    {
        if (localOrigins.Count == 0 || !Directory.Exists(staticDir)) return false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(staticDir, "*.html", SearchOption.AllDirectories))
            {
                // Only need a quick peek
                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }

                foreach (var origin in localOrigins)
                {
                    if (text.Contains(origin, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Catch any *.test host even if not in our list
                if (Regex.IsMatch(text, @"https?://[a-z0-9.-]+\.test\b", RegexOptions.IgnoreCase))
                    return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    /// <summary>
    /// Replace absolute local origins with root-relative paths (empty publicBase)
    /// or with the public base URL. Returns number of files changed.
    /// </summary>
    internal static int RewriteLocalAbsoluteUrls(
        string staticDir,
        IReadOnlyList<string> localOrigins,
        string? publicBase)
    {
        if (!Directory.Exists(staticDir)) return 0;

        var origins = new List<string>(localOrigins);
        // Always strip any http(s)://*.test that slipped through
        // (handled per-file via regex below as well)

        var replacement = string.IsNullOrWhiteSpace(publicBase) ? "" : publicBase.TrimEnd('/');
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".htm", ".css", ".js", ".json", ".xml", ".txt", ".svg", ".webmanifest",
        };

        var changed = 0;
        foreach (var file in Directory.EnumerateFiles(staticDir, "*", SearchOption.AllDirectories))
        {
            if (!exts.Contains(Path.GetExtension(file))) continue;

            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }

            var original = text;
            foreach (var origin in origins.OrderByDescending(o => o.Length))
            {
                if (string.IsNullOrWhiteSpace(origin)) continue;
                text = text.Replace(origin, replacement, StringComparison.OrdinalIgnoreCase);
            }

            // Any remaining https://something.test → root-relative
            text = Regex.Replace(
                text,
                @"https?://[a-z0-9.-]+\.test(?=[:/""'\s?]|$)",
                replacement,
                RegexOptions.IgnoreCase);

            // Also strip accidental Vite dev-server hosts
            text = Regex.Replace(
                text,
                @"https?://(?:localhost|127\.0\.0\.1)(?::\d+)?(?=[:/""'\s?]|$)",
                replacement,
                RegexOptions.IgnoreCase);

            // Collapse doubled pages.dev hosts → root-relative (drop the whole origin)
            text = Regex.Replace(
                text,
                @"https?://[a-z0-9.-]+\.pages\.dev\.pages\.dev",
                replacement,
                RegexOptions.IgnoreCase);

            // Any absolute https://*.pages.dev origin → root-relative so custom domains work too
            // Only strip when used as asset origin (followed by /build or /css etc. is fine for all paths)
            if (replacement == "")
            {
                text = Regex.Replace(
                    text,
                    @"https?://[a-z0-9.-]+\.pages\.dev(?=/)",
                    "",
                    RegexOptions.IgnoreCase);
            }

            if (!string.Equals(original, text, StringComparison.Ordinal))
            {
                try
                {
                    File.WriteAllText(file, text);
                    changed++;
                }
                catch
                {
                    // ignore locked files
                }
            }
        }

        return changed;
    }

    private static void EnsurePublicBuildCopied(string sitePath, string staticDir, Action<string>? onLine, bool force = false)
    {
        try
        {
            var publicBuild = Path.Combine(sitePath, "public", "build");
            var destBuild = Path.Combine(staticDir, "build");
            if (!Directory.Exists(publicBuild)) return;

            // Copy if missing/empty, or forced refresh
            var destEmpty = !Directory.Exists(destBuild)
                            || !Directory.EnumerateFileSystemEntries(destBuild, "*", SearchOption.AllDirectories).Any();
            if (!force && !destEmpty) return;

            onLine?.Invoke(force
                ? "Copying public/build into static output (force)…"
                : "Copying public/build into static output…");
            CopyDirectory(publicBuild, destBuild);
        }
        catch (Exception ex)
        {
            onLine?.Invoke("Could not copy public/build (non-fatal): " + ex.Message);
        }
    }

    /// <summary>Return sample leftover bad absolute asset URLs still present in static HTML.</summary>
    internal static List<string> FindBrokenAssetUrlSamples(string staticDir, int max = 5)
    {
        var found = new List<string>();
        if (!Directory.Exists(staticDir)) return found;
        try
        {
            foreach (var file in Directory.EnumerateFiles(staticDir, "*.html", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }

                // Local Herd
                foreach (Match m in Regex.Matches(text, @"https?://[a-z0-9.-]+\.test\b[^""'\s>]*", RegexOptions.IgnoreCase))
                    Add(m.Value);

                // Doubled pages.dev
                foreach (Match m in Regex.Matches(text, @"https?://[a-z0-9.-]+\.pages\.dev\.pages\.dev[^""'\s>]*", RegexOptions.IgnoreCase))
                    Add(m.Value);

                // Absolute vite/build assets (should be root-relative after rewrite)
                foreach (Match m in Regex.Matches(text, @"https?://[^""'\s>]+/build/assets/[^""'\s>]+", RegexOptions.IgnoreCase))
                    Add(m.Value);

                if (found.Count >= max) return found;
            }
        }
        catch
        {
            // ignore
        }

        return found;

        void Add(string sample)
        {
            if (found.Any(f => f.Equals(sample, StringComparison.OrdinalIgnoreCase))) return;
            found.Add(sample);
        }
    }

    /// <summary>Legacy name kept for any callers.</summary>
    internal static List<string> FindLocalUrlSamples(string staticDir, int max = 5) =>
        FindBrokenAssetUrlSamples(staticDir, max);

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
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

    /// <summary>
    /// Stable production URL is <c>https://project.pages.dev</c>.
    /// Wrangler prints a deployment-specific preview like
    /// <c>https://80bd083d.project.pages.dev</c> — useful once, not as the site domain.
    /// </summary>
    internal static string PreferStablePagesUrl(params string?[] candidates)
    {
        string? stable = null;
        string? any = null;

        foreach (var raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var url = raw.Trim().TrimEnd('/');
            // strip path/query
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    url = uri.GetLeftPart(UriPartial.Authority);
            }
            catch
            {
                // keep trimmed
            }

            any ??= url;

            // Deployment previews look like: https://<hex>.<name>.pages.dev
            // Stable production: https://<name>.pages.dev  (exactly one label before pages.dev)
            if (IsStablePagesProductionUrl(url))
            {
                stable = url;
                break;
            }
        }

        return stable ?? any ?? "https://pages.dev";
    }

    internal static bool IsStablePagesProductionUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        if (!host.EndsWith(".pages.dev", StringComparison.OrdinalIgnoreCase)) return false;
        // name.pages.dev → 2 labels before TLD split carefully:
        // "new-site-7jx.pages.dev" → parts = [new-site-7jx, pages, dev] length 3
        // "80bd083d.new-site-7jx.pages.dev" → 4 parts
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3
               && parts[^2].Equals("pages", StringComparison.OrdinalIgnoreCase)
               && parts[^1].Equals("dev", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Collect all pages.dev URLs; prefer stable production over deploy previews.
        var matches = Regex.Matches(text, @"https://[a-zA-Z0-9.-]+\.pages\.dev");
        string? preview = null;
        foreach (Match m in matches)
        {
            var url = m.Value.TrimEnd('.', ',', ')', ']');
            if (IsStablePagesProductionUrl(url))
                return url;
            preview ??= url;
        }

        return preview;
    }

    private static StaticPublishResult Fail(string message) => new()
    {
        Ok = false,
        Message = message,
    };
}
