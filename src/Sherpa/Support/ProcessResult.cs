namespace Sherpa.Support;

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public string Combined => string.Join("\n", new[] { StdOut, StdErr }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool Success => ExitCode == 0;
}
