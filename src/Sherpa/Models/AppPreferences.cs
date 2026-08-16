namespace Sherpa.Models;

public sealed class AppPreferences
{
    public string DefaultSitesFolder { get; set; } = "";
    public string? PreferredPhpPath { get; set; }
    public string? PreferredComposerPath { get; set; }
    public string? PreferredGitPath { get; set; }
    public List<HostAccount> Hosts { get; set; } = new();
}
