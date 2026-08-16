namespace Sherpa.Models;

public sealed class Site
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Url { get; set; }
    public SiteKind Kind { get; set; } = SiteKind.Unknown;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? PhpBinaryHint { get; set; }
    public List<DeploymentRecord> Deployments { get; set; } = new();
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
}
