using Sherpa.Clients;
using Sherpa.Services;

namespace Sherpa;

/// <summary>Composition root — mirrors Mac AppServices.</summary>
public sealed class AppServices
{
    public ProcessRunner Processes { get; } = new();
    public SecretStore Secrets { get; } = new();
    public SiteStore Sites { get; } = new();
    public PreferencesStore Preferences { get; } = new();
    public RuntimeManager Runtime { get; }
    public HerdService Herd { get; }
    public ComposerService Composer { get; }
    public GitService Git { get; }
    public SiteCommandsService Commands { get; }
    public InstallCoordinator Install { get; }
    public NotificationService Notifications { get; } = new();
    public UpdateService Updates { get; } = new();
    public GitHubClient GitHub { get; } = new();
    public CloudflarePagesClient Cloudflare { get; } = new();
    public PackagistClient Packagist { get; } = new();
    public MarketplaceClient Marketplace { get; } = new();
    public ForgeClient Forge { get; } = new();
    public NetlifyClient Netlify { get; } = new();
    public LaravelCloudClient LaravelCloud { get; } = new();
    public StaticPublishService StaticPublish { get; }

    public AppServices()
    {
        Runtime = new RuntimeManager(Preferences);
        Herd = new HerdService(Processes);
        Composer = new ComposerService(Processes, Runtime);
        Git = new GitService(Processes, Runtime);
        Commands = new SiteCommandsService(Processes, Runtime);
        Install = new InstallCoordinator(Processes, Runtime, Herd, Git, Commands);
        StaticPublish = new StaticPublishService(Processes, Runtime, Cloudflare, Secrets);
    }
}
