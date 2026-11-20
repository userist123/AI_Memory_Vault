using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Planner #12 B3: audit-policy, Guest and local-Administrators configuration findings, from the rows of the STORED evidence (auditpol CSV and the local accounts JSON saved by
/// LocalSecurityStateCollector), each with the raw line as its quote. Facts only, no verdict word. A setting that is safe gives nothing; a setting that cannot be read or
/// understood gives an <see cref="EvidenceGap"/>, never a finding. When a case holds several snapshots, only the newest of each kind is judged.
/// </summary>
public static class LocalSecurityAudit
{
    public const string Producer = "LogAnalyzer.Dfir.Analysis.LocalSecurityAudit";
    public const string AuditSource = "auditpol", AccountsSource = "Conturi locale";
    public const string Category = "Configurație";
    public const int ListCap = 20;

    public const string RuleProcess = "CFG-AUDIT-PROCESS", RuleLogon = "CFG-AUDIT-LOGON", RuleCredVal = "CFG-AUDIT-CREDVAL", RuleGuest = "CFG-GUEST-ENABLED", RuleExtraAdmins = "CFG-EXTRA-ADMINS";

    public const string GuidProcessCreation = "0CCE922B-69AE-11D9-BED3-505054503030", GuidLogon = "0CCE9215-69AE-11D9-BED3-505054503030", GuidCredentialValidation = "0CCE923F-69AE-11D9-BED3-505054503030";

    private static readonly Regex BuiltinAdminSid = new(@"^S-1-5-21-\d+-\d+-\d+-500$", RegexOptions.Compiled);
    private static readonly Regex DomainAdminsSid = new(@"^S-1-5-21-\d+-\d+-\d+-512$", RegexOptions.Compiled);

    public static ConfigAuditResult Evaluate(IEnumerable<TimelineEvent> timeline, string host, Func<string> nextId)
    {
        var res = new ConfigAuditResult();
        var rows = timeline.ToList();
        var audit = Newest(rows, AuditSource);
        if (audit.Count > 0) AuditRules(audit, host, nextId, res);
        var accounts = Newest(rows, AccountsSource);
        if (accounts.Count > 0) AccountRules(accounts, host, nextId, res);
        return res;
    }

    /// <summary>The rows of the newest evidence item of this source (by the time of its rows).</summary>
    private static List<TimelineEvent> Newest(List<TimelineEvent> rows, string source)
    {
        var mine = rows.Where(r => r.Source == source).ToList();
        if (mine.Count == 0) return mine;
        var id = mine.GroupBy(r => r.EvidenceId).OrderByDescending(g => g.Max(r => r.Time.Utc ?? DateTimeOffset.MinValue)).ThenByDescending(g => g.Key, StringComparer.Ordinal).First().Key;
        return mine.Where(r => r.EvidenceId == id).ToList();
    }

    private static string F(TimelineEvent r, string k) => r.Fields.TryGetValue(k, out var v) ? v : "";

    // ---- audit policy ----

