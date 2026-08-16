using System;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Sherpa.Services;

public enum DefaultBrowserKind
{
    Generic,
    Chrome,
    Firefox,
    Edge,
}

/// <summary>
/// Detects the Windows default HTTP(S) browser so the toolbar can show a matching outline icon.
/// Recognizes Chrome, Firefox, and Edge; everything else uses a generic “open in browser” glyph.
/// </summary>
public static class DefaultBrowserDetector
{
    public static DefaultBrowserKind Detect()
    {
        if (!OperatingSystem.IsWindows())
            return DefaultBrowserKind.Generic;

        try
        {
            return DetectWindows();
        }
        catch
        {
            return DefaultBrowserKind.Generic;
        }
    }

    public static string DisplayName(DefaultBrowserKind kind) => kind switch
    {
        DefaultBrowserKind.Chrome => "Chrome",
        DefaultBrowserKind.Firefox => "Firefox",
        DefaultBrowserKind.Edge => "Edge",
        _ => "browser",
    };

    public static string OpenTooltip(DefaultBrowserKind kind) => kind switch
    {
        DefaultBrowserKind.Chrome => "Open in Chrome",
        DefaultBrowserKind.Firefox => "Open in Firefox",
        DefaultBrowserKind.Edge => "Open in Edge",
        _ => "Open in browser",
    };

    /// <summary>Monochrome outline Path geometry (24×24 viewBox) for the toolbar.</summary>
    public static string IconPathData(DefaultBrowserKind kind) => kind switch
    {
        DefaultBrowserKind.Chrome => IconChrome,
        DefaultBrowserKind.Firefox => IconFirefox,
        DefaultBrowserKind.Edge => IconEdge,
        _ => IconGeneric,
    };

    // Generic: arrow.up.right.circle (same energy as before)
    private const string IconGeneric =
        "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M10 14 L16 8 M11.5 8 H16 V12.5";

    // Chrome-ish: outer ring, center hub, three radial spokes (pie segments)
    private const string IconChrome =
        "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z " +
        "M12 8.2 A3.8 3.8 0 1 0 12 15.8 A3.8 3.8 0 1 0 12 8.2 Z " +
        "M12 3.5 V8.2 M18.9 15.9 L15.3 13.7 M5.1 15.9 L8.7 13.7";

    // Firefox-ish: globe ring + curved “fox” swoosh
    private const string IconFirefox =
        "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z " +
        "M7.2 13.5 C7.5 9.5, 10.2 7.2, 14.2 7 C16.8 6.9, 18.5 8.4, 18.8 10.8 " +
        "C19 12.8, 17.8 14.5, 15.8 15.2 C13.2 16.1, 10.2 15.4, 8.4 13.2 " +
        "M9.5 10.2 C11 8.8, 13.5 8.6, 15.2 9.8";

    // Edge-ish: open ring + wave / e-swoosh
    private const string IconEdge =
        "M19.2 12.2 C19 16.6, 15.8 20, 12 20 C7.9 20, 4.6 17, 4.6 12.5 " +
        "C4.6 8.6, 7.6 5.2, 12 4.2 C14.8 3.6, 17.6 4.6, 19 6.8 " +
        "M4.8 13.2 C6.2 11.2, 8.8 10, 12.2 10.2 C15.2 10.4, 17.4 11.6, 18.6 13.2 " +
        "M8.2 13.5 H17.2";

    [SupportedOSPlatform("windows")]
    private static DefaultBrowserKind DetectWindows()
    {
        // Prefer HTTPS association (what we open for *.test sites).
        var progId =
            ReadUserChoiceProgId("https")
            ?? ReadUserChoiceProgId("http")
            ?? ReadHtmlProgId();

        if (string.IsNullOrWhiteSpace(progId))
            return DefaultBrowserKind.Generic;

        return ClassifyProgId(progId);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadUserChoiceProgId(string scheme)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice");
            return key?.GetValue("ProgId") as string;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadHtmlProgId()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.html\UserChoice");
            return key?.GetValue("ProgId") as string;
        }
        catch
        {
            return null;
        }
    }

    private static DefaultBrowserKind ClassifyProgId(string progId)
    {
        var id = progId.Trim();
        var lower = id.ToLowerInvariant();

        // Chrome (stable / beta / dev / SxS)
        if (lower.Contains("chrome") && !lower.Contains("chromium"))
            return DefaultBrowserKind.Chrome;
        if (Regex.IsMatch(id, @"^Chrome(HTML|BHTML|DHTML|SSHTM)", RegexOptions.IgnoreCase))
            return DefaultBrowserKind.Chrome;

        // Microsoft Edge (Chromium + legacy)
        if (lower.Contains("msedge") || lower.Contains("edgehtm") || lower == "appxedg")
            return DefaultBrowserKind.Edge;
        if (Regex.IsMatch(id, @"^(MSEdgeHTM|MSEdgeHTML|AppX[0-9a-z]*Edge)", RegexOptions.IgnoreCase))
            return DefaultBrowserKind.Edge;
        // Windows 10/11 AppX Edge often looks like AppX…microsoftedge…
        if (lower.Contains("microsoftedge") || lower.Contains("microsoft.microsoftedge"))
            return DefaultBrowserKind.Edge;

        // Firefox
        if (lower.Contains("firefox"))
            return DefaultBrowserKind.Firefox;
        if (Regex.IsMatch(id, @"^Firefox(URL|HTML)", RegexOptions.IgnoreCase))
            return DefaultBrowserKind.Firefox;

        // Optional: resolve ProgId → command line and sniff exe name
        if (OperatingSystem.IsWindows())
        {
            var exe = TryGetOpenCommand(id);
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var e = exe.ToLowerInvariant();
                if (e.Contains("chrome.exe")) return DefaultBrowserKind.Chrome;
                if (e.Contains("firefox.exe")) return DefaultBrowserKind.Firefox;
                if (e.Contains("msedge.exe")) return DefaultBrowserKind.Edge;
            }
        }

        return DefaultBrowserKind.Generic;
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetOpenCommand(string progId)
    {
        try
        {
            // HKCR is HKLM\Software\Classes merged view
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
            var cmd = key?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(cmd)) return null;

            // "C:\…\chrome.exe" -- "%1"  → chrome.exe path
            cmd = cmd.Trim();
            if (cmd.StartsWith('"'))
            {
                var end = cmd.IndexOf('"', 1);
                if (end > 1) return cmd.Substring(1, end - 1);
            }

            var space = cmd.IndexOf(' ');
            return space > 0 ? cmd[..space] : cmd;
        }
        catch
        {
            return null;
        }
    }
}
