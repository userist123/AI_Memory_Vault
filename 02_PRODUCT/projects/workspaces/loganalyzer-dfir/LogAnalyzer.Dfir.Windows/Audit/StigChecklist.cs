using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// DISA STIG Viewer 3 checklist (.cklb, JSON) of the station control, for the Windows 11 STIG V2R3. The export never claims more than the application verified:
/// <list type="bullet">
/// <item><see cref="Kind.Exact"/>: the rule is evaluated alone from the exact setting the check read (not from the check's overall status).</item>
/// <item><see cref="Kind.Whole"/>: the application's check has the same criterion as the rule, so its status is carried over.</item>
/// <item><see cref="Kind.WholeConformOnly"/>: like Whole, but a NECONFORM is left "not reviewed" (the application's check cannot exclude another product satisfying the rule).</item>
/// <item><see cref="Kind.Evidence"/>: the check differs from the rule or covers several rules; the rule is always "not reviewed" with the check's evidence attached.</item>
/// </list>
/// A registry value the STIG names must exist with the stated value: a missing value is "open" whatever the Windows default (the STIG check text says so).
/// "not_applicable" is never written: the application's NU SE APLICĂ comes from the station posture, not from the STIG's own NA conditions, so such a rule is
/// "not_reviewed" with the reason. A check that did not run gives "not_reviewed". The rule-id (SV-…) fields are left empty on purpose:
/// they were not verified against the source. Rule version, V-id, severity and title are copied from the V2R3 text (stigaview.com, not the current release).
/// </summary>
public static class StigChecklist
{
    public const string FileExtension = ".cklb";

    public const string NotAFinding = "not_a_finding";
    public const string Open = "open";
    public const string NotApplicable = "not_applicable";
    public const string NotReviewed = "not_reviewed";

    private const string Absent = StationFactCollector.Absent;
    private const int DetailsCap = 8000;

    internal enum Kind { Exact, Whole, WholeConformOnly, Evidence }

    internal sealed record Rule(string Version, string VId, string Severity, string Title, string CheckId, Kind Kind);

