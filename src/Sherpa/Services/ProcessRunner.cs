using System.Diagnostics;
using System.Text;
using Sherpa.Support;

namespace Sherpa.Services;

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDirectory = null,
        IDictionary<string, string?>? env = null,
        Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        var argList = args as IList<string> ?? args.ToList();
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // .cmd/.bat cannot be CreateProcess'd directly with UseShellExecute=false.
        // Route through cmd.exe (also covers npm.cmd / npx.cmd / composer.bat / herd.bat).
        if (OperatingSystem.IsWindows() && IsWindowsBatchFile(fileName))
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/c");
            // One /c argument: quoted batch path + remaining args (cmd parsing rules).
            psi.ArgumentList.Add(BuildCmdCArgument(fileName, argList));
        }
        else
        {
            psi.FileName = fileName;
            foreach (var a in argList) psi.ArgumentList.Add(a);
        }

        if (env != null)
        {
            foreach (var kv in env)
            {
                if (kv.Value is null) psi.Environment.Remove(kv.Key);
                else psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        return new ProcessResult
        {
            ExitCode = proc.ExitCode,
            StdOut = stdout.ToString().TrimEnd(),
            StdErr = stderr.ToString().TrimEnd(),
        };
    }

    private static bool IsWindowsBatchFile(string fileName)
    {
        return fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Build the single string passed to <c>cmd /c</c>, with proper quoting.
    /// </summary>
    private static string BuildCmdCArgument(string batchPath, IList<string> args)
    {
        var sb = new StringBuilder();
        sb.Append('"').Append(batchPath).Append('"');
        foreach (var a in args)
        {
            sb.Append(' ');
            if (a.Length == 0)
            {
                sb.Append("\"\"");
                continue;
            }

            // Quote when needed (spaces, quotes, special cmd chars).
            var needsQuote = a.Any(c => char.IsWhiteSpace(c) || c is '"' or '&' or '|' or '<' or '>' or '^' or '%');
            if (!needsQuote)
            {
                sb.Append(a);
                continue;
            }

            sb.Append('"');
            sb.Append(a.Replace("\"", "\\\""));
            sb.Append('"');
        }

        return sb.ToString();
    }
}
