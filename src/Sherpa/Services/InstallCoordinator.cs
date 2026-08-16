using Sherpa.Models;
using Sherpa.Support;

namespace Sherpa.Services;

/// <summary>Multi-step new site flow — mirrors Mac InstallCoordinator + NewSiteWizard options.</summary>
public sealed class InstallCoordinator
{
    private readonly ProcessRunner _runner;
    private readonly RuntimeManager _runtime;
    private readonly HerdService _herd;

    public InstallCoordinator(ProcessRunner runner, RuntimeManager runtime, HerdService herd)
    {
        _runner = runner;
        _runtime = runtime;
        _herd = herd;
    }

    public enum StartingPoint
    {
        Blank,
        FreshStatamic,
    }

    public async Task<(Site? site, ProcessResult? result, string? error)> CreateAsync(
        string parentFolder,
        string siteName,
        StartingPoint startingPoint,
        bool parkInHerd,
        bool secureHttps,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(siteName))
            return (null, null, "Site name is required.");

        var slug = HerdService.Slug(siteName);
        Directory.CreateDirectory(parentFolder);
        var path = Path.Combine(parentFolder, slug);

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            return (null, null, $"That folder already has files: {path}");

        ProcessResult? result = null;

        if (startingPoint == StartingPoint.Blank)
        {
            Directory.CreateDirectory(path);
            onLine?.Invoke($"Created {path}");
            onLine?.Invoke("Blank site — drop a project in, import later, or run Composer yourself.");
        }
        else
        {
            var php = _runtime.FindPhp();
            var composer = _runtime.FindComposer();
            if (php is null || composer is null)
                return (null, null, "PHP and Composer are required. Install Laravel Herd or set paths under Settings.");

            onLine?.Invoke("Fresh Statamic, no starter kit…");
            onLine?.Invoke("Creating project with composer create-project statamic/statamic…");

            if (composer.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
            {
                result = await _runner.RunAsync(php,
                    new[] { composer, "create-project", "statamic/statamic", slug, "--no-interaction" },
                    parentFolder, null, onLine, ct);
            }
            else
            {
                result = await _runner.RunAsync(composer,
                    new[] { "create-project", "statamic/statamic", slug, "--no-interaction" },
                    parentFolder, null, onLine, ct);
            }

            if (!result.Success)
                return (null, result, "Install failed. See the log and advice below.");
        }

        if (!Directory.Exists(path))
            return (null, result, $"Expected folder missing after install: {path}");

        var site = SiteDetector.FromPath(path);
        site.Name = siteName.Trim();
        site.StartingPoint = startingPoint == StartingPoint.Blank ? "blank" : "fresh-statamic";
        site.Url = _herd.UrlPreview(siteName, secureHttps);
        site.Https = secureHttps;

        if (parkInHerd)
        {
            var (ok, msg) = await _herd.ParkAsync(path, siteName, onLine, ct);
            onLine?.Invoke(msg);
            site.ParkedInHerd = ok;
            if (!ok)
                onLine?.Invoke("Park failed — site files are still on disk. You can Link to Herd from Overview later.");
            else if (secureHttps)
            {
                var (sok, smsg) = await _herd.SecureAsync(siteName, true, onLine, ct);
                onLine?.Invoke(smsg);
                site.Https = sok;
                if (sok) site.Url = _herd.UrlPreview(siteName, true);
            }
        }

        onLine?.Invoke("Created project");
        return (site, result, null);
    }
}