    private static void AuditRules(List<TimelineEvent> audit, string host, Func<string> nextId, ConfigAuditResult res)
    {
        // Process Creation: no Success auditing means no 4688
        if (Find(audit, GuidProcessCreation, "Process Creation", res) is { } pc && F(pc, "AuditSuccess") == "nu")
            res.Findings.Add(Make(pc, host, nextId, RuleProcess, Severity.Medium, Confidence.High,
                $"Politica de audit nu înregistrează crearea proceselor: {F(pc, "Subcategory")} = {F(pc, "Setting")}",
                $"Subcategoria de audit „{F(pc, "Subcategory")}” ({GuidProcessCreation}) are setarea „{F(pc, "Setting")}”: la pornirea unui program nu se scrie evenimentul Security 4688.",
                "Windows nu scrie în jurnalul Security ce programe pornesc",
                "gravitate medie: fără 4688 pornirea programelor nu poate fi reconstituită din jurnalul Security",
                ["Un produs EDR sau Sysmon poate înregistra procesele în alt jurnal.", "Poate fi o alegere a administratorului pentru a reduce volumul jurnalului."],
                ["Se arată politica de la colectare; nu arată de când este așa (schimbările politicii se văd în Security 4719, dacă a fost auditat).", "Lipsa evenimentelor 4688 într-o perioadă poate fi explicată de această setare, dar nu dovedește că nu au rulat programe.", "Linia de comandă din 4688 are nevoie și de politica „Include command line in process creation events”, care nu este citită aici."]));

        // Logon: 4624 / 4625 need Success and Failure
        if (Find(audit, GuidLogon, "Logon", res) is { } lg && Missing(lg) is { Length: > 0 } miss)
            res.Findings.Add(Make(lg, host, nextId, RuleLogon, Severity.Medium, Confidence.High,
                $"Politica de audit nu înregistrează toate autentificările: {F(lg, "Subcategory")} = {F(lg, "Setting")}",
                $"Subcategoria de audit „{F(lg, "Subcategory")}” ({GuidLogon}) are setarea „{F(lg, "Setting")}”: lipsește auditarea pentru {miss}, deci evenimentele Security 4624 (autentificare reușită) și 4625 (autentificare eșuată) nu sunt toate scrise.",
                "Windows nu scrie în jurnalul Security toate autentificările reușite și eșuate",
                "gravitate medie: golurile din 4624/4625 fac imposibilă reconstituirea cine s-a autentificat și când",
                ["Un produs de monitorizare poate colecta autentificările din altă sursă.", "Poate fi o alegere a administratorului pentru a reduce volumul jurnalului."],
                ["Se arată politica de la colectare; nu arată de când este așa (Security 4719).", "Absența unui 4624/4625 într-o perioadă poate fi explicată de această setare, dar nu dovedește că nu a existat o autentificare."]));

        // Credential validation: 4776 (NTLM validation of local / domain credentials)
        if (Find(audit, GuidCredentialValidation, "Credential Validation", res) is { } cv && Missing(cv) is { Length: > 0 } missCv)
            res.Findings.Add(Make(cv, host, nextId, RuleCredVal, Severity.Medium, Confidence.High,
                $"Politica de audit nu înregistrează toate validările de credențiale: {F(cv, "Subcategory")} = {F(cv, "Setting")}",
                $"Subcategoria de audit „{F(cv, "Subcategory")}” ({GuidCredentialValidation}) are setarea „{F(cv, "Setting")}”: lipsește auditarea pentru {missCv}, deci validările de parole (Security 4776) nu sunt toate scrise.",
                "Windows nu scrie în jurnalul Security toate validările de parole (NTLM)",
                "gravitate medie: încercările repetate de parole greșite nu se văd în 4776",
                ["Autentificările Kerberos nu trec prin această subcategorie (au altă subcategorie, pe controlerul de domeniu).", "Poate fi o alegere a administratorului pentru a reduce volumul jurnalului."],
                ["Se arată politica de la colectare; nu arată de când este așa (Security 4719).", "Pe un PC din domeniu validarea parolelor de domeniu se scrie pe controlerul de domeniu, nu pe stație."]));
    }

    /// <summary>"Success" / "Failure" / "Success and Failure" in Romanian words for what the setting does not audit; "" = nothing missing; the unknown case is handled by <see cref="Find"/>.</summary>
    private static string Missing(TimelineEvent r)
    {
        bool s = F(r, "AuditSuccess") == "da", fl = F(r, "AuditFailure") == "da";
        return s && fl ? "" : !s && !fl ? "succes și eșec" : !s ? "succes" : "eșec";
    }

