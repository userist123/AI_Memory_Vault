using System.IO;
using System.Threading;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;

namespace LogAnalyzer.App.Tests;

/// <summary>Shared helpers of the F2/F3/F4/F10/E1/E5/E10/G3 tests: a temporary folder, a case with a complete scope, synthetic timeline rows. No real names.</summary>
internal sealed class GapsTestKit : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "la-gaps-" + Guid.NewGuid().ToString("N"));
    public GapsTestKit() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories)) System.IO.File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static CaseScope Scope() => new()
    {
        Purpose = "test", PeriodFromUtc = DateTimeOffset.UtcNow.AddDays(-1), PeriodToUtc = DateTimeOffset.UtcNow.AddDays(1), SystemsInScope = ["H"], Approver = "a",
        LegalBasis = LegalBasis.Incident, Network = NetworkCategory.StandalonePc, Classification = ClassificationLevel.Unclassified,
    };

    public string File(string relative, string content)
    {
        var p = Path.Combine(Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    /// <summary>A case with <paramref name="evidence"/> files imported (copies; hashes recorded at acquisition).</summary>
    public CaseWorkspace Case(params string[] evidence)
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(Dir, "cases", Guid.NewGuid().ToString("N")[..6]), "caz test", Scope());
        if (evidence.Length > 0) InvestigationPipeline.Import(ws, evidence);
        return ws;
    }

    /// <summary>Rewrites a stored evidence file (clears the read-only flag first).</summary>
    public static void Tamper(CaseWorkspace ws, EvidenceItem e, string content = "changed after acquisition")
    {
        var full = ws.FullPath(e.StoredPath);
        System.IO.File.SetAttributes(full, FileAttributes.Normal);
        System.IO.File.WriteAllText(full, content);
    }

    public static readonly DateTimeOffset T0 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static int _n;

    public static TimelineEvent Ev(string source, double minute, string eventId = "", string evidence = "EV-T", string path = "", string user = "", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0.AddMinutes(minute).UtcDateTime, "", "test"), Source = source, EventId = eventId, EvidenceId = evidence, Path = path, User = user,
        Locator = $"row={Interlocked.Increment(ref _n)}", Summary = $"{source} {eventId} {path}".Trim(),
        Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    public const string Serial = "AA0099";

    public static TimelineEvent Partition1006(double minute, string evidence = "EV-T") =>
        Ev("EventLog:Microsoft-Windows-Partition/Diagnostic", minute, "1006", evidence, f: [("SerialNumber", Serial), ("BusType", "7"), ("Capacity", "32000000000"), ("Manufacturer", "Vendor"), ("Model", "Stick")]);

    public static TimelineEvent UsbRow(double minute, string letter = "E", string evidence = "EV-T") =>
        Ev("USB", minute, evidence: evidence, f: [("Serial", Serial), ("VidPid", "VID_0001&PID_0002"), ("FriendlyName", "Vendor Stick"), ("DriveLetter", letter)]);

    public static TimelineEvent Logon(string id, string user, double minute, bool on = true) =>
        Ev("EventLog:Security", minute, on ? "4624" : "4634", f: [("TargetLogonId", id), ("TargetUserName", user), ("LogonType", "2")]);
}
