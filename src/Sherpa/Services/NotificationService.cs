namespace Sherpa.Services;

/// <summary>
/// Best-effort user notification. Avalonia has no tray toast on all platforms;
/// we expose a callback the UI binds to (and later Win toast can plug in).
/// </summary>
public sealed class NotificationService
{
    public event Action<string, string>? Raised;

    public void Notify(string title, string body)
        => Raised?.Invoke(title, body);
}
