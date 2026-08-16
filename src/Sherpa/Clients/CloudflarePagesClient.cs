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

    public string ProductionUrl =>
        !string.IsNullOrWhiteSpace(Subdomain)
            ? $"https://{Subdomain}.pages.dev"
            : $"https://{Name}.pages.dev";
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

    public async Task<(bool ok, string message)> ValidateAsync(string token, string accountId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return (false, "Cloudflare account ID is required. Find it in the dashboard URL or account overview.");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"accounts/{accountId.Trim()}/pages/projects?per_page=1");
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
        while (page <= 20)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"accounts/{accountId.Trim()}/pages/projects?per_page=50&page={page}");
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

                if (batch < 50) break;
                page++;
            }
            catch (Exception ex)
            {
                return (false, "Could not parse Pages project list: " + ex.Message, list);
            }
        }

        return (true, $"Found {list.Count} Pages project(s).", list);
    }

    public async Task<(bool ok, string message, CloudflarePagesProject? project)> EnsureProjectAsync(
        string token, string accountId, string projectName, CancellationToken ct = default)
    {
        var name = SanitizeProjectName(projectName);
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Project name is empty after cleanup. Use letters, numbers, and hyphens.", null);

        var (listOk, listMsg, projects) = await ListProjectsAsync(token, accountId, ct).ConfigureAwait(false);
        if (!listOk)
            return (false, listMsg, null);

        var existing = projects.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return (true, $"Using existing Pages project “{existing.Name}”.", existing);

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
            // Race: created elsewhere
            if (body.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                || body.Contains("9004", StringComparison.Ordinal))
            {
                var again = await ListProjectsAsync(token, accountId, ct).ConfigureAwait(false);
                var hit = again.projects.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (hit is not null)
                    return (true, $"Using existing Pages project “{hit.Name}”.", hit);
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
