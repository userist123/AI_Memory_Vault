using System.Buffers.Binary;
using System.Globalization;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.FileSystem;
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
                Source = "RunKey", EvidenceId = item.EvidenceId, Path = exe, Process = WinPath.GetFileName(exe),
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
        ParserId = "UserHiveParser", Version = "1.1", Artifact = "Hive utilizator (NTUSER.DAT) — UserAssist, Run/RunOnce, RecentDocs, Office MRU, ShellBags (BagMRU)",
        SourceTypes = ["ntuser_hive"], FileNames = ["NTUSER.DAT", "NTUSER*.hiv", "HKCU.hiv"], Fingerprints = ["regf"],
        SupportedOs = "Oricare (citire offline cu RawRegistry)",
        FormatVersions = ["UserAssist versiunea 5 (Windows 7 – 11, 72 de octeți) și versiunea 3 (XP, 16 octeți)", "Run / RunOnce (REG_SZ, REG_EXPAND_SZ neexpandat)", "RunMRU (ordinea din MRUList) și TypedPaths (url1 = cea mai nouă); doar intrarea cea mai nouă are ora cheii",
                         "RecentDocs (toate tipurile, per extensie și Folder; MRUListEx; doar prima intrare are ora cheii)", "Office MRU: File MRU / Place MRU și User MRU\\<id>, „[F…][T<FILETIME>][O…]*cale” (ora [T] este UTC)", "ShellBags: Software\\Microsoft\\Windows\\Shell\\BagMRU (elemente shell decodate; vezi ShellBagsParser pentru UsrClass.dat)"],
        Limitations =
        [
            "UserAssist înregistrează doar programele pornite din interfața grafică (Explorer, meniul Start), nu din linia de comandă.",
            "Ora unei valori Run este LastWriteTime al cheii, comună tuturor valorilor din cheie.",
            "SID-ul proprietarului nu este stocat în hive; se deduce din locul de unde a fost copiat.",
            "Jurnalele de tranzacții ale hive-ului nu sunt aplicate.",
            "RecentDocs nu păstrează calea completă (doar numele); doar cea mai nouă intrare dintr-o cheie are o oră. Ora [T] din Office MRU este ultima deschidere de către aplicația Office respectivă.",
            "ShellBags din NTUSER.DAT (BagMRU) acoperă în principal Windows XP și locuri speciale; pe Windows 7+ folderele locale sunt în UsrClass.dat. Ora cheii BagMRU este ultima actualizare a intrării de către Explorer, nu prima deschidere, iar orele din elementul shell sunt DOS (oră locală fără fus orar, 2 s).",
        ],
        Status = ParserMaturity.Validated,
        Validation = "RegistryPersistenceParserTests: NTUSER real din corpus comparat cu reg export (UserAssist: număr de rulări și FILETIME; Run/RunOnce: comenzi) și hive sintetic; RecentDocs, Office MRU și ShellBags (NTUSER): doar sintetic (UserActivityParserTests), fără corpus real",
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
                        Summary = $"UserAssist: {path} pornit din interfață de {LogAnalyzer.Dfir.Language.RoText.Times(runs)}",
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

        EmitMountPoints2(reg, item, sink, result, ct);
        EmitTypedCommands(reg, item, sink, result);
        // E7: what the user opened or browsed. A damaged structure is a reported gap; the rest of the hive is still read.
        UserActivity.Guarded("RecentDocs", result, () => UserActivity.RecentDocs(reg, item, sink, result, ct));
        UserActivity.Guarded("Office MRU", result, () => UserActivity.OfficeMru(reg, item, sink, result, ct));
        foreach (var bagRoot in UserActivity.BagRootsNtUser)
            UserActivity.Guarded("ShellBags", result, () => UserActivity.ShellBags(reg, "NTUSER", bagRoot, item, sink, result, ct));
        DeletedRegistryTraces.Emit(fullPath, "NTUSER", item, sink, result, ct);
    }

    internal const string RunMruKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";
    internal const string TypedPathsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths";

    /// <summary>
    /// What the user typed or pasted in the Run dialog (Win+R, RunMRU) and in the Explorer address bar (TypedPaths): the place ClickFix / FileFix lures make
    /// the user paste a command (T1204.004). Order: RunMRU follows MRUList (first letter = newest), TypedPaths url1 = newest. The key's last write time is the
    /// time of the newest entry only; the other entries have no time of their own and are emitted without one.
    /// </summary>
    internal static void EmitTypedCommands(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result)
    {
        var account = ProfileName(item.OriginalPath);
        var run = reg.OpenKey(RunMruKey);
        if (run is not null)
        {
            var values = run.Values.Where(v => v.Name.Length == 1 && char.IsAsciiLetter(v.Name[0])).ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
            var order = (run.Values.FirstOrDefault(v => v.Name.Equals("MRUList", StringComparison.OrdinalIgnoreCase))?.AsText ?? "").Select(c => c.ToString()).ToList();
            foreach (var n in values.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) if (!order.Contains(n, StringComparer.OrdinalIgnoreCase)) order.Add(n);
            int pos = 0;
            foreach (var n in order)
            {
                if (!values.TryGetValue(n, out var v)) continue;
                var command = v.AsText;
                if (command.EndsWith("\\1", StringComparison.Ordinal)) command = command[..^2];   // RunMRU stores "command\1"
                Typed(sink, item, "RunMRU", RunMruKey, v.Name, command, pos, run, account, "Run dialog (Win+R) entry");
                pos++;
                result.Records++;
            }
        }
        var typed = reg.OpenKey(TypedPathsKey);
        if (typed is not null)
        {
            var urls = typed.Values.Select(v => (V: v, N: v.Name.StartsWith("url", StringComparison.OrdinalIgnoreCase) && int.TryParse(v.Name[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : int.MaxValue))
                .Where(x => x.N != int.MaxValue).OrderBy(x => x.N).ToList();
            int pos = 0;
            foreach (var (v, _) in urls)
            {
                Typed(sink, item, "TypedPaths", TypedPathsKey, v.Name, v.AsText, pos, typed, account, "Explorer address bar entry");
                pos++;
                result.Records++;
            }
        }
    }

    private static void Typed(IEventSink sink, EvidenceItem item, string source, string key, string valueName, string command, int position, RawKey k, string account, string what)
    {
        var exe = CommandLine.Executable(command);
        sink.Add(new TimelineEvent
        {
            Time = position == 0 ? Hive.KeyTime(k) : Timestamp.Unknown(),
            TimeSemantics = position == 0 ? $"{what}: key last written (time of the newest entry)" : $"{what}: no time of its own (only the newest entry has the key time)",
            Source = source, EvidenceId = item.EvidenceId, Path = exe, Process = exe.Length > 0 ? WinPath.GetFileName(exe) : "",
            Summary = $"{(source == "RunMRU" ? "Comandă tastată sau lipită în fereastra Run (Win+R)" : "Cale tastată sau lipită în bara de adrese Explorer")} de profilul „{account}” (poziția {position + 1}): {LnkText.Depad(command).Text}",
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
            Locator = $@"NTUSER\{key}\{valueName}",
            Fields = { ["Command"] = command, ["ValueName"] = valueName, ["MruPosition"] = position.ToString(CultureInfo.InvariantCulture), ["Account"] = account, ["Key"] = key },
        });
    }

    /// <summary>
    /// MountPoints2 volume GUIDs: the volumes this profile mounted. The key's last write is the profile's last use of that volume. Network shares
    /// (##server#share) are not emitted here: the device inventory reads them. The profile name is taken from the original path (…\Users\&lt;name&gt;\NTUSER.DAT).
    /// </summary>
    internal const string MountPoints2Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";

    internal static void EmitMountPoints2(RawRegistry reg, EvidenceItem item, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var account = ProfileName(item.OriginalPath);
        foreach (var k in reg.SubKeys(MountPoints2Key))
        {
            ct.ThrowIfCancellationRequested();
            var leaf = k.Path[(k.Path.LastIndexOf('\\') + 1)..];
            if (!(leaf.StartsWith('{') && leaf.EndsWith('}'))) continue;
            var guid = leaf.ToUpperInvariant();
            sink.Add(new TimelineEvent
            {
                Time = Hive.KeyTime(k), TimeSemantics = "MountPoints2 key last written (the profile's last use of this volume)",
                Source = "MountPoints2", EvidenceId = item.EvidenceId, Path = "", Process = "",
                Summary = $"Profilul „{account}” a montat volumul {guid} (MountPoints2)",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
                Locator = $@"NTUSER\{MountPoints2Key}\{leaf}",
                Fields = { ["Account"] = account, ["VolumeGuid"] = guid },
            });
            result.Records++;
        }
    }

    /// <summary>The profile folder name from a path such as C:\Users\&lt;name&gt;\NTUSER.DAT; "profil necunoscut" when the path has no such folder.</summary>
    internal static string ProfileName(string originalPath)
    {
        var m = System.Text.RegularExpressions.Regex.Match(originalPath ?? "", @"[\\/]Users[\\/]([^\\/]+)[\\/]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "profil necunoscut";
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
                    Source = "IFEO", EvidenceId = item.EvidenceId, Path = exe, Process = WinPath.GetFileName(exe),
                    Summary = $"IFEO: la pornirea {target} rulează în schimb {debugger}",
                    TemporalType = TemporalType.CurrentSnapshot, Classification = Classification.Direct, Confidence = Confidence.High,
                    Locator = $@"SOFTWARE\{ifeoPath}\{target}\Debugger",
                    Fields = { ["Target"] = target, ["Command"] = debugger, ["Key"] = $@"{ifeoPath}\{target}" },
                });
                result.Records++;
            }
        }

        DeletedRegistryTraces.Emit(fullPath, "SOFTWARE", item, sink, result, ct);
    }

    private static bool IsDefaultUserinit(string v)
    {
        var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 1 && (parts[0].Equals(@"C:\Windows\system32\userinit.exe", StringComparison.OrdinalIgnoreCase)
                                     || parts[0].Equals(@"%SystemRoot%\system32\userinit.exe", StringComparison.OrdinalIgnoreCase)
                                     || parts[0].Equals("userinit.exe", StringComparison.OrdinalIgnoreCase));
    }
}
