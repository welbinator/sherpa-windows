using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Sherpa.Services;

/// <summary>
/// GitHub Releases auto-update via Velopack.
/// Works only when the app was installed by the Velopack Setup.exe
/// (not when double-clicking a portable/dev build).
/// </summary>
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/welbinator/sherpa-windows";
    public const string PackId = "Sherpa";

    private readonly UpdateManager _mgr;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        // Public repo — no token needed for release checks (uses browser download URLs).
        var source = new GithubSource(RepoUrl, accessToken: null, prerelease: false);
        _mgr = new UpdateManager(source);
    }

    /// <summary>True when running from a Velopack-installed copy (updates can apply).</summary>
    public bool IsInstalled => _mgr.IsInstalled;

    public string? CurrentVersion => _mgr.CurrentVersion?.ToString();

    public string AppVersionDisplay
    {
        get
        {
            var asm = typeof(UpdateService).Assembly.GetName().Version;
            var file = asm is null ? "?" : $"{asm.Major}.{asm.Minor}.{asm.Build}";
            if (IsInstalled && !string.IsNullOrWhiteSpace(CurrentVersion))
                return CurrentVersion!;
            return file;
        }
    }

    public UpdateInfo? Pending => _pending;

    public async Task<(bool ok, string message, UpdateInfo? info)> CheckAsync(
        CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            return (false,
                "This copy isn’t the installed app. Download Setup from GitHub Releases, install Sherpa, then Check for updates works here.",
                null);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            _pending = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (_pending is null)
                return (true, $"You’re on the latest version ({AppVersionDisplay}).", null);

            var ver = _pending.TargetFullRelease.Version.ToString();
            return (true, $"Update available: {ver}. Click Download update, then Restart & install.", _pending);
        }
        catch (Exception ex)
        {
            return (false, "Could not check for updates: " + ex.Message, null);
        }
    }

    public async Task<(bool ok, string message)> DownloadAsync(
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsInstalled)
            return (false, "Install Sherpa with Setup.exe before updating.");

        if (_pending is null)
        {
            var check = await CheckAsync(ct).ConfigureAwait(false);
            if (!check.ok || check.info is null)
                return (false, check.message);
        }

        if (_pending is null)
            return (false, "No update to download.");

        try
        {
            await _mgr.DownloadUpdatesAsync(_pending, p => progress?.Report(p)).ConfigureAwait(false);
            var ver = _pending.TargetFullRelease.Version.ToString();
            return (true, $"Update {ver} downloaded. Click Restart & install to finish.");
        }
        catch (Exception ex)
        {
            return (false, "Download failed: " + ex.Message);
        }
    }

    /// <summary>Applies a downloaded update and restarts. Does not return on success.</summary>
    public (bool ok, string message) ApplyAndRestart()
    {
        if (!IsInstalled)
            return (false, "Install Sherpa with Setup.exe before updating.");

        var asset = _pending?.TargetFullRelease ?? _mgr.UpdatePendingRestart;
        if (asset is null)
            return (false, "Nothing ready to install. Check for updates and download first.");

        try
        {
            _mgr.ApplyUpdatesAndRestart(asset);
            return (true, "Restarting…");
        }
        catch (Exception ex)
        {
            return (false, "Could not apply update: " + ex.Message);
        }
    }
}
