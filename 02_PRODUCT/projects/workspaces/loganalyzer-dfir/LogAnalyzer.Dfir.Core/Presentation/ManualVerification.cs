using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Presentation;

/// <summary>
/// One manual step: the tool, the exact action (a command to paste in PowerShell, or a menu path), what the owner must compare, and whether the action is a command.
/// </summary>
public sealed record VerificationStep(string Tool, string Action, string Compare, bool IsCommand)
{
    /// <summary>One printable line: tool, action, then what to compare.</summary>
    public string ToLine() => $"{Tool}: {Action}" + (Compare.Length > 0 ? $" → de comparat: {Compare}" : "");
}

/// <summary>
/// Owner 2026-11: every piece of evidence of a finding can be checked by hand with standard Windows tools. This turns an evidence reference (its locator, as the parsers
/// write it) and the evidence item (stored copy, channel, SHA-256) into ordered steps. It only READS: no step changes the system (no registry writes, no log clearing).
/// The one exception that touches the registry is loading a saved hive, and only a working COPY of the case copy, under the dedicated key <c>LA_VERIFY</c>, then unloaded.
/// A locator without a known procedure gets one specialist step and never an invented command.
/// </summary>
public static class ManualVerification
{
    public const string Heading = "Cum verifici tu";
    public const string SpecialistTool = "Verificare de specialist";
    public const string HiveKey = @"HKLM\LA_VERIFY";
    private const string WorkingCopy = "$env:TEMP\\LA_VERIFY.hive";

