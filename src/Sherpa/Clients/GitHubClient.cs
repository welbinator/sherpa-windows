using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Sherpa.Clients;

public sealed class GitHubClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.github.com/"),
    };

    public GitHubClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sherpa-Windows/0.1");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<(bool ok, string message, string? login)> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return (false, $"GitHub returned {(int)res.StatusCode}. Re-copy a classic token with repo scope.", null);
        using var doc = JsonDocument.Parse(body);
        var login = doc.RootElement.GetProperty("login").GetString();
        return (true, $"Connected as {login}.", login);
    }

    public async Task<(bool ok, string message, string? htmlUrl)> CreatePrivateRepoAsync(string token, string name, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "user/repos");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var payload = JsonSerializer.Serialize(new { name, @private = true, auto_init = false });
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return (false, $"Could not create repo (HTTP {(int)res.StatusCode}). {TrimHtml(body)}", null);
        using var doc = JsonDocument.Parse(body);
        var url = doc.RootElement.GetProperty("html_url").GetString();
        return (true, $"Created private repo {name}.", url);
    }

    private static string TrimHtml(string body)
        => body.Contains("<html", StringComparison.OrdinalIgnoreCase)
            ? "GitHub returned a web page instead of the API. Re-copy the token."
            : body.Length > 300 ? body[..300] + "…" : body;
}
