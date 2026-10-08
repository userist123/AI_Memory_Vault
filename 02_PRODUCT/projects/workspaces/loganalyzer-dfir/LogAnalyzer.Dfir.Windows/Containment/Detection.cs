using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Containment;

public sealed record SuspectProcess(int Pid, string Name, string Path, IReadOnlyList<string> Reasons, IReadOnlyList<string> RemoteEndpoints, bool HighConfidence,
                                    bool Trusted = false)
{
    /// <summary>Only these may be contained without asking: high confidence and not vouched for by the operator.</summary>
    public bool EligibleForAutoContainment => HighConfidence && !Trusted;
}

/// <summary>
/// Finds third-party processes worth containing. The pattern is the one seen in the NanAgent case: an unsigned
/// program, running from a location any user can write to, talking to the Internet. A process is
/// <see cref="SuspectProcess.HighConfidence"/> only when all three hold; that is the only case automatic
/// containment may act on.
/// </summary>
public static class SuspiciousProcessDetector
{
    private static readonly string[] UserWritableRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        System.IO.Path.GetTempPath(),
        Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public",
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
    ];

    public static bool IsUserWritableLocation(string path) =>
        !string.IsNullOrEmpty(path) &&
        UserWritableRoots.Where(r => !string.IsNullOrEmpty(r))
                         .Any(r => path.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    public static bool IsPrivateOrLocal(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return true;
        if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any)) return true;
        if (a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6UniqueLocal) return true;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        var b = a.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) ||
                                 (b[0] == 169 && b[1] == 254) || b[0] == 127 || b[0] >= 224);
    }

    /// <summary>Scans running processes; never touches them.</summary>
    public static IReadOnlyList<SuspectProcess> Scan(TrustedProgramStore? trusted = null)
    {
        var byPid = TcpTable.All().Where(c => c.State != "LISTEN" && !IsPrivateOrLocal(c.Remote.Address.ToString()))
                                  .GroupBy(c => c.Pid).ToDictionary(g => g.Key, g => g.Select(c => $"{c.Remote.Address}:{c.Remote.Port}").Distinct().ToList());
        var result = new List<SuspectProcess>();
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == self || p.Id <= 4) continue;
                string path;
                try { path = p.MainModule?.FileName ?? ""; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { continue; }
                if (path.Length == 0) continue;

                var reasons = new List<string>();
                bool writable = IsUserWritableLocation(path);
                if (writable) reasons.Add("locație în care orice utilizator poate scrie");
                byPid.TryGetValue(p.Id, out var remotes);
                bool internet = remotes is { Count: > 0 };
                if (internet) reasons.Add("conexiuni către Internet");
                if (!writable && !internet) continue; // keep the expensive signature check for candidates

                var sig = Authenticode.Verify(path);
                bool unsigned = !sig.IsSigned || !sig.IsValid;
                if (unsigned) reasons.Add(sig.IsSigned ? $"semnătură invalidă ({sig.Status})" : "fără semnătură digitală");
                if (!unsigned && !(writable && internet)) continue;

                bool isTrusted = trusted?.IsTrusted(path) == true;
                if (isTrusted) reasons.Add("marcat de operator ca program de încredere (SHA-256)");
                result.Add(new SuspectProcess(p.Id, p.ProcessName, path, reasons, remotes ?? [], unsigned && writable && internet, isTrusted));
            }
        }
        return result.OrderByDescending(s => s.EligibleForAutoContainment).ThenByDescending(s => s.HighConfidence).ThenByDescending(s => s.Reasons.Count).ToList();
    }
}

public sealed record BlockedConnection(DateTimeOffset TimeUtc, string Application, string Direction, string SourceAddress, int? SourcePort,
                                       string DestAddress, int? DestPort, string Protocol, long RecordId);

/// <summary>
/// Reads Security event 5157 ("The Windows Filtering Platform has blocked a connection"). After containment these
/// are the attempts of the isolated program: where it tried to go. Windows writes them only while auditing of
/// "Filtering Platform Connection" failures is enabled.
/// </summary>
public static class BlockedConnectionLog
{
    public const string FilteringPlatformConnectionGuid = "{0CCE9226-69AE-11D9-BED3-505054503030}";

    /// <summary>Reads the effective policy in process (AuditQuerySystemPolicy): no auditpol.exe, no localized text to parse.</summary>
    public static (bool Enabled, string Detail) IsFailureAuditEnabled()
    {
        try
        {
            var flags = LogAnalyzer.Dfir.Windows.Native.AuditPolicy.QueryFlags(Guid.Parse(FilteringPlatformConnectionGuid));
            var setting = (LogAnalyzer.Dfir.Windows.Native.AuditSetting)(flags & 3);
            return ((setting & LogAnalyzer.Dfir.Windows.Native.AuditSetting.Failure) != 0, setting == LogAnalyzer.Dfir.Windows.Native.AuditSetting.None ? "No Auditing" : setting.ToString());
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (false, $"politica de audit nu poate fi citită ({ex.NativeErrorCode}: {ex.Message}); este necesar un cont de administrator");
        }
    }

    public static IReadOnlyList<BlockedConnection> ForProgram(string programPath, DateTimeOffset sinceUtc, int max = 2000)
    {
        var suffix = DeviceIndependentSuffix(programPath);
        var since = sinceUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var query = new EventLogQuery("Security", PathType.LogName,
            $"*[System[(EventID=5157) and TimeCreated[@SystemTime>='{since}']]]") { ReverseDirection = true };
        var list = new List<BlockedConnection>();
        using var reader = new EventLogReader(query);
        for (EventRecord? r = reader.ReadEvent(); r is not null && list.Count < max; r = reader.ReadEvent())
        {
            using (r)
            {
                var d = r.Properties.Select(p => p.Value?.ToString() ?? "").ToList();
                // 5157 data: ProcessID, Application, Direction, SourceAddress, SourcePort, DestAddress, DestPort, Protocol, ...
                if (d.Count < 8) continue;
                if (!d[1].EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new BlockedConnection(r.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue, d[1], DirectionName(d[2]),
                    d[3], int.TryParse(d[4], out var sp) ? sp : null, d[5], int.TryParse(d[6], out var dp) ? dp : null,
                    ProtocolName(d[7]), r.RecordId ?? 0));
            }
        }
        return list;
    }

    /// <summary>5157 logs device paths (\device\harddiskvolume3\...). Compare on the part after the drive.</summary>
    public static string DeviceIndependentSuffix(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(full) ?? "";
        return "\\" + full[root.Length..];
    }

    private static string DirectionName(string v) => v switch { "%%14592" => "inbound", "%%14593" => "outbound", _ => v };
    private static string ProtocolName(string v) => v switch { "6" => "TCP", "17" => "UDP", "1" => "ICMP", "58" => "ICMPv6", _ => v };

    public static NetworkIntent ToIntent(BlockedConnection b) =>
        new(b.Direction == "outbound" ? "conexiune blocată (ieșire)" : "conexiune blocată (intrare)",
            b.Direction == "outbound" ? b.DestAddress : b.SourceAddress,
            b.Direction == "outbound" ? b.DestPort : b.SourcePort,
            b.Protocol, $"Security 5157 (RecordID {b.RecordId})",
            Timestamp.FromUtc(b.TimeUtc.UtcDateTime, b.TimeUtc.ToString("o"), "event 5157 TimeCreated"),
            "Încercare oprită de regula de izolare");
}
