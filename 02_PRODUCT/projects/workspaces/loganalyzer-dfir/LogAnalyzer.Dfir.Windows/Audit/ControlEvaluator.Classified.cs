using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Windows.Native;
using Ro = LogAnalyzer.Dfir.Language.RoText;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// Classified folders (owner request 2026-10-15; HG 585 art. 291: automatic, unconditional access records for classified electronic information;
/// art. 294: media accounting). A10: the folders' audit rules cover read, write and delete for everybody, on success and failure. A14: the other
/// sources that make exits visible are on. A15: who accessed the files and whether any left the station without an entry in the transfer register.
/// </summary>
public static partial class ControlEvaluator
{
    private static readonly string[] BroadSids = ["S-1-1-0", "S-1-5-11"];   // Everyone, Authenticated Users

    /// <summary>The access types an audit rule must record on a classified folder: (rights bits that satisfy it, plain name).</summary>
    private static readonly (int Bits, string Name)[] NeededRights =
        [(ClassifiedAccess.ReadData, "citire"), (ClassifiedAccess.WriteData | ClassifiedAccess.AppendData, "scriere"), (ClassifiedAccess.Delete | 0x40, "ștergere")];

    /// <summary>What the audit rules of one folder do not record: "citire (eșec)", "ștergere (succes)", "fișierele nu moștenesc regulile". Empty = covered.</summary>
    public static List<string> SaclGaps(IReadOnlyList<AuditRuleFact> rules)
    {
        var broad = rules.Where(x => BroadSids.Contains(x.Sid, StringComparer.OrdinalIgnoreCase)).ToList();
        if (broad.Count == 0) return ["nicio regulă pentru Everyone sau Authenticated Users (accesul celorlalte conturi nu se înregistrează)"];
        var gaps = new List<string>();
        foreach (var (bits, name) in NeededRights)
        {
            if (!broad.Any(x => x.Success && (x.Rights & bits) != 0)) gaps.Add(name + " (succes)");
            if (!broad.Any(x => x.Failure && (x.Rights & bits) != 0)) gaps.Add(name + " (eșec)");
        }
        if (!broad.Any(x => x.InheritsToFiles)) gaps.Add("fișierele din folder nu moștenesc regulile");
        return gaps;
    }

    private static void ClassifiedFolders(StationFacts f, ControlReport r)
    {
        A10(f, r);
        A14(f, r);
        A15(f, r);
    }

