using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Sherpa.Clients;

public sealed class ForgeClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://forge.laravel.com/api/v1/") };

    public ForgeClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sherpa-Windows/0.2");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<(bool ok, string message)> ValidateAsync(string token, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return (false, "Forge rejected the API token. Create a token in your Forge account profile.");
        try
        {
            using var doc = JsonDocument.Parse(body);
            var name = doc.RootElement.TryGetProperty("user", out var u) && u.TryGetProperty("name", out var n)
                ? n.GetString()
                : "Forge";
            return (true, $"Connected to Laravel Forge as {name}.");
        }
        catch
        {
            return (true, "Connected to Laravel Forge.");
        }
    }
}

public sealed class NetlifyClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.netlify.com/api/v1/") };

    public NetlifyClient() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sherpa-Windows/0.2");

    public async Task<(bool ok, string message)> ValidateAsync(string token, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            return (false, "Netlify rejected the access token. Create a personal access token in Netlify user settings.");
        return (true, "Connected to Netlify.");
    }
}

public sealed class LaravelCloudClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://cloud.laravel.com/api/") };

    public LaravelCloudClient() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sherpa-Windows/0.2");

    public async Task<(bool ok, string message)> ValidateAsync(string token, CancellationToken ct = default)
    {
        // Best-effort probe — Cloud API shapes evolve; treat non-HTML 401 as auth fail.
        using var req = new HttpRequestMessage(HttpMethod.Get, "sites");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (body.Contains("<html", StringComparison.OrdinalIgnoreCase))
            return (false, "Cloud returned a web page instead of the API. Re-copy the API token from Cloud → API tokens.");
        if ((int)res.StatusCode is 401 or 403)
            return (false, "Laravel Cloud rejected the API token. Create a token in Cloud → API tokens.");
        // 404 may mean path differs but token accepted by gateway
        return (true, "Laravel Cloud token saved. Connect GitHub in the Cloud dashboard (Source Control) once so deploys can use your repos.");
    }
}
