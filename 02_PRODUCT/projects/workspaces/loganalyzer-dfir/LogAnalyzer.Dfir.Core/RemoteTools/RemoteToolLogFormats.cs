using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.RemoteTools;

/// <summary>
/// Names and content tests of the remote-tool session logs: one place for the evidence fingerprint (<c>EvidenceFingerprint.Detect</c>), the import type and the
/// collectors. A file is typed only when BOTH its exact name and its first 8 KiB fit the documented format, so a file of another kind with the same name stays "file".
/// </summary>
public static class RemoteToolLogFormats
{
    public const string AnyDeskConnectionTrace = "anydesk_connection_trace";
    public const string AnyDeskTrace = "anydesk_trace";
    public const string TeamViewerConnections = "teamviewer_connections";
    public const int HeadBytes = 8 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);
    internal static readonly Regex ConnectionTraceRow = new(@"^\s*(?<dir>Incoming|Outgoing)\s+(?<dt>\d{4}-\d{2}-\d{2},?\s+\d{2}:\d{2}(?::\d{2})?)(?<rest>.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);
    internal static readonly Regex TraceRow = new(@"^\s*(?<lvl>info|warning|warn|error|fatal|debug)\s+(?<d>\d{4}-\d{2}-\d{2})\s+(?<t>\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)\s+(?<proc>\S+)\s+(?<pid>\d+)\s+(?<tid>\d+)\s+(?<n>\d+)\s+(?<mod>\S+)\s+-\s+(?<msg>.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);
    internal static readonly Regex TeamViewerTime = new(@"^\d{2}-\d{2}-\d{4} \d{2}:\d{2}:\d{2}$", RegexOptions.CultureInvariant, Timeout);

    /// <summary>
    /// The format a file NAME can have, or null. The case workspace renames a second file of the same name "name_1.ext", "name_2.ext" when it stores it: that numeric
    /// suffix on the stem is ignored, so a stored copy keeps its format.
    /// </summary>
    public static string? ByName(string fileName) => StripCopySuffix(fileName.ToLowerInvariant()) switch
    {
        "connection_trace.txt" => AnyDeskConnectionTrace,
        "ad.trace" or "ad_svc.trace" => AnyDeskTrace,
        "connections_incoming.txt" or "connections.txt" => TeamViewerConnections,
        _ => null,
    };

    private static string StripCopySuffix(string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0) return name;
        var stem = name[..dot];
        int us = stem.LastIndexOf('_');
        if (us > 0 && us < stem.Length - 1 && stem.Length - us - 1 <= 6 && stem[(us + 1)..].All(char.IsAsciiDigit)) stem = stem[..us];
        return stem + name[dot..];
    }

    public static bool IsKnown(string type) => type is AnyDeskConnectionTrace or AnyDeskTrace or TeamViewerConnections;

    /// <summary>The format of the file (name and content), or null. Never throws on a file that cannot be read: null.</summary>
    public static string? Detect(string path)
    {
        var kind = ByName(Path.GetFileName(path));
        if (kind is null) return null;
        try
        {
            var buf = new byte[HeadBytes];
            int n;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            if (n == 0) return null;
            var span = buf.AsSpan(0, n);
            var enc = RemoteToolLogText.SniffFallback(span);
            string text;
            if (n >= 2 && span[0] == 0xFF && span[1] == 0xFE) text = Encoding.Unicode.GetString(buf, 2, (n - 2) & ~1);
            else if (n >= 2 && span[0] == 0xFE && span[1] == 0xFF) text = Encoding.BigEndianUnicode.GetString(buf, 2, (n - 2) & ~1);
            else if (n >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) text = Encoding.UTF8.GetString(buf, 3, n - 3);
            else text = enc.GetString(buf, 0, enc is UnicodeEncoding ? n & ~1 : n);
            var lines = text.Split('\n');
            // the last line may be cut by the 8 KiB window
            int count = lines.Length > 1 && n == HeadBytes ? lines.Length - 1 : lines.Length;
            for (int i = 0; i < count; i++)
            {
                var l = lines[i].TrimEnd('\r').TrimStart('\uFEFF');
                if (l.Length == 0 || l.Length > 4096) continue;
                if (kind == AnyDeskConnectionTrace && ConnectionTraceRow.IsMatch(l)) return kind;
                if (kind == AnyDeskTrace && TraceRow.IsMatch(l)) return kind;
                if (kind == TeamViewerConnections)
                {
                    var c = l.Split('\t');
                    if (c.Length >= 4 && TeamViewerTime.IsMatch(c[2].Trim())) return kind;
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException) { return null; }
    }
}
