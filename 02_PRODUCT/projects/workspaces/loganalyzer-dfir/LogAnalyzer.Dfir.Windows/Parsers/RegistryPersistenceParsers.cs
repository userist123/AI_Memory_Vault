using System.Buffers.Binary;
using System.Globalization;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.Windows.Parsers;

/// <summary>
/// Shared helpers for hive parsers that read with <see cref="RawRegistry"/>: value names exactly as stored, REG_EXPAND_SZ
/// not expanded on the analysis machine, big-data values complete (DiscUtils fails on all three).
/// </summary>
internal static class Hive
{
    public static Timestamp KeyTime(RawKey k) =>
        Timestamp.FromUtc(k.LastWriteUtc, k.LastWriteUtc.ToString("o", CultureInfo.InvariantCulture), "registry key LastWriteTime");

    /// <summary>One event per string value of a Run-type key.</summary>
    public static void RunKey(RawRegistry reg, string hiveLabel, string keyPath, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        var k = reg.OpenKey(keyPath);
        if (k is null) return;
        foreach (var v in k.Values)
        {
            var command = v.AsText;
            var exe = CommandLine.Executable(command);
            sink.Add(new TimelineEvent
            {
                Time = KeyTime(k), TimeSemantics = "Run key last written (not this value's creation)",
                Source = "RunKey", EvidenceId = item.EvidenceId, Path = exe, Process = Path.GetFileName(exe),
                Summary = $"Pornire automată ({hiveLabel}\\{keyPath}): {v.Name} = {command}",
                TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = $@"{hiveLabel}\{keyPath}\{v.Name}",
                Fields = { ["Key"] = keyPath, ["ValueName"] = v.Name, ["Command"] = command, ["Hive"] = hiveLabel, ["ValueType"] = v.Type.ToString(CultureInfo.InvariantCulture) },
            });
            result.Records++;
        }
    }
}

/// <summary>
/// User hive (NTUSER.DAT): UserAssist (programs started from Explorer / Start menu, with run count, focus time and last
/// run) and the user's Run / RunOnce keys.
/// </summary>
public sealed class UserHiveParser : EvidenceParserBase
{
    private const string UserAssistKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";

