using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Sherpa.Services;

/// <summary>
/// One prior Sherpa release the user can roll back to (Velopack full package required).
/// </summary>
public sealed class RollbackRelease
{
    public required string Version { get; init; }
    public required string Tag { get; init; }
    public required string NupkgFileName { get; init; }
    public string? PublishedAt { get; init; }

    public override string ToString() => string.IsNullOrWhiteSpace(PublishedAt)
        ? Version
        : $"{Version}  ({PublishedAt})";
}

/// <summary>
/// GitHub Releases auto-update + rollback via Velopack.
/// Works only when the app was installed by the Velopack Setup.exe
/// (not when double-clicking a portable/dev build).
/// </summary>
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/welbinator/sherpa-windows";
    public const string RepoApiReleases = "https://api.github.com/repos/welbinator/sherpa-windows/releases";
    public const string PackId = "Sherpa";

    private readonly UpdateManager _mgr;
    private readonly HttpClient _http;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        // Public repo — no token needed for release checks (uses browser download URLs).
        var source = new GithubSource(RepoUrl, accessToken: null, prerelease: false);
        // Allow rolling back to an older full package from Settings.
        var options = new UpdateOptions
        {
            AllowVersionDowngrade = true,
            MaximumDeltasBeforeFallback = 0, // always prefer full packages (rollback-safe)
        };
        _mgr = new UpdateManager(source, options);
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sherpa", "Windows"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
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
            var kind = _pending.IsDowngrade ? "Older package available" : "Update available";
            return (true, $"{kind}: {ver}. Click Download update, then Restart & install.", _pending);
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
            await _mgr.DownloadUpdatesAsync(_pending, p => progress?.Report(p), ct).ConfigureAwait(false);
            var ver = _pending.TargetFullRelease.Version.ToString();
            var word = _pending.IsDowngrade ? "Version" : "Update";
            return (true, $"{word} {ver} downloaded. Click Restart & install to finish.");
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

    /// <summary>
    /// Lists up to <paramref name="maxCount"/> Velopack full releases older than the
    /// currently installed version (newest-first among those older builds).
    /// </summary>
    public async Task<(bool ok, string message, IReadOnlyList<RollbackRelease> releases)> ListPreviousReleasesAsync(
        int maxCount = 5,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var resp = await _http.GetAsync(RepoApiReleases + "?per_page=30", ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (false, $"Could not list releases (HTTP {(int)resp.StatusCode}).", Array.Empty<RollbackRelease>());

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (false, "Unexpected GitHub Releases response.", Array.Empty<RollbackRelease>());

            var current = TryParseVersion(AppVersionDisplay);
            var found = new List<RollbackRelease>();

            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                    continue;
                if (rel.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                    continue;

                var tag = rel.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(tag)) continue;

                var versionText = tag.TrimStart('v', 'V').Trim();
                var ver = TryParseVersion(versionText);
                if (ver is null) continue;

                // Only versions strictly older than current
                if (current is not null && ver >= current) continue;

                string? nupkg = null;
                if (rel.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        // Velopack full package — required for in-app install/rollback
                        if (name.EndsWith("-full.nupkg", StringComparison.OrdinalIgnoreCase)
                            && name.StartsWith("Sherpa-", StringComparison.OrdinalIgnoreCase))
                        {
                            nupkg = name;
                            break;
                        }
                    }
                }

                if (nupkg is null) continue; // zip-only era — can't apply via Velopack

                string? published = null;
                if (rel.TryGetProperty("published_at", out var pub) && pub.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(pub.GetString(), out var dto))
                        published = dto.ToLocalTime().ToString("MMM d, yyyy");
                }

                found.Add(new RollbackRelease
                {
                    Version = versionText,
                    Tag = tag.StartsWith('v') || tag.StartsWith('V') ? tag : "v" + versionText,
                    NupkgFileName = nupkg,
                    PublishedAt = published,
                });
            }

            // Newest older builds first, take 5
            var list = found
                .OrderByDescending(r => TryParseVersion(r.Version) ?? new SemanticVersion(0, 0, 0))
                .Take(Math.Max(1, maxCount))
                .ToList();

            if (list.Count == 0)
            {
                var msg = current is null
                    ? "No earlier installer packages found on GitHub yet."
                    : $"No earlier versions before {AppVersionDisplay} (need a prior Setup/Velopack release).";
                return (true, msg, list);
            }

            return (true, $"Found {list.Count} earlier version(s) you can restore.", list);
        }
        catch (Exception ex)
        {
            return (false, "Could not load previous versions: " + ex.Message, Array.Empty<RollbackRelease>());
        }
    }

    /// <summary>
    /// Prepares a specific older (or equal) full package for install via Velopack.
    /// Call <see cref="ApplyAndRestart"/> after a successful download.
    /// </summary>
    public async Task<(bool ok, string message)> DownloadSpecificVersionAsync(
        RollbackRelease release,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsInstalled)
            return (false, "Install Sherpa with Setup.exe before rolling back.");

        if (release is null || string.IsNullOrWhiteSpace(release.Version))
            return (false, "Pick a previous version first.");

        try
        {
            ct.ThrowIfCancellationRequested();

            // Prefer releases.win.json from that tag (has SHA + size). Fall back to filename-only asset.
            VelopackAsset? asset = null;
            var jsonUrl = $"{RepoUrl}/releases/download/{release.Tag}/releases.win.json";
            try
            {
                using var resp = await _http.GetAsync(jsonUrl, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var feed = VelopackAssetFeed.FromJson(json);
                    asset = feed.Assets?.FirstOrDefault(a => a.Type == VelopackAssetType.Full)
                            ?? feed.Assets?.FirstOrDefault();
                }
            }
            catch
            {
                // fall through to synthetic asset
            }

            if (asset is null)
            {
                var ver = TryParseVersion(release.Version)
                          ?? throw new InvalidOperationException("Invalid version: " + release.Version);
                asset = new VelopackAsset
                {
                    PackageId = PackId,
                    Version = ver,
                    Type = VelopackAssetType.Full,
                    FileName = release.NupkgFileName,
                };
            }

            var current = TryParseVersion(AppVersionDisplay);
            var target = asset.Version;
            var isDowngrade = current is null || target < current;
            if (current is not null && target == current)
                return (false, $"You’re already on {release.Version}.");

            if (current is not null && target > current)
            {
                // Newer than installed — treat as normal update path
                isDowngrade = false;
            }

            _pending = new UpdateInfo(asset, isDowngrade);
            await _mgr.DownloadUpdatesAsync(_pending, p => progress?.Report(p), ct).ConfigureAwait(false);
            var word = isDowngrade ? "Earlier version" : "Version";
            return (true, $"{word} {release.Version} downloaded. Click Restart & install to finish.");
        }
        catch (Exception ex)
        {
            _pending = null;
            return (false, "Could not download that version: " + ex.Message);
        }
    }

    private static SemanticVersion? TryParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().TrimStart('v', 'V');
        // File versions sometimes look like 0.3.2.0 — trim to 3-part when needed
        if (SemanticVersion.TryParse(t, out var sv)) return sv;
        var parts = t.Split('.');
        if (parts.Length >= 3
            && int.TryParse(parts[0], out var maj)
            && int.TryParse(parts[1], out var min)
            && int.TryParse(parts[2], out var pat))
        {
            return new SemanticVersion(maj, min, pat);
        }
        return null;
    }
}
