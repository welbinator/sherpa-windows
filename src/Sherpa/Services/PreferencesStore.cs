using System.Text.Json;
using Sherpa.Models;

namespace Sherpa.Services;

public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public PreferencesStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sherpa");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "preferences.json");
    }

    public AppPreferences Load()
    {
        if (!File.Exists(_path))
        {
            var prefs = new AppPreferences
            {
                DefaultSitesFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Sites"),
            };
            return prefs;
        }
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppPreferences>(json, JsonOpts) ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences prefs)
    {
        // Hosts store only secret keys — never raw tokens
        var json = JsonSerializer.Serialize(prefs, JsonOpts);
        File.WriteAllText(_path, json);
    }
}