    private static TimelineEvent? Find(List<TimelineEvent> audit, string guid, string name, ConfigAuditResult res)
    {
        var row = audit.FirstOrDefault(r => F(r, "SubcategoryGuid").Equals(guid, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            res.Gaps.Add(new EvidenceGap($"Politica de audit: {name}", EvidenceStatus.Failed, $"subcategoria {guid} nu apare în ieșirea auditpol",
                "Setarea nu poate fi evaluată: lipsa unei constatări nu înseamnă că auditarea este pornită", $"auditpol /get /subcategory:\"{{{guid}}}\"", "Da, pe stație, cu drepturi de administrator"));
            return null;
        }
        if (F(row, "AuditSuccess") == "necunoscut" || F(row, "AuditFailure") == "necunoscut")
        {
            res.Gaps.Add(new EvidenceGap($"Politica de audit: {name}", EvidenceStatus.Failed, $"textul setării „{F(row, "Setting")}” nu a putut fi interpretat",
                "Setarea nu poate fi evaluată: lipsa unei constatări nu înseamnă că auditarea este pornită", $"auditpol /get /subcategory:\"{F(row, "Subcategory")}\"", "Da, pe stație, cu drepturi de administrator"));
            return null;
        }
        return row;
    }

    // ---- accounts ----

    private static void AccountRules(List<TimelineEvent> rows, string host, Func<string> nextId, ConfigAuditResult res)
    {
        // Guest: the account with RID 501 (renamed or localized, the RID stays) is enabled
        foreach (var g in rows.Where(r => r.Locator.StartsWith("LocalAccounts[", StringComparison.Ordinal) && F(r, "Rid") == "501" && F(r, "Enabled") == "da"))
            res.Findings.Add(Make(g, host, nextId, RuleGuest, Severity.High, Confidence.High,
                $"Contul Guest este activat: {F(g, "Name")} (RID 501)",
                $"Contul local {F(g, "Name")} (SID {F(g, "Sid")}, RID 501, contul Guest) este activat în acest PC.",
                "Windows are activat contul Guest (oaspete), care permite accesul fără parolă personală",
                "gravitate ridicată: un cont partajat și, de regulă, fără parolă lărgește suprafața de acces",
                ["Poate fi activat deliberat pentru o partajare sau o demonstrație.", "O politică de securitate locală poate cere starea, iar un produs de administrare o poate reseta."],
                ["Se arată starea de la colectare; nu arată de când este activat și nici dacă a fost folosit (Security 4624 pentru contul respectiv).", "Faptul că un cont este activat nu arată ce drepturi i s-au dat (apartenența la grupuri nu este citită aici)."]));

        // Administrators other than the built-in RID-500 account and (on a domain PC) Domain Admins
        var members = rows.Where(r => r.Locator.StartsWith("LocalAdministrators[", StringComparison.Ordinal)).ToList();
        if (members.Count == 0) return;
        bool domainPc = members.Any(m => F(m, "PartOfDomain") == "da");
        var extra = members.Where(m => !BuiltinAdminSid.IsMatch(F(m, "Sid")) && !(domainPc && F(m, "Kind") == "group" && DomainAdminsSid.IsMatch(F(m, "Sid")))).ToList();
        if (extra.Count == 0) return;
        string interactive = F(members[0], "InteractiveUser");
        bool onlyInteractive = extra.Count == 1 && F(extra[0], "Kind") == "user" && interactive.Length > 0 && SameAccount(F(extra[0], "Member"), interactive);
        var listing = extra.Select(m => $"{F(m, "Member")} ({KindRo(F(m, "Kind"))}, {ScopeRo(F(m, "Scope"))}{(F(m, "Sid").Length == 0 ? ", SID necitit" : "")})").ToList();
        string shown = string.Join("; ", listing.Take(ListCap)) + (listing.Count > ListCap ? $"; … încă {listing.Count - ListCap}" : "");
        string expected = domainPc ? "contul Administrator încorporat (RID 500) și grupul Domain Admins" : "contul Administrator încorporat (RID 500)";
        var first = extra[0];
        var evidence = extra.Take(ListCap).Select(m => Ref(m, $"membrul {F(m, "Member")} din grupul Administrators (SID {AdminSid}) păstrat în caz")).ToList();
        res.Findings.Add(new Finding
        {
            FindingId = nextId(), RuleId = RuleExtraAdmins, Severity = onlyInteractive ? Severity.Low : Severity.Medium, Confidence = Confidence.High,
            Classification = Classification.Direct, Category = Category, Host = host, SemanticType = SemanticType.Configuration,
            Title = onlyInteractive ? $"Contul utilizatorului curent este membru al grupului Administrators: {F(first, "Member")}"
                                    : $"{extra.Count} conturi sau grupuri în afara {expected} sunt în grupul Administrators",
            Description = $"Grupul local Administrators (SID {AdminSid}) conține, pe lângă {expected}: {shown}. Verificați dacă fiecare cont are nevoie de drepturi de administrator.",
            PlainWhat = onlyInteractive ? "Contul cu care este folosit PC-ul are drepturi de administrator" : "Windows are conturi cu drepturi de administrator în afara celor așteptate",
            FirstSeenUtc = first.Time.Utc, LastSeenUtc = first.Time.Utc, SupportingEvidence = evidence,
            ClassificationReason = $"Apartenență citită direct din grupul cu SID-ul {AdminSid} (după SID, deci numele localizat al grupului nu contează), păstrată în caz cu SHA-256; " +
                                   (onlyInteractive ? "gravitate mică: singurul membru în plus este contul utilizatorului curent." : "gravitate medie: fiecare cont în plus poate schimba setările sistemului."),
            AlternativeExplanations =
            [
                onlyInteractive ? "Pe un PC cu un singur utilizator contul proprietarului este de regulă administrator; este o stare normală." : "Un administrator de sistem sau un produs de administrare poate adăuga conturi în mod deliberat (suport, instalatoare, politici).",
                "Un cont de serviciu sau al unui program instalat poate fi membru din motive legitime.",
            ],
            Limitations =
            [
                "Se arată apartenența directă la colectare; grupurile imbricate nu sunt desfăcute și nu se știe cine a adăugat contul sau când (Security 4732 arată adăugarea, dacă a fost auditată).",
                "Faptul că un cont este membru nu arată că a folosit drepturile.",
                .. domainPc ? ["Pe un PC din domeniu, membrii grupurilor de domeniu nu sunt enumerați."] : Array.Empty<string>(),
            ],
        });
    }

    private const string AdminSid = "S-1-5-32-544";
    private static string KindRo(string k) => k switch { "user" => "utilizator", "group" => "grup", _ => "cont de sistem" };
    private static string ScopeRo(string s) => s switch { "local" => "local", "domain" => "domeniu", _ => "încorporat" };

    private static bool SameAccount(string member, string interactive)
    {
        if (member.Equals(interactive, StringComparison.OrdinalIgnoreCase)) return true;
        static string Name(string s) => s.Contains('\\') ? s[(s.LastIndexOf('\\') + 1)..] : s;
        // the interactive user comes as DOMAIN\name; a member without a domain part is compared by name only
        return !member.Contains('\\') && Name(member).Equals(Name(interactive), StringComparison.OrdinalIgnoreCase);
    }

    // ---- builders ----

    private static EvidenceRef Ref(TimelineEvent row, string description)
    {
        var (quote, _, _) = EvidenceQuote.From(row);
        return new EvidenceRef(row.EvidenceId, row.Locator, description, row.SourceSha256, quote, EvidenceQuote.SnapshotTime, row.Source);
    }

    private static Finding Make(TimelineEvent row, string host, Func<string> nextId, string rule, Severity sev, Confidence conf, string title, string description, string plainWhat,
                                string sevReason, string[] alternatives, string[] limitations) => new()
    {
        FindingId = nextId(), RuleId = rule, Title = title, Description = description, Severity = sev, Confidence = conf, Classification = Classification.Direct,
        Category = Category, Host = host, FirstSeenUtc = row.Time.Utc, LastSeenUtc = row.Time.Utc, PlainWhat = plainWhat, SemanticType = SemanticType.Configuration,
        SupportingEvidence = [Ref(row, row.Source == AuditSource ? $"linia subcategoriei {F(row, "Subcategory")} din politica de audit păstrată în caz" : $"contul {F(row, "Name")} din instantaneul conturilor locale păstrat în caz")],
        ClassificationReason = $"Valoare citită direct din copia păstrată în caz (SHA-256 în indexul probelor); {sevReason}.",
        AlternativeExplanations = [.. alternatives],
        Limitations = [.. limitations, "Ora este momentul colectării (instantaneu), nu momentul în care setarea a fost făcută."],
    };
}
