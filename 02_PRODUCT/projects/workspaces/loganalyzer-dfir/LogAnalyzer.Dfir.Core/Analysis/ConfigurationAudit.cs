using System.Globalization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>One registry value to read from a stored hive: hive label (SOFTWARE / SYSTEM), key path inside the hive and value name. The locator is "hive\key\name".</summary>
public sealed record ConfigProbe(string Hive, string Key, string Name)
{
    public string Locator => $@"{Hive}\{Key}\{Name}";
}

/// <summary>What the reader found for one probe: <see cref="Data"/> = the value as text, null = key or value absent. <see cref="KeyLastWriteUtc"/> = LastWrite of the key that holds the value.
/// <see cref="EvidenceId"/> / <see cref="Sha256"/> are set only when the value comes from another evidence item than the hive's own (Quick profile: a saved SOFTWARE subkey hive); null = the hive's own.</summary>
public sealed record ConfigValue(string? Data, DateTimeOffset? KeyLastWriteUtc, string? EvidenceId = null, string? Sha256 = null, string? TimeSource = null);

/// <summary>The value names of one list key (Defender exclusions: the name is the excluded path / extension / process); null names = the key does not exist.</summary>
public sealed record ConfigNames(IReadOnlyList<string>? Names, DateTimeOffset? KeyLastWriteUtc, string? EvidenceId = null, string? Sha256 = null);

/// <summary>One stored hive (SOFTWARE or SYSTEM) read for the configuration audit. <see cref="Error"/> non-empty = the hive could not be read at all.</summary>
public sealed class ConfigHive
{
    public required string Hive { get; init; }
    public required string EvidenceId { get; init; }
    public string Sha256 { get; init; } = "";
    /// <summary>Where the copy comes from, for a copy that is not the current state of the station ("" = the live save of this run).</summary>
    public string Origin { get; init; } = "";
    public string Error { get; init; } = "";
    /// <summary>SYSTEM only: the control set the values were read from (the hive's Select\Current), for example ControlSet001.</summary>
    public string ControlSet { get; init; } = "ControlSet001";
    public Dictionary<string, ConfigValue> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ConfigNames> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConfigAuditResult
{
    public List<Finding> Findings { get; } = [];
    public List<EvidenceGap> Gaps { get; } = [];
}

/// <summary>
/// Planner #11 A5: configuration FINDINGS of the investigation ("what is set wrongly on this PC"), each readable on paper. Facts only: a setting that is unsafe is
/// stated with its registry path, value and the time of the key; nothing is said about a station procedure (no verdict word). A safe setting gives nothing; a setting
/// that cannot be read gives an <see cref="EvidenceGap"/>, never a finding. The values come from the STORED hive copies (SOFTWARE from the live save or a VSS /
/// Windows.old copy, SYSTEM likewise), so the evidence has a SHA-256 in the case; nothing is read from the live registry here.
/// </summary>
public static class ConfigurationAudit
{
    public const string Producer = "LogAnalyzer.Dfir.Analysis.ConfigurationAudit";
    public const string TimeSource = "Registry LastWrite (cheie)";
    public const string Channel = "Registry";
    public const string Category = "Configurație";
    public const int ListCap = 20;

    public const string RuleUac = "CFG-UAC-OFF", RuleUacPrompt = "CFG-UAC-NOPROMPT", RuleSmb1 = "CFG-SMB1", RuleWdigest = "CFG-WDIGEST", RuleLsa = "CFG-LSA-PPL",
        RuleDefenderOff = "CFG-DEFENDER-OFF", RuleDefenderExcl = "CFG-DEFENDER-EXCL", RuleRdpNla = "CFG-RDP-NLA",
        RuleFirewallOff = "CFG-FIREWALL-OFF", RuleRemoteRegistry = "CFG-REMOTEREGISTRY", RuleAutologon = "CFG-AUTOLOGON", RuleTokenFilter = "CFG-LOCALACCOUNT-TOKENFILTER",
        RuleAlwaysInstall = "CFG-ALWAYSINSTALLELEVATED", RuleSmbSigning = "CFG-SMB-SIGNING";

    /// <summary>What the reader stores instead of a secret value (the Winlogon DefaultPassword): the password itself is never read into the audit, only whether one exists.</summary>
    public const string SecretPresent = "(prezentă)";
    public const string SecretNotShown = "(prezentă, nu se afișează)";

    private const string Sys = @"Microsoft\Windows\CurrentVersion\Policies\System";
    private const string Def = @"Microsoft\Windows Defender";
    private const string DefPol = @"Policies\Microsoft\Windows Defender";

