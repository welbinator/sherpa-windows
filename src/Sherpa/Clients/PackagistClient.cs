using System.Text.Json;

namespace Sherpa.Clients;

public sealed class PackagistClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://packagist.org/"),
    };

    public PackagistClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sherpa-Windows/0.1");
    }

    public async Task<IReadOnlyList<(string name, string description)>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<(string, string)>();
        var url = $"search.json?q={Uri.EscapeDataString(query)}";
        using var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) return Array.Empty<(string, string)>();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var list = new List<(string, string)>();
        if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
        foreach (var item in results.EnumerateArray().Take(15))
        {
            var name = item.GetProperty("name").GetString() ?? "";
            var desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            list.Add((name, desc));
        }
        return list;
    }
}