    private static void A10(StationFacts f, ControlReport r)
    {
        const string a10 = "Auditarea accesului la folderele clasificate (SACL)";
        if (f.FolderAudits is not { } folders)
        {
            // not collected: the control ran without the procedure profile's folders (not a rights problem)
            Add(r, "A10", "Audit", a10, ControlStatus.Nedeterminat,
                "Folderele clasificate din profilul de proceduri nu au fost transmise controlului, deci regulile lor de audit nu au fost citite. Deschideți profilul de proceduri și repetați controlul.",
                rec: "Treceți în profilul de proceduri folderele cu documente clasificate.");
            return;
        }
        if (folders.Count == 0)
        {
            Add(r, "A10", "Audit", a10, ControlStatus.Nedeterminat,
                "Profilul de proceduri nu definește foldere clasificate; completați tabelul «Foldere clasificate» și repetați controlul. Fără el aplicația nu știe ce foldere să verifice.",
                rec: "Treceți în profilul de proceduri folderele cu documente clasificate.");
            return;
        }
        AuditSetting? fs = f.AuditPolicy?.GetValueOrDefault("File System");
        var missing = folders.Where(x => !x.Exists).ToList();
        var unread = folders.Where(x => x.Exists && (x.AuditRules is null || (x.AuditRules > 0 && x.RuleList is null))).ToList();
        var problems = folders.Where(x => x.Exists && x.AuditRules == 0).Select(x => $"folderul {x.Path} nu are nicio regulă de audit").ToList();
        foreach (var x in folders.Where(x => x.Exists && x.AuditRules > 0 && x.RuleList is not null))
            if (SaclGaps(x.RuleList!) is { Count: > 0 } gaps) problems.Add($"regulile folderului {x.Path} nu înregistrează: {string.Join(", ", gaps)}");
        if (fs is { } fsv && (fsv & AuditSetting.Success) != AuditSetting.Success)
            problems.Add("subcategoria de audit „File System” nu este pornită, deci regulile de pe foldere nu produc niciun eveniment");
        var failureNote = fs is { } fv && (fv & AuditSetting.Success) == AuditSetting.Success && (fv & AuditSetting.Failure) != AuditSetting.Failure
            ? " Subcategoria „File System” nu înregistrează și încercările eșuate; porniți-o și pentru eșec ca să se vadă încercările de acces refuzate." : "";
        var links = folders.Where(x => x.LinkTarget.Length > 0).Select(x => $"{x.Path} este o legătură (junction / link simbolic) către {x.LinkTarget}; accesele prin ambele căi sunt urmărite").ToList();
        Add(r, "A10", "Audit", a10,
            problems.Count > 0 ? ControlStatus.Neconform : unread.Count > 0 || fs is null ? ControlStatus.Nedeterminat : missing.Count > 0 ? ControlStatus.DeVerificat : ControlStatus.Conform,
            (problems.Count > 0 ? "Nu se va ști cine a deschis, a modificat sau a șters documentele: " + string.Join("; ", problems) + "."
                : unread.Count > 0 || fs is null ? $"Nu s-a putut stabili: {string.Join(", ", unread.Select(x => x.Path).Concat(fs is null ? ["politica de audit «File System»"] : []))}."
                    + (f.IsAdministrator ? " Nu au putut fi citite deși aplicația rulează cu drepturi de administrator; verificați la stație dreptul „Manage auditing and security log” și accesul la folder." : NoRead)
                : missing.Count > 0 ? $"Folderele din profil care nu există pe stație: {string.Join(", ", missing.Select(x => x.Path))}. Corectați profilul sau verificați unde sunt documentele."
                : (folders.Count == 1 ? "Folderul clasificat are reguli de audit" : $"Toate cele {Ro.Count(folders.Count, "folder clasificat", "foldere clasificate")} au reguli de audit")
                  + " pentru citire, scriere și ștergere (succes și eșec, pentru toți utilizatorii), iar subcategoria „File System” este auditată.")
            + failureNote + (links.Count > 0 ? " " + string.Join("; ", links) + "." : ""),
            folders.Select(x => $"{x.Path}: " + (!x.Exists ? "nu există" : x.AuditRules is null ? x.Error : x.AuditRules == 0 ? "nicio regulă de audit" : $"{Ro.CountFem(x.AuditRules.Value, "regulă de audit", "reguli de audit")}: {x.Rules}")
                                + (x.ShortPath.Length > 0 ? $" (nume scurt {x.ShortPath})" : ""))
                   .Append($"File System: {(fs is null ? "necitit" : fs.ToString())}"),
            "Pe fiecare folder clasificat adăugați o regulă de audit pentru Everyone (Proprietăți → Securitate → Complex → Auditare): citire, scriere și ștergere, succes și eșec, " +
            "aplicată „Acestui folder, subfolderelor și fișierelor”; porniți subcategoria „File System” (succes și eșec). Aplicația nu schimbă setările stației.");
    }

    /// <summary>The sources A14 needs, each with how to turn it on (the application never changes host settings); On = null when not read.</summary>
    internal static List<(string Name, bool? On, string How)> ClassifiedPrerequisites(StationFacts f)
    {
        bool? Sub(string name) => f.AuditPolicy is { } ap ? (ap.GetValueOrDefault(name) & AuditSetting.Success) == AuditSetting.Success : null;
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        bool? Flag(string k) => S(k) switch { "" => null, "1" => true, _ => false };
        const string Gpo = "Computer Configuration → Windows Settings → Security Settings → Advanced Audit Policy Configuration";
        var l = new List<(string, bool?, string)>
        {
            ("auditul „Removable Storage” (fișiere scrise pe stick-uri și discuri externe)", Sub("Removable Storage"), $"{Gpo} → Object Access → Audit Removable Storage (succes și eșec)"),
            ("auditul „Handle Manipulation” (când a fost deschis și închis un fișier)", Sub("Handle Manipulation"), $"{Gpo} → Object Access → Audit Handle Manipulation (succes)"),
            ("auditul „Process Creation” (programele pornite, inclusiv arhivatoarele)", Sub("Process Creation"), $"{Gpo} → Detailed Tracking → Audit Process Creation (succes)"),
            ("linia de comandă în evenimentele 4688 (ce fișiere a primit un arhivator sau o comandă de copiere)", Flag("Audit.ProcessCreationIncludeCmdLine"),
                "Administrative Templates → System → Audit Process Creation → Include command line in process creation events = Enabled"),
            ("jurnalul de tipărire PrintService/Operational (numele documentelor tipărite)", Flag("PrintService.OperationalEnabled"),
                "Event Viewer → Applications and Services Logs → Microsoft → Windows → PrintService → Operational → Enable Log"),
        };
        if (f.Posture != StationPosture.Standalone)
            l.Add(("auditul „Detailed File Share” (fișiere citite prin partajări de rețea)", Sub("Detailed File Share"), $"{Gpo} → Object Access → Audit Detailed File Share (succes și eșec)"));
        return l;
    }

