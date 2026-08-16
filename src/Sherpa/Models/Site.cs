namespace Sherpa.Models;

public sealed class Site
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Url { get; set; }
    public SiteKind Kind { get; set; } = SiteKind.Unknown;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool ParkedInHerd { get; set; }
    public bool Https { get; set; }
    public string StartingPoint { get; set; } = "blank"; // blank | fresh-statamic | kit
    public List<DeploymentRecord> Deployments { get; set; } = new();

    /// <summary>Last Cloudflare Pages project name used for static publish.</summary>
    public string? CloudflarePagesProject { get; set; }

    /// <summary>Public production URL after static publish (e.g. https://name.pages.dev).</summary>
    public string? ProductionUrl { get; set; }
}

public enum SiteKind
{
    Unknown,
    Statamic,
    Laravel,
    OtherPhp
}

public sealed class DeploymentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Host { get; set; } = "";
    public string Status { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Url { get; set; }
}

public sealed class GitFileRow
{
    public string Path { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Selected { get; set; } = true;
}