    /// <summary>Known-folder GUIDs that prefix UserAssist paths.</summary>
    private static readonly Dictionary<string, string> KnownFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = @"%SystemRoot%\System32",
        ["{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}"] = @"%SystemRoot%\SysWOW64",
        ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = "%SystemRoot%",
        ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = "%ProgramFiles%",
        ["{905E63B6-C1BF-494E-B29C-65B732D3D21A}"] = "%ProgramFiles%",
        ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = "%ProgramFiles(x86)%",
        ["{0139D44E-6AFE-49F2-8690-3DAFCAE6FFB8}"] = @"%ProgramData%\Microsoft\Windows\Start Menu\Programs",
        ["{A77F5D77-2E2B-44C3-A6A2-ABA601054A51}"] = @"%AppData%\Microsoft\Windows\Start Menu\Programs",
        ["{9E3995AB-1F9C-4F13-B827-48B24B6C7174}"] = @"%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned",
        ["{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}"] = "%LocalAppData%",
        ["{374DE290-123F-4565-9164-39C4925E467B}"] = @"%UserProfile%\Downloads",
    };

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "UserHiveParser", Version = "1.0", Artifact = "Hive utilizator (NTUSER.DAT) — UserAssist și cheile Run/RunOnce",
        SourceTypes = ["ntuser_hive"], FileNames = ["NTUSER.DAT", "NTUSER*.hiv", "HKCU.hiv"], Fingerprints = ["regf"],
        SupportedOs = "Oricare (citire offline cu RawRegistry)",
        FormatVersions = ["UserAssist versiunea 5 (Windows 7 – 11, 72 de octeți) și versiunea 3 (XP, 16 octeți)", "Run / RunOnce (REG_SZ, REG_EXPAND_SZ neexpandat)"],
        Limitations =
        [
            "UserAssist înregistrează doar programele pornite din interfața grafică (Explorer, meniul Start), nu din linia de comandă.",
            "Ora unei valori Run este LastWriteTime al cheii, comună tuturor valorilor din cheie.",
            "SID-ul proprietarului nu este stocat în hive; se deduce din locul de unde a fost copiat.",
            "Jurnalele de tranzacții ale hive-ului nu sunt aplicate.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "RegistryPersistenceParserTests: NTUSER real din corpus comparat cu reg export (UserAssist: număr de rulări și FILETIME; Run/RunOnce: comenzi) și hive sintetic",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reg = new RawRegistry(fs);
        var ua = reg.OpenKey(UserAssistKey);
        if (ua is null)
            result.Gaps.Add(new EvidenceGap("UserAssist", EvidenceStatus.NotAvailable, "cheia UserAssist lipsește din hive",
                "Nu se știe ce programe a pornit utilizatorul din interfață", "Prefetch, BAM, Amcache", "Nu"));
        else
            foreach (var guid in ua.SubkeyNames)
            {
                ct.ThrowIfCancellationRequested();
                var keyPath = $@"{UserAssistKey}\{guid}\Count";
                var count = reg.OpenKey(keyPath);
                if (count is null) continue;
                foreach (var v in count.Values)
                {
                    var name = Rot13(v.Name);
                    if (name.StartsWith("UEME_CTL", StringComparison.Ordinal)) continue;   // session/counter bookkeeping, not a program
                    var d = v.Data;
                    if (d.Length != 16 && d.Length < 68) { result.MalformedRecords++; continue; }
                    bool v5 = d.Length >= 68;
                    int runs = v5 ? BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(4)) : Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(4)) - 5);
                    long ft = BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(v5 ? 60 : 8));
                    var path = Resolve(name);
                    sink.Add(new TimelineEvent
                    {
                        Time = Timestamp.FromFileTime(ft, "UserAssist FILETIME"),
                        TimeSemantics = "last run from Explorer / Start menu (GUI, UserAssist)",
                        Source = "UserAssist", EvidenceId = item.EvidenceId, Path = path, Process = SafeFileName(path),
                        Summary = $"UserAssist: {path} pornit din interfață de {runs} ori",
                        TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                        Locator = $@"NTUSER\{keyPath}\{v.Name}",
                        Fields =
                        {
                            ["DecodedName"] = name, ["Guid"] = guid, ["RunCount"] = runs.ToString(CultureInfo.InvariantCulture),
                            ["FocusCount"] = v5 ? BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(8)).ToString(CultureInfo.InvariantCulture) : "",
                            ["FocusTimeMs"] = v5 ? BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(12)).ToString(CultureInfo.InvariantCulture) : "",
                            ["LastRunFileTime"] = ft.ToString(CultureInfo.InvariantCulture), ["Format"] = v5 ? "v5" : "v3",
                        },
                    });
                    result.Records++;
                }
            }

        foreach (var key in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run" })
            Hive.RunKey(reg, "NTUSER", key, item, sink, result);
    }

    public static string Rot13(string s) => string.Create(s.Length, s, (span, src) =>
    {
        for (int i = 0; i < src.Length; i++)
            span[i] = src[i] switch
            {
                >= 'a' and <= 'z' => (char)('a' + (src[i] - 'a' + 13) % 26),
                >= 'A' and <= 'Z' => (char)('A' + (src[i] - 'A' + 13) % 26),
                var c => c,
            };
    });

    /// <summary>File name of a path that may contain characters invalid in file names (malformed UserAssist names exist).</summary>
    private static string SafeFileName(string path)
    {
        int i = path.LastIndexOfAny(['\\', '/']);
        return i >= 0 ? path[(i + 1)..] : path;
    }

    private static string Resolve(string name)
    {
        if (name.Length > 38 && name[0] == '{' && name[37] == '}' && KnownFolders.TryGetValue(name[..38], out var folder))
            return folder + name[38..];
        return name;
    }
}