    private static void A14(StationFacts f, ControlReport r)
    {
        const string title = "Surse pentru urmărirea ieșirii informațiilor clasificate";
        var items = ClassifiedPrerequisites(f);
        var off = items.Where(x => x.On == false).ToList();
        var unknown = items.Where(x => x.On is null).ToList();
        Add(r, "A14", "Audit", title,
            off.Count > 0 ? ControlStatus.Neconform : unknown.Count > 0 ? ControlStatus.Nedeterminat : ControlStatus.Conform,
            off.Count > 0
                ? "Ieșirea unui document clasificat nu poate fi văzută complet. " + string.Join(" ", off.Select(x => $"Sursă lipsă: {x.Name}; cum se pornește: {x.How}."))
                  + (unknown.Count > 0 ? " Nu s-au putut citi: " + string.Join(", ", unknown.Select(x => x.Name)) + "." : "")
                : unknown.Count > 0 ? "Nu s-au putut citi: " + string.Join(", ", unknown.Select(x => x.Name)) + "." + NoRead
                : "Toate sursele necesare sunt pornite: copierea pe suporturi, deschiderea și închiderea fișierelor, programele pornite cu linia lor de comandă și tipărirea se înregistrează"
                  + (f.Posture != StationPosture.Standalone ? ", la fel și citirea prin partajări de rețea." : "."),
            items.Select(x => $"{x.Name}: {(x.On is null ? "necitit" : x.On.Value ? "pornit" : "OPRIT")}"),
            "Cereți administratorului să pornească sursele lipsă prin politica de grup; aplicația doar citește setările, nu le schimbă. Windows înregistrează doar de la pornire înainte, nu retroactiv.");
    }

