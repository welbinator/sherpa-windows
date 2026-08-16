using Sherpa.Support;

namespace Sherpa.Services;

public sealed class ComposerService
{
    private readonly ProcessRunner _runner;
    private readonly RuntimeManager _runtime;

    public ComposerService(ProcessRunner runner, RuntimeManager runtime)
    {
        _runner = runner;
        _runtime = runtime;
    }

    public async Task<ProcessResult> RequireAsync(string sitePath, string package, string? version, Action<string>? onLine, CancellationToken ct = default)
    {
        var (file, args) = ComposerInvocation(sitePath, BuildRequireArgs(package, version));
        return await _runner.RunAsync(file, args, sitePath, ComposerEnv(), onLine, ct);
    }

    public async Task<ProcessResult> UpdateAsync(string sitePath, string? package, Action<string>? onLine, CancellationToken ct = default)
    {
        var list = new List<string> { "update", "-W", "--no-interaction" };
        if (!string.IsNullOrWhiteSpace(package)) list.Add(package);
        var (file, args) = ComposerInvocation(sitePath, list);
        return await _runner.RunAsync(file, args, sitePath, ComposerEnv(), onLine, ct);
    }

    public async Task<ProcessResult> RemoveAsync(string sitePath, string package, Action<string>? onLine, CancellationToken ct = default)
    {
        var (file, args) = ComposerInvocation(sitePath, new[] { "remove", package, "--no-interaction" });
        return await _runner.RunAsync(file, args, sitePath, ComposerEnv(), onLine, ct);
    }

    public async Task<ProcessResult> ShowAsync(string sitePath, CancellationToken ct = default)
    {
        var (file, args) = ComposerInvocation(sitePath, new[] { "show", "--direct", "--format=json" });
        return await _runner.RunAsync(file, args, sitePath, ComposerEnv(), null, ct);
    }

    public async Task<ProcessResult> InstallAsync(string sitePath, Action<string>? onLine, CancellationToken ct = default)
    {
        var (file, args) = ComposerInvocation(sitePath, new[] { "install", "--no-interaction" });
        return await _runner.RunAsync(file, args, sitePath, ComposerEnv(), onLine, ct);
    }

    private (string file, IEnumerable<string> args) ComposerInvocation(string sitePath, IEnumerable<string> composerArgs)
    {
        var composer = _runtime.FindComposer()
            ?? throw new InvalidOperationException("Composer is not available. Install Composer or Laravel Herd, then set the path under Settings.");
        var php = _runtime.FindPhp();

        // composer.phar needs php
        if (composer.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
        {
            if (php is null)
                throw new InvalidOperationException("PHP is not available to run Composer. Install PHP via Herd/Laragon or set the path under Settings.");
            return (php, new[] { composer }.Concat(composerArgs));
        }

        return (composer, composerArgs);
    }

    private static IEnumerable<string> BuildRequireArgs(string package, string? version)
    {
        yield return "require";
        yield return string.IsNullOrWhiteSpace(version) ? package : $"{package}:{version}";
        yield return "--no-interaction";
    }

    private static Dictionary<string, string?> ComposerEnv() => new()
    {
        ["COMPOSER_DISABLE_XDEBUG_WARN"] = "1",
    };
}