    /// <summary>The rules of the export (rule version, V-id, severity and title as in the V2R3 text); nothing outside this table is ever written.</summary>
    internal static readonly IReadOnlyList<Rule> Rules =
    [
        new("WN11-SO-000145", "V-253453", "high", "Anonymous enumeration of SAM accounts must not be allowed.", "P40", Kind.Exact),
        new("WN11-SO-000150", "V-253454", "high", "Anonymous enumeration of shares must be restricted.", "P40", Kind.Exact),
        new("WN11-SO-000160", "V-253455", "medium", "The system must be configured to prevent anonymous users from having the same rights as the Everyone group.", "P40", Kind.Exact),
        new("WN11-SO-000165", "V-253456", "high", "Anonymous access to Named Pipes and Shares must be restricted.", "P40", Kind.Exact),
        new("WN11-SO-000195", "V-253461", "high", "The system must be configured to prevent the storage of the LAN Manager hash of passwords.", "P41", Kind.Exact),
        new("WN11-SO-000205", "V-253462", "high", "The LanMan authentication level must be set to send NTLMv2 response only, and to refuse LM and NTLM.", "P41", Kind.Exact),
        new("WN11-UR-000015", "V-253481", "high", "The \"Act as part of the operating system\" user right must not be assigned to any groups or accounts.", "P42", Kind.Exact),
        new("WN11-UR-000045", "V-253486", "high", "The \"Create a token object\" user right must not be assigned to any groups or accounts.", "P42", Kind.Exact),
        new("WN11-UR-000065", "V-253490", "high", "The \"Debug programs\" user right must only be assigned to the Administrators group.", "P42", Kind.Exact),
        new("WN11-CC-000315", "V-253411", "high", "The Windows Installer feature \"Always install with elevated privileges\" must be disabled.", "P43", Kind.Exact),
        new("WN11-00-000145", "V-253283", "high", "Data Execution Prevention (DEP) must be configured to at least OptOut.", "P44", Kind.Exact),
        new("WN11-00-000150", "V-253284", "high", "Structured Exception Handling Overwrite Protection (SEHOP) must be enabled.", "P44", Kind.Exact),
        new("WN11-CC-000330", "V-253416", "high", "The Windows Remote Management (WinRM) client must not use Basic authentication.", "P45", Kind.Exact),
        new("WN11-CC-000335", "V-253417", "medium", "The Windows Remote Management (WinRM) client must not allow unencrypted traffic.", "P45", Kind.Exact),
        new("WN11-CC-000345", "V-253418", "high", "The Windows Remote Management (WinRM) service must not use Basic authentication.", "P45", Kind.Exact),
        new("WN11-CC-000350", "V-253419", "medium", "The Windows Remote Management (WinRM) service must not allow unencrypted traffic.", "P45", Kind.Exact),
        new("WN11-00-000100", "V-253275", "high", "Internet Information System (IIS) or its subcomponents must not be installed on a workstation.", "P47", Kind.Exact),
        new("WN11-SO-000010", "V-253433", "medium", "The built-in guest account must be disabled.", "C01", Kind.Evidence),
        new("WN11-00-000135", "V-253281", "medium", "A host-based firewall must be installed and enabled on the system.", "P07", Kind.WholeConformOnly),
        new("WN11-00-000045", "V-253264", "high", "The Windows 11 system must use an antivirus program.", "P08", Kind.Evidence),
        new("WN11-CC-000038", "V-253358", "medium", "WDigest Authentication must be disabled.", "P13", Kind.Evidence),
        new("WN11-00-000020", "V-253257", "medium", "Secure Boot must be enabled on Windows 11 systems.", "P15", Kind.Whole),
        new("WN11-00-000155", "V-253285", "medium", "The Windows PowerShell 2.0 feature must be disabled on the system.", "P24", Kind.Evidence),
        new("WN11-CC-000155", "V-253382", "high", "Solicited Remote Assistance must not be allowed.", "N07", Kind.Evidence),
        new("WN11-SO-000005", "V-253432", "medium", "The built-in administrator account must be disabled.", "C03", Kind.Evidence),
        new("WN11-SO-000020", "V-253435", "medium", "The built-in administrator account must be renamed.", "C03", Kind.Evidence),
        new("WN11-AC-000035", "V-253303", "medium", "Passwords must, at a minimum, be 14 characters.", "P01", Kind.Evidence),
        new("WN11-AC-000010", "V-253298", "medium", "The number of allowed bad logon attempts must be configured to three or less.", "P02", Kind.Evidence),
        new("WN11-AC-000025", "V-253301", "medium", "The maximum password age must be configured to 60 days or less.", "P03", Kind.Evidence),
        new("WN11-SO-000270", "V-253474", "medium", "User Account Control must run all administrators in Admin Approval Mode, enabling UAC.", "P04", Kind.Evidence),
        new("WN11-SO-000250", "V-253469", "medium", "User Account Control must prompt administrators for consent on the secure desktop.", "P04", Kind.Evidence),
        new("WN11-00-000030", "V-253259", "high", "Windows 11 information systems must use BitLocker to encrypt all disks to protect the confidentiality and integrity of all information at rest.", "P11", Kind.Evidence),
        new("WN11-00-000160", "V-253286", "medium", "The Server Message Block (SMB) v1 protocol must be disabled on the system.", "P12", Kind.Evidence),
        new("WN11-00-000165", "V-253287", "medium", "The Server Message Block (SMB) v1 protocol must be disabled on the SMB server.", "P12", Kind.Evidence),
        new("WN11-00-000170", "V-253288", "medium", "The Server Message Block (SMB) v1 protocol must be disabled on the SMB client.", "P12", Kind.Evidence),
        new("WN11-SO-000070", "V-253444", "medium", "The machine inactivity limit must be set to 15 minutes, locking the system with the screensaver.", "P16", Kind.Evidence),
        new("WN11-00-000035", "V-253262", "medium", "The operating system must employ a deny-all, permit-by-exception policy to allow the execution of authorized software programs.", "P17", Kind.Evidence),
        new("WN11-CC-000180", "V-253386", "high", "Autoplay must be turned off for non-volume devices.", "P18", Kind.Evidence),
        new("WN11-CC-000185", "V-253387", "high", "The default autorun behavior must be configured to prevent autorun commands.", "P18", Kind.Evidence),
        new("WN11-CC-000190", "V-253388", "high", "Autoplay must be disabled for all drives.", "P18", Kind.Evidence),
        new("WN11-CC-000005", "V-253350", "medium", "Camera access from the lock screen must be disabled.", "P19", Kind.Evidence),
        new("WN11-CC-000075", "V-253370", "high", "Credential Guard must be running on Windows 11 domain-joined systems.", "P22", Kind.Evidence),
        new("WN11-CC-000326", "V-253414", "medium", "PowerShell script block logging must be enabled on Windows 11.", "P23", Kind.Evidence),
        new("WN11-CC-000327", "V-253415", "medium", "PowerShell Transcription must be enabled on Windows 11.", "P23", Kind.Evidence),
        new("WN11-SO-000075", "V-253445", "medium", "The required legal notice must be configured to display before console logon.", "P26", Kind.Evidence),
        new("WN11-SO-000080", "V-253446", "low", "The Windows message title for the legal notice must be configured.", "P26", Kind.Evidence),
        new("WN11-00-000031", "V-253260", "high", "Windows 11 systems must use a BitLocker PIN for pre-boot authentication.", "P32", Kind.Evidence),
        new("WN11-00-000010", "V-253255", "medium", "Windows 11 domain-joined systems must have a Trusted Platform Module (TPM) enabled.", "P33", Kind.Evidence),
        new("WN11-CC-000070", "V-253369", "medium", "Virtualization-based Security must be enabled on Windows 11 with the platform security level configured to Secure Boot or Secure Boot with DMA Protection.", "P35", Kind.Evidence),
        new("WN11-CC-000145", "V-253380", "medium", "Users must be prompted for a password on resume from sleep (on battery).", "P36", Kind.Evidence),
        new("WN11-CC-000150", "V-253381", "medium", "The user must be prompted for a password on resume from sleep (plugged in).", "P36", Kind.Evidence),
        new("WN11-AU-000505", "V-253338", "medium", "The Security event log size must be configured to 1024000 KB or greater.", "A02", Kind.Evidence),
    ];

