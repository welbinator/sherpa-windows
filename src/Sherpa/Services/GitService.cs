using Sherpa.Support;

namespace Sherpa.Services;

public sealed class GitService
{
    private readonly ProcessRunner _runner;
    private readonly RuntimeManager _runtime;

    public GitService(ProcessRunner runner, RuntimeManager runtime)
    {
        _runner = runner;
        _runtime = runtime;
    }

    public async Task<ProcessResult> StatusAsync(string sitePath, CancellationToken ct = default)
        => await Git(sitePath, new[] { "status", "--short", "--branch" }, ct);

    public async Task<ProcessResult> LogAsync(string sitePath, int n = 15, CancellationToken ct = default)
        => await Git(sitePath, new[] { "log", $"-{n}", "--oneline", "--decorate" }, ct);

    public async Task<ProcessResult> InitAsync(string sitePath, CancellationToken ct = default)
        => await Git(sitePath, new[] { "init" }, ct);

    public async Task<ProcessResult> AddAllCommitAsync(string sitePath, string message, Action<string>? onLine, CancellationToken ct = default)
    {
        var add = await Git(sitePath, new[] { "add", "-A" }, ct, onLine);
        if (!add.Success) return add;
        return await Git(sitePath, new[] { "commit", "-m", message }, ct, onLine);
    }

    private async Task<ProcessResult> Git(string sitePath, IEnumerable<string> args, CancellationToken ct, Action<string>? onLine = null)
    {
        var git = _runtime.FindGit()
            ?? throw new InvalidOperationException("Git is not available. Install Git for Windows, then reopen Sherpa.");
        return await _runner.RunAsync(git, args, sitePath, null, onLine, ct);
    }
}
