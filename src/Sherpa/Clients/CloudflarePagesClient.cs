using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Sherpa.Clients;

public sealed class CloudflarePagesClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"),
    };

    public CloudflarePagesClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sherpa-Windows/0.1");
    }

    public async Task<(bool ok, string message)> ValidateAsync(string token, string accountId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return (false, "Cloudflare account ID is required. Find it in the dashboard URL or account overview.");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"accounts/{accountId}/pages/projects?per_page=1");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (body.Contains("<html", StringComparison.OrdinalIgnoreCase))
            return (false, "Cloud returned a web page instead of the API. Re-copy the API token from Cloudflare.");
        if (!res.IsSuccessStatusCode)
            return (false, "Cloudflare rejected the API token. Create a Custom Token (not Global API Key) with Pages edit.");
        return (true, "Cloudflare Pages token looks good.");
    }
}
