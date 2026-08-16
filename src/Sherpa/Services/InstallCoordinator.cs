using Sherpa.Models;
using Sherpa.Support;

namespace Sherpa.Services;

/// <summary>Multi-step "new site" flow — blank folder + optional composer create-project.</summary>
public sealed class InstallCoordinator
{
    private readonly ProcessRunner _runner;
    private readonly RuntimeManager _runtime;
    private readonly ComposerService _composer;

    public InstallCoordinator(ProcessRunner runner, RuntimeManager runtime, ComposerService composer)
    {
        _runner = runner;
        _runtime = runtime;
        _composer = composer;
    }

    public Task<(Site? site, ProcessResult? result, string? error)> CreateBlankAsync(
        string parentFolder,
        string name,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult<(Site?, ProcessResult?, string?)>((null, null, "Pick a folder name for the site."));
        Directory.CreateDirectory(parentFolder);
        var path = Path.Combine(parentFolder, name);
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            return Task.FromResult<(Site?, ProcessResult?, string?)>((null, null, $"That folder already has files: {path}"));

        Directory.CreateDirectory(path);
        onLine?.Invoke($"Created {path}");
        var site = SiteDetector.FromPath(path);
        return Task.FromResult<(Site?, ProcessResult?, string?)>((site, null, null));
    }

    public async Task<(Site? site, ProcessResult? result, string? error)> CreateStatamicAsync(
        string parentFolder,
        string name,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        var php = _runtime.FindPhp();
        var composer = _runtime.FindComposer();
        if (php is null || composer is null)
            return (null, null, "PHP and Composer are required to create a Statamic project. Install Laravel Herd or set paths under Settings.");

        Directory.CreateDirectory(parentFolder);
        var path = Path.Combine(parentFolder, name);
        if (Directory.Exists(path))
            return (null, null, $"Folder already exists: {path}");

        onLine?.Invoke("Creating Statamic project…");
        // composer create-project statamic/statamic name
        ProcessResult result;
        if (composer.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
        {
            result = await _runner.RunAsync(php,
                new[] { composer, "create-project", "statamic/statamic", name, "--no-interaction" },
                parentFolder, null, onLine, ct);
        }
        else
        {
            result = await _runner.RunAsync(composer,
                new[] { "create-project", "statamic/statamic", name, "--no-interaction" },
                parentFolder, null, onLine, ct);
        }

        if (!result.Success)
            return (null, result, "Statamic install failed. See the log and advice below.");

        var site = SiteDetector.FromPath(path);
        return (site, result, null);
    }
}
