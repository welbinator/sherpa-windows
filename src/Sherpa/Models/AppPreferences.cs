namespace Sherpa.Models;

public sealed class AppPreferences
{
    public string DefaultSitesFolder { get; set; } = "";
    public string? PreferredPhpPath { get; set; }
    public string? PreferredComposerPath { get; set; }
    public string? PreferredGitPath { get; set; }
    public bool PreferHerdForNewSites { get; set; } = true;
    public bool SecureNewHerdSitesWithHttps { get; set; } = true;
    public string GitUserName { get; set; } = "";
    public string GitUserEmail { get; set; } = "";
    public List<HostAccount> Hosts { get; set; } = new();
}
