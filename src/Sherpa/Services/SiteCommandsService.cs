using Sherpa.Models;
using Sherpa.Support;

namespace Sherpa.Services;

public sealed class SiteCommandsService
{
    private readonly ProcessRunner _runner;
    private readonly RuntimeManager _runtime;

    public SiteCommandsService(ProcessRunner runner, RuntimeManager runtime)
    {
        _runner = runner;
        _runtime = runtime;
    }

    public async Task<ProcessResult> ClearLaravelCacheAsync(string sitePath, Action<string>? onLine, CancellationToken ct = default)
        => await PhpArtisan(sitePath, new[] { "cache:clear" }, onLine, ct);

    public async Task<ProcessResult> ClearStacheAsync(string sitePath, Action<string>? onLine, CancellationToken ct = default)
        => await PhpPlease(sitePath, new[] { "stache:clear" }, onLine, ct);

    public async Task<ProcessResult> WarmStacheAsync(string sitePath, Action<string>? onLine, CancellationToken ct = default)
        => await PhpPlease(sitePath, new[] { "stache:warm" }, onLine, ct);

    public async Task<ProcessResult> ClearGlideAsync(string sitePath, Action<string>? onLine, CancellationToken ct = default)
        => await PhpPlease(sitePath, new[] { "glide:clear" }, onLine, ct);

    public async Task<ProcessResult> MakeUserAsync(
        string sitePath,
        string email,
        string password,
        bool super,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        // please make:user --email= --password=  (super via interactive historically; try flags)
        var args = new List<string> { "make:user", $"--email={email}", $"--password={password}" };
        if (super) args.Add("--super");
        return await PhpPlease(sitePath, args, onLine, ct);
    }

    public async Task<ProcessResult> PleaseAsync(string sitePath, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct = default)
        => await PhpPlease(sitePath, args, onLine, ct);

    public async Task<ProcessResult> ArtisanAsync(string sitePath, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct = default)
        => await PhpArtisan(sitePath, args, onLine, ct);

    private async Task<ProcessResult> PhpPlease(string sitePath, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct)
    {
        var php = RequirePhp();
        var please = Path.Combine(sitePath, "please");
        if (!File.Exists(please))
            throw new InvalidOperationException("No `please` CLI in this site. Clear Stache is for Statamic sites.");
        return await _runner.RunAsync(php, new[] { please }.Concat(args), sitePath, null, onLine, ct);
    }

    private async Task<ProcessResult> PhpArtisan(string sitePath, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct)
    {
        var php = RequirePhp();
        var artisan = Path.Combine(sitePath, "artisan");
        if (!File.Exists(artisan))
            throw new InvalidOperationException("No `artisan` CLI in this site. Clear Cache is for Laravel/Statamic apps.");
        return await _runner.RunAsync(php, new[] { artisan }.Concat(args), sitePath, null, onLine, ct);
    }

    private string RequirePhp()
        => _runtime.FindPhp()
           ?? throw new InvalidOperationException("PHP is not available. Install Laravel Herd or Laragon, or set the PHP path under Settings.");
}