    public static string FileName(string host, DateTimeOffset now)
    {
        var safe = string.Concat((host ?? "").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return $"Control_statie_STIG_{(safe.Length == 0 ? "statie" : safe)}_{now.UtcDateTime:yyyyMMdd_HHmmss}{FileExtension}";
    }

    public static string Build(ControlReport report, DateTimeOffset now)
    {
        var host = report.Facts.Host;
        var stigUuid = Guid.NewGuid().ToString();
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("title", string.Create(CultureInfo.InvariantCulture, $"Control stație — {host} — {now.UtcDateTime:yyyy-MM-dd HH:mm} UTC"));
            w.WriteString("id", Guid.NewGuid().ToString());
            w.WriteStartArray("stigs");
            w.WriteStartObject();
            w.WriteString("stig_name", "Microsoft Windows 11 Security Technical Implementation Guide");
            w.WriteString("display_name", "Microsoft Windows 11");
            w.WriteString("stig_id", "MS_Windows_11_STIG");
            w.WriteString("release_info", "Release: 3 (V2R3; date not verified)");
            w.WriteString("version", "2");
            w.WriteString("uuid", stigUuid);
            w.WriteString("reference_identifier", "");
            w.WriteNumber("size", Rules.Count);
            w.WriteStartArray("rules");
            foreach (var rule in Rules) WriteRule(w, rule, report, stigUuid);
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteBoolean("active", true);
            w.WriteNumber("mode", 2);
            w.WriteBoolean("has_path", false);
            w.WriteStartObject("target_data");
            w.WriteString("target_type", "Computing");
            w.WriteString("host_name", host);
            w.WriteString("ip_address", "");
            w.WriteString("mac_address", "");
            w.WriteString("fqdn", "");
            w.WriteString("comments", "Generat de LogAnalyzer DFIR din controlul stației; ID-urile de regulă SV nu sunt completate (nu au putut fi verificate pe sursă).");
            w.WriteString("role", "None");
            w.WriteBoolean("is_web_database", false);
            w.WriteString("technology_area", "Windows OS");
            w.WriteString("web_db_site", "");
            w.WriteString("web_db_instance", "");
            w.WriteNull("classification");
            w.WriteEndObject();
            w.WriteString("cklb_version", "1.0");
            w.WriteEndObject();
        }
        return new UTF8Encoding(false).GetString(ms.ToArray());
    }