    /// <summary>The analysis input built from the station facts (pure; public for the tests and the UI).</summary>
    public static ClassifiedInput ClassifiedInputFrom(StationFacts f)
    {
        var folders = (f.FolderAudits ?? []).Select(x => new ClassifiedRoot(x.Path,
            new[] { x.LinkTarget, x.ShortPath }.Where(a => a.Length > 0).ToList())).ToList();
        int[] secIds = [4656, 4658, 4660, 4663, 4670, 4907, 4688, 5145];
        var recs = f.Logs.Where(l => (l.Channel == "Security" && secIds.Contains(l.Id)) || (l.Channel == StationFactCollector.PrintServiceChannel && l.Id == 307))
                         .Select(l => new AccessRecord("event", l.Channel, l.Id, l.TimeUtc, Ev(l), l.Data, "", l)).ToList();
        foreach (var t in f.ShellTraces ?? [])
            recs.Add(new AccessRecord("LNK", "", 0, t.LnkModifiedUtc, $"LNK {t.LnkPath}",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["DriveType"] = t.DriveType, ["VolumeSerial"] = t.VolumeSerial, ["Profile"] = t.Profile }, t.Target, t));
        var removable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in f.Volumes ?? []) if (v.Kind == VolumeKind.Amovibil && v.Drive.Length >= 2) removable.Add(v.Drive[..2].ToUpperInvariant());
        foreach (var d in f.RemovableDrivesNow) if (d.Length >= 2) removable.Add(d[..2].ToUpperInvariant());
        var media = Logs(f, "Microsoft-Windows-Partition/Diagnostic", 1006)
            .Where(l => V(l, "Capacity") is not ("0" or "") && V(l, "BusType") is "7" or "12" or "13" or "")
            .Select(l => new MediumSeen(l.TimeUtc, Clean(V(l, "SerialNumber")), $"{Clean(V(l, "Manufacturer"))} {Clean(V(l, "Model"))}".Trim())).ToList();
        return new ClassifiedInput
        {
            Folders = folders, Records = recs, VolumeMap = f.VolumeMap, RemovableDrives = removable, Media = media, Transfers = f.Transfers,
            // a transfer row approves an exit on a medium only when that medium is in the signed register in force (none in force = nothing approves)
            SignedMedia = f.SignedMedia is { } sm ? LogAnalyzer.Dfir.Registers.SignedMediaMatcher.ActiveSet(sm.Usable ? sm.Doc : null) : null,
            KnownFiles = f.ClassifiedFiles ?? [], FileHashes = f.ClassifiedHashes, NetworkApplies = f.Posture != StationPosture.Standalone,
            // the control runs on the station itself: its zone is the station's zone, in which the register times are typed
            RegisterZone = TimeZoneInfo.Local,
            SelfWindows = [.. f.SelfWindows, .. f.SelfWindow is { } own ? new[] { own } : []],
            AppImagePath = f.AppImagePath, AppImageSha256 = f.AppImageSha256, ImageSha256 = f.ImageSha256,
            SidNames = f.Accounts.Where(a => a.Sid.Length > 0).GroupBy(a => a.Sid).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static readonly string[] CappedSources =
        ["Security 4656", "Security 4660", "Security 4663", "Security 4688", "Security 5145", StationFactCollector.PrintServiceChannel + " 307"];

    private static void A15(StationFacts f, ControlReport r)
    {
        const string title = "Informații clasificate: acces și ieșiri fără aprobare";
        if (f.FolderAudits is not { Count: > 0 })
        {
            Add(r, "A15", "Audit", title, ControlStatus.Nedeterminat,
                "Profilul de proceduri nu definește foldere clasificate (sau nu a fost transmis controlului): nu se știe ale cui fișiere trebuie urmărite. Completați tabelul «Foldere clasificate».",
                rec: "Treceți în profilul de proceduri folderele cu documente clasificate.");
            return;
        }
        ClassifiedAccessReport rep;
        try { rep = ClassifiedAccess.Analyze(ClassifiedInputFrom(f)); }
        catch (Exception ex)
        {
            // the analysis must never stop the control: an unexpected failure is a gap and the check says it could not be made
            f.Gaps.Add(new LogAnalyzer.Dfir.Model.EvidenceGap("Analiza informațiilor clasificate", LogAnalyzer.Dfir.Model.EvidenceStatus.Failed, $"{ex.GetType().Name}: {ex.Message}",
                "Accesul și ieșirile fișierelor clasificate nu au fost evaluate", "Repetați controlul; trimiteți eroarea dezvoltatorilor", "Posibil"));
            Add(r, "A15", "Audit", title, ControlStatus.Nedeterminat,
                $"Analiza accesului la fișierele clasificate a eșuat ({ex.GetType().Name}: {ex.Message}), deci nu se poate spune nimic despre ieșiri. Restul controlului este valabil.");
            return;
        }
        r.Classified = rep;
        foreach (var x in rep.Exits.Where(x => x.TimeUtc is not null))
            r.Actions.Add(new ActionEntry(x.TimeUtc!.Value, x.Who, $"informație clasificată: {x.ChannelText} ({x.VerdictText})", $"{x.File} → {x.Target}", x.Chain.LastOrDefault() ?? ""));
        bool secReadable = ChannelReadable(f, "Security");
        var auditChanges = rep.SecurityChanges.Where(s => s.AuditRules).ToList();
        var status = rep.Unjustified > 0 || auditChanges.Count > 0 ? ControlStatus.Neconform
            : rep.ToVerify > 0 || rep.Deleted > 0 || rep.SecurityChanges.Count > 0 ? ControlStatus.DeVerificat
            : !secReadable ? ControlStatus.Nedeterminat
            : ControlStatus.DeVerificat;   // owner decision 24: the absence of observations is never "Conform"
        var accessed = rep.Files.Count(x => x.Accesses.Count > 0);
        var parts = new List<string>();
        if (rep.Unjustified > 0) parts.Add($"{Ro.CountFem(rep.Unjustified, "ieșire NEJUSTIFICATĂ", "ieșiri NEJUSTIFICATE")} (fișier clasificat scris pe suport, tipărit, arhivat, mutat sau citit prin rețea fără intrare în registrul transferurilor)");
        if (auditChanges.Count > 0) parts.Add($"regulile de audit ale unor obiecte clasificate au fost schimbate de {string.Join(", ", auditChanges.Select(s => s.Who).Distinct())}");
        if (rep.ToVerify > 0) parts.Add($"{Ro.CountFem(rep.ToVerify, "urmă de verificat", "urme de verificat")} (coincidențe în timp, redenumiri, arhive fără destinație cunoscută: nu sunt dovezi de copiere)");
        if (rep.Deleted > 0) parts.Add($"{Ro.CountMasc(rep.Deleted, "fișier clasificat șters", "fișiere clasificate șterse")} (cereți procesul-verbal de distrugere)");
        if (rep.Approved > 0) parts.Add($"{Ro.CountFem(rep.Approved, "ieșire aprobată", "ieșiri aprobate")} (corespund registrului transferurilor)");
        var detail = !secReadable
            ? "Jurnalul Security nu a putut fi citit, deci accesul la fișierele clasificate nu poate fi verificat." + NoRead
            : parts.Count > 0 ? Ro.Capitalize(string.Join("; ", parts)) + $". Fișiere clasificate cu accese înregistrate: {accessed}."
            : $"Nu s-a observat nicio ieșire a unui fișier clasificat în sursele colectate; fișiere cu accese înregistrate: {accessed}. Absența observațiilor nu dovedește că nu au existat ieșiri: vezi A10 și A14 pentru sursele care lipsesc.";
        if (rep.Notes.Count > 0) detail += " " + string.Join(" ", rep.Notes);
        // a capped log can hide older accesses: say so, and never report better than DE VERIFICAT
        var capped = f.Gaps.Where(g => g.Status == LogAnalyzer.Dfir.Model.EvidenceStatus.Partial && CappedSources.Any(c => g.Artifact.StartsWith(c, StringComparison.Ordinal))).ToList();
        if (capped.Count > 0)
        {
            detail += " Atenție: au fost citite doar ultimele evenimente (" + string.Join("; ", capped.Select(g => $"{g.Artifact}: {g.Reason}")) + "); pot lipsi accese mai vechi.";
            if (status == ControlStatus.Conform) status = ControlStatus.DeVerificat;
        }
        var invalidWindows = f.InvalidSelfWindows.Concat(rep.InvalidSelfWindows).ToList();
        if (invalidWindows.Count > 0)
        {
            detail += " " + string.Join(" ", invalidWindows.Select(x => "Atenție, " + x));
            if (status == ControlStatus.Conform) status = ControlStatus.DeVerificat;
        }
        if (rep.SelfActivity.Count > 0)
            detail += $" {Ro.Capitalize(Ro.CountFem(rep.SelfActivity.Count, "înregistrare", "înregistrări"))} sunt {ClassifiedAccess.SelfLabel} la un control anterior și nu sunt socotite accese.";
        Add(r, "A15", "Audit", title, status, detail,
            rep.Ordered.SelectMany(fr => fr.Exits.OrderBy(x => x.Verdict).Select(x => $"{x.VerdictText} · {x.ChannelText} · {fr.Path} → {x.Target} · {(x.TimeUtc is { } t ? t.ToString("yyyy-MM-dd HH:mm") + " UTC" : "ora necunoscută")} · {x.Who}"))
               .Concat(rep.SecurityChanges.Select(s => $"{(s.AuditRules ? "SACL schimbat" : "permisiuni schimbate")} · {s.Object} · {s.Who} · {s.Evidence}")),
            "Pentru fiecare ieșire nejustificată cereți intrarea din registrul transferurilor și aprobarea; dacă nu există, tratați-o ca posibil incident de securitate. Fila „Informații clasificate” arată fiecare fișier.");
    }
}
