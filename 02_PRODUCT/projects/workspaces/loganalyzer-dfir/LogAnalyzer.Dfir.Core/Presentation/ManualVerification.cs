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
    private static readonly Regex WmiSub = new(@"^WmiSubscriptions\[(?<f>.*?)->(?<c>.+)\]$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex StartupItem = new(@"^Startup\[(?<x>.+)\]$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Autorun = new(@"^Autoruns\[(?<x>.+)\]$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex AuditPol = new(@"^auditpol:\{?(?<g>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}?$", RegexOptions.Compiled);
    private static readonly Regex LocalAccount = new(@"^LocalAccounts\[(?<x>.+)\]$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BitLockerVolume = new(@"^BitLockerVolume\[(?<x>[A-Za-z]:)\]$", RegexOptions.Compiled);
    private static readonly Regex LocalAdmin = new(@"^LocalAdministrators\[(?<x>.+)\]$", RegexOptions.Compiled | RegexOptions.Singleline);
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
                    $"{EventText(row, id)}; câmpurile (Message, TimeCreated, Id) trebuie să se potrivească cu constatarea{QuoteHint(reference)}", true));
            else steps.Add(NoCopy(item, reference));
            if (Channel(item) is { Length: > 0 } ch)
                steps.Add(new("Event Viewer", $"{ChannelPath(ch)} → Filter Current Log" + (row is { EventId.Length: > 0 } ? $" → Event ID {row.EventId}" : "") + $" → caută înregistrarea {id}" + (row is { Time.UtcIso.Length: > 0 } ? $" (ora {row.Time.UtcIso} UTC)" : ""),
                    "aceleași câmpuri ca la pasul cu copia din caz" + QuoteHint(reference) + ". Jurnalul de pe stația live poate fi ciclat între timp (evenimentele vechi pot lipsi): atunci contează doar copia din caz", false));
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
        if (WmiSub.Match(loc) is { Success: true } ws)
        {
            string filter = ws.Groups["f"].Value, consumer = ws.Groups["c"].Value;
            steps.Add(new("PowerShell", "Get-CimInstance -Namespace root\\subscription -ClassName __FilterToConsumerBinding | Format-List Filter,Consumer",
                $"în lista trebuie să existe legătura dintre filtrul „{filter}” și consumatorul „{consumer}”", true));
            steps.Add(new("PowerShell", $"Get-CimInstance -Namespace root\\subscription -ClassName __EventFilter -Filter {DqParam($"Name='{Wql(filter)}'")} | Format-List Name,Query,EventNamespace",
                "Query și EventNamespace cu cele din citat", true));
            steps.Add(new("PowerShell", $"Get-CimInstance -Namespace root\\subscription -ClassName CommandLineEventConsumer -Filter {DqParam($"Name='{Wql(consumer)}'")} | Format-List Name,CommandLineTemplate,ExecutablePath",
                "CommandLineTemplate / ExecutablePath cu comanda din citat (pentru ActiveScriptEventConsumer se citesc ScriptFileName și ScriptText cu aceeași interogare pe clasa respectivă). Stația live poate fi diferită de cea de la colectare", true));
            steps.Add(Snapshot("abonamentele WMI"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (StartupItem.Match(loc) is { Success: true } su)
        {
            string file = su.Groups["x"].Value;
            string dir = file.Contains('\\') ? file[..file.LastIndexOf('\\')] : file;
            steps.Add(new("PowerShell", $"Get-ChildItem -Force {Sq(dir)} | Format-Table Name,Length,CreationTimeUtc,LastWriteTimeUtc", $"fișierul {Clean(file[(file.LastIndexOf('\\') + 1)..])} trebuie să fie în listă, cu dimensiunea și ora din citat", true));
            steps.Add(new("PowerShell", $"Get-FileHash -LiteralPath {Sq(file)} -Algorithm SHA256", "SHA-256 cu cel din citat (dacă diferă, fișierul s-a schimbat după colectare)", true));
            if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                steps.Add(new("PowerShell", $"(New-Object -ComObject WScript.Shell).CreateShortcut({Sq(file)}) | Format-List TargetPath,Arguments,WorkingDirectory", "TargetPath și Arguments cu cele din citat", true));
            steps.Add(Snapshot("folderele Startup"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (Process.IsMatch(loc) || Autorun.IsMatch(loc))
        {
            steps.Add(Snapshot(Process.IsMatch(loc) ? "procesele (și conexiunile TCP)" : "intrările de pornire automată"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        // planner #12 B3: audit policy, local accounts and local administrators (stored snapshots of live commands); every step only READS
        if (AuditPol.Match(loc) is { Success: true } ap)
        {
            string sub = row is not null && row.Fields.TryGetValue("Subcategory", out var sn) && sn.Length > 0 ? sn : "{" + ap.Groups["g"].Value + "}";
            steps.Add(new("PowerShell (ca administrator)", $"auditpol /get /subcategory:{Dq(sub)}",
                "coloana „Setting” (Success / Failure / No Auditing) cu setarea din citat. Comanda cere o consolă pornită ca administrator; politica de pe stația live poate fi diferită de cea de la colectare" + QuoteHint(reference), true));
            steps.Add(Snapshot("setările politicii de audit"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (LocalAccount.Match(loc) is { Success: true } la)
        {
            string name = la.Groups["x"].Value;
            steps.Add(new("PowerShell", $"net user {Dq(name)}", $"linia „Account active” (în Windows în limba română „Cont activ”) cu starea „Enabled” din citat: Yes = activ, No = dezactivat; indicatorii parolei: liniile „Password required” și „Password expires” cu valorile passwordRequired / passwordExpires din citat (Yes = true, No = false). Etichetele sunt localizate: în Windows în limba română apar în română{QuoteHint(reference)}", true));
            steps.Add(new("PowerShell", "Get-LocalUser | Where-Object { $_.SID.Value -like '*-501' } | Format-List Name,Enabled,SID",
                "contul Guest se găsește după SID-ul care se termină în -501, deci și când numele este tradus sau schimbat; câmpul Enabled cu starea din citat", true));
            steps.Add(Snapshot("conturile locale"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        // planner #14 E8: BitLocker / device encryption of one volume; every step only READS
        if (BitLockerVolume.Match(loc) is { Success: true } bl)
        {
            string drive = bl.Groups["x"].Value.ToUpperInvariant();
            steps.Add(new("PowerShell (ca administrator)", $"manage-bde -status {drive}",
                "liniile „Protection Status” (în Windows în limba română „Stare protecție”) și „Conversion Status” (în română „Stare conversie”) cu valorile din citat: Protection Off = ProtectionStatus 0, Fully Decrypted = complet decriptat. Etichetele și valorile sunt localizate, deci în română se citesc în limba sistemului" + QuoteHint(reference), true));
            steps.Add(new("PowerShell (ca administrator)", $"Get-BitLockerVolume -MountPoint {drive} | Format-List MountPoint,VolumeStatus,ProtectionStatus,EncryptionMethod",
                "ProtectionStatus (Off / On) și VolumeStatus (FullyDecrypted / FullyEncrypted) cu citatul; numele proprietăților nu sunt localizate", true));
            steps.Add(new("Setări Windows", "Setări → Confidențialitate și securitate → Criptarea dispozitivului (în engleză: Settings → Privacy & security → Device encryption); pe ediția Pro și mai sus: Panou de control → Criptarea unității BitLocker",
                "starea „Activat” / „Dezactivat” pentru volumul din constatare; numele paginii depinde de limba și de ediția Windows, iar pe unele ediții pagina lipsește", false));
            steps.Add(Snapshot("starea criptării discului"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (LocalAdmin.Match(loc) is { Success: true } lm)
        {
            string member = lm.Groups["x"].Value;
            steps.Add(new("PowerShell", "Get-LocalGroupMember -SID S-1-5-32-544 | Format-Table Name,SID,ObjectClass,PrincipalSource",
                $"membrul {Clean(member)} trebuie să fie în listă, cu SID-ul din citat. Grupul se găsește după SID-ul S-1-5-32-544, deci numele lui localizat nu contează{QuoteHint(reference)}", true));
            steps.Add(new("PowerShell", "net localgroup Administrators",
                "lista de membri cu cea din constatare. Numele grupului este localizat (în Windows în limba română „Administratori”): dacă „Administrators” nu este găsit, folosește comanda cu SID de mai sus", true));
            steps.Add(Snapshot("membrii grupului Administrators"));
            AddHash(steps, stored, sha, item);
            return steps;
        }
        if (Registry.Match(loc) is { Success: true } rg)
            return RegistrySteps(loc, rg.Groups["hive"].Value, rg.Groups["rel"].Value, stored, sha, item, QuoteHint(reference));
        if (TextLine.Match(loc) is { Success: true } tl)
        {
            AddHash(steps, stored, sha, item);
            // planner #11 A6: a text file (PSReadLine history, hosts, logs): the line is read by a command that only reads the case copy
            if (stored is not null && int.TryParse(tl.Groups["n"].Value, out var lineNumber) && lineNumber >= 1)
                steps.Add(new("PowerShell", $"Get-Content -LiteralPath {Sq(stored)} | Select-Object -Index {lineNumber - 1}",
                    $"rândul {lineNumber} al fișierului (indexul începe de la 0, deci {lineNumber - 1}) trebuie să fie textul din constatare" + QuoteHint(reference), true));
            steps.Add(new("Notepad", $"deschide copia din caz → Editare → Salt la… → linia {tl.Groups["n"].Value}",
                "textul liniei cu cel din constatare (dacă fișierul are antet, verifică și liniile vecine)" + QuoteHint(reference), false));
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

    private static IReadOnlyList<VerificationStep> RegistrySteps(string loc, string hive, string rel, string? stored, string sha, EvidenceItem? item, string quoteHint = "")
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
        string what = tail is not null ? $"valoarea de sub SID-ul din {tail} trebuie să existe și să aibă conținutul din constatare" : "datele valorii (sau ale cheii) trebuie să fie cele din constatare" + quoteHint;
        // planner #14 E2: the Winlogon DefaultPassword is a secret and the command prints it on screen: the step says to check only that the value exists
        if (rel.EndsWith(@"\Winlogon\DefaultPassword", StringComparison.OrdinalIgnoreCase))
            what = "verificați doar că valoarea există; nu o copiați pe hârtie și nu o fotografiați (comanda o afișează în clar pe ecran); constatarea nu conține parola";
        else if (rel.EndsWith(@"\Winlogon\AutoAdminLogon", StringComparison.OrdinalIgnoreCase))
            what = "valoarea AutoAdminLogon trebuie să fie 1; numele din DefaultUserName se verifică în aceeași cheie (reg query … /v DefaultUserName), iar existența parolei cu pasul pentru DefaultPassword" + quoteHint;
        // planner #14 E4: the Windows version values (CFG-OS-EOL) are also visible in winver and in the lifecycle page of Microsoft
        else if (rel.StartsWith(@"Microsoft\Windows NT\CurrentVersion\", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(rel, @"\\(CurrentBuild|EditionID|DisplayVersion|ProductName|UBR)$"))
            what = "valoarea cu cea din constatare" + quoteHint + "; versiunea se vede și în winver (Win+R → winver; etichetele „Versiune” și „Compilare SO” sunt localizate), iar data de sfârșit a serviciului se compară cu pagina Microsoft Lifecycle a versiunii (tabelul aplicației are data din constatare și poate fi depășit)";
        // CFG-PSV2: the CBS package state is also checked with DISM (as administrator); the PowerShellEngine leftover value is not a proof
        bool psv2 = rel.Contains("Microsoft-Windows-PowerShell-V2-Client-Package", StringComparison.OrdinalIgnoreCase);
        string differs = "stația live poate fi diferită de cea de la colectare: o diferență nu infirmă copia din caz";

        if (live.Length > 0)
            steps.Add(new("PowerShell", QueryOf(live, tail), $"{what}; {differs}" + (hive is "NTUSER" or "UsrClass" ? "; HKCU este al utilizatorului cu care ești conectat, deci se potrivește doar dacă este contul din constatare" : ""), true));
        if (psv2)
            steps.Add(new("PowerShell", "dism /online /get-featureinfo /featurename:MicrosoftWindowsPowerShellV2",
                "rulează ca administrator; „State : Enabled” înseamnă că motorul este prezent, iar eroarea 0x800f080c înseamnă că a fost eliminat; stația live poate fi diferită de cea de la colectare", true));
        // planner #14 E6: a service key (SYSTEM\ControlSetNNN\Services\<name>) is also checked with the service control manager, which shows the path as Windows will use it
        if (hive == "SYSTEM" && Regex.Match(rel, @"^ControlSet\d{3}\\Services\\([^\\]+)$") is { Success: true } svc)
            steps.Add(new("PowerShell", $"sc.exe qc {Dq(svc.Groups[1].Value)}",
                "linia BINARY_PATH_NAME trebuie să fie calea din constatare (cu sau fără ghilimele, după cum este scrisă); stația live poate fi diferită de cea de la colectare", true));
        // planner #14 E2: the Quick Winlogon evidence is a small JSON, not a hive: no "reg load"; the live query and the hash of the JSON are the steps
        if (stored is not null && item?.SourceType is not ("winlogon_autologon_json" or "windows_version_json"))
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
        || rel.EndsWith(@"\AppCompatCache", StringComparison.Ordinal)
        || Regex.IsMatch(rel, @"\\Exclusions\\(Paths|Extensions|Processes)$");   // planner #11 A5: Defender exclusions are listed by the NAMES of the values of this key

    // ---- shared pieces ----

    /// <summary>When the reference carries a quote, the owner is told to compare with it (planner #11 A1); nothing is added for a reference without one.</summary>
    private static string QuoteHint(EvidenceRef reference) =>
        reference.Quote is { Length: > 0 } ? "; compară valorile cu rândul „Citat:” al acestei dovezi" : "";

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
