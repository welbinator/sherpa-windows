using Sherpa.Models;

namespace Sherpa.Services;

public static class SiteDetector
{
    public static Site FromPath(string path)
    {
        path = Path.GetFullPath(path);
        var name = new DirectoryInfo(path).Name;
        var kind = DetectKind(path);
        var url = GuessLocalUrl(name);
        return new Site
        {
            Name = name,
            Path = path,
            Kind = kind,
            Url = url,
        };
    }

    public static SiteKind DetectKind(string path)
    {
        if (File.Exists(Path.Combine(path, "please")) && Directory.Exists(Path.Combine(path, "content")))
            return SiteKind.Statamic;
        // Statamic often still has please + composer.json with statamic/cms
        if (File.Exists(Path.Combine(path, "please")))
            return SiteKind.Statamic;
        if (File.Exists(Path.Combine(path, "artisan")))
        {
            var composer = Path.Combine(path, "composer.json");
            if (File.Exists(composer))
            {
                var text = File.ReadAllText(composer);
                if (text.Contains("statamic/cms", StringComparison.OrdinalIgnoreCase))
                    return SiteKind.Statamic;
            }
            return SiteKind.Laravel;
        }
        if (File.Exists(Path.Combine(path, "composer.json")))
            return SiteKind.OtherPhp;
        return SiteKind.Unknown;
    }

    /// <summary>
    /// Find PHP/Statamic/Laravel project roots under a parent folder (one level deep).
    /// A "site" is a directory that contains composer.json.
    /// </summary>
    public static IReadOnlyList<Site> DiscoverInFolder(string? folder)
    {
        var found = new List<Site>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return found;

        try
        {
            // Parent itself might be a site
            if (File.Exists(Path.Combine(folder, "composer.json")))
            {
                var self = FromPath(folder);
                if (self.Kind is not SiteKind.Unknown)
                    found.Add(self);
            }

            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                try
                {
                    // Skip obvious non-projects
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('.') || name is "node_modules" or "vendor" or "storage" or "bootstrap")
                        continue;
                    if (!File.Exists(Path.Combine(dir, "composer.json")))
                        continue;
                    var site = FromPath(dir);
                    if (site.Kind is SiteKind.Unknown)
                        continue;
                    found.Add(site);
                }
                catch
                {
                    // Skip unreadable dirs
                }
            }
        }
        catch
        {
            // Unreadable root
        }

        return found;
    }

    /// <summary>
    /// Merge discovered disk sites into the saved list (match by full path).
    /// Returns how many new sites were added.
    /// </summary>
    public static int MergeDiscovered(IList<Site> existing, IEnumerable<Site> discovered)
    {
        var added = 0;
        foreach (var d in discovered)
        {
            var path = Path.GetFullPath(d.Path);
            if (existing.Any(s =>
                    string.Equals(Path.GetFullPath(s.Path), path, StringComparison.OrdinalIgnoreCase)))
                continue;
            existing.Add(d);
            added++;
        }
        return added;
    }

    private static string GuessLocalUrl(string name)
    {
        // Herd default .test — http is safer until we know HTTPS is trusted
        return $"http://{name.Trim().ToLowerInvariant().Replace(' ', '-')}.test";
    }
}
