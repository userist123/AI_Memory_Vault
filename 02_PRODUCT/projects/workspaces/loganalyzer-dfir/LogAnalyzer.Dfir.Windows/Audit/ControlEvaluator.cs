using System.Globalization;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Audit;

public enum ControlStatus { Conform, Neconform, DeVerificat, Nedeterminat }

public sealed record ControlCheck(string Id, string Area, string Title, ControlStatus Status, string Detail, IReadOnlyList<string> Evidence, string Recommendation);

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
            bl == "1" ? "Protecție activă." : bl == "" ? "Starea nu a putut fi citită." : "Discul de sistem NU este protejat de BitLocker.");

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
            Add(r, "A06", "Audit", "Politica de audit a fost modificată", ControlStatus.DeVerificat, $"{auditChanges.Count} modificări.",
                auditChanges.Select(l => $"{Ev(l)} de {Who(l)}: {V(l, "SubcategoryGuid")} {V(l, "AuditPolicyChanges")}"));
        foreach (var l in auditChanges) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a modificat politica de audit", V(l, "SubcategoryGuid"), Ev(l)));

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
            adminChanges.Count == 0 ? "Nicio modificare în perioada controlată." : $"{adminChanges.Count} modificări: cine a primit sau a pierdut drepturi de administrator.",
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
        Add(r, "D01", "Dispozitive", "Dispozitive de stocare USB folosite pe stație", any == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            $"{f.UsbDevices.Count} dispozitive în istoricul USBSTOR; {partition.Count} conectări înregistrate în perioada controlată.",
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
                connected ? ControlStatus.Neconform : profilesKnown && eventsKnown ? ControlStatus.Conform : ControlStatus.Nedeterminat,
                connected
                    ? $"Conectări în perioada controlată: {inPeriod.Count} profiluri, {evts.Count} evenimente de conectare; interfețe active acum: {f.ConnectedInterfacesNow.Count}."
                    : "Nicio conectare la rețea în perioada controlată și nicio interfață activă acum.",
                inPeriod.Select(p => $"{p.Name} ({p.Kind}) ultima conectare {p.LastConnectedLocal:yyyy-MM-dd HH:mm} ora locală")
                        .Concat(evts.Select(l => $"{Ev(l)}: {V(l, "Name")}{V(l, "SSID")}")).Concat(f.ConnectedInterfacesNow.Select(i => "acum: " + i)),
                "Orice conectare a unei stații izolate trebuie investigată (cine, când, ce s-a transferat).");
        }
        Add(r, "N02", "Rețea", "Rețele cunoscute de stație (istoric)",
            !profilesKnown ? ControlStatus.Nedeterminat : f.NetworkProfiles.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            !profilesKnown ? "Istoricul rețelelor (registry NetworkList) nu a putut fi citit; rulați ca administrator." : $"{f.NetworkProfiles.Count} profiluri de rețea salvate.",
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
        foreach (var l in tasks) r.Actions.Add(new ActionEntry(l.TimeUtc, Who(l), "a creat taskul programat", V(l, "TaskName"), Ev(l)));
        Add(r, "S02", "Software", "Servicii noi și task-uri programate create", services.Count + tasks.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            $"{services.Count} servicii noi, {tasks.Count} task-uri create ({drivers.Count} drivere instalate, listate separat în cronologie).",
            services.Select(l => $"{Ev(l)}: {V(l, "ServiceName")} → {V(l, "ImagePath")}").Concat(tasks.Select(l => $"{Ev(l)}: {Who(l)} → {V(l, "TaskName")}")),
            "Serviciile și task-urile sunt metode frecvente de persistență: verificați fiecare intrare necunoscută.");

        foreach (var l in Logs(f, "System", 1074))
            r.Actions.Add(new ActionEntry(l.TimeUtc, V(l, "param7") is { Length: > 0 } u ? u : l.UserSid, "a oprit/repornit stația", $"{V(l, "param1")} {V(l, "param3")}", Ev(l)));
    }

    private static string Clean(string s) => s.Trim().Trim('.').Replace("NULL", "", StringComparison.Ordinal).Trim();
    private static string FormatBytes(string s) => long.TryParse(s, out var b) ? $"{b / 1_000_000_000.0:0.#} GB" : s;
    private static string Shorten(string s, int n) { s = s.ReplaceLineEndings(" "); return s.Length <= n ? s : s[..n] + "…"; }
}