    public static readonly ConfigProbe UacEnable = new("SOFTWARE", Sys, "EnableLUA");
    public static readonly ConfigProbe UacConsent = new("SOFTWARE", Sys, "ConsentPromptBehaviorAdmin");
    public static readonly ConfigProbe RtpLocal = new("SOFTWARE", Def + @"\Real-Time Protection", "DisableRealtimeMonitoring");
    public static readonly ConfigProbe RtpPolicy = new("SOFTWARE", DefPol + @"\Real-Time Protection", "DisableRealtimeMonitoring");
    public static readonly ConfigProbe AvPolicy = new("SOFTWARE", DefPol, "DisableAntiSpyware");
    public static readonly string[] ExclusionKinds = ["Paths", "Extensions", "Processes"];
    public static readonly string[] ExclusionKeys = [.. ExclusionKinds.Select(k => Def + @"\Exclusions\" + k), .. ExclusionKinds.Select(k => DefPol + @"\Exclusions\" + k)];

    private const string Winlogon = @"Microsoft\Windows NT\CurrentVersion\Winlogon";
    public static readonly ConfigProbe AutoAdminLogon = new("SOFTWARE", Winlogon, "AutoAdminLogon");
    public static readonly ConfigProbe DefaultUserName = new("SOFTWARE", Winlogon, "DefaultUserName");
    public static readonly ConfigProbe DefaultPassword = new("SOFTWARE", Winlogon, "DefaultPassword");
    public static readonly ConfigProbe TokenFilter = new("SOFTWARE", Sys, "LocalAccountTokenFilterPolicy");
    public static readonly ConfigProbe AlwaysInstall = new("SOFTWARE", @"Policies\Microsoft\Windows\Installer", "AlwaysInstallElevated");
    /// <summary>Firewall profiles: (name in both the local FirewallPolicy key and the policy key, readable label).</summary>
    public static readonly (string Name, string Label)[] FirewallProfiles = [("DomainProfile", "Domain"), ("StandardProfile", "Private (Standard)"), ("PublicProfile", "Public")];
    public static ConfigProbe FwPolicy(string profile) => new("SOFTWARE", @"Policies\Microsoft\WindowsFirewall\" + profile, "EnableFirewall");
    public static ConfigProbe FwLocal(string cs, string profile) => new("SYSTEM", $@"{cs}\Services\SharedAccess\Parameters\FirewallPolicy\{profile}", "EnableFirewall");
    public static ConfigProbe RemoteRegistry(string cs) => new("SYSTEM", $@"{cs}\Services\RemoteRegistry", "Start");
    public static ConfigProbe SmbSigning(string cs) => new("SYSTEM", $@"{cs}\Services\LanmanServer\Parameters", "RequireSecuritySignature");

    /// <summary>The text a reader may keep for a probe: a secret value (Winlogon DefaultPassword) is reduced to "(prezentă)" or "" so it never enters the audit, the case findings or a report.</summary>
    public static string? ProbeText(ConfigProbe p, string? raw) =>
        raw is not null && p.Name.Equals("DefaultPassword", StringComparison.OrdinalIgnoreCase) ? (raw.Length > 0 ? SecretPresent : "") : raw;

    public static ConfigProbe Smb1(string cs) => new("SYSTEM", $@"{cs}\Services\LanmanServer\Parameters", "SMB1");
    public static ConfigProbe WDigest(string cs) => new("SYSTEM", $@"{cs}\Control\SecurityProviders\WDigest", "UseLogonCredential");
    public static ConfigProbe Lsa(string cs) => new("SYSTEM", $@"{cs}\Control\Lsa", "RunAsPPL");
    public static ConfigProbe RdpDeny(string cs) => new("SYSTEM", $@"{cs}\Control\Terminal Server", "fDenyTSConnections");
    public static ConfigProbe RdpNla(string cs) => new("SYSTEM", $@"{cs}\Control\Terminal Server\WinStations\RDP-Tcp", "UserAuthentication");

    /// <summary>The values the reader must fetch from a stored hive of this kind (the rules below look them up by locator).</summary>
    public static IReadOnlyList<ConfigProbe> Probes(string hive, string controlSet) => hive switch
    {
        "SOFTWARE" => [UacEnable, UacConsent, RtpLocal, RtpPolicy, AvPolicy, TokenFilter, AlwaysInstall, AutoAdminLogon, DefaultUserName, DefaultPassword,
            .. FirewallProfiles.Select(x => FwPolicy(x.Name))],
        "SYSTEM" => [Smb1(controlSet), WDigest(controlSet), Lsa(controlSet), RdpDeny(controlSet), RdpNla(controlSet), RemoteRegistry(controlSet), SmbSigning(controlSet),
            .. FirewallProfiles.Select(x => FwLocal(controlSet, x.Name))],
        _ => [],
    };

    /// <summary>The list keys (value names are the data) the reader must fetch; locator = "SOFTWARE\key".</summary>
    public static IReadOnlyList<string> ListKeys(string hive) => hive == "SOFTWARE" ? ExclusionKeys : [];

    // ---- evaluation ----

    public static ConfigAuditResult Evaluate(IEnumerable<ConfigHive> hives, string host, Func<string> nextId)
    {
        var res = new ConfigAuditResult();
        hives = hives.ToList();
        foreach (var h in hives)
        {
            if (h.Error.Length > 0)
            {
                res.Gaps.Add(new EvidenceGap($"Configurație din hive-ul {h.Hive}", EvidenceStatus.Failed, h.Error,
                    h.Hive == "SOFTWARE" ? "Setările UAC, Defender, politicile firewall, logarea automată, AlwaysInstallElevated nu au putut fi verificate" : "Setările SMB, WDigest, protecția LSA, RDP/NLA, firewall, RemoteRegistry nu au putut fi verificate",
                    $@"reg query pe stație sau o copie nouă a hive-ului {h.Hive}", "Doar prin reachiziție"));
                continue;
            }
            if (h.Hive == "SOFTWARE") { SoftwareHive(h, host, nextId, res); SoftwareHiveHardening(h, host, nextId, res); }
            else if (h.Hive == "SYSTEM") { SystemHive(h, host, nextId, res); SystemHiveHardening(h, host, nextId, res); }
        }
        // the firewall verdict of a profile needs both hives (the local setting in SYSTEM, the policy override in SOFTWARE)
        var sys = hives.FirstOrDefault(x => x.Hive == "SYSTEM" && x.Error.Length == 0);
        if (sys is not null) Firewall(sys, hives.FirstOrDefault(x => x.Hive == "SOFTWARE" && x.Error.Length == 0), host, nextId, res);
        return res;
    }

    private static string OriginNote(ConfigHive h) => h.Origin.Length > 0 ? $" (copie din {h.Origin}, nu starea de la colectare)" : "";

    private static bool Read(ConfigHive h, ConfigProbe p, ConfigAuditResult res, out uint number, out ConfigValue? value)
    {
        number = 0; value = null;
        if (!h.Values.TryGetValue(p.Locator, out value))
        {
            res.Gaps.Add(Gap(h, p, "valoarea nu a fost citită din hive (citirea nu a rulat sau a eșuat)"));
            return false;
        }
        if (value.Data is null) return false;   // absent: not unreadable, the caller decides what "absent" means
        if (!uint.TryParse(value.Data, NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            res.Gaps.Add(Gap(h, p, $"valoarea nu a putut fi citită ca număr (conținut: „{value.Data}”)"));
            value = null;
            return false;
        }
        return true;
    }

    private static EvidenceGap Gap(ConfigHive h, ConfigProbe p, string why) =>
        new($@"Setare {p.Locator}", EvidenceStatus.Failed, why, "Setarea nu poate fi evaluată: lipsa unei constatări nu înseamnă că setarea este sigură",
            $@"reg query ""HKLM\{p.Hive}\{p.Key}"" /v {p.Name}", "Da, pe stație");

    private static void SoftwareHive(ConfigHive h, string host, Func<string> nextId, ConfigAuditResult res)
    {
        if (Read(h, UacEnable, res, out var lua, out var luaV) && lua == 0)
            res.Findings.Add(Make(h, host, nextId, RuleUac, Severity.High, Confidence.High,
                "UAC este dezactivat: EnableLUA = 0",
                "Controlul contului de utilizator (UAC) este oprit în registru: EnableLUA = 0. Programele pornite de un administrator rulează cu drepturi depline, fără întrebare și fără izolarea de la UAC.",
                "Windows are controlul contului de utilizator (UAC) dezactivat",
                "gravitate ridicată: fără UAC orice program pornit de un administrator are drepturi depline fără nicio confirmare",
                [(UacEnable, luaV!)],
                ["Politica de grup sau un produs de administrare poate seta valoarea în mod deliberat (de exemplu pentru software vechi).", "Setarea poate fi o alegere a administratorului, nu un semn de compromitere."],
                ["Valoarea este starea din hive la colectare; nu arată de când este așa (ora cheii este doar ultima scriere a oricărei valori din cheie).", "O politică aplicată ulterior sau la pornire poate înlocui valoarea."]));

        if (Read(h, UacConsent, res, out var cons, out var consV) && cons == 0)
            res.Findings.Add(Make(h, host, nextId, RuleUacPrompt, Severity.Medium, Confidence.High,
                "Ridicarea drepturilor administratorului se face fără întrebare: ConsentPromptBehaviorAdmin = 0",
                "Registrul spune că administratorii primesc drepturi ridicate fără nicio întrebare: ConsentPromptBehaviorAdmin = 0.",
                "Windows ridică drepturile administratorilor fără să întrebe",
                "gravitate medie: un program pornit de administrator își ridică drepturile fără confirmare, dar UAC rămâne în funcțiune",
                [(UacConsent, consV!)],
                ["Este o valoare setată frecvent pe stații de laborator sau prin politica de grup.", "Poate fi o alegere a administratorului."],
                ["Valoarea este starea din hive la colectare; nu arată de când este așa.", "Dacă UAC este oprit (EnableLUA = 0) valoarea nu mai are efect practic."]));

        var off = new List<(ConfigProbe P, ConfigValue V)>();
        foreach (var p in new[] { RtpLocal, RtpPolicy, AvPolicy })
            if (Read(h, p, res, out var n, out var v) && n == 1) off.Add((p, v!));
        if (off.Count > 0)
            res.Findings.Add(Make(h, host, nextId, RuleDefenderOff, Severity.High, Confidence.Medium,
                "Protecția în timp real Microsoft Defender este oprită în registru: " + string.Join("; ", off.Select(x => $"{x.P.Name} = 1")),
                "Registrul cere oprirea protecției în timp real sau a antivirusului Defender: " + string.Join("; ", off.Select(x => $@"{x.P.Key}\{x.P.Name} = 1")) + ".",
                "Windows are protecția antivirus Defender oprită în registru",
                "gravitate ridicată: fără protecție în timp real programele rău-intenționate nu sunt oprite la pornire",
                off,
                ["Un alt antivirus instalat poate fi motivul (Defender se oprește singur când există altul).", "Politica organizației poate cere oprirea."],
                ["Registrul arată setarea cerută, nu starea efectivă a serviciului (Protecția împotriva modificărilor și politicile aplicate la pornire pot diferi).", "Valoarea este starea din hive la colectare; nu arată de când este așa."]));

        var found = new List<(string Key, ConfigNames N)>();
        foreach (var key in ExclusionKeys)
        {
            if (!h.Lists.TryGetValue("SOFTWARE\\" + key, out var n))
                res.Gaps.Add(new EvidenceGap($@"Excluderi Defender ({key})", EvidenceStatus.Failed, "cheia nu a fost citită din hive", "Excluderile nu pot fi enumerate: lipsa unei constatări nu înseamnă că nu există",
                    $@"reg query ""HKLM\SOFTWARE\{key}""", "Da, pe stație"));
            else if (n.Names is { Count: > 0 }) found.Add((key, n));
        }
        if (found.Count > 0)
        {
            int total = found.Sum(x => x.N.Names!.Count);
            string Kind(string key) => key.EndsWith("Paths") ? "căi" : key.EndsWith("Extensions") ? "extensii" : "procese";
            var listing = found.SelectMany(x => x.N.Names!.Select(nm => $"{Kind(x.Key)}: {nm}")).ToList();
            string shown = string.Join("; ", listing.Take(ListCap)) + (listing.Count > ListCap ? $"; … încă {listing.Count - ListCap}" : "");
            res.Findings.Add(MakeList(h, host, nextId, total, shown, found));
        }
    }

    private static void SystemHive(ConfigHive h, string host, Func<string> nextId, ConfigAuditResult res)
    {
        string cs = h.ControlSet;
        var smb = Smb1(cs);
        if (Read(h, smb, res, out var s1, out var s1V) && s1 == 1)
            res.Findings.Add(Make(h, host, nextId, RuleSmb1, Severity.High, Confidence.Medium,
                "Serverul SMB1 este activat: SMB1 = 1",
                "Serverul de fișiere acceptă protocolul vechi SMB versiunea 1: LanmanServer\\Parameters\\SMB1 = 1.",
                "Windows acceptă protocolul vechi de partajare SMB1",
                "gravitate ridicată: SMB1 are vulnerabilități cunoscute (de exemplu cele exploatate de EternalBlue) și nu mai este necesar în mod normal",
                [(smb, s1V!)],
                ["Echipamente sau programe foarte vechi pot cere SMB1.", "Funcția Windows SMB1 poate fi neinstalată, caz în care valoarea nu are efect."],
                ["Se arată doar setarea serverului; driverul clientului (mrxsmb10) și starea funcției Windows nu sunt citite aici.", "Valoarea este starea din hive la colectare; nu arată de când este așa."]));

        var wd = WDigest(cs);
        if (Read(h, wd, res, out var w, out var wV) && w == 1)
            res.Findings.Add(Make(h, host, nextId, RuleWdigest, Severity.High, Confidence.High,
                "WDigest păstrează parolele în clar în memorie: UseLogonCredential = 1",
                "Furnizorul WDigest este setat să păstreze parola în clar a utilizatorilor conectați în memoria procesului lsass: UseLogonCredential = 1.",
                "Windows păstrează parolele utilizatorilor conectați în clar în memorie (WDigest)",
                "gravitate ridicată: un program cu drepturi de administrator poate citi parolele în clar din memorie",
                [(wd, wV!)],
                ["Este o setare cerută uneori de aplicații vechi de autentificare.", "Aceeași setare este folosită și de atacatori pentru a obține parole; valoarea singură nu arată cine a setat-o."],
                ["Valoarea este starea din hive la colectare; nu arată de când este așa și nici dacă parole au fost efectiv citite din memorie."]));

        var lsa = Lsa(cs);
        bool lsaRead = Read(h, lsa, res, out var pl, out var plV);
        if (lsaRead && pl == 0)
            res.Findings.Add(Make(h, host, nextId, RuleLsa, Severity.Low, Confidence.Medium,
                "Protecția LSA este oprită: RunAsPPL = 0",
                "Procesul de securitate lsass nu este pornit ca proces protejat: RunAsPPL = 0.",
                "Windows nu protejează procesul de securitate lsass (LSA)",
                "gravitate mică: protecția este o măsură de întărire suplimentară, lipsa ei nu este o breșă în sine",
                [(lsa, plV!)],
                ["Pe unele versiuni Windows protecția este activă implicit chiar și când valoarea lipsește; aici valoarea este 0 explicit.", "Unele programe de securitate cer oprirea ei."],
                ["Valoarea este starea din hive la colectare; nu arată de când este așa."]));
        else if (!lsaRead && h.Values.TryGetValue(lsa.Locator, out var lv) && lv.Data is null)
            res.Findings.Add(Make(h, host, nextId, RuleLsa, Severity.Low, Confidence.Low,
                "Protecția LSA nu este setată în registru: RunAsPPL lipsește",
                "Valoarea RunAsPPL nu există în registru, deci protecția procesului de securitate lsass nu este cerută explicit.",
                "Windows nu are cerută explicit protecția procesului de securitate lsass (LSA)",
                "gravitate mică: este o măsură de întărire suplimentară și nu se poate spune din registru dacă Windows o aplică implicit",
                [(lsa, new ConfigValue("lipsă", lv.KeyLastWriteUtc))],
                ["Windows 11 (22H2 și ulterior) poate activa protecția implicit fără a scrie valoarea; valoarea lipsă nu dovedește că protecția este oprită."],
                ["Starea efectivă se vede în Event Viewer, jurnalul System, evenimentul 12 din Wininit sau în Securitate Windows → Securitatea dispozitivului → Izolarea nucleului.", "Valoarea este starea din hive la colectare."]));

        var deny = RdpDeny(cs); var nla = RdpNla(cs);
        bool denyRead = Read(h, deny, res, out var d, out var dV);
        bool nlaRead = Read(h, nla, res, out var n, out var nV);
        if (denyRead && d == 0 && nlaRead && n == 0)
            res.Findings.Add(Make(h, host, nextId, RuleRdpNla, Severity.Medium, Confidence.High,
                "Conexiunile Remote Desktop sunt permise fără autentificare la nivel de rețea: fDenyTSConnections = 0 și UserAuthentication = 0",
                "Remote Desktop este pornit (fDenyTSConnections = 0) și nu cere autentificare la nivel de rețea (UserAuthentication = 0): oricine ajunge la portul 3389 vede ecranul de conectare înainte de a se autentifica.",
                "Windows primește conexiuni Remote Desktop fără autentificare prealabilă la nivel de rețea (NLA)",
                "gravitate medie: suprafață de atac mai mare înainte de autentificare; depinde de cine poate ajunge la portul 3389 (regulile firewall nu sunt citite aici)",
                [(deny, dV!), (nla, nV!)],
                ["Clienți foarte vechi sau scenarii de laborator pot cere dezactivarea NLA.", "O politică de grup (Terminal Services) poate înlocui valoarea din RDP-Tcp."],
                ["Se arată doar registrul; firewall-ul poate bloca portul 3389, iar politica de grup poate înlocui setarea.", "Valoarea este starea din hive la colectare; nu arată de când este așa."]));
    }

    // ---- planner #14 E2: more configuration rules (each only when unsafe; unreadable or not collected = gap) ----

    private static readonly string[] HklmLimit = ["Valoarea este starea din hive la colectare; nu arată de când este așa."];

    private static void SoftwareHiveHardening(ConfigHive h, string host, Func<string> nextId, ConfigAuditResult res)
    {
        if (Read(h, TokenFilter, res, out var tf, out var tfV) && tf == 1)
            res.Findings.Add(Make(h, host, nextId, RuleTokenFilter, Severity.Medium, Confidence.High,
                "Conturile locale de administrator se pot conecta la distanță cu drepturi depline: LocalAccountTokenFilterPolicy = 1",
                "Filtrarea jetonului pentru conturile locale este oprită în registru: LocalAccountTokenFilterPolicy = 1. Un cont local din grupul Administratori care se conectează prin rețea (de exemplu prin partajări administrative) primește jetonul complet, nu cel filtrat de UAC.",
                "Windows lasă conturile locale de administrator să se conecteze prin rețea cu drepturi depline (filtrarea UAC a jetonului este oprită)",
                "gravitate medie: o parolă de administrator local aflată de un atacator poate fi folosită direct prin rețea",
                [(TokenFilter, tfV!)],
                ["Este o setare cerută uneori de unelte de administrare la distanță sau de scripturi pe stații fără domeniu.", "Poate fi o alegere a administratorului."],
                [.. HklmLimit, "Se arată doar registrul; regulile firewall și politicile de rețea nu sunt citite aici."]));

        if (Read(h, AlwaysInstall, res, out var ai, out var aiV) && ai == 1)
            res.Findings.Add(Make(h, host, nextId, RuleAlwaysInstall, Severity.High, Confidence.Medium,
                "Politica Windows Installer cere instalarea cu drepturi ridicate: AlwaysInstallElevated = 1 (HKLM)",
                @"Politica Windows Installer din HKLM cere ca pachetele .msi să se instaleze cu drepturi de sistem: Policies\Microsoft\Windows\Installer\AlwaysInstallElevated = 1. Pentru ca un utilizator obișnuit să o poată folosi la ridicarea drepturilor, aceeași valoare trebuie să existe și în HKCU al utilizatorului.",
                "Windows are politica de instalare cu drepturi ridicate (AlwaysInstallElevated) pornită pentru stație",
                "gravitate ridicată: dacă și valoarea din HKCU este 1, orice utilizator poate instala un pachet .msi cu drepturi de sistem",
                [(AlwaysInstall, aiV!)],
                ["O politică de grup veche a organizației poate fi cauza (setare rar intenționată).", "Fără valoarea din HKCU setarea singură nu permite ridicarea drepturilor."],
                [.. HklmLimit, @"Limitare: se citește doar HKLM; valoarea HKCU\SOFTWARE\Policies\Microsoft\Windows\Installer\AlwaysInstallElevated aparține profilului fiecărui utilizator și nu este citită aici."]));

        Autologon(h, host, nextId, res);
    }

    private static void Autologon(ConfigHive h, string host, Func<string> nextId, ConfigAuditResult res)
    {
        if (!h.Values.TryGetValue(AutoAdminLogon.Locator, out var aal))
        {
            res.Gaps.Add(Gap(h, AutoAdminLogon, "valoarea nu a fost citită (citirea nu a rulat sau a eșuat)"));
            return;
        }
        if (aal.Data is null || aal.Data.Trim() != "1") return;
        if (!h.Values.TryGetValue(DefaultPassword.Locator, out var pw))
        {
            res.Gaps.Add(Gap(h, DefaultPassword, "valoarea nu a fost citită din hive (citirea nu a rulat sau a eșuat)"));
            return;
        }
        string user = h.Values.TryGetValue(DefaultUserName.Locator, out var un) && !string.IsNullOrEmpty(un.Data) ? un.Data : "(lipsă)";
        bool hasPassword = !string.IsNullOrEmpty(pw.Data);
        // the password is never read into the audit (ProbeText): only that it exists
        string quote = $"AutoAdminLogon = 1; DefaultUserName = {user}; DefaultPassword = " + (hasPassword ? SecretNotShown : "(lipsă)");
        string ts = aal.TimeSource ?? TimeSource, src = aal.TimeSource is null ? $"din hive-ul {h.Hive} păstrat în caz" : "dintr-o citire doar-citire la colectare (parola nu este citită în caz)";
        var evidence = new List<EvidenceRef> { new(aal.EvidenceId ?? h.EvidenceId, AutoAdminLogon.Locator, $"valoarea AutoAdminLogon {src}", aal.Sha256 ?? h.Sha256, quote, ts, Channel) };
        evidence.Add(new(pw.EvidenceId ?? h.EvidenceId, DefaultPassword.Locator, $"existența valorii DefaultPassword {src} (conținutul nu este citit)", pw.Sha256 ?? h.Sha256, "DefaultPassword = " + (hasPassword ? SecretNotShown : "(lipsă)"), ts, Channel));
        var times = new[] { aal, pw }.Where(x => x.KeyLastWriteUtc is not null).Select(x => x.KeyLastWriteUtc!.Value).ToList();
        res.Findings.Add(hasPassword
            ? Build(h, host, nextId, RuleAutologon, Severity.High, Confidence.High,
                $"Windows se conectează automat în contul {user} cu o parolă stocată în registru: AutoAdminLogon = 1 și DefaultPassword prezentă",
                $"Logarea automată este pornită (AutoAdminLogon = 1) pentru contul {user} și registrul conține o valoare DefaultPassword nevidă. Parola se păstrează în clar în registru și poate fi citită de un cont cu drepturi de administrator. Valoarea ei nu este afișată în acest raport.",
                $"Windows se conectează singur în contul {user} și păstrează parola în clar în registru",
                "gravitate ridicată: oricine poate citi registrul (administrator, copie a hive-ului) vede parola contului",
                evidence, times,
                ["Stații de tip chioșc, ecrane de afișare sau laborator sunt configurate uneori deliberat așa.", "Parola stocată poate fi veche sau a unui cont fără alte drepturi; registrul nu o spune."],
                ["Parola nu este citită sau afișată de aplicație; se arată doar că valoarea există.", "Valoarea este starea din hive la colectare; nu arată de când este așa."])
            : Build(h, host, nextId, RuleAutologon, Severity.Medium, Confidence.Medium,
                $"Logare automată activată fără parolă stocată în registru: AutoAdminLogon = 1 (contul {user})",
                $"Logarea automată este pornită (AutoAdminLogon = 1) pentru contul {user}, dar registrul nu conține o valoare DefaultPassword nevidă: logare automată fără parolă stocată în registru (parola poate fi în LSA secrets, sau contul nu are parolă).",
                $"Windows se conectează singur în contul {user}; parola nu este în registru, dar poate fi stocată în LSA secrets",
                "gravitate medie: logarea automată există, dar parola nu se vede în registru",
                evidence, times,
                ["Parola poate fi păstrată în LSA secrets (DefaultPassword în secretele LSA), unde nu se vede din registru.", "Contul poate să nu aibă parolă."],
                ["Din hive nu se poate spune unde este parola sau dacă există una.", "Valoarea este starea din hive la colectare; nu arată de când este așa."]));
    }

    private static void SystemHiveHardening(ConfigHive h, string host, Func<string> nextId, ConfigAuditResult res)
    {
        string cs = h.ControlSet;
        var rr = RemoteRegistry(cs);
        if (Read(h, rr, res, out var start, out var startV) && start == 2)
            res.Findings.Add(Make(h, host, nextId, RuleRemoteRegistry, Severity.Medium, Confidence.High,
                "Serviciul Remote Registry pornește automat: Start = 2",
                @"Serviciul Registru la distanță (RemoteRegistry) este configurat să pornească automat: Services\RemoteRegistry\Start = 2. Implicit serviciul este manual sau dezactivat; când rulează, registrul stației poate fi citit prin rețea de conturi cu drepturi.",
                "Windows pornește automat serviciul care lasă registrul să fie citit prin rețea (Remote Registry)",
                "gravitate medie: suprafață de atac în rețea; accesul cere totuși drepturi de administrator",
                [(rr, startV!)],
                ["Unele produse de inventariere sau administrare cer serviciul pornit.", "Poate fi o alegere a administratorului."],
                [.. HklmLimit, "Se arată configurația, nu faptul că serviciul rulează acum; regulile firewall nu sunt citite aici."]));

        var sg = SmbSigning(cs);
        if (Read(h, sg, res, out var sig, out var sigV) && sig == 0)
            res.Findings.Add(Make(h, host, nextId, RuleSmbSigning, Severity.Low, Confidence.Low,
                "Serverul SMB nu cere semnarea mesajelor: RequireSecuritySignature = 0",
                @"Serverul de fișiere nu cere semnarea mesajelor SMB: LanmanServer\Parameters\RequireSecuritySignature = 0. Fără semnare un atacator aflat în rețea poate modifica mesajele sau le poate retransmite.",
                "Windows nu cere semnarea mesajelor la partajarea de fișiere SMB",
                "gravitate mică: este o măsură de întărire; valoarea implicită diferă între versiuni și un client poate cere el semnarea",
                [(sg, sigV!)],
                ["Windows 11 24H2 și Windows Server 2025 cer semnarea implicit; la versiunile mai vechi valoarea implicită este 0 pe stații, deci 0 poate fi doar starea implicită scrisă explicit.", "Politica de grup poate înlocui valoarea."],
                [.. HklmLimit, "Se arată doar serverul; semnarea cerută de client (LanmanWorkstation) și valoarea EnableSecuritySignature nu sunt citite aici."]));
    }

    /// <summary>CFG-FIREWALL-OFF: per profile the policy value (SOFTWARE\Policies\Microsoft\WindowsFirewall\&lt;profile&gt;) wins over the local one (SYSTEM FirewallPolicy); the finding says which source decided.</summary>
    private static void Firewall(ConfigHive sys, ConfigHive? soft, string host, Func<string> nextId, ConfigAuditResult res)
    {
        var off = new List<(string Label, ConfigProbe P, ConfigValue V, bool ByPolicy)>();
        var policyUnknown = new List<string>();
        foreach (var (name, label) in FirewallProfiles)
        {
            var local = FwLocal(sys.ControlSet, name);
            bool localRead = Read(sys, local, res, out var lv, out var lval);
            var pol = FwPolicy(name);
            bool policyRead = false, policyAbsent = false; uint pv = 0; ConfigValue? pval = null;
            if (soft is not null)
            {
                policyRead = Read(soft, pol, res, out pv, out pval);   // a policy probe that was not read is already a gap
                policyAbsent = !policyRead && soft.Values.TryGetValue(pol.Locator, out var rec) && rec.Data is null;
            }
            if (policyRead)
            {
                if (pv == 0) off.Add((label, pol, pval! with { EvidenceId = pval.EvidenceId ?? soft!.EvidenceId, Sha256 = pval.Sha256 ?? soft!.Sha256 }, true));
                continue;   // the policy decides, on or off
            }
            if (localRead && lv == 0)
            {
                off.Add((label, local, lval!, false));
                if (!policyAbsent) policyUnknown.Add(label);
            }
        }
        if (off.Count == 0) return;
        string names = string.Join(", ", off.Select(x => x.Label));
        string sources = string.Join("; ", off.Select(x => $"{x.Label}: {(x.ByPolicy ? @"politica (SOFTWARE\Policies\Microsoft\WindowsFirewall)" : @"setarea locală (SYSTEM\…\FirewallPolicy)")}"));
        var evidence = off.Select(x => new EvidenceRef(x.V.EvidenceId ?? sys.EvidenceId, x.P.Locator, $"valoarea EnableFirewall (profil {x.Label}) din hive-ul {x.P.Hive} păstrat în caz", x.V.Sha256 ?? sys.Sha256, "EnableFirewall = 0", TimeSource, Channel)).ToList();
        var times = off.Where(x => x.V.KeyLastWriteUtc is not null).Select(x => x.V.KeyLastWriteUtc!.Value).ToList();
        var limits = new List<string> { "Se arată registrul, nu starea efectivă: un produs de securitate poate înlocui Windows Firewall, iar politica aplicată la pornire poate diferi.", "Valoarea este starea din hive la colectare; nu arată de când este așa." };
        if (policyUnknown.Count > 0) limits.Add($"Pentru profilurile {string.Join(", ", policyUnknown)} nu s-a putut confirma lipsa unei politici care să înlocuiască setarea locală (hive-ul SOFTWARE sau cheia de politică nu a fost citită).");
        res.Findings.Add(Build(sys, host, nextId, RuleFirewallOff, Severity.High, Confidence.Medium,
            "Windows Firewall este oprit în registru pentru profilurile: " + names,
            $@"Registrul arată EnableFirewall = 0 pentru profilurile {names}. Sursa care a decis, pe profil: {sources}. O valoare din politică (SOFTWARE\Policies\Microsoft\WindowsFirewall) are prioritate față de cea locală.",
            "Windows are firewall-ul oprit în registru pentru unele profiluri de rețea",
            "gravitate ridicată: fără firewall conexiunile primite nu sunt filtrate pe profilurile respective",
            evidence, times,
            ["Un firewall al unui alt produs de securitate poate fi motivul (Windows Firewall este oprit când altul preia rolul).", "Poate fi o alegere a administratorului sau o politică a organizației."],
            [.. limits]));
    }

    // ---- finding builders ----

    private static Finding Make(ConfigHive h, string host, Func<string> nextId, string rule, Severity sev, Confidence conf, string title, string description, string plainWhat,
                                string sevReason, IReadOnlyList<(ConfigProbe P, ConfigValue V)> refs, string[] alternatives, string[] limitations)
    {
        var evidence = refs.Select(x => new EvidenceRef(x.V.EvidenceId ?? h.EvidenceId, x.P.Locator, $"valoarea {x.P.Name} din hive-ul {h.Hive} păstrat în caz", x.V.Sha256 ?? h.Sha256,
            $"{x.P.Name} = {x.V.Data}", TimeSource, Channel)).ToList();
        var times = refs.Where(x => x.V.KeyLastWriteUtc is not null).Select(x => x.V.KeyLastWriteUtc!.Value).ToList();
        return Build(h, host, nextId, rule, sev, conf, title, description, plainWhat, sevReason, evidence, times, alternatives, limitations);
    }

    private static Finding MakeList(ConfigHive h, string host, Func<string> nextId, int total, string shown, List<(string Key, ConfigNames N)> found)
    {
        var evidence = found.Select(x => new EvidenceRef(x.N.EvidenceId ?? h.EvidenceId, "SOFTWARE\\" + x.Key, $"cheia {x.Key} din hive-ul SOFTWARE păstrat în caz", x.N.Sha256 ?? h.Sha256,
            $"{x.N.Names!.Count} excluderi: " + string.Join("; ", x.N.Names!.Take(ListCap)) + (x.N.Names!.Count > ListCap ? $"; … încă {x.N.Names!.Count - ListCap}" : ""), TimeSource, Channel)).ToList();
        var times = found.Where(x => x.N.KeyLastWriteUtc is not null).Select(x => x.N.KeyLastWriteUtc!.Value).ToList();
        return Build(h, host, nextId, RuleDefenderExcl, Severity.Medium, Confidence.High,
            $"Microsoft Defender are {total} excluderi de la scanare",
            $"Registrul listează {total} excluderi de la scanarea antivirus: {shown}.",
            $"Windows are {total} excluderi de la scanarea antivirus Defender",
            "gravitate medie: un program sau un fișier dintr-o excludere nu este scanat, deci poate ascunde un program rău-intenționat",
            evidence, times,
            ["Excluderile sunt adesea puse de administrator pentru performanță (baze de date, mașini virtuale, unelte de compilare).", "Programele de securitate sau de dezvoltare își adaugă singure excluderi."],
            ["Se arată excluderile din registru; excluderile aplicate prin politică sau gestionate de un produs de administrare pot diferi.", "Ora este ultima scriere a cheii de excluderi, nu momentul adăugării fiecărei excluderi."]);
    }

    private static Finding Build(ConfigHive h, string host, Func<string> nextId, string rule, Severity sev, Confidence conf, string title, string description, string plainWhat,
                                 string sevReason, List<EvidenceRef> evidence, List<DateTimeOffset> times, string[] alternatives, string[] limitations)
    {
        DateTimeOffset? first = times.Count > 0 ? times.Min() : null, last = times.Count > 0 ? times.Max() : null;
        return new Finding
        {
            FindingId = nextId(), RuleId = rule, Title = title + OriginNote(h), Description = description, Severity = sev, Confidence = conf,
            Classification = Classification.Direct, Category = Category, Host = host,
            FirstSeenUtc = first, LastSeenUtc = last, PlainWhat = plainWhat, SemanticType = SemanticType.Configuration,
            SupportingEvidence = evidence,
            ClassificationReason = $"Valoare citită direct din hive-ul {h.Hive} păstrat în caz (SHA-256 în indexul probelor); {sevReason}.",
            AlternativeExplanations = [.. alternatives],
            Limitations = [.. limitations, "Ora afișată este ultima scriere a cheii care conține valoarea (Registry LastWrite), nu momentul în care valoarea a fost setată." + OriginNote(h)],
        };
    }
}
