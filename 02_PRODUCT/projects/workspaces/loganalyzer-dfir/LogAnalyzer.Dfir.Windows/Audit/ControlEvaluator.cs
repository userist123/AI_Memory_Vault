using System.Globalization;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;
using Ro = LogAnalyzer.Dfir.Language.RoText;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// NuSeAplica ("Nu se aplică"): the rule does not apply to the posture of this edition (see <see cref="ControlApplicability"/>). It is neither
/// compliant nor non-compliant and is never counted as a finding. Added last so the numeric values of reports saved earlier are unchanged.
/// </summary>
public enum ControlStatus { Conform, Neconform, DeVerificat, Nedeterminat, NuSeAplica }

/// <param name="Subjects">Accounts or hosts the check is about (sAMAccountName), when the evaluator knows them; null otherwise.</param>
public sealed record ControlCheck(string Id, string Area, string Title, ControlStatus Status, string Detail, IReadOnlyList<string> Evidence, string Recommendation,
                                  IReadOnlyList<string>? Subjects = null);

/// <summary>A notable action: who did what, when, according to which record.</summary>
public sealed record ActionEntry(DateTimeOffset TimeUtc, string Who, string Action, string Detail, string Source);

public sealed class UserActivity
{
    public required string User { get; init; }
    public int InteractiveLogons { get; set; }
    public int RemoteLogons { get; set; }
    public int FailedLogons { get; set; }
    public int PrivilegedSessions { get; set; }
    public DateTimeOffset? FirstLogonUtc { get; set; }
    public DateTimeOffset? LastLogonUtc { get; set; }
    public int AccountChangesMade { get; set; }
    public HashSet<string> RemoteSources { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ControlReport
{
    public required StationFacts Facts { get; init; }
    public List<ControlCheck> Checks { get; } = [];
    public List<UserActivity> Users { get; } = [];
    public List<ActionEntry> Actions { get; } = [];
    /// <summary>Accesses to the classified folders' files and their exits (A15); null when the profile defines no classified folders.</summary>
    public LogAnalyzer.Dfir.Analysis.ClassifiedAccessReport? Classified { get; set; }
    public int Count(ControlStatus s) => Checks.Count(c => c.Status == s);
    /// <summary>The checks that apply to the posture (every status except NU SE APLICĂ): the only ones counted in summaries and verdicts.</summary>
    public int Applicable => Checks.Count(c => c.Status != ControlStatus.NuSeAplica);
    /// <summary>The head of the application's audit log when the control finished, printed as the anchor code for the next control; null = not available.</summary>
    public LogAnalyzer.Dfir.Case.AuditAnchorPoint? Anchor { get; set; }
    /// <summary>The values STORED in the anchored state entry (read back after writing it), printed next to the anchor code; null = not written.</summary>
    public LogAnalyzer.Dfir.Case.AnchoredInstall? AnchoredState { get; set; }
    /// <summary>The trusted officers of the procedure profile, printed so their fingerprints are on paper.</summary>
    public IReadOnlyList<LogAnalyzer.Dfir.Reporting.SecurityOfficerRow> RegisteredOfficers { get; set; } = [];
}

/// <summary>
/// Turns collected facts into control checks, per-user activity and a timeline of notable actions.
/// Pure function of <see cref="StationFacts"/>: no I/O, fully testable.
/// "DeVerificat" means the facts are shown but only the organisation can say whether they were authorised.
/// </summary>
public static partial class ControlEvaluator
{
    private static readonly HashSet<string> SystemAccounts = new(StringComparer.OrdinalIgnoreCase)
    { "SYSTEM", "LOCAL SERVICE", "NETWORK SERVICE", "ANONYMOUS LOGON", "-", "" };

    public static ControlReport Evaluate(StationFacts f)
    {
        var r = new ControlReport { Facts = f };
        Accounts(f, r);
        Policies(f, r);
        AuditAndLogs(f, r);
        AccountChanges(f, r);
        Logons(f, r);
        Devices(f, r);
        Network(f, r);
        Software(f, r);
        StationHardening(f, r);
        AirGapChannels(f, r);
        AirGapStation(f, r);
        InternetEgress(f, r);
        DomainMembership(f, r);
        // The posture of the executable decides which rules apply: the rest become "Nu se aplică" (never counted as findings).
        for (var i = 0; i < r.Checks.Count; i++) r.Checks[i] = ControlApplicability.Resolve(r.Checks[i], f.Posture);
        r.Actions.Sort((a, b) => a.TimeUtc.CompareTo(b.TimeUtc));
        return r;
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private static string V(LogFact l, string k) => l.Data.TryGetValue(k, out var v) ? v : "";
    private static string Who(LogFact l, string user = "SubjectUserName", string domain = "SubjectDomainName")
    {
        var u = V(l, user);
        var d = V(l, domain);
        return u.Length == 0 ? (l.UserSid.Length > 0 ? l.UserSid : "necunoscut") : (d.Length > 0 && d != "-" ? $"{d}\\{u}" : u);
    }
    private static string Ev(LogFact l) => $"{l.Channel} {l.Id} RecordID {l.RecordId} ({l.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC)";
    private static IEnumerable<LogFact> Logs(StationFacts f, string channel, params int[] ids) =>
        f.Logs.Where(l => l.Channel == channel && ids.Contains(l.Id));
    private static bool ChannelReadable(StationFacts f, string channel) => f.Coverage.Any(c => c.Channel == channel && c.Readable);

    private static void Add(ControlReport r, string id, string area, string title, ControlStatus s, string detail, IEnumerable<string>? ev = null, string rec = "") =>
        r.Checks.Add(new ControlCheck(id, area, title, s,
            s == ControlStatus.Nedeterminat && (detail.StartsWith("Nicio", StringComparison.Ordinal) || detail.StartsWith("Niciun", StringComparison.Ordinal))
                ? "Sursa necesară (de regulă jurnalul Security) nu a putut fi citită, deci verificarea nu se poate face. Rulați aplicația ca administrator."
                : detail,
            (ev ?? []).Take(200).ToList(), rec));

    // ---- accounts ---------------------------------------------------------------------------------------------

    private static void Accounts(StationFacts f, ControlReport r)
    {
        // A gap on the accounts source means the list may be PARTIAL (enumeration stopped midway): a check is never CONFORM on it, only what was
        // actually found is reported (an active Guest stays NECONFORM), the rest is NEDETERMINATE with the reason of the gap.
        var gap = f.Gaps.FirstOrDefault(g => g.Artifact == "Conturi locale");
        string partial = gap is null ? "" : $"Lista conturilor locale este incompletă ({gap.Reason}), deci verificarea nu se poate face pe ea.";
        if (f.Accounts.Count == 0)
        {
            Add(r, "C01", "Conturi", "Conturi locale", ControlStatus.Nedeterminat, "Lista conturilor locale nu a putut fi citită." + (gap is null ? "" : $" ({gap.Reason})"));
            return;
        }
        var guest = f.Accounts.FirstOrDefault(a => a.Sid.EndsWith("-501", StringComparison.Ordinal));
        Add(r, "C01", "Conturi", "Contul Guest este dezactivat",
            guest is { Disabled: false } ? ControlStatus.Neconform : gap is not null ? ControlStatus.Nedeterminat : ControlStatus.Conform,
            guest is { Disabled: false } ? $"Contul {guest.Name} este ACTIV." : gap is not null ? partial
                : guest is null ? "Contul Guest nu există." : $"Contul {guest.Name} este dezactivat.",
            rec: "Dezactivați contul Guest.");

        var admins = f.Accounts.Where(a => a.IsAdministrator && !a.Disabled).ToList();
        Add(r, "C02", "Conturi", "Administratori locali activi", ControlStatus.DeVerificat,
            $"{Ro.Capitalize(Ro.CountMasc(admins.Count, "cont activ", "conturi active"))} cu drepturi de administrator: {string.Join(", ", admins.Select(a => a.Name))}. Comparați cu lista aprobată."
            + (gap is null ? "" : $" Atenție: lista conturilor locale este incompletă ({gap.Reason}); pot exista și alți administratori."),
            admins.Select(a => $"{a.Name} ({a.Sid}), ultima autentificare {a.LastLogonUtc?.ToString("yyyy-MM-dd HH:mm") ?? "necunoscută"}"),
            "Fiecare administrator trebuie să aibă aprobare scrisă; conturile de administrator nu se folosesc pentru lucrul zilnic.");

        var builtin = f.Accounts.FirstOrDefault(a => a.Sid.EndsWith("-500", StringComparison.Ordinal));
        if (builtin is { Disabled: false })
            Add(r, "C03", "Conturi", "Contul Administrator încorporat", ControlStatus.DeVerificat,
                $"Contul încorporat {builtin.Name} este activ.", rec: "Dezactivați sau redenumiți contul Administrator încorporat dacă nu este necesar.");
        else if (gap is not null)
            Add(r, "C03", "Conturi", "Contul Administrator încorporat", ControlStatus.Nedeterminat, partial);
        else if (builtin is not null)
            Add(r, "C03", "Conturi", "Contul Administrator încorporat", ControlStatus.Conform, "Dezactivat.", rec: "Dezactivați sau redenumiți contul Administrator încorporat dacă nu este necesar.");

        var noPwd = f.Accounts.Where(a => !a.Disabled && !a.PasswordRequired).ToList();
        Add(r, "C04", "Conturi", "Conturi active pentru care parola nu este obligatorie",
            noPwd.Count > 0 ? ControlStatus.DeVerificat : gap is not null ? ControlStatus.Nedeterminat : ControlStatus.Conform,
            noPwd.Count > 0 ? $"Conturi cu indicatorul PASSWD_NOTREQD: {string.Join(", ", noPwd.Select(a => a.Name))}. Indicatorul permite o parolă goală; nu dovedește că parola este goală."
                + (gap is null ? "" : $" Lista conturilor este incompletă ({gap.Reason}).")
                : gap is not null ? partial : "Toate conturile active necesită parolă.",
            noPwd.Select(a => a.Name), "Verificați că fiecare cont are parolă; eliminați indicatorul (net user <cont> /passwordreq:yes).");

        var neverExpire = f.Accounts.Where(a => !a.Disabled && !a.PasswordExpires && !a.Sid.EndsWith("-503") && !a.Sid.EndsWith("-504")).ToList();
        if (neverExpire.Count > 0)
            Add(r, "C05", "Conturi", "Parole care nu expiră", ControlStatus.DeVerificat,
                $"Conturi active cu parolă care nu expiră: {string.Join(", ", neverExpire.Select(a => a.Name))}." + (gap is null ? "" : $" Lista conturilor este incompletă ({gap.Reason})."),
                neverExpire.Select(a => a.Name), "Justificați fiecare excepție sau activați expirarea parolei.");
        else if (gap is not null)
            Add(r, "C05", "Conturi", "Parole care nu expiră", ControlStatus.Nedeterminat, partial);

        var rdpUsers = f.Accounts.Where(a => a.IsRemoteDesktopUser).ToList();
        if (rdpUsers.Count > 0)
            Add(r, "C06", "Conturi", "Membri Remote Desktop Users", ControlStatus.DeVerificat,
                string.Join(", ", rdpUsers.Select(a => a.Name)) + (gap is null ? "" : $". Lista conturilor este incompletă ({gap.Reason})."));
        else if (gap is not null)
            Add(r, "C06", "Conturi", "Membri Remote Desktop Users", ControlStatus.Nedeterminat, partial);
    }

    // ---- configuration ----------------------------------------------------------------------------------------

    private static void Policies(StationFacts f, ControlReport r)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        if (f.PasswordPolicy is { } p)
        {
            Add(r, "P01", "Politici", "Lungimea minimă a parolei", p.MinLength >= 12 ? ControlStatus.Conform : p.MinLength >= 8 ? ControlStatus.DeVerificat : ControlStatus.Neconform,
                $"Lungime minimă: {Ro.Count(p.MinLength, "caracter", "caractere")}.", rec: "Minimum 12 caractere (minimum absolut 8).");
            Add(r, "P02", "Politici", "Blocarea contului după încercări eșuate",
                p.LockoutThreshold is > 0 and <= 10 ? ControlStatus.Conform : ControlStatus.Neconform,
                p.LockoutThreshold == 0 ? "Conturile NU se blochează niciodată (parolele pot fi ghicite la nesfârșit)." : $"Blocare după {Ro.CountFem(p.LockoutThreshold, "încercare", "încercări")}, pentru {(p.LockoutDuration == TimeSpan.MaxValue ? "până la deblocarea de către administrator" : Ro.Minutes(p.LockoutDuration.TotalMinutes))}.",
                rec: "Blocare după cel mult 10 încercări.");
            Add(r, "P03", "Politici", "Expirarea parolei", p.MaxAge is null ? ControlStatus.DeVerificat : ControlStatus.Conform,
                p.MaxAge is null ? "Parolele nu expiră niciodată." : $"Parolele expiră după {Ro.Days(p.MaxAge.Value.TotalDays)}; {(p.History == 0 ? "fără istoric (o parolă veche poate fi refolosită imediat)" : p.History == 1 ? "istoric: ultima parolă nu poate fi refolosită" : $"istoric: ultimele {Ro.Count(p.History, "parolă", "parole")} nu pot fi refolosite")}.");
        }
        else Add(r, "P01", "Politici", "Politica de parole", ControlStatus.Nedeterminat, "Nu a putut fi citită.");

        // UAC protects an administrator only when it is on, asks before elevating (ConsentPromptBehaviorAdmin 1-5) and asks on the secure desktop.
        string lua = S("UAC.EnableLUA"), consent = S("UAC.ConsentPromptBehaviorAdmin"), secure = S("UAC.PromptOnSecureDesktop");
        bool prompts = consent is "1" or "2" or "3" or "4" or "5";
        var (uacStatus, uacDetail) =
            lua == "" ? (ControlStatus.Nedeterminat, "Valoarea EnableLUA nu a putut fi citită din registru.")
            : lua != "1" ? (ControlStatus.Neconform, "UAC este dezactivat (EnableLUA=0): orice program al unui administrator rulează cu drepturi complete.")
            : consent == "0" ? (ControlStatus.Neconform, "UAC nu cere confirmare la ridicarea drepturilor (ConsentPromptBehaviorAdmin=0): orice program pornit de un administrator capătă drepturi depline fără întrebare.")
            : !prompts ? (ControlStatus.DeVerificat, consent == "" || consent == Absent
                ? "UAC este activ, dar modul în care întreabă un administrator (ConsentPromptBehaviorAdmin) nu a putut fi citit; verificați la stație că se cere confirmarea."
                : $"UAC este activ, dar ConsentPromptBehaviorAdmin are valoarea neașteptată {consent}; verificați la stație că se cere confirmarea.")
            : secure == "1" ? (ControlStatus.Conform, $"UAC este activ și cere confirmarea administratorului pe ecranul securizat (ConsentPromptBehaviorAdmin={consent}, PromptOnSecureDesktop=1).")
            : secure == "0" ? (ControlStatus.DeVerificat, $"UAC este activ și cere confirmarea (ConsentPromptBehaviorAdmin={consent}), dar întrebarea apare pe ecranul obișnuit (PromptOnSecureDesktop=0), unde un program rău-intenționat poate răspunde singur.")
            : (ControlStatus.DeVerificat, $"UAC este activ și cere confirmarea (ConsentPromptBehaviorAdmin={consent}), dar valoarea PromptOnSecureDesktop nu a putut fi citită; verificați la stație că întrebarea apare pe ecranul securizat.");
        Add(r, "P04", "Politici", "UAC (Control cont utilizator) activ", uacStatus, uacDetail,
            [@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → EnableLUA = " + (lua == "" ? "necitit" : lua),
             @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → ConsentPromptBehaviorAdmin = " + (consent == "" ? "necitit" : consent),
             @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → PromptOnSecureDesktop = " + (secure == "" ? "necitit" : secure)],
            "Porniți UAC (EnableLUA=1), cereți confirmarea administratorului (ConsentPromptBehaviorAdmin=2) și afișați întrebarea pe ecranul securizat (PromptOnSecureDesktop=1).");

        bool autologon = S("Winlogon.AutoAdminLogon") == "1";
        Add(r, "P05", "Politici", "Autentificare automată fără parolă", autologon || S("Winlogon.DefaultPasswordPresent") == "da" ? ControlStatus.Neconform : ControlStatus.Conform,
            autologon ? $"Stația pornește automat în contul {S("Winlogon.DefaultUserName")}" + (S("Winlogon.DefaultPasswordPresent") == "da" ? ", cu parola stocată în registry." : ".")
                      : S("Winlogon.DefaultPasswordPresent") == "da" ? "Există o parolă de autologon stocată în registry (valoarea nu a fost citită)." : "Dezactivată.",
            rec: "Dezactivați AutoAdminLogon și ștergeți DefaultPassword.");

        var rdp = S("RDP.fDenyTSConnections");
        Add(r, "P06", "Politici", "Conexiuni Remote Desktop", rdp == "1" ? ControlStatus.Conform : rdp == "" ? ControlStatus.Nedeterminat : f.Posture == StationPosture.Standalone ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            rdp == "1" ? "Dezactivate." : "Remote Desktop este ACTIVAT.", rec: "Pe o stație izolată, Remote Desktop trebuie dezactivat.");

        var fw = new[] { "Domain", "Private", "Public" }.Select(p => (p, S("Firewall." + p))).ToList();
        Add(r, "P07", "Politici", "Firewall Windows activ pe toate profilurile",
            fw.All(x => x.Item2 == "activ") ? ControlStatus.Conform : fw.All(x => x.Item2 == "") ? ControlStatus.Nedeterminat : ControlStatus.Neconform,
            string.Join("; ", fw.Select(x => $"{x.p}: {(x.Item2 == "" ? "necunoscut" : x.Item2)}")));

        var av = S("Defender.AntivirusEnabled");
        Add(r, "P08", "Politici", "Microsoft Defender activ (protecție în timp real)",
            av == "" ? ControlStatus.Nedeterminat : av == "True" && S("Defender.RealTime") == "True" ? ControlStatus.Conform : ControlStatus.Neconform,
            av == "" ? "Starea Defender nu a putut fi citită (poate exista alt antivirus)." : $"Antivirus: {av}; timp real: {S("Defender.RealTime")}; protecție la modificare: {S("Defender.TamperProtected")}.");
        if (DateTimeOffset.TryParse(S("Defender.SignaturesUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sig))
        {
            var age = f.CollectedUtc - sig;
            Add(r, "P09", "Politici", "Semnături antivirus actualizate", age.TotalDays <= 7 ? ControlStatus.Conform : ControlStatus.DeVerificat,
                $"Ultima actualizare: {sig:yyyy-MM-dd} (acum {Ro.Days(age.TotalDays)}).",
                rec: f.Posture == StationPosture.Standalone ? "Pe stația izolată semnăturile se actualizează offline, periodic, după o procedură documentată." : "Verificați actualizarea automată.");
        }
        if (f.DefenderExclusions.Count > 0)
            Add(r, "P10", "Politici", "Excluderi Microsoft Defender", ControlStatus.DeVerificat,
                $"{Ro.Capitalize(Ro.CountFem(f.DefenderExclusions.Count, "excludere configurată", "excluderi configurate"))}. Excluderile sunt folosite des de atacatori pentru a-și ascunde programele.", f.DefenderExclusions);

        var bl = S("BitLocker.SystemDrive");
        Add(r, "P11", "Politici", "Criptarea discului de sistem (BitLocker)", bl == "1" ? ControlStatus.Conform : bl == "" ? ControlStatus.Nedeterminat : ControlStatus.Neconform,
            bl == "1" ? "Protecție activă." : bl == "" ? $"Starea nu a putut fi citită.{(S("BitLocker.SystemDrive.Error") is { Length: > 0 } blErr ? " " + blErr : "")}" : "Discul de sistem NU este protejat de BitLocker.");

        bool smb1 = S("SMB1.Server") == "1" || S("SMB1.ClientDriverStart") is "2" or "3";
        Add(r, "P12", "Politici", "SMBv1 dezactivat", smb1 ? ControlStatus.Neconform : ControlStatus.Conform, smb1 ? "Protocolul învechit SMBv1 este activ." : "Dezactivat.");

        Add(r, "P13", "Politici", "WDigest nu păstrează parole în memorie", S("WDigest.UseLogonCredential") == "1" ? ControlStatus.Neconform : ControlStatus.Conform,
            S("WDigest.UseLogonCredential") == "1" ? "UseLogonCredential=1: parolele în clar pot fi extrase din memorie." : "Configurare implicită sigură.");

        bool usbBlocked = S("USBSTOR.Start") == "4" || S("RemovableStorage.DenyAll") == "1";
        Add(r, "P14", "Politici", "Stocare USB blocată prin politică", usbBlocked ? ControlStatus.Conform : f.Posture == StationPosture.Standalone ? ControlStatus.DeVerificat : ControlStatus.DeVerificat,
            usbBlocked ? "Accesul la stocare amovibilă este blocat." : "Stocarea USB este permisă.", rec: "Pe stațiile izolate, permiteți doar suporturi aprobate și înregistrate.");
    }

    // ---- audit and logs ---------------------------------------------------------------------------------------

    private static readonly (string Name, AuditSetting Needed)[] RequiredAudit =
    [
        ("Logon", AuditSetting.Success | AuditSetting.Failure), ("Special Logon", AuditSetting.Success),
        ("User Account Management", AuditSetting.Success), ("Security Group Management", AuditSetting.Success),
        ("Audit Policy Change", AuditSetting.Success), ("Process Creation", AuditSetting.Success),
        ("Removable Storage", AuditSetting.Success), ("Plug and Play Events", AuditSetting.Success),
        ("Security State Change", AuditSetting.Success), ("Other Object Access Events", AuditSetting.Success),
    ];

    private static void AuditAndLogs(StationFacts f, ControlReport r)
    {
        if (f.AuditPolicy is { } ap)
        {
            var missing = RequiredAudit.Where(x => (ap.GetValueOrDefault(x.Name) & x.Needed) != x.Needed).ToList();
            Add(r, "A01", "Audit", "Politica de audit înregistrează activitatea necesară controlului",
                missing.Count == 0 ? ControlStatus.Conform : ControlStatus.Neconform,
                missing.Count == 0 ? "Toate subcategoriile necesare sunt auditate." : $"Nu sunt auditate complet: {string.Join(", ", missing.Select(m => $"{m.Name} ({ap.GetValueOrDefault(m.Name)})"))}. Ce nu este auditat nu poate fi verificat la control.",
                ap.Select(kv => $"{kv.Key}: {kv.Value}"), "Activați auditul pentru subcategoriile lipsă (GPO sau auditpol).");
        }
        else Add(r, "A01", "Audit", "Politica de audit", ControlStatus.Nedeterminat, "Nu a putut fi citită (necesită administrator).");

        if (int.TryParse(f.Settings.GetValueOrDefault("SecurityLog.MaxSizeMB"), out var mb))
            Add(r, "A02", "Audit", "Dimensiunea jurnalului Security", mb >= 128 ? ControlStatus.Conform : ControlStatus.Neconform,
                $"{mb} MB, mod {f.Settings.GetValueOrDefault("SecurityLog.Mode")}.", rec: "Minimum 128 MB; arhivare înainte de suprascriere pe stațiile care nu trimit jurnalele centralizat.");

        foreach (var c in f.Coverage.Where(c => c.Channel is "Security" or "System"))
            Add(r, c.Channel == "Security" ? "A03" : "A04", "Audit", $"Jurnalul {c.Channel} acoperă perioada controlată",
                !c.Readable ? ControlStatus.Nedeterminat : c.OldestUtc is { } o && o > f.PeriodStartUtc ? ControlStatus.Neconform : ControlStatus.Conform,
                !c.Readable ? c.Note : c.OldestUtc is { } o2 && o2 > f.PeriodStartUtc
                    ? $"Cel mai vechi eveniment: {o2:yyyy-MM-dd HH:mm} UTC. Activitatea dinainte de această dată NU mai poate fi verificată."
                    : $"Cel mai vechi eveniment: {c.OldestUtc:yyyy-MM-dd HH:mm} UTC.");

        var clears = Logs(f, "Security", 1102).Concat(Logs(f, "System", 104)).ToList();
        Add(r, "A05", "Audit", "Jurnale șterse în perioada controlată",
            !ChannelReadable(f, "Security") ? ControlStatus.Nedeterminat : clears.Count == 0 ? ControlStatus.Conform : ControlStatus.Neconform,
            clears.Count == 0 ? "Nicio ștergere de jurnal înregistrată." : $"{Ro.Capitalize(Ro.CountFem(clears.Count, "ștergere de jurnal", "ștergeri de jurnal"))}.", clears.Select(l => $"{Ev(l)} de {Who(l)} {V(l, "Channel")}"),
            "Fiecare ștergere de jurnal trebuie justificată.");
        foreach (var l in clears) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a șters un jurnal", l.Id == 1102 ? "Security" : V(l, "Channel"), Ev(l)));

        var auditChanges = Logs(f, "Security", 4719).ToList();
        // Always reported (tamper indicators, owner decision 2026-10-16): "no change" is itself evidence the controller can rely on.
        if (auditChanges.Count > 0)
            Add(r, "A06", "Audit", "Politica de audit a fost modificată", ControlStatus.DeVerificat, $"{Ro.Capitalize(Ro.CountFem(auditChanges.Count, "modificare", "modificări"))}.",
                auditChanges.Select(l => $"{Ev(l)} de {Who(l)}: {AuditName(V(l, "SubcategoryGuid"))} {V(l, "AuditPolicyChanges")}"),
                "Cereți aprobarea scrisă a fiecărei modificări; o oprire a auditului urmată de repornire ascunde ce s-a făcut între ele.");
        else
            Add(r, "A06", "Audit", "Politica de audit a fost modificată",
                ChannelReadable(f, "Security") ? ControlStatus.Conform : ControlStatus.Nedeterminat,
                ChannelReadable(f, "Security") ? "Politica de audit nu a fost modificată în perioada controlată (niciun eveniment 4719)." : "Niciun rezultat: jurnalul Security nu a putut fi citit.");
        foreach (var l in auditChanges) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a modificat politica de audit", AuditName(V(l, "SubcategoryGuid")), Ev(l)));

        var timeChanges = Logs(f, "Security", 4616).Where(l => !V(l, "ProcessName").EndsWith("svchost.exe", StringComparison.OrdinalIgnoreCase) &&
                                                             !SystemAccounts.Contains(V(l, "SubjectUserName"))).ToList();
        Add(r, "A07", "Audit", "Ora sistemului schimbată manual", timeChanges.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            timeChanges.Count == 0 ? "Nicio schimbare manuală a orei." : $"{Ro.Capitalize(Ro.CountFem(timeChanges.Count, "schimbare a orei făcută", "schimbări ale orei făcute"))} de utilizatori (pot afecta cronologia).",
            timeChanges.Select(l => $"{Ev(l)} de {Who(l)} prin {V(l, "ProcessName")}: {V(l, "PreviousTime")} → {V(l, "NewTime")}"));
        foreach (var l in timeChanges) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a schimbat ora sistemului", $"{V(l, "PreviousTime")} → {V(l, "NewTime")}", Ev(l)));

        LogServiceStops(f, r);
        AnchorContinuityCheck(f, r);
        OfficerListCheck(f, r);
        InstallDateCheck(f, r);
    }

    /// <summary>
    /// A12: Security 1100 "serviciul de jurnalizare s-a oprit". Windows writes it at every normal shutdown too, so a stop is explained only by an ACTUAL shutdown:
    /// System 6006 (the event log stopped at shutdown) within 5 minutes, or a requested shutdown (System 1074) in the 30 minutes before it followed by a boot
    /// (System 6005) as the next start/stop record. A 1074 that was cancelled (shutdown /a) has no boot after it and explains nothing.
    /// Killing the service writes nothing, so absence proves nothing.
    /// </summary>
    private static void LogServiceStops(StationFacts f, ControlReport r)
    {
        const string title = "Serviciul de jurnalizare Windows oprit în afara unei opriri a calculatorului";
        if (!ChannelReadable(f, "Security")) { Add(r, "A12", "Audit", title, ControlStatus.Nedeterminat, "Niciun rezultat: jurnalul Security nu a putut fi citit."); return; }
        var stops = Logs(f, "Security", 1100).ToList();
        var system = Logs(f, "System", 6005, 6006, 1074).OrderBy(l => l.TimeUtc).ToList();
        bool Explained(LogFact stop)
        {
            if (system.Any(l => l.Id == 6006 && (l.TimeUtc - stop.TimeUtc).Duration() <= TimeSpan.FromMinutes(5))) return true;
            bool requested = system.Any(l => l.Id == 1074 && l.TimeUtc <= stop.TimeUtc.AddMinutes(1) && l.TimeUtc >= stop.TimeUtc.AddMinutes(-30));
            var next = system.FirstOrDefault(l => l.Id is 6005 or 6006 && l.TimeUtc > stop.TimeUtc);
            return requested && next?.Id == 6005;
        }
        var unexplained = stops.Where(l => !Explained(l)).ToList();
        string sysNote = ChannelReadable(f, "System") ? "" : " Jurnalul System nu a putut fi citit, deci nicio oprire nu a putut fi pusă pe seama unei închideri a calculatorului.";
        Add(r, "A12", "Audit", title, unexplained.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            stops.Count == 0 ? "Serviciul de jurnalizare nu s-a oprit în perioada controlată (niciun eveniment 1100)."
            : unexplained.Count == 0 ? $"{Ro.Capitalize(Ro.CountFem(stops.Count, "oprire a serviciului de jurnalizare", "opriri ale serviciului de jurnalizare"))}, toate la o închidere sau repornire efectivă a calculatorului."
            : $"{Ro.Capitalize(Ro.CountFem(unexplained.Count, "oprire a serviciului de jurnalizare", "opriri ale serviciului de jurnalizare"))} fără o închidere efectivă a calculatorului (o cerere de închidere anulată nu contează). Cât timp serviciul este oprit, Windows nu înregistrează nimic.{sysNote}",
            unexplained.Select(Ev), "Cereți explicația fiecărei opriri; comparați cu registrul de intervenții. O oprire neexplicată poate ascunde activitatea din acel interval.");
        foreach (var l in unexplained) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a oprit serviciul de jurnalizare", "fără închiderea calculatorului", Ev(l)));
    }

    /// <summary>
    /// A13: who may sign as security officer must not change unnoticed between controls. The baseline is the state at the anchor typed from the paper; files
    /// changed outside the application are NECONFORM, changes made through the application are listed for review.
    /// </summary>
    private static void OfficerListCheck(StationFacts f, ControlReport r)
    {
        const string title = "Lista ofițerilor de securitate schimbată de la controlul anterior";
        if (f.OfficerAssessment is not { } a) { Add(r, "A13", "Audit", title, ControlStatus.Nedeterminat, "Jurnalul aplicației nu a fost disponibil, deci lista ofițerilor nu s-a putut verifica."); return; }
        if (!a.Determinable)
        {
            Add(r, "A13", "Audit", title, ControlStatus.Nedeterminat, "Lista ofițerilor nu se poate compara cu controlul anterior: " + a.Since + ".",
                rec: "Introduceți la începutul controlului codul de ancorare de pe procesul-verbal semnat al controlului anterior.");
            return;
        }
        if (a.Outside.Count > 0)
        {
            Add(r, "A13", "Audit", title, ControlStatus.Neconform,
                "Lista ofițerilor / autoritățile de încredere modificate în afara aplicației: fișierele nu corespund stării de la controlul anterior, iar schimbarea nu a fost făcută din aplicație. " +
                "Cine poate semna raportul oficial a putut fi schimbat fără urmă în aplicație.",
                a.Outside.Concat(a.Changes), "Tratați stația ca incident de securitate: comparați lista ofițerilor și certificatele autorităților cu procesele-verbale semnate; ofițerii se înregistrează din nou doar din aplicație.");
            return;
        }
        Add(r, "A13", "Audit", title, a.Changes.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            a.Changes.Count == 0 ? $"Nicio schimbare a listei ofițerilor de securitate sau a autorităților de încredere {a.Since}."
                                 : $"{Ro.Capitalize(Ro.CountFem(a.Changes.Count, "schimbare făcută", "schimbări făcute"))} din aplicație {a.Since}. Fiecare trebuie confirmată de ofițer: cine este pe listă poate semna raportul oficial.",
            a.Changes, "Comparați amprentele ofițerilor din raport cu cele de pe procesele-verbale semnate; o înregistrare necunoscută ofițerului este un incident de securitate.");
    }

    /// <summary>
    /// A16: against the anchored state of the control typed from the paper. An INCIDENT is decided only by the identity of the application's audit chain (its first
    /// record): another identity means the chain was reinitialised (NECONFORM). The install date computed from traces is informative: it legitimately moves later
    /// after an update of the application, an eviction of Prefetch traces or a control under another Windows account, so any difference is only to verify.
    /// </summary>
    private static void InstallDateCheck(StationFacts f, ControlReport r)
    {
        const string title = "Data instalării aplicației față de controlul anterior";
        string Fmt(DateTimeOffset? t) => t is { } v ? v.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "necunoscută";
        string Id(LogAnalyzer.Dfir.Case.ChainIdentity? c) => c is null ? "necunoscută" : $"prima înregistrare {c.FirstKind} la {c.FirstWhenUtc}, amprenta {(c.FirstHash.Length > 16 ? c.FirstHash[..16] : c.FirstHash)}";
        if (f.InstallAtPaperAnchor is not { } a) { Add(r, "A16", "Audit", title, ControlStatus.Nedeterminat, "Jurnalul aplicației nu a fost disponibil, deci starea de la controlul anterior nu s-a putut citi."); return; }
        if (!a.Determinable || !a.Recorded)
        {
            Add(r, "A16", "Audit", title, ControlStatus.Nedeterminat, "Data instalării nu s-a putut compara: " + a.Why + ".",
                rec: "Introduceți la începutul controlului codul de ancorare de pe procesul-verbal semnat al controlului anterior.");
            return;
        }
        var ev = new List<string>
        {
            $"identitatea jurnalului la controlul anterior: {Id(a.Chain)}; acum: {Id(f.CurrentChain)}",
            $"data instalării la controlul anterior (codul de pe procesul-verbal): {Fmt(a.Utc)} ({a.Basis})",
            $"data instalării calculată acum: {Fmt(f.AppInstalledUtc)} ({f.AppInstallBasis})",
        };
        if (a.Chain is { } then && (f.CurrentChain is not { } now || now.FirstHash != then.FirstHash || now.FirstWhenUtc != then.FirstWhenUtc || now.FirstKind != then.FirstKind))
        {
            Add(r, "A16", "Audit", title, ControlStatus.Neconform,
                "Jurnalul aplicației a fost reinițializat după controlul anterior (identitatea jurnalului s-a schimbat): " + Id(then) + " la controlul anterior, " + Id(f.CurrentChain) + " acum.", ev,
                "Tratați stația ca incident de securitate: un jurnal reinițializat poate muta data instalării și ascunde conectări și operații dinaintea lui.");
            return;
        }
        string chainNote = a.Chain is null ? " Identitatea jurnalului nu a fost înregistrată la controlul anterior, deci reinițializarea nu s-a putut verifica." : " Jurnalul aplicației este același ca la controlul anterior.";
        var tolerance = TimeSpan.FromDays(1);
        if (a.Utc is { } t0 && f.AppInstalledUtc is { } t1)
        {
            if (t1 - t0 > tolerance)
                Add(r, "A16", "Audit", title, ControlStatus.DeVerificat,
                    $"Data instalării calculată din urme s-a mutat mai târziu ({Fmt(t0)} → {Fmt(t1)}) — normal după o actualizare a aplicației sau ștergerea urmelor Prefetch; confirmați din procesul-verbal." + chainNote, ev,
                    "Comparați cu procesul-verbal și cu registrul de intervenții (actualizări ale aplicației).");
            else if (t0 - t1 > tolerance)
                Add(r, "A16", "Audit", title, ControlStatus.DeVerificat,
                    $"Data instalării calculată din urme este acum mai veche ({Fmt(t1)}, față de {Fmt(t0)}): s-a găsit o urmă mai veche a aplicației." + chainNote, ev,
                    "Verificați de unde provine urma mai veche (o instalare anterioară sau o copie a aplicației).");
            else
                Add(r, "A16", "Audit", title, a.Chain is null ? ControlStatus.DeVerificat : ControlStatus.Conform, $"Data instalării este aceeași ca la controlul anterior ({Fmt(t1)})." + chainNote, ev);
            return;
        }
        if (a.Utc is not null)
            Add(r, "A16", "Audit", title, ControlStatus.DeVerificat,
                $"Data instalării era cunoscută la controlul anterior ({Fmt(a.Utc)}), iar acum nu se poate calcula din urme ({f.AppInstallBasis}) — de exemplu după ștergerea urmelor Prefetch sau un control din alt cont Windows." + chainNote, ev);
        else if (f.AppInstalledUtc is not null)
            Add(r, "A16", "Audit", title, ControlStatus.DeVerificat, "La controlul anterior data instalării nu se putea stabili; acum este " + Fmt(f.AppInstalledUtc) + "." + chainNote, ev);
        else
            Add(r, "A16", "Audit", title, a.Chain is null ? ControlStatus.Nedeterminat : ControlStatus.Conform, "Data instalării nu se poate stabili, nici acum, nici la controlul anterior." + chainNote, ev);
    }

    /// <summary>A11: the anchor code of the previous control (from the signed paper) is still in the application's audit log (see LogAnalyzer.Dfir.Case.AuditAnchor).</summary>
    private static void AnchorContinuityCheck(StationFacts f, ControlReport r)
    {
        const string title = "Continuitatea jurnalului aplicației față de controlul anterior";
        var a = f.PreviousAnchor;
        if (a is null) { Add(r, "A11", "Audit", title, ControlStatus.Nedeterminat, "Continuitatea nu a fost verificată (jurnalul aplicației nu este disponibil)."); return; }
        string source = a.GivenCode.Length == 0 ? ""
            : f.PreviousAnchorFromStation ? " ATENȚIE: codul NU a fost tastat de pe procesul-verbal, ci este cel propus de stație (nesigur): cine a rescris jurnalul poate schimba și propunerea. Confirmați codul cu procesul-verbal semnat."
            : " Codul a fost tastat de pe procesul-verbal.";
        var status = a.Status switch
        {
            LogAnalyzer.Dfir.Case.AnchorContinuityStatus.Continuous => ControlStatus.Conform,
            LogAnalyzer.Dfir.Case.AnchorContinuityStatus.Rewritten => ControlStatus.Neconform,
            _ => ControlStatus.Nedeterminat,
        };
        Add(r, "A11", "Audit", title, status,
            (status == ControlStatus.Neconform ? "Jurnalul aplicației a fost rescris după controlul anterior. " + a.Detail : a.Detail) + source,
            a.GivenCode.Length > 0 ? [$"Cod introdus: {a.GivenCode}" + (a.MatchedSeq is { } s ? $"; găsit la înregistrarea nr. {s}" : "") + (f.PreviousAnchorFromStation ? "; sursa: codul propus de stație (nesigur)" : "; sursa: tastat de pe procesul-verbal")] : null,
            status == ControlStatus.Neconform ? "Comparați jurnalul de pe CD-R-ul de la controlul anterior cu cel actual; tratați stația ca incident de securitate până la lămurire."
            : status == ControlStatus.Nedeterminat ? "Introduceți la începutul controlului codul de ancorare de pe procesul-verbal semnat al controlului anterior." : "");
    }

    // ---- account management -----------------------------------------------------------------------------------

    private static readonly Dictionary<int, string> AccountActions = new()
    {
        [4720] = "a creat contul", [4722] = "a activat contul", [4723] = "a încercat să-și schimbe parola", [4724] = "a resetat parola contului",
        [4725] = "a dezactivat contul", [4726] = "a șters contul", [4738] = "a modificat contul", [4740] = "cont blocat",
        [4732] = "a adăugat în grupul local", [4733] = "a eliminat din grupul local", [4728] = "a adăugat în grupul global",
        [4729] = "a eliminat din grupul global", [4756] = "a adăugat în grupul universal", [4757] = "a eliminat din grupul universal",
    };

    private static void AccountChanges(StationFacts f, ControlReport r)
    {
        var changes = Logs(f, "Security", AccountActions.Keys.ToArray()).ToList();
        foreach (var l in changes)
        {
            var target = l.Id is 4732 or 4733 or 4728 or 4729 or 4756 or 4757
                ? $"{(V(l, "MemberName") is { Length: > 1 } mn ? mn : V(l, "MemberSid"))} → {V(l, "TargetUserName")}"
                : $"{V(l, "TargetDomainName")}\\{V(l, "TargetUserName")}".TrimStart('\\');
            r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), AccountActions[l.Id], target, Ev(l)));
        }
        bool adminGroup(LogFact l) => l.Id is 4732 or 4733 && (V(l, "TargetSid") == "S-1-5-32-544");
        var adminChanges = changes.Where(adminGroup).ToList();
        Add(r, "U01", "Utilizatori", "Modificări ale grupului Administrators",
            !ChannelReadable(f, "Security") ? ControlStatus.Nedeterminat : adminChanges.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            adminChanges.Count == 0 ? "Nicio modificare în perioada controlată." : $"{LogAnalyzer.Dfir.Language.RoText.Count(adminChanges.Count, "modificare", "modificări")}: cine a primit sau a pierdut drepturi de administrator.",
            adminChanges.Select(l => $"{Ev(l)}: {Who(l)} {AccountActions[l.Id]} Administrators: {V(l, "MemberName")} {V(l, "MemberSid")}"),
            "Fiecare acordare de drepturi de administrator trebuie să aibă aprobare.");
        var lifecycle = changes.Where(l => l.Id is 4720 or 4726 or 4722 or 4725 or 4724).ToList();
        Add(r, "U02", "Utilizatori", "Conturi create, șterse, activate, dezactivate sau parole resetate",
            !ChannelReadable(f, "Security") ? ControlStatus.Nedeterminat : lifecycle.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            lifecycle.Count == 0 ? "Nicio astfel de operație." : $"{Ro.Capitalize(Ro.CountFem(lifecycle.Count, "operație pe conturi", "operații pe conturi"))}.",
            lifecycle.Select(l => $"{Ev(l)}: {Who(l)} {AccountActions[l.Id]} {V(l, "TargetUserName")}"));
    }

    // ---- logons -------------------------------------------------------------------------------------------------

    private static void Logons(StationFacts f, ControlReport r)
    {
        var users = new Dictionary<string, UserActivity>(StringComparer.OrdinalIgnoreCase);
        UserActivity U(string name) => users.TryGetValue(name, out var u) ? u : users[name] = new UserActivity { User = name };

        foreach (var l in Logs(f, "Security", 4624))
        {
            var lt = V(l, "LogonType");
            if (lt is not ("2" or "10" or "11" or "7")) continue;
            var name = Who(l, "TargetUserName", "TargetDomainName");
            if (SystemAccounts.Contains(V(l, "TargetUserName")) || V(l, "TargetUserName").StartsWith("DWM-") || V(l, "TargetUserName").StartsWith("UMFD-")) continue;
            var u = U(name);
            if (lt == "10") { u.RemoteLogons++; u.RemoteSources.Add(V(l, "IpAddress")); r.Actions.Add(new ActionEntry(l.TimeUtc, name, "autentificare RDP", $"de la {V(l, "IpAddress")} {V(l, "WorkstationName")}", Ev(l))); }
            else u.InteractiveLogons++;
            u.FirstLogonUtc = u.FirstLogonUtc is { } a && a < l.TimeUtc ? a : l.TimeUtc;
            u.LastLogonUtc = u.LastLogonUtc is { } b && b > l.TimeUtc ? b : l.TimeUtc;
        }
        foreach (var l in Logs(f, "Security", 4625))
            if (!SystemAccounts.Contains(V(l, "TargetUserName"))) U(Who(l, "TargetUserName", "TargetDomainName")).FailedLogons++;
        foreach (var l in Logs(f, "Security", 4672))
            if (!SystemAccounts.Contains(V(l, "SubjectUserName")) && !V(l, "SubjectUserName").EndsWith('$') && !V(l, "SubjectUserName").StartsWith("DWM-"))
                U(Who(l)).PrivilegedSessions++;
        foreach (var a in r.Actions.Where(a => a.Action.StartsWith("a ")))
            if (users.TryGetValue(a.Who, out var u)) u.AccountChangesMade++;
        foreach (var l in Logs(f, "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", 21, 25))
        {
            var addr = V(l, "Address");
            if (addr is "LOCAL" or "") continue;
            U(V(l, "User")).RemoteSources.Add(addr);
            r.Actions.Add(new ActionEntry(l.TimeUtc, V(l, "User"), l.Id == 21 ? "sesiune RDP deschisă" : "sesiune RDP reconectată", $"de la {addr}", Ev(l)));
        }
        r.Users.AddRange(users.Values.OrderByDescending(u => u.InteractiveLogons + u.RemoteLogons));

        var remote = r.Users.Where(u => u.RemoteLogons > 0 || u.RemoteSources.Count > 0).ToList();
        Add(r, "U03", "Utilizatori", "Autentificări de la distanță (RDP)",
            !ChannelReadable(f, "Security") ? ControlStatus.Nedeterminat : remote.Count == 0 ? ControlStatus.Conform : f.Posture == StationPosture.Standalone ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            remote.Count == 0 ? "Nicio autentificare de la distanță." : string.Join("; ", remote.Select(u => $"{u.User}: {u.RemoteLogons} RDP de la {string.Join(", ", u.RemoteSources)}")));

        var bursts = Logs(f, "Security", 4625).GroupBy(l => (Who(l, "TargetUserName", "TargetDomainName"), l.TimeUtc.UtcDateTime.ToString("yyyy-MM-dd HH")))
                                              .Where(g => g.Count() >= 5).ToList();
        Add(r, "U04", "Utilizatori", "Serii de parole greșite (5+ într-o oră)",
            !ChannelReadable(f, "Security") ? ControlStatus.Nedeterminat : bursts.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            bursts.Count == 0 ? "Nicio serie de eșecuri." : $"{Ro.Capitalize(Ro.CountFem(bursts.Count, "serie", "serii"))} — posibilă ghicire a parolei sau parolă uitată.",
            bursts.Select(g => $"{g.Key.Item1}: {Ro.Count(g.Count(), "eșec", "eșecuri")} în ora {g.Key.Item2} UTC, de la {string.Join(", ", g.Select(l => V(l, "IpAddress")).Distinct())}"));

        var priv = r.Users.Where(u => u.PrivilegedSessions > 0).ToList();
        if (priv.Count > 0)
            Add(r, "U05", "Utilizatori", "Cine a lucrat cu drepturi de administrator", ControlStatus.DeVerificat,
                string.Join("; ", priv.Select(u => $"{u.User}: {Ro.CountFem(u.PrivilegedSessions, "sesiune privilegiată", "sesiuni privilegiate")}")));

        var ps = Logs(f, "Microsoft-Windows-PowerShell/Operational", 4104)
            .Where(l => V(l, "ScriptBlockText") is var t && (t.Contains("DownloadString", StringComparison.OrdinalIgnoreCase) || t.Contains("FromBase64String", StringComparison.OrdinalIgnoreCase) ||
                       t.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase) || t.Contains("IEX ", StringComparison.OrdinalIgnoreCase) ||
                       t.Contains("Set-MpPreference", StringComparison.OrdinalIgnoreCase) || t.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (ps.Count > 0)
            Add(r, "U06", "Utilizatori", "Scripturi PowerShell cu tehnici frecvent abuzate", ControlStatus.DeVerificat, $"{Ro.Capitalize(Ro.CountMasc(ps.Count, "bloc de script", "blocuri de script"))}.",
                ps.Select(l => $"{Ev(l)} (utilizator {l.UserSid}): {Shorten(V(l, "ScriptBlockText"), 160)}"));
    }

    // ---- devices / network / software ----------------------------------------------------------------------

    private static void Devices(StationFacts f, ControlReport r)
    {
        // BusType: 7 = USB, 12 = SD, 13 = MMC (STORAGE_BUS_TYPE). Internal NVMe/SATA disks are not removable media.
        var partition = Logs(f, "Microsoft-Windows-Partition/Diagnostic", 1006)
            .Where(l => V(l, "Capacity") is not ("0" or "") && V(l, "BusType") is "7" or "12" or "13" or "").ToList();
        foreach (var l in partition)
            r.Actions.Add(new ActionEntry(l.TimeUtc, "", "dispozitiv de stocare conectat", $"{Clean(V(l, "Manufacturer"))} {Clean(V(l, "Model"))} SN {Clean(V(l, "SerialNumber"))} ({FormatBytes(V(l, "Capacity"))})", Ev(l)));
        var pnp = Logs(f, "Security", 6416).ToList();
        foreach (var l in pnp.Where(l => V(l, "ClassName") is "DiskDrive" or "WPD" or "USB"))
            r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "dispozitiv extern recunoscut", $"{V(l, "DeviceDescription")} {V(l, "DeviceId")}", Ev(l)));
        var any = f.UsbDevices.Count + partition.Count;
        // Owner decision 24: the absence of observations is never "Conform". With readable sources it is "de verificat" (nothing observed in what was
        // collected); with an unreadable source it is undetermined.
        bool usbSourcesKnown = ChannelReadable(f, "Microsoft-Windows-Partition/Diagnostic") && !f.Gaps.Any(g => g.Artifact == "Dispozitive USB (USBSTOR)");
        var d01Status = any > 0 ? ControlStatus.DeVerificat : usbSourcesKnown ? ControlStatus.DeVerificat : ControlStatus.Nedeterminat;
        var d01Detail = any > 0
            ? $"{Ro.Capitalize(Ro.CountMasc(f.UsbDevices.Count, "dispozitiv", "dispozitive"))} în istoricul USBSTOR; {Ro.CountFem(partition.Count, "conectare înregistrată", "conectări înregistrate")} în perioada controlată."
            : usbSourcesKnown
                ? "Dispozitive USB: nu s-a observat în sursele colectate (istoricul USBSTOR și jurnalul Partition/Diagnostic); absența observațiilor nu dovedește că nu au fost folosite suporturi (jurnalul are o istorie limitată, iar dispozitivele pot lăsa urme doar în alte surse)."
                : "Dispozitive USB: nedeterminat. Sursă lipsă: istoricul USBSTOR sau jurnalul Partition/Diagnostic, de ce lipsește: nu a putut fi citit la colectare, cum se pornește: rulați controlul ca administrator.";
        Add(r, "D01", "Dispozitive", "Dispozitive de stocare USB folosite pe stație", d01Status,
            d01Detail,
            f.UsbDevices.Select(u => $"{u.FriendlyName} [{u.Device}] SN {u.Serial}").Concat(partition.Select(l => $"{Ev(l)}: {V(l, "Manufacturer")} {V(l, "Model")} SN {V(l, "SerialNumber")}")),
            "Comparați cu registrul suporturilor aprobate.");
    }

    private static void Network(StationFacts f, ControlReport r)
    {
        var inPeriod = f.NetworkProfiles.Where(p => p.LastConnectedLocal is { } d && new DateTimeOffset(d).ToUniversalTime() >= f.PeriodStartUtc).ToList();
        var evts = Logs(f, "Microsoft-Windows-NetworkProfile/Operational", 10000).Concat(Logs(f, "Microsoft-Windows-WLAN-AutoConfig/Operational", 8001))
            .Where(l => !(V(l, "Name") is var n && (n.StartsWith("Identifying", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Se identific", StringComparison.OrdinalIgnoreCase))))
            .ToList();
        foreach (var l in evts)
            r.Actions.Add(new ActionEntry(l.TimeUtc, "", "conectare la rețea", V(l, "Name") is { Length: > 0 } n ? n : V(l, "SSID"), Ev(l)));
        foreach (var p in inPeriod)
            r.Actions.Add(new ActionEntry(new DateTimeOffset(p.LastConnectedLocal!.Value).ToUniversalTime(), "", "ultima conectare la rețeaua", $"{p.Name} ({p.Kind})", "NetworkList\\Profiles"));

        bool profilesKnown = !f.Gaps.Any(g => g.Artifact == "Profiluri de rețea");
        bool eventsKnown = ChannelReadable(f, "Microsoft-Windows-NetworkProfile/Operational") || ChannelReadable(f, "Microsoft-Windows-WLAN-AutoConfig/Operational");
        // Owner directive 2026-10-16: on a Standalone station the boundary is the installation date of the application (first chained record of its audit chain).
        // Every connection after it is NECONFORM, whatever the controlled period; a connection only before it is the station's history (DE VERIFICAT).
        bool standalone = f.Posture == LogAnalyzer.Core.Services.Edition.StationPosture.Standalone;
        // Review of PR #288: a re-initialised or contradicted audit chain makes the boundary itself untrustworthy: NECONFORM on Standalone.
        var installViolations = standalone ? f.AppInstallViolations : [];
        string violationText = installViolations.Count > 0 ? " NECONFORM și pentru data instalării: " + string.Join("; ", installViolations) + "." : "";
        // Review of PR #290: DE VERIFICAT notes (mismatch without proof, missing Windows traces) and the two dates the officer compares between controls.
        var installToVerify = standalone ? f.AppInstallToVerify : [];
        violationText += installToVerify.Count > 0 ? " DE VERIFICAT pentru data instalării: " + string.Join("; ", installToVerify) + "." : "";
        IEnumerable<string> installDates = standalone
            ? [$"data instalării calculată: {(f.AppInstalledUtc is { } ci ? ci.ToString("yyyy-MM-dd HH:mm") + " UTC" : "necunoscută")}",
               $"primul rând al jurnalului de audit: {(f.AppInstallChainFirstUtc is { } cf ? cf.ToString("yyyy-MM-dd HH:mm") + " UTC" : "necunoscut")}"]
            : [];
        if (standalone && f.AppInstalledUtc is { } inst)
        {
            static DateTimeOffset Utc(DateTime local) => new DateTimeOffset(local).ToUniversalTime();
            var profAfter = f.NetworkProfiles.Where(p => p.LastConnectedLocal is { } d && Utc(d) >= inst).ToList();
            var profBefore = f.NetworkProfiles.Where(p => p.LastConnectedLocal is { } d && Utc(d) < inst).ToList();
            var evAfter = evts.Where(l => l.TimeUtc >= inst).ToList();
            var evBefore = evts.Where(l => l.TimeUtc < inst).ToList();
            string at = $"instalarea aplicației: {inst:yyyy-MM-dd HH:mm} UTC ({f.AppInstallBasis})";
            bool after = profAfter.Count > 0 || evAfter.Count > 0 || f.ConnectedInterfacesNow.Count > 0;
            bool before = profBefore.Count > 0 || evBefore.Count > 0;
            Add(r, "N01", "Rețea", "Stația izolată nu s-a conectat la nicio rețea",
                after || installViolations.Count > 0 ? ControlStatus.Neconform : before || profilesKnown && eventsKnown ? ControlStatus.DeVerificat : ControlStatus.Nedeterminat,
                (after
                    ? $"NECONFORM: stația Standalone a fost conectată la rețea după {at}: {Ro.CountMasc(profAfter.Count, "profil", "profiluri")} cu ultima conectare după instalare, " +
                      $"{Ro.CountMasc(evAfter.Count, "eveniment", "evenimente")} de conectare după instalare, interfețe active acum: {f.ConnectedInterfacesNow.Count}. Pe o stație Standalone nicio conectare nu este aprobată." +
                      (before ? $" Separat, {Ro.CountFem(profBefore.Count + evBefore.Count, "urmă", "urme")} de conectare sunt dinaintea instalării (istoric, de verificat)." : "")
                    : before
                        ? $"DE VERIFICAT (istoric): există conectări doar înainte de {at} ({Ro.CountMasc(profBefore.Count, "profil", "profiluri")}, {Ro.CountMasc(evBefore.Count, "eveniment", "evenimente")}); " +
                          "nicio conectare după instalare în sursele citite. Verificați că stația a fost pregătită (reinstalată sau curățată) înainte de izolare."
                        : profilesKnown && eventsKnown
                            ? $"Conectare la rețea după {at}: nu s-a observat în sursele colectate (profiluri de rețea, NetworkProfile, WLAN-AutoConfig) și nicio interfață activă acum; jurnalele acoperă doar intervalul lor de retenție."
                            : "Conectare la rețea: nedeterminat. Sursă lipsă: profilurile de rețea (NetworkList) sau jurnalele NetworkProfile / WLAN-AutoConfig, de ce lipsește: nu au putut fi citite la colectare, cum se pornește: rulați controlul ca administrator (canalul WLAN-AutoConfig există doar pe stațiile cu Wi-Fi).") + violationText,
                installDates.Concat(installViolations.Select(v => "data instalării: " + v)).Concat(installToVerify.Select(v => "de verificat: " + v)).Concat(profAfter.Select(p => $"după instalare: {p.Name} ({p.Kind}) ultima conectare {p.LastConnectedLocal:yyyy-MM-dd HH:mm} ora locală")
                    .Concat(evAfter.Select(l => $"după instalare: {Ev(l)}: {V(l, "Name")}{V(l, "SSID")}")).Concat(f.ConnectedInterfacesNow.Select(i => "acum: " + i))
                    .Concat(profBefore.Select(p => $"înainte de instalare (istoric): {p.Name} ({p.Kind}) ultima conectare {p.LastConnectedLocal:yyyy-MM-dd HH:mm} ora locală"))
                    .Concat(evBefore.Select(l => $"înainte de instalare (istoric): {Ev(l)}: {V(l, "Name")}{V(l, "SSID")}"))),
                after ? "Orice conectare a unei stații Standalone după instalarea aplicației se trece în procesul-verbal: cine, când, cu ce echipament, ce s-a transferat (vezi constatarea de conectare din investigație: adaptor, volum SRUM, NCSI, firewall)."
                      : "Comparați conectările dinaintea instalării cu procesul-verbal de pregătire a stației.");
        }
        // Emitted in every posture (ControlApplicability turns it into "Nu se aplică" where a connection is expected).
        else
        {
            // Standalone with an unknown installation date (review of PR #288): no connection can be placed before it, so every one ever recorded counts,
            // not only those of the controlled period.
            if (standalone) inPeriod = f.NetworkProfiles.Where(p => p.LastConnectedLocal is not null).ToList();
            bool connected = inPeriod.Count > 0 || evts.Count > 0 || f.ConnectedInterfacesNow.Count > 0;
            string unknownDate = standalone ? $" Data instalării aplicației nu este cunoscută ({(f.AppInstallBasis.Length > 0 ? f.AppInstallBasis : "nu a fost citită")}), deci orice conectare înregistrată vreodată este NECONFORMĂ." : "";
            Add(r, "N01", "Rețea", "Stația izolată nu s-a conectat la nicio rețea",
                connected || installViolations.Count > 0 ? ControlStatus.Neconform : profilesKnown && eventsKnown ? ControlStatus.DeVerificat : ControlStatus.Nedeterminat,
                (connected
                    ? $"Conectări {(standalone ? "înregistrate" : "în perioada controlată")}: {Ro.CountMasc(inPeriod.Count, "profil", "profiluri")}, {Ro.CountMasc(evts.Count, "eveniment", "evenimente")} de conectare; interfețe active acum: {f.ConnectedInterfacesNow.Count}."
                    : profilesKnown && eventsKnown
                        ? "Conectare la rețea: nu s-a observat în sursele colectate (profiluri de rețea, NetworkProfile, WLAN-AutoConfig) și nicio interfață activă acum; absența observațiilor nu dovedește că stația nu s-a conectat (istorie limitată a jurnalelor, adaptoare fără urme în aceste surse)."
                        : "Conectare la rețea: nedeterminat. Sursă lipsă: profilurile de rețea (NetworkList) sau jurnalele NetworkProfile / WLAN-AutoConfig, de ce lipsește: nu au putut fi citite la colectare, cum se pornește: rulați controlul ca administrator (canalul WLAN-AutoConfig există doar pe stațiile cu Wi-Fi).") + unknownDate + violationText,
                installDates.Concat(installViolations.Select(v => "data instalării: " + v)).Concat(installToVerify.Select(v => "de verificat: " + v)).Concat(inPeriod.Select(p => $"{p.Name} ({p.Kind}) ultima conectare {p.LastConnectedLocal:yyyy-MM-dd HH:mm} ora locală")
                        .Concat(evts.Select(l => $"{Ev(l)}: {V(l, "Name")}{V(l, "SSID")}")).Concat(f.ConnectedInterfacesNow.Select(i => "acum: " + i))),
                "Orice conectare a unei stații izolate trebuie investigată (cine, când, ce s-a transferat).");
        }
        Add(r, "N02", "Rețea", "Rețele cunoscute de stație (istoric)",
            !profilesKnown ? ControlStatus.Nedeterminat : ControlStatus.DeVerificat,
            !profilesKnown ? "Istoricul rețelelor (registry NetworkList) nu a putut fi citit; rulați ca administrator."
                : f.NetworkProfiles.Count == 0 ? "Rețele cunoscute: nu s-a observat în sursele colectate (NetworkList); absența profilurilor nu dovedește că stația nu s-a conectat (profilurile pot fi șterse)."
                : $"{Ro.Capitalize(Ro.CountMasc(f.NetworkProfiles.Count, "profil de rețea salvat", "profiluri de rețea salvate"))}.",
            f.NetworkProfiles.OrderByDescending(p => p.LastConnectedLocal).Select(p => $"{p.Name} ({p.Kind}) creat {p.CreatedLocal:yyyy-MM-dd} · ultima conectare {p.LastConnectedLocal:yyyy-MM-dd HH:mm}"));
    }

    /// <summary>
    /// N11 "no path to the internet", PASSIVE: only local configuration that was read (adapters, gateways, DNS servers, persistent routes, proxies, VPN
    /// profiles, radios, cellular, saved Wi-Fi profiles, ICS). Nothing is probed, so a clean result means "no configured path found", not proof.
    /// Standalone: any path at all is non-conform. IsolatedNetwork: internal gateway and DNS are expected; a path that leads outside the internal
    /// ranges (public gateway or DNS, a proxy or VPN to outside, a radio, cellular, ICS) is non-conform. Internet: does not apply.
    /// </summary>
    private static void InternetEgress(StationFacts f, ControlReport r)
    {
        const string id = "N11", area = "Rețea", title = "Nicio cale spre internet (verificare pasivă a configurației)";
        const string limits = " Verificarea citește doar configurația locală (registru, proprietățile adaptoarelor, lista dispozitivelor, fișiere locale) și nu trimite trafic: absența unei căi găsite nu dovedește că nu există una, iar ce se află dincolo de gateway nu poate fi stabilit așa.";
        if (f.EgressPaths is null)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, "Configurația adaptoarelor, a rutelor, a proxy-urilor și a profilurilor VPN nu a putut fi citită." + limits);
            return;
        }
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        var paths = new List<EgressPathFact>(f.EgressPaths);
        // An empty value means the source was NOT read, never "nothing there": every unread source is named, and with any of them N11 is never CONFORM.
        var unread = new List<string>(f.EgressUnread);
        if (!int.TryParse(S("Wlan.ProfileCount"), out var wp))
            unread.Add($"profilurile Wi-Fi salvate nu au putut fi citite: {(S("Wlan.ProfileUnread") is { Length: > 0 } why ? why : "sursa nu a fost citită")}");
        else if (wp > 0) paths.Add(new("Profil Wi-Fi salvat", $"{Ro.CountMasc(wp, "profil Wi-Fi salvat", "profiluri Wi-Fi salvate")}", false, false));
        var ics = S("ICS.Shared");
        if (ics.StartsWith("da", StringComparison.Ordinal)) paths.Add(new("Partajarea conexiunii (ICS)", ics, false, true));
        else if (ics == "" && S("ICS.PolicyShowUI") == "0")   // as N10: unread, but the policy forbids turning it on
            paths.Add(new("Partajarea conexiunii (ICS)", "starea partajării conexiunii nu a putut fi citită; politica interzice activarea ei", null, false));
        else if (ics == "") unread.Add("partajarea conexiunii (ICS): starea ei nu a putut fi citită");
        if (S("Net.AdapterCount") == "")
            unread.Add("lista adaptoarelor de rețea (modemuri celulare, telefoane folosite ca modem) nu a putut fi citită");
        if (S("Net.CellularAdapters").Length > 0) paths.Add(new("Date mobile (WWAN)", S("Net.CellularAdapters"), false, true));
        if (S("Net.PhoneAdapters").Length > 0) paths.Add(new("Telefon folosit ca modem", S("Net.PhoneAdapters"), false, true));
        string unreadText = unread.Count == 0 ? "" : $" Surse necitite: {string.Join("; ", unread)}.";

        // Kinds that lead outside by nature: a path found while active is a finding; present but inactive needs a look.
        static bool ByNature(EgressPathFact p) => p.Kind is not ("Gateway" or "Server DNS" or "Rută permanentă" or "Proxy");
        bool standalone = f.Posture == StationPosture.Standalone;
        // an ICS state that could not be read (policy forbids it) is a review item in every posture, as in N10, never a path found
        static bool UnreadIcs(EgressPathFact p) => p.Kind == "Partajarea conexiunii (ICS)" && p.Internal is null;
        var bad = paths.Where(p => !UnreadIcs(p) && (standalone || (p.Internal == false && (!ByNature(p) || p.Active)))).ToList();
        var review = paths.Where(p => !bad.Contains(p) && (UnreadIcs(p) || (!standalone && (p.Internal is null || (p.Internal == false && ByNature(p)))))).ToList();
        var evidence = paths.Select(p => $"{p.Kind}: {p.Description}{(p.Internal is true ? " (adresă internă)" : p.Internal is null ? " (nu se poate stabili fără trafic)" : "")}{(p.Active ? "" : " [neactiv acum]")}");
        string rec = standalone
            ? "Scoateți adaptorul, ștergeți configurația (gateway, DNS, proxy, profil VPN sau Wi-Fi) și dezactivați radiourile; investigați cine a configurat-o."
            : "Scoateți sau dezactivați calea care duce în afara rețelei interne (proxy, VPN, radio, modem, gateway sau DNS public) și investigați cine a configurat-o.";
        if (bad.Count > 0)
            Add(r, id, area, title, ControlStatus.Neconform,
                $"{Ro.Capitalize(Ro.CountFem(bad.Count, "cale spre exterior găsită", "căi spre exterior găsite"))}: {string.Join("; ", bad.Take(8).Select(p => $"{p.Kind} — {p.Description}"))}." + (standalone ? " O stație Standalone nu trebuie să aibă nicio cale." : " Rețeaua izolată nu trebuie să aibă nicio cale spre internet.") + unreadText + limits,
                evidence, rec);
        else if (unread.Count > 0)
            Add(r, id, area, title, ControlStatus.Nedeterminat,
                $"Nu se poate spune că nu există o cale spre internet: {string.Join("; ", unread)}."
                + (review.Count > 0 ? $" De lămurit și: {string.Join("; ", review.Take(8).Select(p => $"{p.Kind} — {p.Description}"))}." : "") + limits,
                evidence, "Rulați controlul ca administrator, ca toate sursele să poată fi citite.");
        else if (review.Count > 0)
            Add(r, id, area, title, ControlStatus.DeVerificat,
                $"{Ro.Capitalize(Ro.CountFem(review.Count, "configurație nu poate fi încadrată automat", "configurații nu pot fi încadrate automat"))}: {string.Join("; ", review.Take(8).Select(p => $"{p.Kind} — {p.Description}"))}. Confirmați că nu duc în afara rețelei interne." + limits,
                evidence, rec);
        else
            Add(r, id, area, title, ControlStatus.Conform,
                (paths.Count == 0 ? "Nu s-a găsit nicio gateway, server DNS, proxy, profil VPN, radio, modem sau partajare a conexiunii." : "Singurele căi găsite sunt interne (gateway și DNS din domeniile de adrese interne).") + limits,
                evidence);
    }

    /// <summary>N12: on an isolated domain LAN the station is expected to be a domain member (domain controller reachability would need traffic and is not checked).</summary>
    private static void DomainMembership(StationFacts f, ControlReport r)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        var member = S("Domain.PartOfDomain"); var name = S("Domain.Name");
        Add(r, "N12", "Rețea", "Stația este membră a domeniului",
            member == "" ? ControlStatus.Nedeterminat : member == "da" ? ControlStatus.Conform : ControlStatus.Neconform,
            member == "" ? "Apartenența la domeniu nu a putut fi citită." + NoRead
                : member == "da" ? $"Stația este membră a domeniului {name}. Accesibilitatea controlerului de domeniu se verifică separat, activ (N13)."
                : $"Stația nu este membră a unui domeniu (grup de lucru „{name}”), deși ediția așteaptă o stație dintr-o rețea de domeniu.",
            [$"WMI Win32_ComputerSystem → PartOfDomain = {(member == "" ? "necitit" : member)}, Domain = {name}"],
            "Alăturați stația la domeniul rețelei izolate sau folosiți ediția Standalone.");

        // N13: ACTIVE check, run only by the Network edition's probe (LogAnalyzer.Connectors) and only on a domain-joined station.
        const string n13 = "Controlerul de domeniu este accesibil (verificare activă, doar citire)";
        const string n13Rec = "Verificați rezolvarea DNS a domeniului și legătura cu controlerul de domeniu; un controler care se rezolvă la o adresă publică indică o configurare greșită sau o deturnare.";
        var dc = f.DomainController;
        if (member != "da")
            Add(r, "N13", "Rețea", n13, ControlStatus.Nedeterminat,
                "Verificarea nu a rulat: stația nu este membră a unui domeniu (vezi N12), deci nu are un controler de domeniu de contactat.", rec: n13Rec);
        else if (dc is { Outcome: DcProbeOutcome.Cancelled })
            Add(r, "N13", "Rețea", n13, ControlStatus.Nedeterminat, "Verificarea activă nu s-a încheiat: verificarea a fost anulată.", rec: n13Rec);
        else if (dc is null || dc.Outcome == DcProbeOutcome.Skipped)
            Add(r, "N13", "Rețea", n13, ControlStatus.Nedeterminat,
                "Verificarea activă nu a rulat" + (dc is null ? " (nu este disponibilă în această rulare)." : $": {dc.Detail}"), rec: n13Rec);
        else
        {
            var ev = new[] { $"Controler: {(dc.DcName.Length > 0 ? dc.DcName : "—")}", $"Adrese: {(dc.Addresses.Count > 0 ? string.Join(", ", dc.Addresses) : "—")}" };
            var (status, detail) = dc.Outcome switch
            {
                DcProbeOutcome.Reachable => (ControlStatus.Conform, $"Controlerul de domeniu {dc.DcName} al domeniului {name} se află la o adresă internă și răspunde pe LDAP (389). {dc.Detail}".TrimEnd()),
                DcProbeOutcome.PublicAddress => (ControlStatus.Neconform, $"Atenție: controlerul de domeniu se rezolvă la o adresă publică ({string.Join(", ", dc.Addresses)}). {dc.Detail}".TrimEnd()),
                DcProbeOutcome.TimedOut => (ControlStatus.DeVerificat, $"Verificarea activă s-a oprit: {dc.Detail}".TrimEnd()),
                DcProbeOutcome.Unreachable => (ControlStatus.DeVerificat, $"Controlerul de domeniu {dc.DcName} se află la o adresă internă, dar nu a răspuns pe LDAP (389): {dc.Detail}".TrimEnd()),
                _ => (ControlStatus.DeVerificat, $"Nu s-a găsit niciun controler de domeniu la o adresă internă pentru domeniul {name}. {dc.Detail}".TrimEnd()),
            };
            Add(r, "N13", "Rețea", n13, status, detail, ev, n13Rec);
        }
    }

    private static void Software(StationFacts f, ControlReport r)
    {
        var msi = Logs(f, "Application", 11707, 11724, 1033, 1034).ToList();
        foreach (var l in msi)
            r.Actions.Add(new ActionEntry(l.TimeUtc, l.UserSid, l.Id is 11707 or 1033 ? "a instalat" : "a dezinstalat", V(l, "Data0") is { Length: > 0 } d ? d : string.Join(" ", l.Data.Values.Take(2)), Ev(l)));
        Add(r, "S01", "Software", "Programe instalate sau dezinstalate", msi.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            $"{Ro.Capitalize(Ro.CountFem(msi.Count, "operație Windows Installer", "operații Windows Installer"))} în perioada controlată.", msi.Select(l => $"{Ev(l)}: {string.Join(" ", l.Data.Values.Take(2))}"),
            "Comparați cu lista de software aprobat.");

        var allServices = Logs(f, "System", 7045).ToList();
        bool IsDriver(LogFact l) => V(l, "ServiceType").Contains("driver", StringComparison.OrdinalIgnoreCase) || V(l, "ImagePath").EndsWith(".sys", StringComparison.OrdinalIgnoreCase);
        var services = allServices.Where(l => !IsDriver(l)).ToList();
        var drivers = allServices.Where(IsDriver).ToList();
        foreach (var l in drivers) r.Actions.Add(new ActionEntry(l.TimeUtc, V(l, "AccountName"), "driver instalat", $"{V(l, "ServiceName")}: {V(l, "ImagePath")}", Ev(l)));
        var tasks = Logs(f, "Security", 4698).ToList();
        foreach (var l in services) r.Actions.Add(new ActionEntry(l.TimeUtc, V(l, "AccountName"), "serviciu nou instalat", $"{V(l, "ServiceName")}: {V(l, "ImagePath")}", Ev(l)));
        foreach (var l in tasks) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a creat activitatea planificată", V(l, "TaskName"), Ev(l)));
        Add(r, "S02", "Software", "Servicii noi și activități planificate create", services.Count + tasks.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            $"{LogAnalyzer.Dfir.Language.RoText.Count(services.Count, "serviciu", "servicii")} noi, {LogAnalyzer.Dfir.Language.RoText.Count(tasks.Count, "activitate planificată", "activități planificate")} create ({LogAnalyzer.Dfir.Language.RoText.Count(drivers.Count, "driver", "drivere")} instalate, listate separat în cronologie).",
            services.Select(l => $"{Ev(l)}: {V(l, "ServiceName")} → {V(l, "ImagePath")}").Concat(tasks.Select(l => $"{Ev(l)}: {Who(l)} → {V(l, "TaskName")}")),
            "Serviciile și activitățile planificate sunt metode frecvente de persistență: verificați fiecare intrare necunoscută.");

        foreach (var l in Logs(f, "System", 1074))
            r.Actions.Add(new ActionEntry(l.TimeUtc, V(l, "param7") is { Length: > 0 } u ? u : l.UserSid, "a oprit/repornit stația", $"{V(l, "param1")} {V(l, "param3")}", Ev(l)));
    }

    // ---- station hardening, radios, data left behind (read-only registry / WMI facts) ----------------------------

    private const string Absent = StationFactCollector.Absent;
    private const string NoRead = " Nu a putut fi citit; pot fi necesare drepturi de administrator. Rulați aplicația ca administrator și repetați controlul.";

    /// <summary>Checks about the second channels out of an isolated station: adapter kinds (N05), CD/DVD burning (P28), printing (P29), clipboard and activity history (P30), camera / microphone / speakers (P31).</summary>
    private static void AirGapChannels(StationFacts f, ControlReport r)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        bool isolated = f.Posture == StationPosture.Standalone;
        bool noInternet = f.Posture != StationPosture.Internet;   // Standalone and IsolatedNetwork: no path to the internet is expected
        string Shown(string v) => v == Absent ? "nesetat" : v;
        int N(string k) => int.TryParse(S(k), out var n) ? n : 0;

        // N05 network adapter kinds
        var nCount = S("Net.AdapterCount");
        var phone = S("Net.PhoneAdapters"); var cell = S("Net.CellularAdapters"); var usbE = S("Net.UsbEthernetAdapters");
        if (nCount == "")
            Add(r, "N05", "Rețea", "Adaptoare de telefon, modem celular sau Ethernet pe USB", ControlStatus.Nedeterminat, "Lista adaptoarelor de rețea nu a putut fi citită." + NoRead);
        else
        {
            bool outbound = phone.Length > 0 || cell.Length > 0;
            var parts = new List<string>();
            if (phone.Length > 0) parts.Add($"telefon folosit ca modem (RNDIS): {phone}");
            if (cell.Length > 0) parts.Add($"modem celular (WWAN): {cell}");
            if (usbE.Length > 0) parts.Add($"placă Ethernet pe USB: {usbE}");
            Add(r, "N05", "Rețea", "Adaptoare de telefon, modem celular sau Ethernet pe USB",
                parts.Count == 0 ? ControlStatus.Conform : outbound && noInternet ? ControlStatus.Neconform : ControlStatus.DeVerificat,
                parts.Count == 0 ? (N("Net.AdapterCount") == 1 ? "Singurul adaptor de rețea activ nu este" : $"Dintre cele {Ro.Count(N("Net.AdapterCount"), "adaptor", "adaptoare")} de rețea active, niciunul nu este") + " telefon folosit ca modem, modem celular sau placă Ethernet pe USB (adaptoarele virtuale nu se numără)."
                    : "Au fost găsite: " + string.Join("; ", parts) + "." + (noInternet ? " Un astfel de adaptor dă stației izolate o cale de internet sau către altă rețea, ocolind rețeaua stației." : " Verificați că adaptorul este cel aprobat."),
                [$"WMI Win32_PnPEntity (clasa Net): {Ro.CountMasc(N("Net.AdapterCount"), "adaptor activ", "adaptoare active")}", "tip stabilit din identificatori compatibili, serviciu și descriere"],
                "Scoateți telefonul / modemul / placa USB; dacă sunt aprobate, notați documentul de aprobare.");
        }

        // P28 CD/DVD burning
        var wc = S("Optical.WriterCount");
        if (wc == "")
            Add(r, "P28", "Politici", "Inscripționarea de CD/DVD blocată", ControlStatus.Nedeterminat, "Lista unităților optice nu a putut fi citită." + NoRead);
        else
        {
            int writers = N("Optical.WriterCount");
            bool denyWrite = S("Optical.DenyWrite") == "1" || S("Optical.DenyAll") == "1";
            bool explorerOnly = S("Optical.NoCDBurningMachine") == "1" || S("Optical.NoCDBurningUser") == "1";
            var ev = new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56308-b6bf-11d0-94f2-00a0c91efb8b} → Deny_Write = " + Shown(S("Optical.DenyWrite")),
                             @"Policies\Explorer → NoCDBurning (mașină) = " + Shown(S("Optical.NoCDBurningMachine")) + "; (utilizator) = " + Shown(S("Optical.NoCDBurningUser")), "WMI Win32_CDROMDrive → Capabilities (4 = scrie discuri)" };
            Add(r, "P28", "Politici", "Inscripționarea de CD/DVD blocată",
                writers == 0 || denyWrite ? ControlStatus.Conform : isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
                writers == 0 ? $"Stația nu are nicio unitate optică cu capacitate de scriere ({Ro.CountFem(N("Optical.DriveCount"), "unitate optică", "unități optice")})."
                    : denyWrite ? $"Scrierea pe CD/DVD este refuzată prin politica de stocare amovibilă (unități cu scriere: {S("Optical.WriterNames")})."
                    : explorerOnly ? $"ATENȚIE: stația are {Ro.CountFem(writers, "unitate care scrie discuri", "unități care scriu discuri")} ({S("Optical.WriterNames")}), iar politica NoCDBurning blochează doar inscripționarea din Explorer. Un program de inscripționare sau serviciul IMAPI pot scrie în continuare; blocarea reală este politica „CD și DVD: refuzare scriere”, care nu este setată."
                    : $"Stația are {Ro.CountFem(writers, "unitate care scrie discuri", "unități care scriu discuri")} ({S("Optical.WriterNames")}) și nicio politică nu oprește scrierea (nici NoCDBurning, nici „CD și DVD: refuzare scriere”).",
                ev, "Setați politica „CD și DVD: refuzare scriere” (Deny_Write); NoCDBurning singur nu este suficient.");
        }

        // P29 printing recorded
        var log = S("Print.LogEnabled"); var pc = S("Print.PrinterCount"); var netPr = S("Print.NetworkPrinters");
        if (pc == "" && log == "")
            Add(r, "P29", "Audit", "Tipărirea este înregistrată", ControlStatus.Nedeterminat, "Jurnalul de tipărire și lista imprimantelor nu au putut fi citite." + NoRead);
        else
        {
            int printers = N("Print.PrinterCount");
            var issues = new List<string>();
            if (printers > 0 && log == "0") issues.Add("jurnalul de tipărire (PrintService/Operational) este OPRIT, deci nu se înregistrează ce documente se tipăresc");
            if (netPr.Length > 0) issues.Add($"există imprimante de rețea: {netPr}");
            Add(r, "P29", "Audit", "Tipărirea este înregistrată",
                printers == 0 ? ControlStatus.Conform : log == "" ? ControlStatus.Nedeterminat : issues.Count == 0 ? ControlStatus.Conform : isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
                printers == 0 ? "Nu este instalată nicio imprimantă."
                    : log == "" ? $"Starea jurnalului de tipărire nu a putut fi citită ({Ro.CountFem(printers, "imprimantă instalată", "imprimante instalate")})." + NoRead
                    : issues.Count == 0 ? $"Jurnalul de tipărire este pornit ({Ro.CountFem(printers, "imprimantă instalată", "imprimante instalate")}: {S("Print.PrinterNames")}); fiecare document tipărit lasă evenimentul 307 (document, utilizator, imprimantă, pagini)."
                    : "Probleme: " + string.Join("; ", issues) + (log == "0" ? ". Jurnalul este oprit implicit în Windows și trebuie pornit; fără el absența evenimentelor nu dovedește că nu s-a tipărit." : "."),
                [$"canalul Microsoft-Windows-PrintService/Operational → activat = {(log == "1" ? "da" : log == "0" ? "nu" : "necitit")}", $"WMI Win32_Printer: {(pc == "" ? "necitit" : Ro.CountFem(printers, "imprimantă", "imprimante"))}"],
                "Porniți jurnalul PrintService/Operational și țineți registrul foilor tipărite; imprimantele de rețea se scot dintr-o stație izolată.");
        }

        // P30 clipboard history and activity history
        var cu = S("Clipboard.HistoryUser"); var cp = S("Clipboard.HistoryPolicy"); var ap = S("Activity.FeedPolicy"); var cache = S("Activity.CachePresent");
        if (cu == "" && cp == "" && ap == "" && cache == "")
            Add(r, "P30", "Politici", "Istoric clipboard și istoric de activitate Windows oprite", ControlStatus.Nedeterminat, "Setările de istoric nu au putut fi citite." + NoRead);
        else
        {
            bool clipOn = cu == "1" && cp != "0";
            bool actPolicyOn = ap == "1";
            bool actDefaultOn = cache == "da" && ap != "0" && ap != "1";
            Add(r, "P30", "Politici", "Istoric clipboard și istoric de activitate Windows oprite",
                !clipOn && !actPolicyOn && !actDefaultOn ? ControlStatus.Conform : (clipOn || actPolicyOn) && isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
                clipOn ? $"Istoricul clipboard este PORNIT (EnableClipboardHistory = 1): ultimele elemente copiate, inclusiv text dintr-un document clasificat, rămân pe disc. {(actPolicyOn ? "Istoricul de activitate este și el permis explicit prin politică." : "")}"
                    : actPolicyOn ? "Istoricul de activitate (Timeline) este permis explicit prin politică (EnableActivityFeed = 1)."
                    : actDefaultOn ? $"Istoricul clipboard este oprit, dar baza de activitate Windows (ActivitiesCache.db) există și politica EnableActivityFeed nu este setată ({Shown(ap)}), deci istoricul de activitate rămâne în regimul implicit al Windows. Opriți-l prin politică."
                    : $"Istoricul clipboard și cel de activitate sunt oprite (EnableClipboardHistory = {Shown(cu)}, AllowClipboardHistory = {Shown(cp)}, EnableActivityFeed = {Shown(ap)}).",
                [@"HKCU\Software\Microsoft\Clipboard → EnableClipboardHistory = " + Shown(cu) + " (doar utilizatorul care a pornit aplicația)", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System → AllowClipboardHistory = " + Shown(cp),
                 @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System → EnableActivityFeed = " + Shown(ap), "ActivitiesCache.db prezent = " + (cache == "da" ? "da" : cache == "nu" ? "nu" : "necitit")],
                "Opriți istoricul clipboard (AllowClipboardHistory = 0) și istoricul de activitate (EnableActivityFeed = 0) prin politică.");
        }

        // P31 camera, microphone, speakers
        var camN = S("Hardware.Cameras"); var micN = S("Hardware.Microphones"); var spkN = S("Hardware.Speakers");
        if (camN == "" && micN == "" && spkN == "")
            Add(r, "P31", "Politici", "Cameră, microfon și difuzoare prezente și active", ControlStatus.Nedeterminat, "Lista dispozitivelor cameră, microfon și audio nu a putut fi citită." + NoRead);
        else
        {
            int cams = N("Hardware.Cameras"), mics = N("Hardware.Microphones"), spk = N("Hardware.Speakers");
            bool camDeny = S("Privacy.Webcam").Equals("Deny", StringComparison.OrdinalIgnoreCase), micDeny = S("Privacy.Microphone").Equals("Deny", StringComparison.OrdinalIgnoreCase);
            bool exposed = cams > 0 && !camDeny || mics > 0 && !micDeny;
            Add(r, "P31", "Politici", "Cameră, microfon și difuzoare prezente și active",
                cams + mics + spk == 0 ? ControlStatus.Conform : exposed && isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
                cams + mics + spk == 0 ? "Nu a fost găsită nicio cameră, niciun microfon și nicio ieșire audio activă."
                    : $"Active: {Ro.CountFem(cams, "cameră", "camere")} ({S("Hardware.CameraNames")}), {Ro.CountMasc(mics, "microfon", "microfoane")} ({S("Hardware.MicrophoneNames")}), {Ro.CountFem(spk, "ieșire audio", "ieșiri audio")}. Politica de acces (P19): cameră {(camDeny ? "blocată" : "NEblocată")}, microfon {(micDeny ? "blocat" : "NEblocat")}. " +
                      (exposed ? "Cel puțin una dintre ele este lăsată aplicațiilor." : "Politica blochează aplicațiile, dar dispozitivele rămân pornite în driver.") + " Acoperirea fizică a camerei și a microfonului se verifică separat (verificarea manuală M09).",
                [$"WMI Win32_PnPEntity (clasele Camera, Image, MEDIA, AudioEndpoint): {cams} + {mics} + {spk}", @"…\ConsentStore\webcam → Value = " + Shown(S("Privacy.Webcam")), @"…\ConsentStore\microphone → Value = " + Shown(S("Privacy.Microphone"))],
                "Dezactivați în BIOS camera și microfonul neaprobate, acoperiți camera și completați verificarea manuală M09.");
        }
    }

    private static void StationHardening(StationFacts f, ControlReport r)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        bool noInternet = f.Posture != StationPosture.Internet;   // Standalone and IsolatedNetwork: no path to the internet is expected
        string Shown(string v) => v == Absent ? "nesetat" : v;

        // N03 Wi-Fi
        var wifiCount = S("Wifi.AdapterCount");
        if (wifiCount == "")
            Add(r, "N03", "Rețea", "Adaptor Wi-Fi (rețea fără fir)", ControlStatus.Nedeterminat, "Lista adaptoarelor de rețea nu a putut fi citită." + NoRead);
        else
        {
            int wifi = int.TryParse(wifiCount, out var wc) ? wc : 0, wifiUp = int.TryParse(S("Wifi.UpCount"), out var wu) ? wu : 0;
            var profiles = S("Wlan.ProfileCount");
            var svc = S("Wlan.ServiceStart");
            var svcText = svc switch { "" => "starea serviciului Wi-Fi nu a putut fi citită", Absent => "serviciul Wi-Fi nu există pe această stație", "4" => "serviciul Wi-Fi este dezactivat", "2" => "serviciul Wi-Fi pornește singur", _ => "serviciul Wi-Fi poate porni la cerere" };
            int profN = int.TryParse(profiles, out var profParsed) ? profParsed : 0;
            var profText = profiles == "" ? $"profilurile Wi-Fi salvate nu au putut fi numărate ({(S("Wlan.ProfileUnread") is { Length: > 0 } why ? why : "sursa nu a fost citită")})" : profN == 1 ? "un profil Wi-Fi salvat" : $"{Ro.Count(profN, "profil", "profiluri")} Wi-Fi salvate";
            var status = wifi == 0 ? ControlStatus.Conform : noInternet && wifiUp > 0 ? ControlStatus.Neconform : ControlStatus.DeVerificat;
            Add(r, "N03", "Rețea", "Adaptor Wi-Fi (rețea fără fir)", status,
                wifi == 0 ? $"Stația nu are niciun adaptor Wi-Fi; {profText}."
                    : $"Stația are {Ro.CountMasc(wifi, "adaptor Wi-Fi", "adaptoare Wi-Fi")} ({(wifiUp == 0 ? "niciunul pornit acum" : wifiUp == 1 ? (wifi == 1 ? "pornit acum" : "unul pornit acum") : $"{wifiUp} pornite acum")}); {svcText}; {profText}." + (noInternet ? " Pe o stație izolată un adaptor Wi-Fi poate crea o legătură cu exteriorul." : ""),
                (wifi > 0 ? S("Wifi.Adapters").Split("; ", StringSplitOptions.RemoveEmptyEntries) : []).Concat([@"HKLM\SYSTEM\CurrentControlSet\Services\WlanSvc → Start = " + Shown(svc)]),
                "Adaptorul Wi-Fi se scoate fizic sau se dezactivează din BIOS; dacă este aprobat, notați documentul de aprobare.");
        }

        // N04 Bluetooth
        var btCount = S("Bluetooth.ComponentCount");
        if (btCount == "")
            Add(r, "N04", "Rețea", "Bluetooth", ControlStatus.Nedeterminat, "Lista dispozitivelor Bluetooth nu a putut fi citită." + NoRead);
        else
        {
            int bt = int.TryParse(btCount, out var bc) ? bc : 0;
            var svc = S("Bluetooth.ServiceStart");
            bool svcOff = svc is "4" or Absent;
            var status = bt == 0 ? ControlStatus.Conform : noInternet && !svcOff ? ControlStatus.Neconform : ControlStatus.DeVerificat;
            Add(r, "N04", "Rețea", "Bluetooth", status,
                bt == 0 ? "Stația nu are componente Bluetooth."
                    : $"Stația are Bluetooth ({Ro.CountFem(bt, "componentă", "componente")}: {S("Bluetooth.Components")}); serviciul Bluetooth este {(svcOff ? "dezactivat" : svc == "" ? "într-o stare necitită" : "permis să pornească")}." +
                      (noInternet ? " Prin Bluetooth se pot trimite fișiere fără cablu." : ""),
                [@"HKLM\SYSTEM\CurrentControlSet\Services\bthserv → Start = " + Shown(svc)],
                "Bluetooth trebuie oprit dacă organizația nu l-a aprobat în scris.");
        }

        // P15 Secure Boot and firmware mode
        var sb = S("SecureBoot.Enabled");
        var fw = S("Firmware.Type");
        var fwText = fw switch { "2" => "UEFI (modern)", "1" => "BIOS vechi (Legacy)", "" => "necitit", Absent => "necunoscut", var x => $"cod {x}" };
        Add(r, "P15", "Politici", "Pornire securizată (Secure Boot)",
            sb == "" ? ControlStatus.Nedeterminat : sb == "1" ? ControlStatus.Conform : sb == "0" || fw == "1" ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            sb == "" ? "Starea Secure Boot nu a putut fi citită." + NoRead
                : sb == "1" ? $"Secure Boot este pornit. Mod de pornire: {fwText}."
                : sb == "0" ? $"Secure Boot este OPRIT: stația poate fi pornită de pe un stick cu alt sistem. Mod de pornire: {fwText}."
                : fw == "1" ? "Stația pornește în mod BIOS vechi (Legacy), care nu are Secure Boot."
                : $"Nu s-a găsit informația Secure Boot în registru (stație veche sau fără UEFI). Mod de pornire: {fwText}.",
            [@"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State → UEFISecureBootEnabled = " + Shown(sb)],
            "Activați Secure Boot din setările UEFI/BIOS (protejate cu parolă).");

        // P16 screen lock
        var lockV = S("ScreenLock.InactivityTimeoutSecs");
        bool lockNum = int.TryParse(lockV, out var lockSecs);
        Add(r, "P16", "Politici", "Blocarea automată a ecranului la inactivitate",
            lockV == "" ? ControlStatus.Nedeterminat : lockV == Absent ? ControlStatus.DeVerificat : !lockNum ? ControlStatus.DeVerificat : lockSecs is > 0 and <= 900 ? ControlStatus.Conform : ControlStatus.Neconform,
            lockV == "" ? "Timpul de blocare nu a putut fi citit." + NoRead
                : lockV == Absent ? "Nu este setată o politică de blocare după inactivitate. Ecranul poate fi blocat prin screensaver cu parolă, pe care aplicația nu îl citește; verificați la stație."
                : !lockNum ? $"Valoare neașteptată: {lockV}."
                : lockSecs == 0 ? "Blocarea după inactivitate este dezactivată: dacă utilizatorul pleacă, ecranul rămâne deschis."
                : lockSecs <= 900 ? $"Ecranul se blochează după {Ro.Minutes(lockSecs / 60.0)} de inactivitate."
                : $"Ecranul se blochează abia după {Ro.Minutes(lockSecs / 60.0)}; regula este cel mult 15 minute.",
            [@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → InactivityTimeoutSecs = " + Shown(lockV)],
            "Setați blocarea la cel mult 15 minute (900 de secunde).");

        // P17 application allow-list
        var al = S("AppLocker.Collections");
        var alSvc = S("AppLocker.AppIDSvcStart");
        bool alEnforced = al.Contains("mod 1", StringComparison.Ordinal) && !al.Contains("mod 0", StringComparison.Ordinal) && !al.Contains("nesetat", StringComparison.Ordinal);
        Add(r, "P17", "Politici", "Listă albă de aplicații (AppLocker)",
            al == "" ? ControlStatus.Nedeterminat : al == Absent || al.StartsWith('(') ? ControlStatus.DeVerificat : alEnforced && alSvc == "4" ? ControlStatus.Neconform : alEnforced ? ControlStatus.Conform : ControlStatus.DeVerificat,
            al == "" ? "Politica AppLocker nu a putut fi citită." + NoRead
                : al == Absent ? "Nu s-a găsit nicio politică AppLocker. Lista albă App Control (WDAC) este verificată separat (P37)."
                : al.StartsWith('(') ? "Există o politică AppLocker, dar fără colecții de reguli."
                : alEnforced && alSvc == "4" ? $"Politica AppLocker cere blocarea ({al}), dar serviciul care o aplică (AppIDSvc) este dezactivat: lista albă nu funcționează."
                : alEnforced ? $"Lista albă blochează programele neaprobate: {al}."
                : $"Lista albă există, dar cel puțin o colecție este doar în mod audit (notează, nu blochează) sau fără mod setat: {al}.",
            [@"HKLM\SOFTWARE\Policies\Microsoft\Windows\SrpV2 → EnforcementMode: " + Shown(al), @"HKLM\SYSTEM\CurrentControlSet\Services\AppIDSvc → Start = " + Shown(alSvc)],
            "Pe o stație clasificată doar programele aprobate trebuie să poată porni: folosiți modul de blocare.");

        // P18 AutoPlay / AutoRun
        var nd = S("AutoRun.NoDriveTypeAutoRun");
        var na = S("AutoRun.NoAutorun");
        bool ndOk = int.TryParse(nd, out var ndv) && (ndv & 0x24) == 0x24;   // 0x04 removable drives + 0x20 CD-ROM
        Add(r, "P18", "Politici", "Pornirea automată de pe stick/CD (AutoPlay / AutoRun)",
            ndOk || na == "1" ? ControlStatus.Conform : nd == "" || na == "" ? ControlStatus.Nedeterminat : nd == Absent && na is Absent or "0" ? ControlStatus.DeVerificat : ControlStatus.Neconform,
            ndOk || na == "1" ? $"Pornirea automată a conținutului de pe stick și CD este oprită prin politică (NoDriveTypeAutoRun = {Shown(nd)}, NoAutorun = {Shown(na)})."
                : nd == "" || na == "" ? "Politica de pornire automată nu a putut fi citită." + NoRead
                : nd == Absent && na is Absent or "0" ? "Nu este setată nicio politică de oprire a pornirii automate; se aplică comportamentul implicit al Windows, care poate afișa sau porni conținutul unui stick introdus."
                : $"Pornirea automată nu este oprită pentru stick-uri și CD-uri (NoDriveTypeAutoRun = {Shown(nd)}, NoAutorun = {Shown(na)}).",
            [@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer → NoDriveTypeAutoRun = " + Shown(nd), @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer → NoAutorun = " + Shown(na)],
            "Setați NoDriveTypeAutoRun la 255 și NoAutorun la 1.");

        // P19 camera and microphone
        var cam = S("Privacy.Webcam");
        var mic = S("Privacy.Microphone");
        Add(r, "P19", "Politici", "Camera și microfonul blocate prin politică",
            cam == "" || mic == "" ? ControlStatus.Nedeterminat : cam.Equals("Deny", StringComparison.OrdinalIgnoreCase) && mic.Equals("Deny", StringComparison.OrdinalIgnoreCase) ? ControlStatus.Conform : ControlStatus.DeVerificat,
            cam == "" || mic == "" ? "Setările pentru cameră și microfon nu au putut fi citite." + NoRead
                : $"Acces la cameră: {(cam == Absent ? "nesetat" : cam)}; acces la microfon: {(mic == Absent ? "nesetat" : mic)}. „Deny” înseamnă blocat pentru toate aplicațiile. Aplicația nu poate spune dacă stația are fizic cameră sau microfon.",
            [@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam → Value = " + Shown(cam),
             @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone → Value = " + Shown(mic)],
            "Camera și microfonul se acoperă, se deconectează sau se dezactivează din BIOS; verificați fizic stația.");

        // P20 Recall / Copilot
        var dis = S("AI.DisableAIDataAnalysis");
        var allow = S("AI.AllowRecallEnablement");
        var cH = S("AI.CopilotTurnOffHklm");
        var cU = S("AI.CopilotTurnOffHkcu");
        bool recallOff = dis == "1" || allow == "0", recallOn = dis == "0" || allow == "1";
        bool copOff = cH == "1" || cU == "1", copOn = cH == "0" || cU == "0";
        Add(r, "P20", "Politici", "Windows Recall și Copilot oprite",
            dis == "" && allow == "" && cH == "" ? ControlStatus.Nedeterminat : recallOn || copOn ? ControlStatus.Neconform : recallOff && copOff ? ControlStatus.Conform : ControlStatus.DeVerificat,
            dis == "" && allow == "" && cH == "" ? "Politicile Recall/Copilot nu au putut fi citite." + NoRead
                : recallOn || copOn ? "Recall sau Copilot sunt permise explicit. Recall face poze periodice ale ecranului, adică copii neînregistrate ale documentelor."
                : recallOff && copOff ? "Politicile opresc atât Recall (poze periodice ale ecranului), cât și Copilot."
                : $"Nu sunt setate politici care să oprească Recall ({(recallOff ? "oprit" : "nesetat")}) și Copilot ({(copOff ? "oprit" : "nesetat")}). Funcțiile pot lipsi pe această versiune de Windows; confirmați la stație.",
            [@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI → DisableAIDataAnalysis = " + Shown(dis), @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI → AllowRecallEnablement = " + Shown(allow),
             @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot → TurnOffWindowsCopilot = " + Shown(cH),
             @"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot → TurnOffWindowsCopilot = " + Shown(cU) + " (doar utilizatorul care a pornit aplicația)"],
            "Opriți Recall (DisableAIDataAnalysis = 1) și Copilot prin politică.");

        // P21 LSA protection
        var ppl = S("LSA.RunAsPPL");
        bool lsaReadable = S("LSA.KeyReadable") == "da";
        Add(r, "P21", "Politici", "Protecția procesului care păstrează parolele (LSA)",
            ppl is "1" or "2" ? ControlStatus.Conform : ppl == "" && !lsaReadable ? ControlStatus.Nedeterminat : ppl == "0" ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            ppl is "1" or "2" ? $"Procesul LSA rulează protejat (RunAsPPL = {ppl})."
                : ppl == "" && !lsaReadable ? "Setarea de protecție LSA nu a putut fi citită." + NoRead
                : ppl == "0" ? "Protecția LSA este oprită explicit: un program rău-intenționat poate încerca să citească parolele din memorie."
                : "Protecția LSA nu este setată în registru. Pe unele versiuni recente de Windows poate fi activă implicit; aplicația nu o poate confirma.",
            [@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa → RunAsPPL = " + (ppl == "" ? "nesetat" : ppl)],
            "Setați RunAsPPL la 1 (cu blocare UEFI) și reporniți stația.");

        // P22 Credential Guard
        var dg = S("DeviceGuard.ServicesRunning");
        var dgList = dg.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Add(r, "P22", "Politici", "Credential Guard (parolele izolate în memorie)",
            dg == "" ? ControlStatus.Nedeterminat : dgList.Contains("1") ? ControlStatus.Conform : ControlStatus.DeVerificat,
            dg == "" ? "Starea Credential Guard nu a putut fi citită (poate necesita administrator sau ediția Windows nu o oferă)." + NoRead
                : dgList.Contains("1") ? $"Credential Guard rulează (servicii de securitate active: {dg}){(dgList.Contains("2") ? "; protecția integrității codului (HVCI) este activă" : "")}."
                : "Credential Guard nu rulează. Cerința oficială este formulată pentru stațiile din domeniu; pe o stație izolată decideți conform documentației de securitate.",
            [@"WMI root\Microsoft\Windows\DeviceGuard Win32_DeviceGuard → SecurityServicesRunning = " + dg]);

        // P23 PowerShell logging
        var sbl = S("PS.ScriptBlockLogging");
        var trn = S("PS.Transcription");
        Add(r, "P23", "Politici", "Înregistrarea comenzilor PowerShell",
            sbl == "" ? ControlStatus.Nedeterminat : sbl == "1" && trn == "1" ? ControlStatus.Conform : sbl == "1" ? ControlStatus.DeVerificat : ControlStatus.Neconform,
            sbl == "" ? "Politica de înregistrare PowerShell nu a putut fi citită." + NoRead
                : sbl == "1" && trn == "1" ? "Textul scripturilor PowerShell și transcrierea sesiunilor sunt înregistrate."
                : sbl == "1" ? $"Textul scripturilor este înregistrat, dar transcrierea sesiunilor nu ({(trn == "" ? "necitită" : Shown(trn))})."
                : "Înregistrarea scripturilor PowerShell nu este pornită prin politică: dacă cineva rulează un script, poate să nu rămână nicio urmă.",
            [@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging → EnableScriptBlockLogging = " + Shown(sbl),
             @"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell\Transcription → EnableTranscripting = " + Shown(trn)],
            "Activați ScriptBlockLogging și Transcription prin politică.");

        // P24 PowerShell 2.0
        var ps2 = S("PS.V2Engine");
        Add(r, "P24", "Politici", "PowerShell 2.0 (versiunea veche, fără jurnalizare) eliminat",
            ps2 == "" ? ControlStatus.Nedeterminat : ps2 == Absent ? ControlStatus.Conform : ps2.StartsWith("2.", StringComparison.Ordinal) ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            ps2 == "" ? "Prezența motorului PowerShell 2.0 nu a putut fi citită." + NoRead
                : ps2 == Absent ? "Nu s-a găsit motorul PowerShell 2.0."
                : ps2.StartsWith("2.", StringComparison.Ordinal) ? $"Motorul PowerShell 2.0 este instalat (versiune {ps2}); nu înregistrează scripturile și poate fi folosit pentru a ocoli protecțiile."
                : $"Cheia motorului PowerShell vechi există, cu versiunea {ps2}; verificați dacă funcția opțională PowerShell 2.0 este dezactivată.",
            [@"HKLM\SOFTWARE\Microsoft\PowerShell\1\PowerShellEngine → PowerShellVersion = " + Shown(ps2)],
            "Dezactivați funcția opțională Windows „PowerShell 2.0”.");

        // P25 data left on disk
        var cp = S("Disk.ClearPageFile");
        var hib = S("Disk.HibernateEnabled");
        var dump = S("Disk.CrashDumpEnabled");
        var problems = new List<string>();
        var unread = new List<string>();
        if (cp == "") unread.Add("ștergerea fișierului de paginare"); else if (cp != "1") problems.Add("fișierul de paginare NU se șterge la oprire (fragmente de documente pot rămâne pe disc)");
        if (hib is "" or Absent) unread.Add("hibernarea"); else if (hib != "0") problems.Add("hibernarea este permisă (tot conținutul memoriei, inclusiv documentele deschise, se scrie pe disc)");
        if (dump is "" or Absent) unread.Add("copia memoriei la o cădere"); else if (dump != "0") problems.Add($"după o cădere a sistemului, o copie a memoriei se salvează pe disc (CrashDumpEnabled = {dump})");
        Add(r, "P25", "Politici", "Date rămase pe disc (paginare, hibernare, copie a memoriei)",
            problems.Count > 0 ? ControlStatus.DeVerificat : unread.Count > 0 ? ControlStatus.Nedeterminat : ControlStatus.Conform,
            problems.Count > 0 ? "Pot rămâne date pe disc: " + string.Join("; ", problems) + "." + (unread.Count > 0 ? $" Necitit: {string.Join(", ", unread)}." : "")
                : unread.Count > 0 ? $"Nu s-a putut stabili: {string.Join(", ", unread)}." + NoRead
                : "Fișierul de paginare se șterge la oprire, hibernarea este oprită, iar copia memoriei la cădere este dezactivată.",
            [@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management → ClearPageFileAtShutdown = " + Shown(cp),
             @"HKLM\SYSTEM\CurrentControlSet\Control\Power → HibernateEnabled = " + Shown(hib), @"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl → CrashDumpEnabled = " + Shown(dump)],
            "Decideți conform documentației de securitate dacă aceste copii pe disc sunt acceptabile pentru informațiile tratate.");

        // P26 logon banner
        var cap = S("LegalNotice.Caption");
        var txt = S("LegalNotice.Text");
        bool noCap = cap is Absent or StationFactCollector.EmptyValue, noTxt = txt is Absent or StationFactCollector.EmptyValue;
        Add(r, "P26", "Politici", "Mesaj de avertizare la pornire (cu nivelul de clasificare)",
            cap == "" && txt == "" ? ControlStatus.Nedeterminat : noCap && noTxt ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            cap == "" && txt == "" ? "Mesajul de avertizare nu a putut fi citit." + NoRead
                : noCap && noTxt ? "La pornire nu apare niciun mesaj de avertizare că stația tratează informații clasificate."
                : $"La pornire apare titlul „{(noCap ? "—" : cap)}” și un text de {Ro.Count(noTxt ? 0 : txt.Length, "caracter", "caractere")}. Comparați-l cu marcajul de clasificare aprobat pentru stație.",
            [@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → LegalNoticeCaption = " + (noCap ? "nesetat" : cap),
             @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → LegalNoticeText = " + (noTxt ? "nesetat" : txt)],
            "Setați un mesaj care indică nivelul maxim de clasificare al stației.");

        // P27 time source
        var tType = S("Time.Type");
        var tNtp = S("Time.NtpServer");
        var tMeaning = tType switch
        {
            "NTP" => "se sincronizează cu un server de timp NTP",
            "NT5DS" => "se sincronizează cu domeniul",
            "NoSync" => "nu se sincronizează deloc (ceasul merge liber)",
            "AllSync" => "folosește toate sursele disponibile",
            Absent => "nesetat",
            "" => "necitit",
            var x => $"tip „{x}”",
        };
        Add(r, "P27", "Politici", "Sursa de timp a stației",
            tType == "" && tNtp == "" ? ControlStatus.Nedeterminat : ControlStatus.DeVerificat,
            tType == "" && tNtp == "" ? "Setările sursei de timp nu au putut fi citite." + NoRead
                : $"Ceasul stației: {tMeaning}; server de timp configurat: {(tNtp == Absent ? "niciunul" : tNtp == "" ? "necitit" : tNtp)}. Aplicația nu măsoară abaterea ceasului; comparați ora stației cu ceasul de referință la control.",
            [@"HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters → Type = " + Shown(tType), @"HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters → NtpServer = " + Shown(tNtp)],
            "Ora din jurnale este de încredere doar dacă ceasul este verificat periodic față de o sursă aprobată.");

        // A08 copies to removable media recorded?
        var hot = S("Storage.HotPlugSecureOpen");
        if (f.AuditPolicy is { } ap)
        {
            var rs = ap.GetValueOrDefault("Removable Storage");
            bool logs = (rs & AuditSetting.Success) == AuditSetting.Success;
            Add(r, "A08", "Audit", "Copierea fișierelor pe stick-uri este înregistrată",
                logs ? ControlStatus.Conform : ControlStatus.Neconform,
                logs ? $"Stația notează accesul la fișiere de pe suporturi amovibile (auditare: {rs}). Pe unele versiuni de Windows este nevoie și de HotPlugSecureOpen (acum: {Shown(hot)}); verificați la un test de copiere."
                     : "Stația NU notează ce fișiere se copiază pe stick-uri. Dacă s-a copiat ceva, nu vom putea afla ce anume.",
                [$"Removable Storage: {rs}", @"HKLM\SYSTEM\CurrentControlSet\Control\Storage → HotPlugSecureOpen = " + Shown(hot)],
                "Cereți administratorului să pornească auditarea „Removable Storage” (succes).");
        }
        else Add(r, "A08", "Audit", "Copierea fișierelor pe stick-uri este înregistrată", ControlStatus.Nedeterminat, "Politica de audit nu a putut fi citită." + NoRead);

        // S03 Windows version and last update (facts; support status needs the organisation's offline table)
        var build = S("OS.Build");
        if (build == "")
            Add(r, "S03", "Software", "Versiunea de Windows și ultima actualizare", ControlStatus.Nedeterminat, "Versiunea de Windows nu a putut fi citită." + NoRead);
        else
        {
            var family = int.TryParse(build, out var bn) && bn >= 22000 ? "Windows 11" : "Windows 10 sau mai vechi";
            var ubr = S("OS.UBR");
            var upd = S("OS.LastUpdateDate");
            var updText = upd == "" ? "data ultimei actualizări nu a putut fi citită" : upd == Absent ? "nu este înregistrată nicio actualizare" : $"ultima actualizare instalată: {upd}";
            Add(r, "S03", "Software", "Versiunea de Windows și ultima actualizare", ControlStatus.DeVerificat,
                $"{family}, versiunea {Shown(S("OS.DisplayVersion"))}, build {build}{(ubr is "" or Absent ? "" : "." + ubr)}; {updText}. Aplicația nu are tabelul oficial al versiunilor încă suportate; comparați cu registrul actualizărilor offline.",
                [@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion → ProductName = " + Shown(S("OS.ProductName")), @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion → DisplayVersion = " + Shown(S("OS.DisplayVersion")),
                 @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion → CurrentBuild = " + build, @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion → UBR = " + Shown(ubr), "WMI Win32_QuickFixEngineering → InstalledOn = " + Shown(upd)],
                "Verificați că versiunea mai primește actualizări de securitate și că ultima actualizare corespunde procedurii.");
        }
    }

    // ---- air-gap station requirements (comparison 2026-10-12, rows A2, A3, A4, A5, A7, A14, A16, B8, C8) ------------

    private static readonly Dictionary<int, string> ProtectorNames = new()
    {
        [1] = "TPM", [2] = "cheie pe stick", [3] = "parolă de recuperare", [4] = "TPM și PIN", [5] = "TPM și cheie pe stick", [6] = "TPM, PIN și cheie pe stick",
        [7] = "cheie publică", [8] = "parolă", [9] = "certificat TPM", [10] = "cheie protejată de Windows (CNG)",
    };

    private static string ProtectionText(int? p) => p switch { 1 => "protejat", 0 => "NEPROTEJAT", 2 => "stare necunoscută (de exemplu suport criptat încă blocat)", _ => "stare necitită" };
    private static string ConversionText(int c) => c switch
    {
        0 => "necriptat", 1 => "criptat complet", 2 => "criptare în curs", 3 => "decriptare în curs", 4 => "criptare oprită temporar", 5 => "decriptare oprită temporar", _ => $"stare de criptare {c}",
    };
    private static string KindText(VolumeKind k) => k switch { VolumeKind.Sistem => "discul de sistem", VolumeKind.Fix => "disc fix", _ => "suport amovibil" };

    /// <summary>"D: (disc fix): protejat, criptat complet, protecții: TPM și PIN, parolă de recuperare".</summary>
    internal static string VolumeLine(VolumeFact v) =>
        $"{v.Drive} ({KindText(v.Kind)}): {ProtectionText(v.ProtectionStatus)}" + (v.ConversionStatus is { } c ? $", {ConversionText(c)}" : "") +
        (v.Protectors is { Count: > 0 } p ? $", protecții: {string.Join(", ", p.Select(x => ProtectorNames.GetValueOrDefault(x, $"tip {x}")))}"
            : v.Protectors is { Count: 0 } ? ", nicio protecție configurată" : v.ProtectionStatus is not null ? ", tipul protecțiilor nu a putut fi citit" : "") +
        (v.Note.Length > 0 ? $" ({v.Note})" : "");

    private static string ShortChannel(string channel) => channel switch
    {
        StationFactCollector.AppLockerExeChannel or StationFactCollector.AppLockerMsiChannel => "AppLocker",
        StationFactCollector.CodeIntegrityChannel => "CodeIntegrity",
        StationFactCollector.DefenderChannel => "Defender",
        "Microsoft-Windows-Partition/Diagnostic" => "Partition",
        _ => channel,
    };
    private static string EvShort(LogFact l) => $"{ShortChannel(l.Channel)} {l.Id} RecordID {l.RecordId} ({l.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC)";
    private static string FileOf(LogFact l) => V(l, "FullFilePath") is { Length: > 0 } a ? a : V(l, "FilePath") is { Length: > 0 } b ? b : V(l, "File Name") is { Length: > 0 } c ? c : "(fișier necunoscut)";

    private static void AirGapStation(StationFacts f, ControlReport r)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        bool isolated = f.Posture == StationPosture.Standalone;
        bool noInternet = f.Posture != StationPosture.Internet;   // Standalone and IsolatedNetwork: no path to the internet is expected
        string Shown(string v) => v == Absent ? "nesetat" : v == "" ? "necitit" : v;

        // P32 (A2) BitLocker on every fixed volume, PIN at boot.
        const string p28 = "BitLocker pe toate discurile fixe, cu PIN la pornire";
        if (f.Volumes is not { } vols)
            Add(r, "P32", "Politici", p28, ControlStatus.Nedeterminat,
                "Starea BitLocker a discurilor nu a putut fi citită." + (S("BitLocker.Volumes.Error") is { Length: > 0 } ve ? " " + ve + "." : "") + NoRead);
        else
        {
            var fixedVols = vols.Where(v => v.Kind != VolumeKind.Amovibil).ToList();
            var unprotected = fixedVols.Where(v => v.ProtectionStatus == 0).ToList();
            var unknown = fixedVols.Where(v => v.ProtectionStatus is null or 2).ToList();
            var sysVol = fixedVols.FirstOrDefault(v => v.Kind == VolumeKind.Sistem);
            bool? pin = sysVol is { ProtectionStatus: 1, Protectors: { } pr } ? pr.Any(x => x is 4 or 6) : null;
            var problems = unprotected.Select(v => $"{v.Drive} ({KindText(v.Kind)}) NU este criptat").ToList();
            if (pin == false) problems.Add($"discul de sistem {sysVol!.Drive} este criptat, dar pornește fără PIN: dacă stația este furată întreagă, discul poate fi citit");
            var status = fixedVols.Count == 0 ? ControlStatus.Nedeterminat : problems.Count > 0 ? ControlStatus.Neconform : unknown.Count > 0 ? ControlStatus.Nedeterminat
                : pin is null ? ControlStatus.DeVerificat : ControlStatus.Conform;
            Add(r, "P32", "Politici", p28, status,
                fixedVols.Count == 0 ? "Windows nu a raportat niciun disc fix." + NoRead
                : problems.Count > 0 ? char.ToUpperInvariant(problems[0][0]) + string.Join("; ", problems)[1..] + "."
                : unknown.Count > 0 ? $"Starea BitLocker nu a putut fi citită pentru: {string.Join(", ", unknown.Select(v => v.Drive))}." + NoRead
                : pin is null ? (fixedVols.Count == 1 ? "Discul fix este criptat" : $"Toate cele {Ro.Count(fixedVols.Count, "disc fix", "discuri fixe")} sunt criptate") + ", dar tipul protecției discului de sistem nu a putut fi citit; verificați la stație că se cere un PIN la pornire."
                : (fixedVols.Count == 1 ? "Discul fix este criptat" : $"Toate cele {Ro.Count(fixedVols.Count, "disc fix", "discuri fixe")} sunt criptate") + ", iar discul de sistem cere PIN la pornire.",
                vols.Select(VolumeLine).Prepend("WMI Win32_EncryptableVolume (ProtectionStatus, ConversionStatus, GetKeyProtectorType)"),
                "Criptați fiecare disc fix cu BitLocker și adăugați discului de sistem protecția „TPM și PIN” (PIN de cel puțin 6 cifre).");
        }

        // P33 (A3) TPM present, enabled, ready, version.
        var tpm = S("TPM.Present");
        var tpmVersion = S("TPM.SpecVersion").Split(',')[0].Trim();
        bool tpmOn = S("TPM.Enabled") == "True" && S("TPM.Activated") == "True";
        var tpmReady = S("TPM.Ready");
        Add(r, "P33", "Politici", "Cipul de securitate TPM prezent și pregătit",
            tpm == "" ? ControlStatus.Nedeterminat : tpm == "nu" || !tpmOn || tpmReady == "False" ? ControlStatus.Neconform
                : tpmVersion.StartsWith("1.", StringComparison.Ordinal) || tpmReady == "" ? ControlStatus.DeVerificat : ControlStatus.Conform,
            tpm == "" ? "Starea cipului TPM nu a putut fi citită." + NoRead
                : tpm == "nu" ? "Stația nu are cip TPM sau acesta este oprit din BIOS: criptarea discului nu se poate lega de placa de bază."
                : !tpmOn ? "Cipul TPM există, dar este dezactivat sau neactivat din BIOS."
                : tpmReady == "False" ? "Cipul TPM este pornit, dar nu este pregătit de folosire (nu a fost inițializat)."
                : tpmVersion.StartsWith("1.", StringComparison.Ordinal) ? $"Cipul TPM are versiunea veche {tpmVersion}; cerința este TPM 2.0."
                : tpmReady == "" ? $"Cipul TPM {tpmVersion} este pornit; dacă este pregătit de folosire nu s-a putut afla."
                : $"Cipul TPM {tpmVersion} este prezent, pornit și pregătit.",
            [$"WMI Win32_Tpm → IsEnabled_InitialValue = {Shown(S("TPM.Enabled"))}, IsActivated_InitialValue = {Shown(S("TPM.Activated"))}, SpecVersion = {Shown(S("TPM.SpecVersion"))}, IsReady = {Shown(tpmReady)}"],
            "Porniți și inițializați cipul TPM 2.0 din setările UEFI/BIOS (protejate cu parolă).");

        // P34 (A3) Kernel DMA protection.
        var dma = S("DMA.KernelProtection");
        var dmaPol = S("DMA.EnumerationPolicy");
        var dmaPolText = dmaPol switch { "0" => "blochează toate dispozitivele externe care nu suportă protecția", "1" => "le permite doar după autentificare", "2" => "le permite pe toate", Absent => "nesetată", "" => "necitită", var x => $"valoarea {x}" };
        Add(r, "P34", "Politici", "Protecția memoriei la porturile Thunderbolt/USB4 (Kernel DMA Protection)",
            dma == "" ? ControlStatus.Nedeterminat : dma == "1" ? (dmaPol == "2" ? ControlStatus.DeVerificat : ControlStatus.Conform) : isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            dma == "" ? "Starea protecției DMA nu a putut fi citită." + NoRead
                : dma == "1" ? $"Protecția DMA în nucleu este pornită; politica pentru dispozitivele externe incompatibile: {dmaPolText}."
                : "Protecția DMA în nucleu este OPRITĂ sau stația nu o are: un dispozitiv conectat la un port Thunderbolt/USB4 poate citi memoria stației. Dacă stația nu are astfel de porturi, notați acest lucru în procesul-verbal.",
            ["Windows (informația afișată de msinfo32 ca „Kernel DMA Protection”) = " + (dma == "1" ? "pornită" : dma == "0" ? "oprită" : "necitită"),
             @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Kernel DMA Protection → DeviceEnumerationPolicy = " + Shown(dmaPol)],
            "Porniți protecția DMA din UEFI/BIOS (VT-d / IOMMU); dacă nu există, porturile Thunderbolt se dezactivează sau se sigilează.");

        // P35 (A3) virtualization-based security.
        var vbs = S("DeviceGuard.VbsStatus");
        Add(r, "P35", "Politici", "Securitatea bazată pe virtualizare (VBS) rulează",
            vbs == "" ? ControlStatus.Nedeterminat : vbs == "2" ? ControlStatus.Conform : vbs == "1" ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            vbs == "" ? "Starea securității bazate pe virtualizare nu a putut fi citită." + NoRead
                : vbs == "2" ? $"Securitatea bazată pe virtualizare rulează (servicii active: {Shown(S("DeviceGuard.ServicesRunning"))})."
                : vbs == "1" ? "Securitatea bazată pe virtualizare este cerută în configurare, dar NU rulează; de obicei virtualizarea este oprită din BIOS."
                : "Securitatea bazată pe virtualizare nu este pornită: Credential Guard și protecția integrității codului nu pot funcționa.",
            [@"WMI root\Microsoft\Windows\DeviceGuard Win32_DeviceGuard → VirtualizationBasedSecurityStatus = " + Shown(vbs)],
            "Porniți virtualizarea în UEFI/BIOS și securitatea bazată pe virtualizare prin politică.");

        // P36 (A4) password on wake.
        string pAc = S("Wake.PolicyAC"), pDc = S("Wake.PolicyDC"), eAc = S("Wake.EffectiveAC"), eDc = S("Wake.EffectiveDC"), dli = S("Wake.DelayLockInterval");
        bool polOn = pAc == "1" && pDc == "1", polOff = pAc == "0" || pDc == "0", effOff = eAc == "0" || eDc == "0", never = dli is "-1" or "4294967295";
        bool wakeUnread = pAc == "" && pDc == "" && eAc == "" && eDc == "";
        Add(r, "P36", "Politici", "Parolă cerută la revenirea din somn",
            wakeUnread ? ControlStatus.Nedeterminat : polOff ? ControlStatus.Neconform : polOn ? ControlStatus.Conform : effOff || never ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            wakeUnread ? "Setarea de parolă la trezire nu a putut fi citită." + NoRead
                : polOff ? "Politica stației spune că la revenirea din somn NU se cere parola: oricine trezește stația ajunge în sesiunea deschisă."
                : polOn ? "Politica stației cere parola la revenirea din somn, atât în priză, cât și pe baterie."
                : effOff || never ? "La revenirea din somn NU se cere parola (setarea planului de alimentare sau „Solicitare autentificare: niciodată”): oricine trezește stația ajunge în sesiunea deschisă."
                : "Stația cere acum parola la revenirea din somn, dar regula nu este impusă prin politică, deci utilizatorul o poate schimba.",
            [$@"HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings\0e796bdb-100d-47d6-a2d5-f7d2daa51f51 → ACSettingIndex = {Shown(pAc)}, DCSettingIndex = {Shown(pDc)}",
             $"Planul de alimentare activ, CONSOLELOCK → în priză = {Shown(eAc)}, pe baterie = {Shown(eDc)}",
             @"HKCU\Control Panel\Desktop → DelayLockInterval = " + Shown(dli) + " (doar utilizatorul care a pornit aplicația)"],
            "Setați prin politică „Require a password when a computer wakes” (în priză și pe baterie) = Enabled.");

        // P37 (A5) App Control for Business (WDAC).
        var um = S("CI.UserModeEnforcement");
        var al = S("AppLocker.Collections");
        bool appLockerBlocks = al.Contains("mod 1", StringComparison.Ordinal) && !al.Contains("mod 0", StringComparison.Ordinal) && !al.Contains("nesetat", StringComparison.Ordinal) && S("AppLocker.AppIDSvcStart") != "4";
        Add(r, "P37", "Politici", "Listă albă de aplicații App Control (WDAC)",
            um == "" ? ControlStatus.Nedeterminat : um == "2" ? ControlStatus.Conform : um == "1" ? ControlStatus.DeVerificat : appLockerBlocks ? ControlStatus.DeVerificat : ControlStatus.Neconform,
            um == "" ? "Modul App Control nu a putut fi citit." + NoRead
                : um == "2" ? "App Control blochează programele neaprobate (mod de aplicare)."
                : um == "1" ? "App Control este doar în mod audit: notează programele neaprobate, dar nu le oprește."
                : appLockerBlocks ? "App Control nu se folosește pentru programe; lista albă este asigurată de AppLocker (vezi P17)."
                : "Nici App Control, nici AppLocker nu opresc programele neaprobate: pe stație poate porni orice program adus pe un stick.",
            [@"WMI Win32_DeviceGuard → UsermodeCodeIntegrityPolicyEnforcementStatus = " + Shown(um) + ", CodeIntegrityPolicyEnforcementStatus = " + Shown(S("CI.KernelEnforcement")),
             @"C:\Windows\System32\CodeIntegrity\CiPolicies\Active → fișiere de politică active: " + Shown(S("WDAC.ActivePolicyFiles")) + "; SIPolicy.p7b: " + Shown(S("WDAC.LegacyPolicy"))],
            "Pe o stație clasificată doar programele aprobate trebuie să poată porni: App Control sau AppLocker în mod de blocare.");

        // A09 (A5) blocked or would-be-blocked programs in the control period.
        var allow = Logs(f, StationFactCollector.AppLockerExeChannel, 8003, 8004).Concat(Logs(f, StationFactCollector.AppLockerMsiChannel, 8006, 8007))
                   .Concat(Logs(f, StationFactCollector.CodeIntegrityChannel, 3076, 3077)).OrderBy(l => l.TimeUtc).ToList();
        bool allowReadable = ChannelReadable(f, StationFactCollector.AppLockerExeChannel) || ChannelReadable(f, StationFactCollector.AppLockerMsiChannel) || ChannelReadable(f, StationFactCollector.CodeIntegrityChannel);
        int blocked = allow.Count(l => l.Id is 8004 or 8007 or 3077), audited = allow.Count - blocked;
        bool hasAllowList = (al.Length > 0 && al != Absent && !al.StartsWith('(')) || um is "1" or "2";
        foreach (var l in allow.Where(l => l.Id is 8004 or 8007 or 3077))
            r.Actions.Add(new ActionEntry(l.TimeUtc, l.UserSid, "pornire de program oprită de lista albă", FileOf(l), EvShort(l)));
        Add(r, "A09", "Audit", "Programe oprite sau semnalate de lista albă",
            !allowReadable ? ControlStatus.Nedeterminat : allow.Count > 0 ? ControlStatus.DeVerificat : hasAllowList ? ControlStatus.Conform : ControlStatus.DeVerificat,
            !allowReadable ? "Jurnalele listei albe (AppLocker, CodeIntegrity) nu au putut fi citite." + NoRead
                : allow.Count > 0 ? $"{LogAnalyzer.Dfir.Language.RoText.Count(blocked, "pornire oprită", "porniri oprite")} și {LogAnalyzer.Dfir.Language.RoText.Count(audited, "program care ar fi fost oprit", "programe care ar fi fost oprite")} (mod audit: au rulat totuși) în perioada controlată. Cineva a încercat să pornească programe neaprobate; aflați cine și de ce."
                : hasAllowList ? "Nicio încercare de a porni un program neaprobat nu a fost înregistrată în perioada controlată."
                : "Stația nu are o listă albă activă, deci jurnalele nu pot înregistra încercări de a porni programe neaprobate (vezi P17 și P37).",
            allow.Select(l => $"{EvShort(l)}: {FileOf(l)}" + (l.UserSid.Length > 0 ? $" (utilizator {l.UserSid})" : V(l, "TargetUser") is { Length: > 0 } tu ? $" (utilizator {tu})" : "")),
            "Fiecare încercare de a porni un program neaprobat se explică de utilizator; programele necesare se aprobă în scris și se adaugă în listă.");

        // P38 (A7) last full and quick antivirus scan.
        var full = S("Defender.FullScanEndUtc");
        var quick = S("Defender.QuickScanEndUtc");
        double? Age(string v) => DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? (f.CollectedUtc - t).TotalDays : null;
        var fullAge = Age(full);
        var quickAge = Age(quick);
        string When(string v, double? age) => v == Absent ? "niciodată" : age is { } a ? $"{DateTimeOffset.Parse(v, CultureInfo.InvariantCulture):yyyy-MM-dd} (acum {Ro.Days(a)})" : "necitită";
        Add(r, "P38", "Politici", "Ultima scanare antivirus completă și rapidă",
            full == "" && quick == "" ? ControlStatus.Nedeterminat : full == Absent ? ControlStatus.Neconform
                : fullAge is null || fullAge > 30 || quick == Absent || quickAge > 7 ? ControlStatus.DeVerificat : ControlStatus.Conform,
            full == "" && quick == "" ? "Data ultimelor scanări antivirus nu a putut fi citită (poate exista alt antivirus)." + NoRead
                : full == Absent ? $"Microsoft Defender nu a făcut NICIODATĂ o scanare completă a stației. Ultima scanare rapidă: {When(quick, quickAge)}."
                : $"Ultima scanare completă: {When(full, fullAge)}; ultima scanare rapidă: {When(quick, quickAge)}." +
                  (fullAge is null || fullAge > 30 || quick == Absent || quickAge > 7 ? " Comparați cu frecvența cerută de procedură (reper: scanare completă lunar, rapidă săptămânal)." : ""),
            ["MSFT_MpComputerStatus → FullScanEndTime = " + Shown(full) + ", QuickScanEndTime = " + Shown(quick) + ", AMProductVersion = " + Shown(S("Defender.ProductVersion"))],
            "Programați o scanare completă periodică, după procedură, și notați-o în registrul stației.");

        // P39 (A7) antivirus registered in Windows Security Center (including another vendor's).
        if (f.AntivirusProducts is not { } avs)
            Add(r, "P39", "Politici", "Antivirus înregistrat în Centrul de securitate Windows", ControlStatus.Nedeterminat,
                "Lista antivirusurilor înregistrate în Windows nu a putut fi citită (pe Windows Server această listă nu există)." + NoRead);
        else
        {
            // [NESIGUR] productState decoding (bits 12-15 = on, bits 4-7 = signatures out of date) is community documentation, not an official Microsoft source.
            static bool On(AntivirusProductFact a) => a.ProductState is { } s && ((s >> 12) & 0xF) == 1;
            static bool Current(AntivirusProductFact a) => a.ProductState is { } s && ((s >> 4) & 0xF) == 0;
            string Line(AntivirusProductFact a) => $"{a.Name}: {(a.ProductState is null ? "stare necunoscută" : $"{(On(a) ? "pornit" : "oprit")}, semnături {(Current(a) ? "la zi" : "vechi")}")} (productState = {a.ProductState?.ToString(CultureInfo.InvariantCulture) ?? "—"})";
            var on = avs.Where(On).ToList();
            Add(r, "P39", "Politici", "Antivirus înregistrat în Centrul de securitate Windows",
                avs.Count == 0 || on.Count == 0 ? ControlStatus.Neconform : on.Any(Current) ? ControlStatus.Conform : ControlStatus.DeVerificat,
                avs.Count == 0 ? "Niciun antivirus nu este înregistrat în Centrul de securitate Windows."
                    : on.Count == 0 ? $"Antivirusurile înregistrate sunt OPRITE: {string.Join("; ", avs.Select(Line))}."
                    : on.Any(Current) ? $"Antivirus pornit: {string.Join("; ", on.Where(Current).Select(a => a.Name))}."
                    : $"Antivirusul este pornit, dar Windows raportează semnături vechi: {string.Join("; ", on.Select(Line))}.",
                avs.Select(Line).Prepend(@"WMI root\SecurityCenter2 AntiVirusProduct (displayName, productState)"),
                "Pe stație trebuie să ruleze un antivirus pornit, cu semnături actualizate după procedura offline.");
        }

        // N06 (A14) shared folders other than the default administrative shares.
        if (f.Shares is not { } shares)
            Add(r, "N06", "Rețea", "Foldere partajate în rețea", ControlStatus.Nedeterminat, "Lista folderelor partajate nu a putut fi citită." + NoRead);
        else
        {
            var extra = shares.Where(x => !x.IsDefaultAdminShare).ToList();
            Add(r, "N06", "Rețea", "Foldere partajate în rețea",
                extra.Count == 0 ? ControlStatus.Conform : isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
                extra.Count == 0 ? "Stația nu partajează niciun folder sau imprimantă în rețea, în afara partajărilor administrative implicite ale Windows (C$, ADMIN$, IPC$)."
                    : $"Stația partajează în rețea: {string.Join(", ", extra.Select(x => x.Name))}." + (isolated ? " Pe o stație izolată nu trebuie să existe partajări." : ""),
                shares.Select(x => $"{x.Name} → {(x.Path.Length > 0 ? x.Path : "—")} ({(x.IsDefaultAdminShare ? "partajare administrativă implicită" : "partajare creată")})").Prepend("WMI Win32_Share (Name, Path, Type)"),
                "Ștergeți partajările care nu sunt aprobate în scris.");
        }

        // N07 (A14) Remote Assistance.
        string raPol = S("RA.PolicyAllowToGetHelp"), raCtl = S("RA.AllowToGetHelp");
        Add(r, "N07", "Rețea", "Asistența la distanță (Remote Assistance)",
            raPol == "" && raCtl == "" ? ControlStatus.Nedeterminat : raPol == "0" ? ControlStatus.Conform : raPol == "1" || raCtl == "1" ? (isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat)
                : raCtl == "0" ? ControlStatus.Conform : ControlStatus.DeVerificat,
            raPol == "" && raCtl == "" ? "Setarea Asistenței la distanță nu a putut fi citită." + NoRead
                : raPol == "0" ? "Asistența la distanță este interzisă prin politică."
                : raPol == "1" || raCtl == "1" ? "Asistența la distanță este PERMISĂ: altcineva poate prelua stația de la distanță, la invitație." + (isolated ? " Pe o stație izolată trebuie dezactivată." : "")
                : raCtl == "0" ? "Asistența la distanță este oprită din setările stației (nu prin politică)."
                : "Setarea Asistenței la distanță nu există în registru; pe multe instalări Windows o permite implicit. Verificați în Proprietăți sistem → Acces de la distanță.",
            [@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services → fAllowToGetHelp = " + Shown(raPol), @"HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance → fAllowToGetHelp = " + Shown(raCtl)],
            "Interziceți prin politică „Configure Solicited Remote Assistance” (Disabled).");

        // N08 (A14) WinRM.
        string wStart = S("WinRM.ServiceStart"), wLis = S("WinRM.Listeners"), wState = S("WinRM.State");
        bool listening = wLis is not ("" or Absent or "(niciuna)"), running = wState.Equals("Running", StringComparison.OrdinalIgnoreCase);
        var wStartText = wStart switch { "2" => "pornire automată", "3" => "pornire manuală", "4" => "dezactivat", Absent => "serviciul nu există", "" => "necitit", var x => $"pornire {x}" };
        Add(r, "N08", "Rețea", "Administrarea la distanță (WinRM)",
            wStart == "" && wLis == "" && wState == "" ? ControlStatus.Nedeterminat : listening && running ? (isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat)
                : listening || running ? ControlStatus.DeVerificat
                : wStart is "4" || wStart == Absent || wState.Length > 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            wStart == "" && wLis == "" && wState == "" ? "Starea serviciului WinRM nu a putut fi citită." + NoRead
                : listening && running ? $"Administrarea la distanță (WinRM) rulează și are configurate puncte de ascultare ({wLis}): din rețea se pot rula comenzi pe stație." + (isolated ? " Pe o stație izolată trebuie oprită." : "")
                : listening && wState.Length == 0 ? $"Serviciul WinRM are configurate puncte de ascultare ({wLis}), dar dacă rulează acum nu s-a putut afla ({wStartText}); dacă rulează, stația acceptă administrare de la distanță." + (isolated ? " Pe o stație izolată ștergeți punctele de ascultare și dezactivați serviciul." : "")
                : listening ? $"Serviciul WinRM este oprit acum, dar are configurate puncte de ascultare ({wLis}); dacă pornește, stația acceptă administrare de la distanță ({wStartText})." + (isolated ? " Pe o stație izolată ștergeți punctele de ascultare și dezactivați serviciul." : "")
                : running ? $"Serviciul WinRM rulează, dar nu are puncte de ascultare configurate ({wStartText}); stația nu acceptă încă administrare de la distanță, dar un punct de ascultare adăugat o pornește imediat." + (isolated ? " Pe o stație izolată serviciul trebuie oprit." : "")
                : wStart is "4" || wStart == Absent ? $"Administrarea la distanță (WinRM) este oprită ({wStartText})."
                : wState.Length > 0 ? $"Serviciul WinRM nu rulează și nu are puncte de ascultare configurate ({wStartText})."
                : $"Serviciul WinRM: {wStartText}; dacă rulează acum nu s-a putut afla.",
            [@"HKLM\SYSTEM\CurrentControlSet\Services\WinRM → Start = " + Shown(wStart), @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WSMAN\Listener → " + Shown(wLis), "WMI Win32_Service WinRM → State = " + Shown(wState)],
            "Dezactivați serviciul WinRM pe stația izolată.");

        // N09 (A14) netsh portproxy.
        var pp = S("PortProxy.Entries");
        Add(r, "N09", "Rețea", "Redirecționări de porturi (portproxy)",
            pp == "" ? ControlStatus.Nedeterminat : pp == Absent ? ControlStatus.Conform : isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            pp == "" ? "Redirecționările de porturi nu au putut fi citite." + NoRead
                : pp == Absent ? "Nu există redirecționări de porturi (portproxy)."
                : $"Stația redirecționează conexiuni de rețea către alte adrese: {pp}. Este o metodă folosită pentru a ocoli separarea rețelelor.",
            [@"HKLM\SYSTEM\CurrentControlSet\Services\PortProxy → " + Shown(pp)],
            "Ștergeți redirecționările neaprobate și aflați cine le-a creat.");

        // N10 (A14) Internet Connection Sharing.
        string ics = S("ICS.Shared"), icsPol = S("ICS.PolicyShowUI"), icsSvc = S("ICS.ServiceStart");
        Add(r, "N10", "Rețea", "Partajarea conexiunii la Internet (ICS)",
            ics == "" ? (icsPol == "0" ? ControlStatus.DeVerificat : ControlStatus.Nedeterminat) : ics.StartsWith("da", StringComparison.Ordinal) ? (noInternet ? ControlStatus.Neconform : ControlStatus.DeVerificat) : ControlStatus.Conform,
            ics == "" ? (icsPol == "0" ? "Starea partajării nu a putut fi citită; politica interzice activarea ei." : "Starea partajării conexiunii nu a putut fi citită." + NoRead)
                : ics.StartsWith("da", StringComparison.Ordinal) ? $"Partajarea conexiunii la Internet este PORNITĂ ({ics[3..].Trim()}): stația face legătura între două rețele."
                : "Partajarea conexiunii la Internet nu este pornită pe nicio conexiune." + (icsPol == "0" ? " Politica interzice și activarea ei." : ""),
            ["Interfața Windows de partajare (HNetCfg.HNetShare) → " + Shown(ics), @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Network Connections → NC_ShowSharedAccessUI = " + Shown(icsPol),
             @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess → Start = " + Shown(icsSvc)],
            "Opriți partajarea și interziceți-o prin politică („Prohibit use of Internet Connection Sharing”).");

        // A10, A14, A15: classified folders - audit rules, the sources that make exits visible, and the accesses and exits themselves.
        ClassifiedFolders(f, r);

        // D02 (B8) BitLocker To Go on each connected removable volume; write access to unencrypted media.
        var rdv = S("BitLocker.RDVDenyWriteAccess");
        bool deny = rdv == "1";
        var removable = f.Volumes?.Where(v => v.Kind == VolumeKind.Amovibil).ToList();
        var letters = removable?.Select(v => v.Drive).ToList() ?? f.RemovableDrivesNow;
        var unprot = removable?.Where(v => v.ProtectionStatus == 0).ToList() ?? [];
        var unknownRem = removable?.Where(v => v.ProtectionStatus is null or 2).ToList() ?? [];
        var rdvText = deny ? "scrierea pe suporturi necriptate este blocată prin politică" : rdv == "" ? "politica de scriere nu a putut fi citită" : "politica nu blochează scrierea pe suporturi necriptate";
        ControlStatus d02 = letters.Count == 0 ? (deny ? ControlStatus.Conform : rdv == "" ? ControlStatus.Nedeterminat : ControlStatus.DeVerificat)
            : removable is null ? (deny ? ControlStatus.Conform : ControlStatus.Nedeterminat)
            : unprot.Count > 0 ? (deny ? ControlStatus.Conform : ControlStatus.Neconform)
            : unknownRem.Count > 0 ? (deny ? ControlStatus.Conform : ControlStatus.DeVerificat) : ControlStatus.Conform;
        Add(r, "D02", "Dispozitive", "Suporturile amovibile conectate sunt criptate (BitLocker To Go)", d02,
            letters.Count == 0 ? $"Niciun suport amovibil nu este conectat acum; {rdvText}." + (rdv == "" ? NoRead : "")
                : removable is null ? $"Suporturi conectate acum: {string.Join(", ", letters)}. Starea lor de criptare nu a putut fi citită; {rdvText}." + (deny ? "" : NoRead)
                : unprot.Count > 0 ? $"Suporturi NECRIPTATE conectate acum: {string.Join(", ", unprot.Select(v => v.Drive))}; {rdvText}." + (deny ? " De pe ele se poate doar citi." : " Pe ele se pot copia date care, dacă suportul se pierde, pot fi citite de oricine.")
                : unknownRem.Count > 0 ? $"Starea criptării nu este cunoscută pentru: {string.Join(", ", unknownRem.Select(v => v.Drive))}; {rdvText}."
                : $"Toate suporturile conectate acum sunt criptate ({string.Join(", ", letters)}); {rdvText}.",
            (removable?.Select(VolumeLine) ?? letters.Select(l => $"{l} (suport amovibil): stare necitită"))
                .Append(@"HKLM\SYSTEM\CurrentControlSet\Policies\Microsoft\FVE (sau SOFTWARE\Policies\Microsoft\FVE) → RDVDenyWriteAccess = " + Shown(rdv)),
            "Folosiți doar suporturi criptate cu BitLocker To Go și setați prin politică interzicerea scrierii pe suporturi necriptate (RDVDenyWriteAccess = 1).");

        // D03 (C8) medium scanned with the antivirus right after it was connected.
        var arrivals = Logs(f, "Microsoft-Windows-Partition/Diagnostic", 1006)
            .Where(l => V(l, "Capacity") is not ("0" or "") && V(l, "BusType") is "7" or "12" or "13" or "").OrderBy(l => l.TimeUtc).ToList();
        var scans = Logs(f, StationFactCollector.DefenderChannel, 1000, 1001).ToList();
        var detections = Logs(f, StationFactCollector.DefenderChannel, 1116, 1117).ToList();
        bool d03Readable = ChannelReadable(f, StationFactCollector.DefenderChannel) && ChannelReadable(f, "Microsoft-Windows-Partition/Diagnostic");
        var unscanned = arrivals.Where(a => !scans.Any(s => s.TimeUtc >= a.TimeUtc && s.TimeUtc <= a.TimeUtc.AddMinutes(10))).ToList();
        var foundAfter = detections.Where(d => arrivals.Any(a => d.TimeUtc >= a.TimeUtc && d.TimeUtc <= a.TimeUtc.AddMinutes(60))).ToList();
        foreach (var d in foundAfter) r.Actions.Add(new ActionEntry(d.TimeUtc, V(d, "Detection User"), "antivirusul a găsit un program rău-intenționat", $"{V(d, "Threat Name")} {V(d, "Path")}".Trim(), EvShort(d)));
        Add(r, "D03", "Dispozitive", "Suporturile au fost scanate cu antivirusul la conectare",
            !d03Readable ? ControlStatus.Nedeterminat : foundAfter.Count > 0 ? ControlStatus.Neconform : arrivals.Count == 0 || unscanned.Count > 0 ? ControlStatus.DeVerificat : ControlStatus.Conform,
            !d03Readable ? "Jurnalul antivirusului (Defender) sau jurnalul conectărilor de discuri nu a putut fi citit." + NoRead
                : foundAfter.Count > 0 ? $"Antivirusul a găsit {LogAnalyzer.Dfir.Language.RoText.Count(foundAfter.Count, "program rău-intenționat", "programe rău-intenționate")} în cel mult o oră după conectarea unui suport: {string.Join("; ", foundAfter.Select(d => V(d, "Threat Name")).Where(n => n.Length > 0).Distinct())}."
                : arrivals.Count == 0 ? "Nu s-a observat nicio conectare de suport în perioada controlată (jurnalul Partition/Diagnostic), deci nu există scanări de comparat."
                : unscanned.Count > 0 ? $"{unscanned.Count} din {Ro.Count(arrivals.Count, "conectare de suport", "conectări de suport")} {(unscanned.Count == 1 ? "nu este urmată" : "nu sunt urmate")}, în 10 minute, de o scanare antivirus pe această stație. Scanarea poate fi făcută înainte, pe stația de decontaminare; comparați cu registrul acelei stații (verificarea M04)."
                : (arrivals.Count == 1 ? "Conectarea de suport este urmată" : $"Fiecare din cele {Ro.Count(arrivals.Count, "conectare", "conectări")} de suport este urmată") + ", în cel mult 10 minute, de o scanare antivirus pe această stație.",
            arrivals.Select(a => $"{EvShort(a)}: {Clean(V(a, "Manufacturer"))} {Clean(V(a, "Model"))} SN {Clean(V(a, "SerialNumber"))} → " +
                                 (scans.FirstOrDefault(s => s.TimeUtc >= a.TimeUtc && s.TimeUtc <= a.TimeUtc.AddMinutes(10)) is { } sc ? $"scanat: {EvShort(sc)} {V(sc, "Scan Resources")}".TrimEnd() : "nicio scanare în 10 minute"))
                    .Concat(foundAfter.Select(d => $"{EvShort(d)}: {V(d, "Threat Name")} {V(d, "Path")}".TrimEnd())),
            "Fiecare suport se scanează înainte de prima conectare la stație (pe stația de decontaminare sau pe stație, imediat la conectare), iar scanarea se notează.");

        // S05 the application's own time memory: station clock against the latest time the application recorded in its chained audit.
        DateTimeOffset? installAt = DateTimeOffset.TryParse(S("OS.InstallDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instParsed) ? instParsed : null;
        var clockMem = LogAnalyzer.Dfir.Case.ClockMemoryCheck.Evaluate(f.CollectedUtc, f.LatestAppRecordUtc, installAt);
        Add(r, "S05", "Software", "Ceasul stației față de memoria de timp a aplicației",
            clockMem.State switch
            {
                LogAnalyzer.Dfir.Case.ClockMemoryState.Ok => f.ClockSuspectNote.Length > 0 ? ControlStatus.DeVerificat : ControlStatus.Conform,
                LogAnalyzer.Dfir.Case.ClockMemoryState.Behind or LogAnalyzer.Dfir.Case.ClockMemoryState.BeforeInstall => ControlStatus.Neconform,
                LogAnalyzer.Dfir.Case.ClockMemoryState.ForwardJump => ControlStatus.DeVerificat,
                _ => ControlStatus.Nedeterminat,
            },
            clockMem.Message + (clockMem.TimeUnreliable ? " Constatările care depind de oră au încredere redusă." : "")
                + (f.ClockSuspectNote.Length > 0 ? " " + f.ClockSuspectNote + " Confirmați corectarea ceasului în aplicație." : "")
                + (f.ClockAckText.Length > 0 ? " " + f.ClockAckText + "." : ""),
            new[] { $"Ora stației (UTC): {f.CollectedUtc:yyyy-MM-dd HH:mm:ss}", $"Ultima oră înregistrată de aplicație (UTC): {(f.LatestAppRecordUtc is { } la ? la.ToString("yyyy-MM-dd HH:mm:ss") : "niciuna")}",
                    $"Data instalării Windows (UTC): {(installAt is { } ia ? ia.ToString("yyyy-MM-dd HH:mm:ss") : "necitită")}" }
                .Concat(f.ClockAckText.Length > 0 ? [f.ClockAckText] : []),
            "Dacă ceasul este în urmă, corectați ora (sursă de timp aprobată) și verificați bateria CMOS; treceți incidentul în registrul stației.");

        // S04 (C8) Windows updates installed in the period, compared with the register of offline updates.
        if (f.InstalledUpdates is not { } installed)
            Add(r, "S04", "Software", "Actualizări offline aplicate și registrul actualizărilor", ControlStatus.Nedeterminat, "Lista actualizărilor instalate nu a putut fi citită." + NoRead);
        else
        {
            var inPeriod = installed.Where(u => u.InstalledOn is { } d && d >= f.PeriodStartUtc.UtcDateTime.Date).OrderBy(u => u.InstalledOn).ToList();
            var reg = f.OfflineUpdateRegister;
            var regKbs = reg?.SelectMany(x => x.KbNumbers).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var notInRegister = reg is { Count: > 0 } ? inPeriod.Where(u => u.HotFixId.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && !regKbs.Contains(u.HotFixId.Trim())).ToList() : [];
            var lastAv = reg?.Where(x => LogAnalyzer.Dfir.Registers.UpdateRegister.NormalizeKind(x.Kind) == "antivirus").Select(x => x.Date).Where(d => d.Length > 0).Order().LastOrDefault();
            var avNote = reg is { Count: > 0 } && S("Defender.SignaturesUtc") is { Length: > 0 } sigText && DateTimeOffset.TryParse(sigText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sigAt)
                ? $" Semnăturile antivirus de pe stație sunt din {sigAt:yyyy-MM-dd}; ultima actualizare antivirus din registru: {lastAv ?? "niciuna"}." : "";
            Add(r, "S04", "Software", "Actualizări offline aplicate și registrul actualizărilor",
                reg is not { Count: > 0 } ? ControlStatus.DeVerificat : notInRegister.Count > 0 ? ControlStatus.Neconform : inPeriod.Count == 0 ? ControlStatus.DeVerificat : ControlStatus.Conform,
                reg is not { Count: > 0 } ? $"Registrul actualizărilor offline nu este completat în aplicație; comparați manual {(inPeriod.Count == 1 ? "actualizarea Windows instalată" : $"cele {Ro.Count(inPeriod.Count, "actualizare Windows instalată", "actualizări Windows instalate")}")} în perioadă cu registrul organizației (verificarea M05)."
                    : notInRegister.Count > 0 ? $"{Ro.Capitalize(Ro.CountFem(notInRegister.Count, "actualizare instalată", "actualizări instalate"))} în perioadă {(notInRegister.Count == 1 ? "nu apare" : "nu apar")} în registrul actualizărilor offline: {string.Join(", ", notInRegister.Select(u => u.HotFixId))}. Aflați cine le-a adus și de pe ce suport." + avNote
                    : inPeriod.Count == 0 ? "În lista Windows nu apare nicio actualizare instalată în perioada controlată; comparați cu registrul: dacă registrul are actualizări în perioadă, ele nu au ajuns pe stație." + avNote
                    : (inPeriod.Count == 1 ? "Actualizarea Windows instalată în perioadă apare" : $"Toate cele {Ro.Count(inPeriod.Count, "actualizare Windows instalată", "actualizări Windows instalate")} în perioadă apar") + " în registrul actualizărilor offline." + avNote,
                inPeriod.Select(u => $"{u.HotFixId} instalată {u.InstalledOn:yyyy-MM-dd} ({u.Description})" + (reg is { Count: > 0 } ? regKbs.Contains(u.HotFixId.Trim()) ? " · în registru" : " · NU este în registru" : ""))
                        .Prepend("WMI Win32_QuickFixEngineering → HotFixID, InstalledOn"),
                "Fiecare actualizare adusă pe stație se trece în registrul actualizărilor offline (data, pachetul, amprenta, cine a adus suportul).");
        }
    }

    /// <summary>"Logon {0CCE9215-…}": the subcategory name next to the GUID Windows writes in 4719, so the timeline is readable.</summary>
    private static string AuditName(string guid) =>
        Guid.TryParse(guid.Trim('{', '}'), out var g) && LogAnalyzer.Dfir.Policy.AuditSubcategories.ByName.FirstOrDefault(kv => kv.Value == g).Key is { } n ? $"{n} {guid}" : guid;

    private static string Clean(string s) => s.Trim().Trim('.').Replace("NULL", "", StringComparison.Ordinal).Trim();
    private static string FormatBytes(string s) => long.TryParse(s, out var b) ? $"{b / 1_000_000_000.0:0.#} GB" : s;
    private static string Shorten(string s, int n) { s = s.ReplaceLineEndings(" "); return s.Length <= n ? s : s[..n] + "…"; }
}
