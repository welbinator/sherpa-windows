namespace Sherpa.Models;

public enum HostProviderKind
{
    GitHub,
    Forge,
    LaravelCloud,
    CloudflarePages,
    Netlify
}

public sealed class HostAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public HostProviderKind Provider { get; set; }
    public string Label { get; set; } = "";
    /// <summary>Key into secret store — never the raw token.</summary>
    public string SecretKey { get; set; } = "";
    public string? Extra { get; set; } // e.g. Cloudflare account id
}
