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
public sealed record ConfigValue(string? Data, DateTimeOffset? KeyLastWriteUtc, string? EvidenceId = null, string? Sha256 = null);

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
        RuleDefenderOff = "CFG-DEFENDER-OFF", RuleDefenderExcl = "CFG-DEFENDER-EXCL", RuleRdpNla = "CFG-RDP-NLA";

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

    public static ConfigProbe Smb1(string cs) => new("SYSTEM", $@"{cs}\Services\LanmanServer\Parameters", "SMB1");
    public static ConfigProbe WDigest(string cs) => new("SYSTEM", $@"{cs}\Control\SecurityProviders\WDigest", "UseLogonCredential");
    public static ConfigProbe Lsa(string cs) => new("SYSTEM", $@"{cs}\Control\Lsa", "RunAsPPL");
    public static ConfigProbe RdpDeny(string cs) => new("SYSTEM", $@"{cs}\Control\Terminal Server", "fDenyTSConnections");
    public static ConfigProbe RdpNla(string cs) => new("SYSTEM", $@"{cs}\Control\Terminal Server\WinStations\RDP-Tcp", "UserAuthentication");

    /// <summary>The values the reader must fetch from a stored hive of this kind (the rules below look them up by locator).</summary>
    public static IReadOnlyList<ConfigProbe> Probes(string hive, string controlSet) => hive switch
    {
        "SOFTWARE" => [UacEnable, UacConsent, RtpLocal, RtpPolicy, AvPolicy],
        "SYSTEM" => [Smb1(controlSet), WDigest(controlSet), Lsa(controlSet), RdpDeny(controlSet), RdpNla(controlSet)],
        _ => [],
    };

    /// <summary>The list keys (value names are the data) the reader must fetch; locator = "SOFTWARE\key".</summary>
    public static IReadOnlyList<string> ListKeys(string hive) => hive == "SOFTWARE" ? ExclusionKeys : [];

    // ---- evaluation ----

    public static ConfigAuditResult Evaluate(IEnumerable<ConfigHive> hives, string host, Func<string> nextId)
    {
        var res = new ConfigAuditResult();
        foreach (var h in hives)
        {
            if (h.Error.Length > 0)
            {
                res.Gaps.Add(new EvidenceGap($"Configurație din hive-ul {h.Hive}", EvidenceStatus.Failed, h.Error,
                    h.Hive == "SOFTWARE" ? "Setările UAC, Defender (protecție în timp real, excluderi) nu au putut fi verificate" : "Setările SMB1, WDigest, protecția LSA, RDP/NLA nu au putut fi verificate",
                    $@"reg query pe stație sau o copie nouă a hive-ului {h.Hive}", "Doar prin reachiziție"));
                continue;
            }
            if (h.Hive == "SOFTWARE") SoftwareHive(h, host, nextId, res); else if (h.Hive == "SYSTEM") SystemHive(h, host, nextId, res);
        }
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
