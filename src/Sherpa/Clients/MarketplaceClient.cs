using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sherpa.Clients;

/// <summary>
/// Public Statamic Marketplace API (same base the official CLI uses).
/// GET https://statamic.com/api/v1/marketplace/starter-kits
/// </summary>
public sealed class MarketplaceClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://statamic.com/api/v1/"),
        Timeout = TimeSpan.FromSeconds(60),
    };

    public MarketplaceClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sherpa-Windows/0.2");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<IReadOnlyList<StarterKitInfo>> GetAllStarterKitsAsync(CancellationToken ct = default)
    {
        var all = new List<StarterKitInfo>();
        var page = 1;
        var last = 1;
        while (page <= last)
        {
            var resp = await _http.GetFromJsonAsync<StarterKitsPage>(
                $"marketplace/starter-kits?page={page}", ct);
            if (resp?.Data is null) break;
            foreach (var row in resp.Data)
                all.Add(StarterKitInfo.FromApi(row));
            last = resp.Meta?.LastPage ?? page;
            page++;
            if (page > 20) break; // safety
        }

        return all
            .OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class StarterKitsPage
{
    [JsonPropertyName("data")] public List<StarterKitDto>? Data { get; set; }
    [JsonPropertyName("meta")] public StarterKitsMeta? Meta { get; set; }
}

public sealed class StarterKitsMeta
{
    [JsonPropertyName("last_page")] public int LastPage { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}

public sealed class StarterKitDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("package")] public string? Package { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("price_range")] public List<decimal?>? PriceRange { get; set; }
    [JsonPropertyName("seller")] public StarterKitSellerDto? Seller { get; set; }
    [JsonPropertyName("assets")] public List<StarterKitAssetDto>? Assets { get; set; }
}

public sealed class StarterKitSellerDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
}

public sealed class StarterKitAssetDto
{
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public sealed class StarterKitInfo
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Package { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Url { get; init; } = "";
    public string SellerName { get; init; } = "";
    public string? CoverUrl { get; init; }
    public bool IsPaid { get; init; }
    public string PriceLabel { get; init; } = "Free";

    public static StarterKitInfo FromApi(StarterKitDto dto)
    {
        var range = dto.PriceRange ?? new List<decimal?>();
        var nums = range.Where(x => x is not null && x > 0).Select(x => x!.Value).ToList();
        var paid = nums.Count > 0;
        var priceLabel = paid
            ? (nums.Min() == nums.Max() ? $"${nums.Min():0.##}" : $"${nums.Min():0.##}–${nums.Max():0.##}")
            : "Free";

        return new StarterKitInfo
        {
            Id = dto.Id,
            Name = dto.Name ?? dto.Slug ?? "Starter kit",
            Slug = dto.Slug ?? "",
            Package = dto.Package ?? "",
            Summary = dto.Summary ?? "",
            Url = dto.Url ?? "",
            SellerName = dto.Seller?.Name ?? "",
            CoverUrl = dto.Assets?.FirstOrDefault()?.Url,
            IsPaid = paid,
            PriceLabel = priceLabel,
        };
    }
}