    private static void WriteRule(Utf8JsonWriter w, Rule rule, ControlReport report, string stigUuid)
    {
        var check = report.Checks.FirstOrDefault(c => c.Id == rule.CheckId);
        var (status, how) = Decide(rule, check, report.Facts);
        string comments, details;
        if (check is null)
        {
            comments = $"LogAnalyzer {rule.CheckId}: verificarea nu a rulat";
            details = "";
        }
        else
        {
            comments = $"LogAnalyzer {rule.CheckId}: {ControlReportPdf.StatusText(check.Status)} — {how}";
            details = check.Detail + "\n" + string.Join("\n", check.Evidence);
            if (details.Length > DetailsCap) details = details[..(char.IsHighSurrogate(details[DetailsCap - 1]) ? DetailsCap - 1 : DetailsCap)];   // never split a surrogate pair (the JSON writer throws on one)
        }

        w.WriteStartObject();
        w.WriteString("group_id_src", rule.VId);
        w.WriteStartArray("group_tree");
        w.WriteStartObject();
        w.WriteString("id", rule.VId);
        w.WriteString("title", "");
        w.WriteString("description", "");
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteString("group_id", rule.VId);
        w.WriteString("severity", rule.Severity);
        w.WriteString("group_title", rule.Title);
        w.WriteString("rule_id_src", "");
        w.WriteString("rule_id", "");
        w.WriteString("rule_version", rule.Version);
        w.WriteString("rule_title", rule.Title);
        w.WriteString("fix_text", "");
        w.WriteString("weight", "10.0");
        w.WriteString("check_content", "");
        w.WriteStartObject("check_content_ref");
        w.WriteString("href", "");
        w.WriteString("name", "M");
        w.WriteEndObject();
        w.WriteString("classification", "Unclassified");
        w.WriteString("discussion", "");
        w.WriteString("false_positives", "");
        w.WriteString("false_negatives", "");
        w.WriteString("documentable", "false");
        w.WriteString("security_override_guidance", "");
        w.WriteString("potential_impacts", "");
        w.WriteString("third_party_tools", "");
        w.WriteString("ia_controls", "");
        w.WriteString("responsibility", "");
        w.WriteString("mitigations", "");
        w.WriteString("mitigation_control", "");
        w.WriteStartArray("legacy_ids"); w.WriteEndArray();
        w.WriteStartArray("ccis"); w.WriteEndArray();
        w.WriteString("reference_identifier", "");
        w.WriteString("uuid", Guid.NewGuid().ToString());
        w.WriteString("stig_uuid", stigUuid);
        w.WriteString("status", status);
        w.WriteStartObject("overrides"); w.WriteEndObject();
        w.WriteString("comments", comments);
        w.WriteString("finding_details", details);
        w.WriteString("srg_id", "");
        w.WriteEndObject();
    }

    private static (string Status, string How) Decide(Rule rule, ControlCheck? check, StationFacts facts)
    {
        if (check is null) return (NotReviewed, "verificarea nu a rulat");
        // the application's "nu se aplică" comes from the station posture (edition, level), not from the STIG's own NA conditions: it is never carried over as NA
        if (check.Status == ControlStatus.NuSeAplica) return (NotReviewed, "verificarea aplicației nu se aplică acestei posturi a stației (ediție, nivel); STIG are propriile condiții de „Not Applicable”, pe care le stabilește evaluatorul");
        switch (rule.Kind)
        {
            case Kind.Evidence:
                return (NotReviewed, "verificarea aplicației diferă de criteriul STIG sau acoperă mai multe reguli; dovada este atașată, iar starea regulii rămâne de stabilit de evaluator");
            case Kind.Whole:
            case Kind.WholeConformOnly:
                return check.Status switch
                {
                    ControlStatus.Conform => (NotAFinding, "regula are același criteriu ca verificarea aplicației; starea este preluată"),
                    ControlStatus.Neconform when rule.Kind == Kind.Whole => (Open, "regula are același criteriu ca verificarea aplicației; starea este preluată"),
                    ControlStatus.Neconform => (NotReviewed, "verificarea aplicației arată NECONFORM, dar nu poate exclude alt produs care îndeplinește regula (de exemplu un firewall sau antivirus terț); evaluatorul decide"),
                    _ => (NotReviewed, "verificarea aplicației nu a putut stabili starea (DE VERIFICAT sau NEDETERMINAT)"),
                };
            default:
                return Exact(rule.Version, facts);
        }
    }