/// <summary>SOFTWARE hive: machine Run / RunOnce (64- and 32-bit views), Winlogon Shell / Userinit and IFEO debuggers.</summary>
public sealed class SoftwareHiveParser : EvidenceParserBase
{
    private const string Winlogon = @"Microsoft\Windows NT\CurrentVersion\Winlogon";
    private static readonly string[] Ifeo = [@"Microsoft\Windows NT\CurrentVersion\Image File Execution Options", @"WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"];

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "SoftwareHiveParser", Version = "1.0", Artifact = "Hive SOFTWARE — Run/RunOnce, Winlogon, IFEO Debugger",
        SourceTypes = ["software_hive"], FileNames = ["SOFTWARE", "SOFTWARE.hiv", "*_SOFTWARE.hiv"], Fingerprints = ["regf"],
        SupportedOs = "Oricare (citire offline cu RawRegistry)",
        FormatVersions = ["Run / RunOnce (inclusiv WOW6432Node și Policies\\Explorer\\Run)", "Winlogon Shell / Userinit", "Image File Execution Options\\*\\Debugger"],
        Limitations =
        [
            "Alte locuri de persistență din SOFTWARE (AppInit_DLLs, Active Setup, extensii shell, SilentProcessExit) nu sunt încă citite.",
            "Ora este LastWriteTime al cheii, comună tuturor valorilor.",
            "Jurnalele de tranzacții ale hive-ului nu sunt aplicate.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "RegistryPersistenceParserTests: SOFTWARE real din corpus comparat cu reg export (Run, RunOnce, WOW6432Node, Winlogon) și hive sintetic",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reg = new RawRegistry(fs);
        foreach (var key in new[] { @"Microsoft\Windows\CurrentVersion\Run", @"Microsoft\Windows\CurrentVersion\RunOnce",
                                    @"WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
                                    @"Microsoft\Windows\CurrentVersion\Policies\Explorer\Run" })
            Hive.RunKey(reg, "SOFTWARE", key, item, sink, result);

        var wl = reg.OpenKey(Winlogon);
        if (wl is null)
            result.Gaps.Add(new EvidenceGap("Winlogon", EvidenceStatus.NotAvailable, "cheia Winlogon lipsește", "Shell/Userinit necunoscute", "reg query pe stație", "Nu"));
        else
            foreach (var name in new[] { "Shell", "Userinit" })
            {
                var value = wl.Value(name)?.AsText ?? "";
                if (value.Length == 0) continue;
                bool nonDefault = name == "Shell" ? !value.Trim().Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) : !IsDefaultUserinit(value);
                var parts = name == "Userinit" ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [value];
                sink.Add(new TimelineEvent
                {
                    Time = Hive.KeyTime(wl), TimeSemantics = "Winlogon key last written",
                    Source = "Winlogon", EvidenceId = item.EvidenceId, Path = CommandLine.Executable(parts.LastOrDefault() ?? value),
                    Summary = $"Winlogon {name} = {value}" + (nonDefault ? " (diferit de valoarea implicită)" : ""),
                    TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                    Locator = $@"SOFTWARE\{Winlogon}\{name}",
                    Fields = { ["ValueName"] = name, ["Command"] = value, ["NonDefault"] = nonDefault ? "true" : "false", ["Key"] = Winlogon },
                });
                result.Records++;
            }

        foreach (var ifeoPath in Ifeo)
        {
            var ifeo = reg.OpenKey(ifeoPath);
            if (ifeo is null) continue;
            foreach (var target in ifeo.SubkeyNames)
            {
                ct.ThrowIfCancellationRequested();
                var k = reg.OpenKey($@"{ifeoPath}\{target}");
                var debugger = k?.Value("Debugger")?.AsText ?? "";
                if (debugger.Length == 0) continue;
                var exe = CommandLine.Executable(debugger);
                sink.Add(new TimelineEvent
                {
                    Time = Hive.KeyTime(k!), TimeSemantics = "IFEO subkey last written",
                    Source = "IFEO", EvidenceId = item.EvidenceId, Path = exe, Process = Path.GetFileName(exe),
                    Summary = $"IFEO: la pornirea {target} rulează în schimb {debugger}",
                    TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                    Locator = $@"SOFTWARE\{ifeoPath}\{target}\Debugger",
                    Fields = { ["Target"] = target, ["Command"] = debugger, ["Key"] = $@"{ifeoPath}\{target}" },
                });
                result.Records++;
            }
        }
    }

    private static bool IsDefaultUserinit(string v)
    {
        var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 1 && (parts[0].Equals(@"C:\Windows\system32\userinit.exe", StringComparison.OrdinalIgnoreCase)
                                     || parts[0].Equals(@"%SystemRoot%\system32\userinit.exe", StringComparison.OrdinalIgnoreCase)
                                     || parts[0].Equals("userinit.exe", StringComparison.OrdinalIgnoreCase));
    }
}