    private static readonly Regex EvtxRecord = new(@"^EventRecordID=(\d+)$", RegexOptions.Compiled);
    private static readonly Regex LiveRecord = new(@"^RecordID (\d+)$", RegexOptions.Compiled);
    private static readonly Regex Prefetch = new(@"^(?<f>.+\.pf)#run(?<n>\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Service = new(@"^Services\[(?<x>.+)\]$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Task = new(@"^Tasks\[(?<x>.+)\]$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Process = new(@"^Processes\[pid=(?<x>\d+)\]$", RegexOptions.Compiled);
    private static readonly Regex Autorun = new(@"^Autoruns\[(?<x>.+)\]$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex TextLine = new(@"(^|:)line=(?<n>\d+)$", RegexOptions.Compiled);
    private static readonly Regex Registry = new(@"^(?<hive>SYSTEM|SOFTWARE|NTUSER|UsrClass|Amcache\.hve)\\(?<rel>.+)$", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Locators that name a position inside the stored file; the file can be hashed by hand, the position needs a specialist tool.</summary>
    private static readonly Regex[] FilePositions =
    [
        new(@"^(summary|MS-SHLLINK|Actions(/.+)?)$", RegexOptions.Compiled), new(@"^MFT#\d+$", RegexOptions.Compiled), new(@"^USN=.+$", RegexOptions.Compiled),
        new(@"^(frame|row)=\d+$", RegexOptions.Compiled), new(@"^offset 0x[0-9a-fA-F]+$", RegexOptions.Compiled),
        new(@"^(INDEX_ROOT|VCN \d+)\+0x[0-9A-F]+$", RegexOptions.Compiled), new(@"^DestList entry .+", RegexOptions.Compiled),
        new(@"^(visits|downloads|moz_historyvisits|moz_annos)\.id=\d+$", RegexOptions.Compiled), new(@"^(TabState|DetectionHistory)/.+", RegexOptions.Compiled),
        new(@"^.+ \(v\d+\)$", RegexOptions.Compiled), new(@"^.+:session=\d+$", RegexOptions.Compiled),
        new(@"^[A-Za-z_]+(/InterfaceLuid=|/AppId=).+/ts=.+$", RegexOptions.Compiled),
        new(@"^.+ (DocumentID|WorkId)=.+$", RegexOptions.Compiled),
    ];

    /// <param name="reference">The evidence reference of the finding.</param>
    /// <param name="item">The evidence item it points at (from the case's evidence index), or null when the index has no such item.</param>
    /// <param name="caseRoot">Absolute case folder; when null the stored path stays relative to the case folder.</param>
    /// <param name="row">The timeline row of the reference (gives the event id and time for an EVTX record), or null.</param>
    public static IReadOnlyList<VerificationStep> For(EvidenceRef reference, EvidenceItem? item, string? caseRoot = null, TimelineEvent? row = null)
    {
        var loc = (reference.Locator ?? "").Trim();
        var steps = new List<VerificationStep>();
        string? stored = StoredPath(item, caseRoot);
        string sha = item is { Sha256.Length: > 0 } ? item.Sha256 : reference.Sha256 ?? "";

        if (EvtxRecord.Match(loc) is { Success: true } ev)
        {
            string id = ev.Groups[1].Value;
            if (stored is not null)
                steps.Add(new("PowerShell", $"Get-WinEvent -Path {Sq(stored)} -FilterXPath \"*[System[EventRecordID={id}]]\" | Format-List *",
                    $"{EventText(row, id)}; câmpurile (Message, TimeCreated, Id) trebuie să se potrivească cu constatarea", true));
            else steps.Add(NoCopy(item, reference));
            if (Channel(item) is { Length: > 0 } ch)
                steps.Add(new("Event Viewer", $"{ChannelPath(ch)} → Filter Current Log" + (row is { EventId.Length: > 0 } ? $" → Event ID {row.EventId}" : "") + $" → caută înregistrarea {id}" + (row is { Time.UtcIso.Length: > 0 } ? $" (ora {row.Time.UtcIso} UTC)" : ""),
                    "aceleași câmpuri ca la pasul cu copia din caz. Jurnalul de pe stația live poate fi ciclat între timp (evenimentele vechi pot lipsi): atunci contează doar copia din caz", false));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (LiveRecord.Match(loc) is { Success: true } lr)
        {
            steps.Add(new("Event Viewer", $"Windows Logs → Security → Filter Current Log → caută înregistrarea {lr.Groups[1].Value}",
                "câmpurile evenimentului cu cele din constatare. Este un jurnal live: poate fi ciclat între timp", false));
            return steps;
        }
        if (Prefetch.Match(loc) is { Success: true } pf)
        {
            AddHash(steps, stored, sha, item);
            AddSize(steps, item, stored);
            steps.Add(Specialist($"rularea {pf.Groups["n"].Value} din {pf.Groups["f"].Value} se citește cu un instrument pentru fișiere Prefetch; Windows nu are unul"));
            return steps;
        }
        if (Service.Match(loc) is { Success: true } sv)
        {
            string name = sv.Groups["x"].Value;
            steps.Add(new("PowerShell", $"Get-CimInstance Win32_Service -Filter {DqParam($"Name='{Wql(name)}'")} | Format-List *",
                "PathName, StartMode, State, StartName cu cele din constatare. Stația live poate fi diferită de cea de la colectare", true));
            steps.Add(Snapshot("serviciile"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (Task.Match(loc) is { Success: true } tk)
        {
            steps.Add(new("PowerShell", $"schtasks /query /tn {Dq(tk.Groups["x"].Value)} /v /fo LIST", "Task To Run, Run As User, Next Run Time cu cele din constatare. Starea live poate fi diferită de cea de la colectare", true));
            steps.Add(Snapshot("activitățile planificate"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (Process.IsMatch(loc) || Autorun.IsMatch(loc))
        {
            steps.Add(Snapshot(Process.IsMatch(loc) ? "procesele (și conexiunile TCP)" : "intrările de pornire automată"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (Registry.Match(loc) is { Success: true } rg)
            return RegistrySteps(loc, rg.Groups["hive"].Value, rg.Groups["rel"].Value, stored, sha, item);
        if (TextLine.Match(loc) is { Success: true } tl)
        {
            AddHash(steps, stored, sha, item);
            steps.Add(new("Notepad", $"deschide copia din caz → Editare → Salt la… → linia {tl.Groups["n"].Value}",
                "textul liniei cu cel din constatare (dacă fișierul are antet, verifică și liniile vecine)", false));
            return steps;
        }
        if (FilePositions.Any(r => r.IsMatch(loc)))
        {
            AddHash(steps, stored, sha, item);
            AddSize(steps, item, stored);
            steps.Add(Specialist($"poziția {loc} din fișier se citește cu un instrument de specialist; Windows nu are unul pentru acest format"));
            return steps;
        }
        steps.Add(Specialist($"localizatorul {loc} nu are o procedură automată"));
        return steps;
    }

    // ---- registry ----

    private static IReadOnlyList<VerificationStep> RegistrySteps(string loc, string hive, string rel, string? stored, string sha, EvidenceItem? item)
    {
        var steps = new List<VerificationStep>();
        int deleted = rel.IndexOf("#deleted", StringComparison.Ordinal);
        if (deleted >= 0)
        {
            AddHash(steps, stored, sha, item);
            steps.Add(Specialist($"urma ștearsă {loc} nu mai există ca cheie în registru; se citește din copia hive-ului cu un instrument de specialist"));
            return steps;
        }
        rel = Regex.Replace(rel, @"\s#\d+$", "");   // ShimCache entry number: the entry is inside one binary value
        bool amcache = hive == "Amcache.hve";
        // the BAM locator abbreviates the middle of the path with "..."
        int dots = rel.IndexOf(@"\...\", StringComparison.Ordinal);
        string? tail = null;
        if (dots >= 0) { tail = rel[(dots + 5)..]; rel = rel[..dots]; }
        string live = hive switch { "SYSTEM" => @"HKLM\SYSTEM\" + rel, "SOFTWARE" => @"HKLM\SOFTWARE\" + rel, "NTUSER" => @"HKCU\" + rel, "UsrClass" => @"HKCU\Software\Classes\" + rel, _ => "" };
        string loaded = HiveKey + "\\" + (amcache ? (rel.StartsWith("Root\\", StringComparison.OrdinalIgnoreCase) ? rel : "Root\\" + rel) : rel);

        string QueryOf(string path, string? tailNote)
        {
            if (tailNote is not null) return $"reg query {Dq(path)} /s";
            if (IsKey(hive, rel) || !path.Contains('\\')) return $"reg query {Dq(path)}";
            int cut = path.LastIndexOf('\\');
            string value = path[(cut + 1)..];
            return $"reg query {Dq(path[..cut])} /v {(value.Any(char.IsWhiteSpace) ? Dq(value) : DqIfNeeded(value))}";
        }
        string what = tail is not null ? $"valoarea de sub SID-ul din {tail} trebuie să existe și să aibă conținutul din constatare" : "datele valorii (sau ale cheii) trebuie să fie cele din constatare";
        string differs = "stația live poate fi diferită de cea de la colectare: o diferență nu infirmă copia din caz";

        if (live.Length > 0)
            steps.Add(new("PowerShell", QueryOf(live, tail), $"{what}; {differs}" + (hive is "NTUSER" or "UsrClass" ? "; HKCU este al utilizatorului cu care ești conectat, deci se potrivește doar dacă este contul din constatare" : ""), true));
        if (stored is not null)
        {
            steps.Add(new("PowerShell", $"Copy-Item -LiteralPath {Sq(stored)} -Destination \"{WorkingCopy}\"; reg load {HiveKey} \"{WorkingCopy}\"",
                "comanda trebuie să spună că operațiunea s-a terminat cu succes; se încarcă doar o copie de lucru a copiei din caz, nu originalul (rulează PowerShell ca administrator)", true));
            steps.Add(new("PowerShell", QueryOf(loaded, tail), what, true));
            steps.Add(new("PowerShell", $"reg unload {HiveKey}", "comanda trebuie să spună că operațiunea s-a terminat cu succes; copia de lucru din %TEMP% (LA_VERIFY.hive) o poți șterge apoi din Explorer", true));
            steps.Add(new("Registry Editor", "alternativ: regedit → HKEY_LOCAL_MACHINE → File → Load Hive… → copia de lucru (nume LA_VERIFY) → răsfoiește cheia → File → Unload Hive…", what, false));
        }
        AddHash(steps, stored, sha, item);
        return steps;
    }

    /// <summary>Whether the last segment of the locator is a key, not a value, for the families whose parsers write a key path.</summary>
    private static bool IsKey(string hive, string rel) =>
        hive == "Amcache.hve" || Regex.IsMatch(rel, @"(^|\\)Services\\[^\\]+$") || rel.Contains(@"\Tree", StringComparison.Ordinal) || Regex.IsMatch(rel, @"\\Actions\[\d+\]$")
        || rel.EndsWith(@"\AppCompatCache", StringComparison.Ordinal);

    // ---- shared pieces ----

    private static void AddHash(List<VerificationStep> steps, string? stored, string sha, EvidenceItem? item)
    {
        if (stored is null) { if (steps.Count == 0 || !steps.Any(s => s.Tool == SpecialistTool)) steps.Add(NoCopy(item, null)); return; }
        steps.Add(new("PowerShell", $"Get-FileHash -Algorithm SHA256 -LiteralPath {Sq(stored)}",
            sha.Length > 0 ? $"amprenta SHA-256 trebuie să fie {sha}" : "amprenta SHA-256 trebuie să fie cea din indexul cazului (evidence.json); aici nu este înregistrată", true));
    }

    private static void AddSize(List<VerificationStep> steps, EvidenceItem? item, string? stored)
    {
        if (stored is null || item is null || item.Size <= 0) return;
        steps.Add(new("Proprietăți fișier", $"clic dreapta pe copia din caz ({item.StoredPath}) → Proprietăți → General", $"dimensiunea trebuie să fie {item.Size} octeți", false));
    }

    private static VerificationStep Snapshot(string what) =>
        new("Explicație", $"{what} sunt un instantaneu luat la colectare: nu se pot reverifica mai târziu decât din copia din caz",
            "starea de pe stația live poate fi diferită; pentru verificare folosește amprenta copiei din caz", false);

    private static VerificationStep NoCopy(EvidenceItem? item, EvidenceRef? reference) =>
        Specialist(item is null
            ? $"elementul de probă {reference?.EvidenceId ?? ""} nu are copie în indexul cazului, deci nu pot indica fișierul de verificat".Replace("  ", " ")
            : $"elementul de probă {item.EvidenceId} nu are o cale păstrată în caz");

    private static VerificationStep Specialist(string reason) => new(SpecialistTool, $"{SpecialistTool}: {reason}", "", false);

    private static string EventText(TimelineEvent? row, string id) =>
        row is { EventId.Length: > 0 } ? $"EventRecordID {id}, Id eveniment {row.EventId}" + (row.Time.UtcIso.Length > 0 ? $", ora {row.Time.UtcIso} UTC" : "") : $"EventRecordID {id}";

    private static string? StoredPath(EvidenceItem? item, string? caseRoot)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.StoredPath)) return null;
        return string.IsNullOrEmpty(caseRoot) ? item.StoredPath : Path.Combine(caseRoot, item.StoredPath);
    }

    /// <summary>The Windows event channel of an EVTX copy: "EventLog:Security" in the item's source type (or source).</summary>
    private static string? Channel(EvidenceItem? item)
    {
        if (item is null) return null;
        foreach (var s in new[] { item.SourceType, item.Source })
            if (s.StartsWith("EventLog:", StringComparison.Ordinal) && s.Length > 9) return s[9..];
        return null;
    }

    private static readonly string[] WindowsLogs = ["Application", "Security", "Setup", "System", "ForwardedEvents"];

    private static string ChannelPath(string channel) =>
        WindowsLogs.Contains(channel, StringComparer.OrdinalIgnoreCase)
            ? $"Windows Logs → {channel}"
            : $"Applications and Services Logs → jurnalul „{channel}” (în arbore, urmărește numele canalului)";

    // ---- quoting (PowerShell) ----

    private static string Clean(string s) => Regex.Replace(s ?? "", @"[\r\n\t\0]", " ");

    /// <summary>PowerShell single-quoted string: a quote inside is doubled, nothing else is interpreted.</summary>
    public static string Sq(string s) => "'" + Clean(s).Replace("'", "''") + "'";

    /// <summary>PowerShell double-quoted string: backtick, dollar and double quote are escaped so the text is taken literally.</summary>
    public static string Dq(string s) => "\"" + DqBody(s) + "\"";

    private static string DqBody(string s) => Clean(s).Replace("`", "``").Replace("$", "`$").Replace("\"", "`\"");

    private static string DqParam(string s) => Dq(s);

    private static string DqIfNeeded(string s) => s.Any(c => char.IsWhiteSpace(c) || c is '"' or '$' or '`' or '&' or '(' or ')' or ';' or '|' or '\'' or '{' or '}' or '?') ? Dq(s) : s;

    /// <summary>WQL string literal body: backslash and quote are escaped with a backslash.</summary>
    private static string Wql(string s) => Clean(s).Replace("\\", "\\\\").Replace("'", "\\'");
}
