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

        if (OperatingSystem.IsWindows() && IsWindowsBatchFile(fileName))
        {
            // Best path: skip cmd entirely when we can resolve a real PE + script.
            // npm.cmd / npx.cmd → node.exe + npm-cli.js / npx-cli.js
            // foo.bat next to foo.exe → foo.exe
            if (TryResolveWindowsBatchTarget(fileName, argList, out var exe, out var resolvedArgs))
            {
                psi.FileName = exe;
                foreach (var a in resolvedArgs) psi.ArgumentList.Add(a);
            }
            else
            {
                // Fallback: cmd.exe with the nested-quote CreateProcess form.
                // IMPORTANT: use Arguments (one string), NOT ArgumentList — .NET's
                // ArgumentList re-quoting breaks cmd and produces:
                //   '"C:\Program Files\nodejs\npm.cmd"' is not recognized...
                // Form: cmd.exe /d /c ""C:\Path with spaces\app.cmd" arg1 arg2"
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = "/d /c " + BuildCmdCCommandLine(fileName, argList);
            }
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

    private static bool IsWindowsBatchFile(string fileName) =>
        fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve Windows batch shims to a real PE when possible so we never need cmd.exe.
    /// </summary>
    internal static bool TryResolveWindowsBatchTarget(
        string batchPath,
        IList<string> args,
        out string exe,
        out List<string> resolvedArgs)
    {
        exe = "";
        resolvedArgs = new List<string>();
        try
        {
            var dir = Path.GetDirectoryName(batchPath) ?? "";
            var leaf = Path.GetFileName(batchPath);

            // Node official Windows install: npm.cmd / npx.cmd → node + *-cli.js
            if (leaf.Equals("npm.cmd", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("npm.bat", StringComparison.OrdinalIgnoreCase))
            {
                var node = Path.Combine(dir, "node.exe");
                var cli = Path.Combine(dir, "node_modules", "npm", "bin", "npm-cli.js");
                if (File.Exists(node) && File.Exists(cli))
                {
                    exe = node;
                    resolvedArgs.Add(cli);
                    resolvedArgs.AddRange(args);
                    return true;
                }
            }

            if (leaf.Equals("npx.cmd", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("npx.bat", StringComparison.OrdinalIgnoreCase))
            {
                var node = Path.Combine(dir, "node.exe");
                var cli = Path.Combine(dir, "node_modules", "npm", "bin", "npx-cli.js");
                if (File.Exists(node) && File.Exists(cli))
                {
                    exe = node;
                    resolvedArgs.Add(cli);
                    resolvedArgs.AddRange(args);
                    return true;
                }
            }

            // Same-folder PE: php.bat → php.exe, composer.bat → composer.exe, etc.
            var stem = Path.GetFileNameWithoutExtension(batchPath);
            var siblingExe = Path.Combine(dir, stem + ".exe");
            if (File.Exists(siblingExe))
            {
                exe = siblingExe;
                resolvedArgs.AddRange(args);
                return true;
            }

            // Herd php.bat often lives in .config\herd\bin and points at a real php.exe elsewhere.
            // Peek at a short batch and pick the first existing .exe path token if obvious.
            if (leaf.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                || leaf.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                var target = TryParseExeFromBatch(batchPath);
                if (target is not null)
                {
                    exe = target;
                    resolvedArgs.AddRange(args);
                    return true;
                }
            }
        }
        catch
        {
            // fall through to cmd
        }

        return false;
    }

    private static string? TryParseExeFromBatch(string batchPath)
    {
        try
        {
            // Only read a little — these shims are tiny.
            var text = File.ReadAllText(batchPath);
            if (text.Length > 8000) text = text[..8000];

            // Match quoted or bare paths ending in .exe
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         text,
                         "\"([^\"]+\\.exe)\"|((?:[A-Za-z]:)?[^\\s\"&|<>^]+\\.exe)",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                raw = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                // Resolve %~dp0-style is hard; skip relative junk
                if (raw.Contains("%", StringComparison.Ordinal)) continue;
                if (File.Exists(raw)) return raw;

                // Relative to batch dir
                var dir = Path.GetDirectoryName(batchPath) ?? "";
                var combined = Path.GetFullPath(Path.Combine(dir, raw));
                if (File.Exists(combined)) return combined;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Build the command tail after <c>cmd /d /c</c>.
    /// Nested-quote form: <c>""C:\Program Files\app.cmd" arg1 arg2"</c>
    /// </summary>
    internal static string BuildCmdCCommandLine(string batchPath, IList<string> args)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        sb.Append(CmdQuote(batchPath));
        foreach (var a in args)
        {
            sb.Append(' ');
            sb.Append(CmdQuote(a));
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Quote one argv token for cmd.exe (double embedded quotes).</summary>
    internal static string CmdQuote(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        var needsQuote = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || c is '"' or '&' or '|' or '<' or '>' or '^' or '%' or '(' or ')')
            {
                needsQuote = true;
                break;
            }
        }

        if (!needsQuote)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
