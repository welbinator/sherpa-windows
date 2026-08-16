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

    private string RequireGit()
        => _runtime.FindGit()
           ?? throw new InvalidOperationException("Git was not found. Install Git for Windows, then reopen Sherpa.");

    public Task<ProcessResult> StatusPorcelainAsync(string sitePath, CancellationToken ct = default)
        => Git(sitePath, new[] { "status", "--porcelain=v1", "-b" }, ct);

    public Task<ProcessResult> StatusShortAsync(string sitePath, CancellationToken ct = default)
        => Git(sitePath, new[] { "status", "--short", "--branch" }, ct);

    public Task<ProcessResult> LogAsync(string sitePath, int n = 15, CancellationToken ct = default)
        => Git(sitePath, new[] { "log", $"-{n}", "--oneline", "--decorate" }, ct);

    public Task<ProcessResult> InitAsync(string sitePath, CancellationToken ct = default)
        => Git(sitePath, new[] { "init" }, ct);

    public Task<ProcessResult> RemoteUrlAsync(string sitePath, CancellationToken ct = default)
        => Git(sitePath, new[] { "remote", "get-url", "origin" }, ct);

    public async Task EnsureIdentityAsync(string sitePath, string name, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Git needs your name and email before it can save changes. Set them under Settings or the Git tab.");

        await Git(sitePath, new[] { "config", "user.name", name }, ct);
        await Git(sitePath, new[] { "config", "user.email", email }, ct);
    }

    public async Task<ProcessResult> SaveChangesAsync(
        string sitePath,
        IEnumerable<string> paths,
        string message,
        string name,
        string email,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        await EnsureIdentityAsync(sitePath, name, email, ct);
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0)
            return new ProcessResult { ExitCode = 1, StdErr = "Nothing selected to commit." };

        foreach (var p in list)
        {
            var r = await Git(sitePath, new[] { "add", "--", p }, ct, onLine);
            if (!r.Success) return r;
        }

        onLine?.Invoke("Save changes…");
        return await Git(sitePath, new[] { "commit", "-m", string.IsNullOrWhiteSpace(message) ? "Update" : message }, ct, onLine);
    }

    public async Task<ProcessResult> PullRebaseAsync(string sitePath, Action<string>? onLine, CancellationToken ct = default)
    {
        onLine?.Invoke("Fetching and rebasing (git pull --rebase --autostash)…");
        return await Git(sitePath, new[] { "pull", "--rebase", "--autostash" }, ct, onLine);
    }

    public async Task<ProcessResult> PushAsync(string sitePath, Action<string>? onLine, CancellationToken ct = default)
    {
        onLine?.Invoke("Push…");
        return await Git(sitePath, new[] { "push", "-u", "origin", "HEAD" }, ct, onLine);
    }

    /// <summary>
    /// Mac Sync: commit selection → pull --rebase --autostash → push.
    /// </summary>
    public async Task<ProcessResult> SyncAsync(
        string sitePath,
        IEnumerable<string> paths,
        string message,
        string name,
        string email,
        Action<string>? onLine,
        CancellationToken ct = default)
    {
        var pathsList = paths.ToList();
        if (pathsList.Count > 0)
        {
            var commit = await SaveChangesAsync(sitePath, pathsList, message, name, email, onLine, ct);
            // empty commit is ok if nothing to commit after add
            if (!commit.Success && !commit.Combined.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
                return commit;
        }

        var pull = await PullRebaseAsync(sitePath, onLine, ct);
        if (!pull.Success) return pull;
        return await PushAsync(sitePath, onLine, ct);
    }

    public Task<ProcessResult> AddRemoteAsync(string sitePath, string url, CancellationToken ct = default)
        => Git(sitePath, new[] { "remote", "add", "origin", url }, ct);

    public Task<ProcessResult> SetRemoteAsync(string sitePath, string url, CancellationToken ct = default)
        => Git(sitePath, new[] { "remote", "set-url", "origin", url }, ct);

    private async Task<ProcessResult> Git(string sitePath, IEnumerable<string> args, CancellationToken ct, Action<string>? onLine = null)
    {
        var git = RequireGit();
        return await _runner.RunAsync(git, args, sitePath, null, onLine, ct);
    }
}
