using Sherpa.Clients;
using Sherpa.Services;

namespace Sherpa;

/// <summary>Composition root — Views receive this, not raw HTTP clients.</summary>
public sealed class AppServices
{
    public ProcessRunner Processes { get; } = new();
    public SecretStore Secrets { get; } = new();
    public SiteStore Sites { get; } = new();
    public PreferencesStore Preferences { get; } = new();
    public RuntimeManager Runtime { get; }
    public ComposerService Composer { get; }
    public GitService Git { get; }
    public SiteCommandsService Commands { get; }
    public InstallCoordinator Install { get; }
    public NotificationService Notifications { get; } = new();
    public GitHubClient GitHub { get; } = new();
    public CloudflarePagesClient Cloudflare { get; } = new();
    public PackagistClient Packagist { get; } = new();

    public AppServices()
    {
        Runtime = new RuntimeManager(Preferences);
        Composer = new ComposerService(Processes, Runtime);
        Git = new GitService(Processes, Runtime);
        Commands = new SiteCommandsService(Processes, Runtime);
        Install = new InstallCoordinator(Processes, Runtime, Composer);
    }
}