    // ---- per-rule exact evaluation from the settings the check read (same keys and values as ControlEvaluator.StigChecks.cs) ----

    private static (string, string) Exact(string rule, StationFacts f)
    {
        string S(string k) => f.Settings.TryGetValue(k, out var v) ? v : "";
        static string Shown(string v) => v == Absent ? "nesetat" : v == "" ? "necitit" : v;
        (string, string) Unread(string name) => (NotReviewed, $"{name} nu a putut fi citită; starea regulii nu poate fi stabilită");
        (string, string) Odd(string name, string v) => (NotReviewed, $"{name} are o valoare neașteptată ({v}); starea regulii nu poate fi stabilită");
        (string, string) Res(string st, string name, string v, string why) => (st, $"regula evaluată separat din {name} = {Shown(v)}: {why}");
        // the STIG check text: "If the following registry value does not exist or is not configured as specified, this is a finding" (review 2026-11-09:
        // a Windows default never stands in for the value)
        static string AbsentIsFinding(string wanted) => $"STIG cere valoarea {wanted} setată explicit; o valoare lipsă sau alta este abatere, oricare ar fi implicitul Windows";

        switch (rule)
        {
            case "WN11-SO-000145":
            {
                var v = S("Stig.RestrictAnonymousSAM");
                return v == "" ? Unread("RestrictAnonymousSAM") : v == "1" ? Res(NotAFinding, "RestrictAnonymousSAM", v, "1 cerut") : v is "0" or Absent ? Res(Open, "RestrictAnonymousSAM", v, AbsentIsFinding("1")) : Odd("RestrictAnonymousSAM", v);
            }
            case "WN11-SO-000150":
            {
                var v = S("Stig.RestrictAnonymous");
                return v == "" ? Unread("RestrictAnonymous") : v == "1" ? Res(NotAFinding, "RestrictAnonymous", v, "1 cerut")
                    : v is "0" or Absent ? Res(Open, "RestrictAnonymous", v, "STIG cere 1 (implicitul Windows este 0)") : Odd("RestrictAnonymous", v);
            }
            case "WN11-SO-000160":
            {
                var v = S("Stig.EveryoneIncludesAnonymous");
                return v == "" ? Unread("EveryoneIncludesAnonymous") : v == "0" ? Res(NotAFinding, "EveryoneIncludesAnonymous", v, "0 cerut")
                    : v is "1" or Absent ? Res(Open, "EveryoneIncludesAnonymous", v, AbsentIsFinding("0")) : Odd("EveryoneIncludesAnonymous", v);
            }
            case "WN11-SO-000165":
            {
                var v = S("Stig.RestrictNullSessAccess");
                return v == "" ? Unread("RestrictNullSessAccess") : v == "1" ? Res(NotAFinding, "RestrictNullSessAccess", v, "1 cerut")
                    : v is "0" or Absent ? Res(Open, "RestrictNullSessAccess", v, AbsentIsFinding("1")) : Odd("RestrictNullSessAccess", v);
            }
            case "WN11-SO-000195":
            {
                var v = S("Stig.NoLMHash");
                return v == "" ? Unread("NoLMHash") : v == "1" ? Res(NotAFinding, "NoLMHash", v, "1 cerut")
                    : v is "0" or Absent ? Res(Open, "NoLMHash", v, AbsentIsFinding("1")) : Odd("NoLMHash", v);
            }
            case "WN11-SO-000205":
            {
                var v = S("Stig.LmCompatibilityLevel");
                return v == "" ? Unread("LmCompatibilityLevel") : v == "5" ? Res(NotAFinding, "LmCompatibilityLevel", v, "5 cerut")
                    : Res(Open, "LmCompatibilityLevel", v, "STIG cere 5 setat explicit");
            }
            case "WN11-UR-000015": return Right("SeTcbPrivilege", S("UserRights.SeTcbPrivilege"), adminsAllowed: false);
            case "WN11-UR-000045": return Right("SeCreateTokenPrivilege", S("UserRights.SeCreateTokenPrivilege"), adminsAllowed: false);
            case "WN11-UR-000065": return Right("SeDebugPrivilege", S("UserRights.SeDebugPrivilege"), adminsAllowed: true);
            case "WN11-CC-000315":
            {
                // the STIG rule is the HKLM policy value only (it must exist and be 0); HKCU is reported in the comment, it does not decide the rule
                var hm = S("Stig.AlwaysInstallElevatedHklm");
                var hc = S("Stig.AlwaysInstallElevatedHkcu");
                var hcNote = $" (HKCU, doar pentru utilizatorul care a pornit aplicația: {Shown(hc)}; nu face parte din regulă, vezi P43)";
                if (hm == "") return Unread("AlwaysInstallElevated (HKLM)");
                if (hm == "0") return (NotAFinding, "regula evaluată separat din AlwaysInstallElevated (HKLM) = 0" + hcNote);
                if (hm is "1" || hm == Absent) return (Open, $"regula evaluată separat din AlwaysInstallElevated (HKLM) = {Shown(hm)}: {AbsentIsFinding("0")}" + hcNote);
                return (NotReviewed, $"AlwaysInstallElevated (HKLM) are o valoare neașteptată ({hm}); starea regulii nu poate fi stabilită" + hcNote);
            }
            case "WN11-00-000145":
            {
                var v = S("Stig.DepSupportPolicy");
                return v == "" ? Unread("DEP (DataExecutionPrevention_SupportPolicy)") : v is "1" or "3" ? Res(NotAFinding, "DataExecutionPrevention_SupportPolicy", v, "cel puțin OptOut")
                    : v is "0" or "2" ? Res(Open, "DataExecutionPrevention_SupportPolicy", v, "STIG cere cel puțin OptOut") : Odd("DEP", v);
            }
            case "WN11-00-000150":
            {
                var v = S("Stig.DisableExceptionChainValidation");
                return v == "" ? Unread("DisableExceptionChainValidation") : v == "0" ? Res(NotAFinding, "DisableExceptionChainValidation", v, "0 cerut")
                    : v is "1" or Absent ? Res(Open, "DisableExceptionChainValidation", v, "STIG cere valoarea 0 setată explicit") : Odd("DisableExceptionChainValidation", v);
            }
            case "WN11-CC-000330": return WinRm("WinRM Client AllowBasic", S("Stig.WinRmClientAllowBasic"));
            case "WN11-CC-000345": return WinRm("WinRM Service AllowBasic", S("Stig.WinRmServiceAllowBasic"));
            case "WN11-CC-000335": return WinRm("WinRM Client AllowUnencryptedTraffic", S("Stig.WinRmClientAllowUnencrypted"));
            case "WN11-CC-000350": return WinRm("WinRM Service AllowUnencryptedTraffic", S("Stig.WinRmServiceAllowUnencrypted"));
            case "WN11-00-000100":
            {
                var key = S("Stig.W3svcKey");
                var inet = S("Stig.IisInetStpKey");
                if (key == "da" || (key == "nu" && inet == "da")) return (Open, $"regula evaluată separat: cheia W3SVC = {key}, cheia InetStp = {(inet == "" ? "necitită" : inet)}; IIS sau componente IIS sunt instalate");
                if (key == "" || inet == "") return Unread("Prezența IIS");
                if (key == "nu" && inet == "nu") return (NotAFinding, "regula evaluată separat: nici cheia W3SVC, nici cheia InetStp nu există");
                return (NotReviewed, $"prezența IIS are o valoare neașteptată (W3SVC = {key}, InetStp = {inet}); starea regulii nu poate fi stabilită");
            }
            default:
                return (NotReviewed, "regulă fără evaluare exactă");
        }

        (string, string) Right(string name, string v, bool adminsAllowed)
        {
            if (v == "") return Unread(name);
            var holders = v == UserRights.Nobody ? new List<string>() : v.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            var extra = adminsAllowed ? holders.Where(h => h != "S-1-5-32-544").ToList() : holders;
            var shown = v == UserRights.Nobody ? "(niciunul)" : string.Join("; ", holders);
            return extra.Count > 0
                ? (Open, $"regula evaluată separat din {name} = {shown}: " + (adminsAllowed ? "dreptul îl poate avea doar Administrators" : "nimeni nu trebuie să aibă dreptul"))
                : (NotAFinding, $"regula evaluată separat din {name} = {shown}");
        }

        (string, string) WinRm(string name, string v) =>
            v == "" ? Unread(name) : v == "0" ? Res(NotAFinding, name, v, "0 cerut")
            : v is "1" or Absent ? Res(Open, name, v, "STIG cere valoarea 0 setată explicit") : Odd(name, v);
    }
}
