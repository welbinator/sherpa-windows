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

    private static string GuessLocalUrl(string name)
    {
        // Herd default .test
        return $"http://{name}.test";
    }
}
