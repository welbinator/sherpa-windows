using System.Text.Json;
using Sherpa.Models;

namespace Sherpa.Services;

public sealed class SiteStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public SiteStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sherpa");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "sites.json");
    }

    public List<Site> Load()
    {
        if (!File.Exists(_path)) return new List<Site>();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<Site>>(json, JsonOpts) ?? new List<Site>();
        }
        catch
        {
            return new List<Site>();
        }
    }

    public void Save(IEnumerable<Site> sites)
    {
        var json = JsonSerializer.Serialize(sites.ToList(), JsonOpts);
        File.WriteAllText(_path, json);
    }
}
