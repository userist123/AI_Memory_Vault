using System.Globalization;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Audit;

public enum ControlStatus { Conform, Neconform, DeVerificat, Nedeterminat }

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
    public int Count(ControlStatus s) => Checks.Count(c => c.Status == s);
}

/// <summary>
/// Turns collected facts into control checks, per-user activity and a timeline of notable actions.
/// Pure function of <see cref="StationFacts"/>: no I/O, fully testable.
/// "DeVerificat" means the facts are shown but only the organisation can say whether they were authorised.
/// </summary>
public static class ControlEvaluator
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
        if (f.Accounts.Count == 0)
        {
            Add(r, "C01", "Conturi", "Conturi locale", ControlStatus.Nedeterminat, "Lista conturilor locale nu a putut fi citită.");
            return;
        }
        var guest = f.Accounts.FirstOrDefault(a => a.Sid.EndsWith("-501", StringComparison.Ordinal));
        Add(r, "C01", "Conturi", "Contul Guest este dezactivat",
            guest is null || guest.Disabled ? ControlStatus.Conform : ControlStatus.Neconform,
            guest is null ? "Contul Guest nu există." : guest.Disabled ? $"Contul {guest.Name} este dezactivat." : $"Contul {guest.Name} este ACTIV.",
            rec: "Dezactivați contul Guest.");

        var admins = f.Accounts.Where(a => a.IsAdministrator && !a.Disabled).ToList();
        Add(r, "C02", "Conturi", "Administratori locali activi", ControlStatus.DeVerificat,
            $"{admins.Count} conturi active cu drepturi de administrator: {string.Join(", ", admins.Select(a => a.Name))}. Comparați cu lista aprobată.",
            admins.Select(a => $"{a.Name} ({a.Sid}), ultima autentificare {a.LastLogonUtc?.ToString("yyyy-MM-dd HH:mm") ?? "necunoscută"}"),
            "Fiecare administrator trebuie să aibă aprobare scrisă; conturile de administrator nu se folosesc pentru lucrul zilnic.");

        var builtin = f.Accounts.FirstOrDefault(a => a.Sid.EndsWith("-500", StringComparison.Ordinal));
        if (builtin is not null)
            Add(r, "C03", "Conturi", "Contul Administrator încorporat", builtin.Disabled ? ControlStatus.Conform : ControlStatus.DeVerificat,
                builtin.Disabled ? "Dezactivat." : $"Contul încorporat {builtin.Name} este activ.", rec: "Dezactivați sau redenumiți contul Administrator încorporat dacă nu este necesar.");

        var noPwd = f.Accounts.Where(a => !a.Disabled && !a.PasswordRequired).ToList();
        Add(r, "C04", "Conturi", "Conturi active pentru care parola nu este obligatorie", noPwd.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            noPwd.Count == 0 ? "Toate conturile active necesită parolă."
                : $"Conturi cu indicatorul PASSWD_NOTREQD: {string.Join(", ", noPwd.Select(a => a.Name))}. Indicatorul permite o parolă goală; nu dovedește că parola este goală.",
            noPwd.Select(a => a.Name), "Verificați că fiecare cont are parolă; eliminați indicatorul (net user <cont> /passwordreq:yes).");

        var neverExpire = f.Accounts.Where(a => !a.Disabled && !a.PasswordExpires && !a.Sid.EndsWith("-503") && !a.Sid.EndsWith("-504")).ToList();
        if (neverExpire.Count > 0)
            Add(r, "C05", "Conturi", "Parole care nu expiră", ControlStatus.DeVerificat,
                $"Conturi active cu parolă care nu expiră: {string.Join(", ", neverExpire.Select(a => a.Name))}.", neverExpire.Select(a => a.Name),
                "Justificați fiecare excepție sau activați expirarea parolei.");

        var rdpUsers = f.Accounts.Where(a => a.IsRemoteDesktopUser).ToList();
        if (rdpUsers.Count > 0)
            Add(r, "C06", "Conturi", "Membri Remote Desktop Users", ControlStatus.DeVerificat, string.Join(", ", rdpUsers.Select(a => a.Name)));
    }

    // ---- configuration ----------------------------------------------------------------------------------------

    private static void Policies(StationFacts f, ControlReport r)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        if (f.PasswordPolicy is { } p)
        {
            Add(r, "P01", "Politici", "Lungimea minimă a parolei", p.MinLength >= 12 ? ControlStatus.Conform : p.MinLength >= 8 ? ControlStatus.DeVerificat : ControlStatus.Neconform,
                $"Lungime minimă: {p.MinLength} caractere.", rec: "Minimum 12 caractere (minimum absolut 8).");
            Add(r, "P02", "Politici", "Blocarea contului după încercări eșuate",
                p.LockoutThreshold is > 0 and <= 10 ? ControlStatus.Conform : ControlStatus.Neconform,
                p.LockoutThreshold == 0 ? "Conturile NU se blochează niciodată (parolele pot fi ghicite la nesfârșit)." : $"Blocare după {p.LockoutThreshold} încercări, pentru {(p.LockoutDuration == TimeSpan.MaxValue ? "până la deblocarea de către administrator" : p.LockoutDuration.TotalMinutes + " minute")}.",
                rec: "Blocare după cel mult 10 încercări.");
            Add(r, "P03", "Politici", "Expirarea parolei", p.MaxAge is null ? ControlStatus.DeVerificat : ControlStatus.Conform,
                p.MaxAge is null ? "Parolele nu expiră niciodată." : $"Parolele expiră după {p.MaxAge.Value.TotalDays:0} zile; istoric {p.History}.");
        }
        else Add(r, "P01", "Politici", "Politica de parole", ControlStatus.Nedeterminat, "Nu a putut fi citită.");

        Add(r, "P04", "Politici", "UAC (Control cont utilizator) activ", S("UAC.EnableLUA") == "1" ? ControlStatus.Conform : S("UAC.EnableLUA") == "" ? ControlStatus.Nedeterminat : ControlStatus.Neconform,
            S("UAC.EnableLUA") == "1" ? $"Activ (ConsentPromptBehaviorAdmin={S("UAC.ConsentPromptBehaviorAdmin")})." : "UAC este dezactivat: orice program al unui administrator rulează cu drepturi complete.");

        bool autologon = S("Winlogon.AutoAdminLogon") == "1";
        Add(r, "P05", "Politici", "Autentificare automată fără parolă", autologon || S("Winlogon.DefaultPasswordPresent") == "da" ? ControlStatus.Neconform : ControlStatus.Conform,
            autologon ? $"Stația pornește automat în contul {S("Winlogon.DefaultUserName")}" + (S("Winlogon.DefaultPasswordPresent") == "da" ? ", cu parola stocată în registry." : ".")
                      : S("Winlogon.DefaultPasswordPresent") == "da" ? "Există o parolă de autologon stocată în registry (valoarea nu a fost citită)." : "Dezactivată.",
            rec: "Dezactivați AutoAdminLogon și ștergeți DefaultPassword.");

        var rdp = S("RDP.fDenyTSConnections");
        Add(r, "P06", "Politici", "Conexiuni Remote Desktop", rdp == "1" ? ControlStatus.Conform : rdp == "" ? ControlStatus.Nedeterminat : f.StationShouldBeIsolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
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
                $"Ultima actualizare: {sig:yyyy-MM-dd} ({age.TotalDays:0} zile).",
                rec: f.StationShouldBeIsolated ? "Pe stația izolată semnăturile se actualizează offline, periodic, după o procedură documentată." : "Verificați actualizarea automată.");
        }
        if (f.DefenderExclusions.Count > 0)
            Add(r, "P10", "Politici", "Excluderi Microsoft Defender", ControlStatus.DeVerificat,
                $"{f.DefenderExclusions.Count} excluderi configurate. Excluderile sunt folosite des de atacatori pentru a-și ascunde programele.", f.DefenderExclusions);

        var bl = S("BitLocker.SystemDrive");
        Add(r, "P11", "Politici", "Criptarea discului de sistem (BitLocker)", bl == "1" ? ControlStatus.Conform : bl == "" ? ControlStatus.Nedeterminat : ControlStatus.Neconform,
            bl == "1" ? "Protecție activă." : bl == "" ? $"Starea nu a putut fi citită.{(S("BitLocker.SystemDrive.Error") is { Length: > 0 } blErr ? " " + blErr : "")}" : "Discul de sistem NU este protejat de BitLocker.");

        bool smb1 = S("SMB1.Server") == "1" || S("SMB1.ClientDriverStart") is "2" or "3";
        Add(r, "P12", "Politici", "SMBv1 dezactivat", smb1 ? ControlStatus.Neconform : ControlStatus.Conform, smb1 ? "Protocolul învechit SMBv1 este activ." : "Dezactivat.");

        Add(r, "P13", "Politici", "WDigest nu păstrează parole în memorie", S("WDigest.UseLogonCredential") == "1" ? ControlStatus.Neconform : ControlStatus.Conform,
            S("WDigest.UseLogonCredential") == "1" ? "UseLogonCredential=1: parolele în clar pot fi extrase din memorie." : "Configurare implicită sigură.");

        bool usbBlocked = S("USBSTOR.Start") == "4" || S("RemovableStorage.DenyAll") == "1";
        Add(r, "P14", "Politici", "Stocare USB blocată prin politică", usbBlocked ? ControlStatus.Conform : f.StationShouldBeIsolated ? ControlStatus.DeVerificat : ControlStatus.DeVerificat,
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
            clears.Count == 0 ? "Nicio ștergere de jurnal înregistrată." : $"{clears.Count} ștergeri de jurnal.", clears.Select(l => $"{Ev(l)} de {Who(l)} {V(l, "Channel")}"),
            "Fiecare ștergere de jurnal trebuie justificată.");
        foreach (var l in clears) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a șters un jurnal", l.Id == 1102 ? "Security" : V(l, "Channel"), Ev(l)));

        var auditChanges = Logs(f, "Security", 4719).ToList();
        if (auditChanges.Count > 0)
            Add(r, "A06", "Audit", "Politica de audit a fost modificată", ControlStatus.DeVerificat, $"{LogAnalyzer.Dfir.Language.RoText.Count(auditChanges.Count, "modificare", "modificări")}.",
                auditChanges.Select(l => $"{Ev(l)} de {Who(l)}: {AuditName(V(l, "SubcategoryGuid"))} {V(l, "AuditPolicyChanges")}"));
        foreach (var l in auditChanges) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a modificat politica de audit", AuditName(V(l, "SubcategoryGuid")), Ev(l)));

        var timeChanges = Logs(f, "Security", 4616).Where(l => !V(l, "ProcessName").EndsWith("svchost.exe", StringComparison.OrdinalIgnoreCase) &&
                                                             !SystemAccounts.Contains(V(l, "SubjectUserName"))).ToList();
        Add(r, "A07", "Audit", "Ora sistemului schimbată manual", timeChanges.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            timeChanges.Count == 0 ? "Nicio schimbare manuală a orei." : $"{timeChanges.Count} schimbări ale orei făcute de utilizatori (pot afecta cronologia).",
            timeChanges.Select(l => $"{Ev(l)} de {Who(l)} prin {V(l, "ProcessName")}: {V(l, "PreviousTime")} → {V(l, "NewTime")}"));
        foreach (var l in timeChanges) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a schimbat ora sistemului", $"{V(l, "PreviousTime")} → {V(l, "NewTime")}", Ev(l)));
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
            lifecycle.Count == 0 ? "Nicio astfel de operație." : $"{lifecycle.Count} operații pe conturi.",
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
            !ChannelReadable(f, "Security") ? ControlStatus.Nedeterminat : remote.Count == 0 ? ControlStatus.Conform : f.StationShouldBeIsolated ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            remote.Count == 0 ? "Nicio autentificare de la distanță." : string.Join("; ", remote.Select(u => $"{u.User}: {u.RemoteLogons} RDP de la {string.Join(", ", u.RemoteSources)}")));

        var bursts = Logs(f, "Security", 4625).GroupBy(l => (Who(l, "TargetUserName", "TargetDomainName"), l.TimeUtc.UtcDateTime.ToString("yyyy-MM-dd HH")))
                                              .Where(g => g.Count() >= 5).ToList();
        Add(r, "U04", "Utilizatori", "Serii de parole greșite (5+ într-o oră)",
            !ChannelReadable(f, "Security") ? ControlStatus.Nedeterminat : bursts.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            bursts.Count == 0 ? "Nicio serie de eșecuri." : $"{bursts.Count} serii — posibilă ghicire a parolei sau parolă uitată.",
            bursts.Select(g => $"{g.Key.Item1}: {g.Count()} eșecuri în ora {g.Key.Item2} UTC, de la {string.Join(", ", g.Select(l => V(l, "IpAddress")).Distinct())}"));

        var priv = r.Users.Where(u => u.PrivilegedSessions > 0).ToList();
        if (priv.Count > 0)
            Add(r, "U05", "Utilizatori", "Cine a lucrat cu drepturi de administrator", ControlStatus.DeVerificat,
                string.Join("; ", priv.Select(u => $"{u.User}: {u.PrivilegedSessions} sesiuni privilegiate")));

        var ps = Logs(f, "Microsoft-Windows-PowerShell/Operational", 4104)
            .Where(l => V(l, "ScriptBlockText") is var t && (t.Contains("DownloadString", StringComparison.OrdinalIgnoreCase) || t.Contains("FromBase64String", StringComparison.OrdinalIgnoreCase) ||
                       t.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase) || t.Contains("IEX ", StringComparison.OrdinalIgnoreCase) ||
                       t.Contains("Set-MpPreference", StringComparison.OrdinalIgnoreCase) || t.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (ps.Count > 0)
            Add(r, "U06", "Utilizatori", "Scripturi PowerShell cu tehnici frecvent abuzate", ControlStatus.DeVerificat, $"{ps.Count} blocuri de script.",
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
            ? $"{f.UsbDevices.Count} dispozitive în istoricul USBSTOR; {partition.Count} conectări înregistrate în perioada controlată."
            : usbSourcesKnown
                ? "Dispozitive USB: nu s-a observat în sursele colectate (istoricul USBSTOR și jurnalul Partition/Diagnostic); absența observațiilor nu dovedește că nu au fost folosite suporturi (jurnalul are o istorie limitată, iar dispozitivele pot lăsa urme doar în alte surse)."
                : "Dispozitive USB: nu se poate stabili, deoarece istoricul USBSTOR sau jurnalul Partition/Diagnostic nu a putut fi citit; rulați ca administrator.";
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
        if (f.StationShouldBeIsolated)
        {
            bool connected = inPeriod.Count > 0 || evts.Count > 0 || f.ConnectedInterfacesNow.Count > 0;
            Add(r, "N01", "Rețea", "Stația izolată nu s-a conectat la nicio rețea",
                connected ? ControlStatus.Neconform : profilesKnown && eventsKnown ? ControlStatus.DeVerificat : ControlStatus.Nedeterminat,
                connected
                    ? $"Conectări în perioada controlată: {LogAnalyzer.Dfir.Language.RoText.Count(inPeriod.Count, "profil", "profiluri")}, {LogAnalyzer.Dfir.Language.RoText.Count(evts.Count, "eveniment", "evenimente")} de conectare; interfețe active acum: {f.ConnectedInterfacesNow.Count}."
                    : profilesKnown && eventsKnown
                        ? "Conectare la rețea: nu s-a observat în sursele colectate (profiluri de rețea, NetworkProfile, WLAN-AutoConfig) și nicio interfață activă acum; absența observațiilor nu dovedește că stația nu s-a conectat (istorie limitată a jurnalelor, adaptoare fără urme în aceste surse)."
                        : "Conectare la rețea: nu se poate stabili, deoarece profilurile de rețea sau jurnalele NetworkProfile / WLAN-AutoConfig nu au putut fi citite.",
                inPeriod.Select(p => $"{p.Name} ({p.Kind}) ultima conectare {p.LastConnectedLocal:yyyy-MM-dd HH:mm} ora locală")
                        .Concat(evts.Select(l => $"{Ev(l)}: {V(l, "Name")}{V(l, "SSID")}")).Concat(f.ConnectedInterfacesNow.Select(i => "acum: " + i)),
                "Orice conectare a unei stații izolate trebuie investigată (cine, când, ce s-a transferat).");
        }
        Add(r, "N02", "Rețea", "Rețele cunoscute de stație (istoric)",
            !profilesKnown ? ControlStatus.Nedeterminat : ControlStatus.DeVerificat,
            !profilesKnown ? "Istoricul rețelelor (registry NetworkList) nu a putut fi citit; rulați ca administrator."
                : f.NetworkProfiles.Count == 0 ? "Rețele cunoscute: nu s-a observat în sursele colectate (NetworkList); absența profilurilor nu dovedește că stația nu s-a conectat (profilurile pot fi șterse)."
                : $"{LogAnalyzer.Dfir.Language.RoText.Count(f.NetworkProfiles.Count, "profil", "profiluri")} de rețea salvate.",
            f.NetworkProfiles.OrderByDescending(p => p.LastConnectedLocal).Select(p => $"{p.Name} ({p.Kind}) creat {p.CreatedLocal:yyyy-MM-dd} · ultima conectare {p.LastConnectedLocal:yyyy-MM-dd HH:mm}"));
    }

    private static void Software(StationFacts f, ControlReport r)
    {
        var msi = Logs(f, "Application", 11707, 11724, 1033, 1034).ToList();
        foreach (var l in msi)
            r.Actions.Add(new ActionEntry(l.TimeUtc, l.UserSid, l.Id is 11707 or 1033 ? "a instalat" : "a dezinstalat", V(l, "Data0") is { Length: > 0 } d ? d : string.Join(" ", l.Data.Values.Take(2)), Ev(l)));
        Add(r, "S01", "Software", "Programe instalate sau dezinstalate", msi.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            $"{msi.Count} operații Windows Installer în perioada controlată.", msi.Select(l => $"{Ev(l)}: {string.Join(" ", l.Data.Values.Take(2))}"),
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

    private static void StationHardening(StationFacts f, ControlReport r)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        bool isolated = f.StationShouldBeIsolated;
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
            var profText = profiles == "" ? "profilurile Wi-Fi salvate nu au putut fi numărate (necesită administrator)" : $"{profiles} profiluri Wi-Fi salvate";
            var status = wifi == 0 ? ControlStatus.Conform : isolated && wifiUp > 0 ? ControlStatus.Neconform : ControlStatus.DeVerificat;
            Add(r, "N03", "Rețea", "Adaptor Wi-Fi (rețea fără fir)", status,
                wifi == 0 ? $"Stația nu are niciun adaptor Wi-Fi; {profText}."
                    : $"Stația are {wifi} adaptor(oare) Wi-Fi ({wifiUp} pornit/pornite acum); {svcText}; {profText}." + (isolated ? " Pe o stație izolată un adaptor Wi-Fi poate crea o legătură cu exteriorul." : ""),
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
            var status = bt == 0 ? ControlStatus.Conform : isolated && !svcOff ? ControlStatus.Neconform : ControlStatus.DeVerificat;
            Add(r, "N04", "Rețea", "Bluetooth", status,
                bt == 0 ? "Stația nu are componente Bluetooth."
                    : $"Stația are Bluetooth ({bt} componente: {S("Bluetooth.Components")}); serviciul Bluetooth este {(svcOff ? "dezactivat" : svc == "" ? "într-o stare necitită" : "permis să pornească")}." +
                      (isolated ? " Prin Bluetooth se pot trimite fișiere fără cablu." : ""),
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
                : lockSecs <= 900 ? $"Ecranul se blochează după {lockSecs / 60.0:0.#} minute de inactivitate."
                : $"Ecranul se blochează abia după {lockSecs / 60.0:0.#} minute; regula este cel mult 15 minute.",
            [@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → InactivityTimeoutSecs = " + Shown(lockV)],
            "Setați blocarea la cel mult 15 minute (900 de secunde).");

        // P17 application allow-list
        var al = S("AppLocker.Collections");
        var alSvc = S("AppLocker.AppIDSvcStart");
        bool alEnforced = al.Contains("mod 1", StringComparison.Ordinal) && !al.Contains("mod 0", StringComparison.Ordinal) && !al.Contains("nesetat", StringComparison.Ordinal);
        Add(r, "P17", "Politici", "Listă albă de aplicații (AppLocker)",
            al == "" ? ControlStatus.Nedeterminat : al == Absent || al.StartsWith('(') ? ControlStatus.DeVerificat : alEnforced && alSvc == "4" ? ControlStatus.Neconform : alEnforced ? ControlStatus.Conform : ControlStatus.DeVerificat,
            al == "" ? "Politica AppLocker nu a putut fi citită." + NoRead
                : al == Absent ? "Nu s-a găsit nicio politică AppLocker. Aplicația nu verifică și App Control (WDAC); dacă organizația îl folosește, constatarea se completează manual."
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
                : $"La pornire apare titlul „{(noCap ? "—" : cap)}” și un text de {(noTxt ? 0 : txt.Length)} caractere. Comparați-l cu marcajul de clasificare aprobat pentru stație.",
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

    /// <summary>"Logon {0CCE9215-…}": the subcategory name next to the GUID Windows writes in 4719, so the timeline is readable.</summary>
    private static string AuditName(string guid) =>
        Guid.TryParse(guid.Trim('{', '}'), out var g) && LogAnalyzer.Dfir.Policy.AuditSubcategories.ByName.FirstOrDefault(kv => kv.Value == g).Key is { } n ? $"{n} {guid}" : guid;

    private static string Clean(string s) => s.Trim().Trim('.').Replace("NULL", "", StringComparison.Ordinal).Trim();
    private static string FormatBytes(string s) => long.TryParse(s, out var b) ? $"{b / 1_000_000_000.0:0.#} GB" : s;
    private static string Shorten(string s, int n) { s = s.ReplaceLineEndings(" "); return s.Length <= n ? s : s[..n] + "…"; }
}
