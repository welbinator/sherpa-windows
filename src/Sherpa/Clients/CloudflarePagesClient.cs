using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sherpa.Clients;

public sealed class CloudflarePagesProject
{
    public string Name { get; init; } = "";
    public string? Subdomain { get; init; }
    public string? Id { get; init; }

    /// <summary>
    /// Canonical production host URL. Cloudflare's API often returns
    /// <c>subdomain</c> already as <c>name.pages.dev</c> — never append twice.
    /// </summary>
    public string ProductionUrl => "https://" + NormalizePagesHost(Subdomain, Name);

    /// <summary>e.g. <c>my-site.pages.dev</c> (no scheme).</summary>
    public static string NormalizePagesHost(string? subdomain, string? name)
    {
        var host = !string.IsNullOrWhiteSpace(subdomain) ? subdomain.Trim() : (name ?? "").Trim();
        if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            host = host["https://".Length..];
        else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            host = host["http://".Length..];

        host = host.Trim().TrimEnd('/');
        // Strip accidental path
        var slash = host.IndexOf('/');
        if (slash >= 0) host = host[..slash];

        if (string.IsNullOrWhiteSpace(host))
            host = "example";

        // Already a full pages.dev host (API often returns this)
        if (host.EndsWith(".pages.dev", StringComparison.OrdinalIgnoreCase))
        {
            // Collapse name.pages.dev.pages.dev if somehow doubled
            while (host.EndsWith(".pages.dev.pages.dev", StringComparison.OrdinalIgnoreCase))
                host = host[..^".pages.dev".Length];
            return host;
        }

        return host + ".pages.dev";
    }
}

public sealed class CloudflarePagesClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"),
        Timeout = TimeSpan.FromMinutes(2),
    };

    public CloudflarePagesClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sherpa-Windows/0.3");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // Cloudflare Pages list endpoint rejects per_page outside a small range
    // (Wrangler uses 10; 50 returns "Invalid list options… page or per_page").
    private const int ProjectsPageSize = 10;

    public async Task<(bool ok, string message)> ValidateAsync(string token, string accountId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return (false, "Cloudflare account ID is required. Find it in the dashboard URL or account overview.");

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"accounts/{accountId.Trim()}/pages/projects?page=1&per_page={ProjectsPageSize}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (body.Contains("<html", StringComparison.OrdinalIgnoreCase))
            return (false, "Cloud returned a web page instead of the API. Re-copy the API token from Cloudflare.");
        if (!res.IsSuccessStatusCode)
            return (false, "Cloudflare rejected the API token. Create a Custom Token (not Global API Key) with Account → Cloudflare Pages → Edit.");
        return (true, "Cloudflare Pages token looks good.");
    }

    public async Task<(bool ok, string message, IReadOnlyList<CloudflarePagesProject> projects)> ListProjectsAsync(
        string token, string accountId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return (false, "Cloudflare account ID is required.", Array.Empty<CloudflarePagesProject>());

        var list = new List<CloudflarePagesProject>();
        var page = 1;
        while (page <= 50)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"accounts/{accountId.Trim()}/pages/projects?page={page}&per_page={ProjectsPageSize}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return (false, ParseError(body, "Could not list Pages projects."), list);

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
                    break;

                var batch = 0;
                foreach (var item in result.EnumerateArray())
                {
                    batch++;
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var subdomain = item.TryGetProperty("subdomain", out var s) ? s.GetString() : name;
                    var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
                    list.Add(new CloudflarePagesProject { Name = name, Subdomain = subdomain, Id = id });
                }

                if (batch < ProjectsPageSize) break;
                page++;
            }
            catch (Exception ex)
            {
                return (false, "Could not parse Pages project list: " + ex.Message, list);
            }
        }

        return (true, $"Found {list.Count} Pages project(s).", list);
    }

    public async Task<(bool ok, string message, CloudflarePagesProject? project)> GetProjectAsync(
        string token, string accountId, string projectName, CancellationToken ct = default)
    {
        var name = SanitizeProjectName(projectName);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(accountId))
            return (false, "Project name and account ID are required.", null);

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"accounts/{accountId.Trim()}/pages/projects/{Uri.EscapeDataString(name)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (false, "not found", null);
        if (!res.IsSuccessStatusCode)
            return (false, ParseError(body, "Could not load Pages project."), null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var result = doc.RootElement.GetProperty("result");
            var n = result.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? name : name;
            var subdomain = result.TryGetProperty("subdomain", out var s) ? s.GetString() : n;
            var id = result.TryGetProperty("id", out var i) ? i.GetString() : null;
            return (true, "ok", new CloudflarePagesProject { Name = n, Subdomain = subdomain, Id = id });
        }
        catch (Exception ex)
        {
            return (false, "Could not parse Pages project: " + ex.Message, null);
        }
    }

    public async Task<(bool ok, string message, CloudflarePagesProject? project)> EnsureProjectAsync(
        string token, string accountId, string projectName, CancellationToken ct = default)
    {
        var name = SanitizeProjectName(projectName);
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Project name is empty after cleanup. Use letters, numbers, and hyphens.", null);

        // Prefer direct GET by name (no pagination quirks) before listing/creating.
        var existing = await GetProjectAsync(token, accountId, name, ct).ConfigureAwait(false);
        if (existing.ok && existing.project is not null)
            return (true, $"Using existing Pages project “{existing.project.Name}”.", existing.project);

        // Create project (Direct Upload / production branch main — matches Mac Sherpa)
        var payload = JsonSerializer.Serialize(new
        {
            name,
            production_branch = "main",
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"accounts/{accountId.Trim()}/pages/projects")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            // Race: created elsewhere — fetch by name again
            if (body.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                || body.Contains("9004", StringComparison.Ordinal)
                || body.Contains("8000004", StringComparison.Ordinal))
            {
                var again = await GetProjectAsync(token, accountId, name, ct).ConfigureAwait(false);
                if (again.ok && again.project is not null)
                    return (true, $"Using existing Pages project “{again.project.Name}”.", again.project);
            }

            return (false, ParseError(body, "Could not create Pages project."), null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var result = doc.RootElement.GetProperty("result");
            var createdName = result.TryGetProperty("name", out var n) ? n.GetString() ?? name : name;
            var subdomain = result.TryGetProperty("subdomain", out var s) ? s.GetString() : createdName;
            var id = result.TryGetProperty("id", out var i) ? i.GetString() : null;
            var project = new CloudflarePagesProject
            {
                Name = createdName,
                Subdomain = subdomain,
                Id = id,
            };
            return (true, $"Created Pages project “{createdName}”.", project);
        }
        catch
        {
            return (true, $"Created Pages project “{name}”.",
                new CloudflarePagesProject { Name = name, Subdomain = name });
        }
    }

    public static string SanitizeProjectName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9-]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        if (s.Length > 58) s = s[..58].Trim('-');
        return s;
    }

    private static string ParseError(string body, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                if (first.TryGetProperty("message", out var m))
                {
                    var msg = m.GetString();
                    if (!string.IsNullOrWhiteSpace(msg)) return msg!;
                }
            }
        }
        catch
        {
            // ignore
        }

        if (body.Length > 280) body = body[..280] + "…";
        return string.IsNullOrWhiteSpace(body) ? fallback : fallback + " " + body;
    }
}
