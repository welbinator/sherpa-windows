using Sherpa.Models;

namespace Sherpa.Support;

/// <summary>
/// Turns machine stderr into calm, actionable advice — Sherpa's ConflictTranslator energy.
/// </summary>
public static class ConflictTranslator
{
    public static IReadOnlyList<ConflictAdvice> Translate(string output)
    {
        var text = output ?? "";
        var advice = new List<ConflictAdvice>();

        void Add(string title, string detail)
        {
            if (advice.Any(a => a.Title == title)) return;
            advice.Add(new ConflictAdvice { Title = title, Detail = detail });
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Add("Nothing to show", "The command produced no output. Check that PHP and Composer are available under Settings.");
            return advice;
        }

        var lower = text.ToLowerInvariant();

        if (lower.Contains("could not resolve") || lower.Contains("your requirements could not be resolved"))
            Add("Composer could not resolve dependencies",
                "A package version you asked for conflicts with something already locked. Try a looser constraint, or update related packages together.");

        if (lower.Contains("mutually exclusive") || lower.Contains("can only install one of"))
            Add("Mutually exclusive packages",
                "Composer found packages that replace each other. Remove one, or pick versions that don't conflict.");

        if (lower.Contains("requires php") || lower.Contains("php version"))
            Add("PHP version mismatch",
                "This package needs a newer (or older) PHP than this site is using. Switch PHP in Herd/Laragon, or pick another package version.");

        if (lower.Contains("not found") && lower.Contains("package"))
            Add("Package not found",
                "That name is not on Packagist, or the version constraint matches nothing. Check the vendor/name spelling.");

        if (lower.Contains("authentication required") || lower.Contains("401") || lower.Contains("bad credentials"))
            Add("Authentication failed",
                "Add or refresh your token under Settings → Hosts. Tokens stay in the Windows secret store, not in the project.");

        if (lower.Contains("403") || lower.Contains("forbidden"))
            Add("Access denied",
                "The token is valid but lacks permission for this action. Create a classic GitHub token with repo scope, or a Cloudflare token with Pages edit.");

        if (lower.Contains("cloud returned a web page") || (lower.Contains("<html") && lower.Contains("login")))
            Add("Got a login page instead of the API",
                "Re-copy the API token from the provider dashboard. Personal access tokens and session cookies are not the same thing.");

        if (lower.Contains("npm") && (lower.Contains("not found") || lower.Contains("not recognized")))
            Add("Node/npm not found",
                "This site has a frontend build script, but Node/npm was not found. Install Node or enable it in Herd/Laragon, then try again.");

        if (lower.Contains("not a git repository"))
            Add("Not a Git repository",
                "Initialize Git for this site first, or import a folder that already has a .git directory.");

        if (lower.Contains("composer could not find a composer.json"))
            Add("No composer.json here",
                "This folder doesn't look like a PHP app root. Point Sherpa at the directory that contains composer.json.");

        if (advice.Count == 0)
            Add("Command failed",
                "Read the log below for details. Use Copy errors if you want to paste it somewhere else.");

        return advice;
    }
}
