using System.Globalization;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>One local account as the snapshot stores it (one JSON object, one line of the file; the parser quotes that line).</summary>
public sealed record LocalAccountEntry(string Name, string Sid, int? Rid, bool Enabled, DateTimeOffset? LastLogonUtc, string Description);

/// <summary>One member of the local Administrators group (well-known SID S-1-5-32-544): name, domain part, SID, kind (user / group / system) and scope (local / domain / builtin).</summary>
public sealed record AdminMemberEntry(string Name, string Domain, string Sid, string Kind, string Scope);

/// <summary>What the live read returns. A part that could not be read is null with its reason in the matching error, so "no members" is never confused with "not read".</summary>
public sealed record LocalSecuritySnapshot(string Machine, bool? PartOfDomain, string InteractiveUser,
                                            IReadOnlyList<LocalAccountEntry>? Accounts, string AccountsError,
                                            IReadOnlyList<AdminMemberEntry>? Administrators, string AdministratorsError);

/// <summary>
/// Planner #12 B3: the audit policy and the local accounts are SAVED as hashed evidence files of the case, so the owner can verify them on paper:
/// <c>auditpol /get /category:* /r</c> (CSV, type auditpol_csv; needs elevation) and a JSON snapshot of the local accounts (name, SID, RID, enabled, last logon) and of the
/// members of the local Administrators group found by its well-known SID, so the localized group name does not matter (type local_accounts_json; WMI, the same
/// source as the station control, no PowerShell process). A part that fails is an error of the outcome (a gap), never a silent omission.
/// Profiles: Standard and FullForensic. The output of auditpol is in the OEM code page of the station; it is stored as UTF-8.
/// </summary>
public sealed class LocalSecurityStateCollector : ICollector
{
    public const string AuditSourceType = "auditpol_csv", AccountsSourceType = "local_accounts_json";
    public const string AdministratorsSid = "S-1-5-32-544";

    private readonly ToolRunFunc _run;
    private readonly Func<LocalSecuritySnapshot> _read;

    public LocalSecurityStateCollector() : this(null, null) { }
    public LocalSecurityStateCollector(ToolRunFunc? run, Func<LocalSecuritySnapshot>? read)
    {
        _run = run ?? ((exe, args, logDir, label, timeout, ct) => ToolRunner.Run(exe, args, logDir, label, timeout, ct, OemEncoding()));
        _read = read ?? ReadLive;
    }

    public string Name => "LocalSecurityStateCollector";
    public string Version => "1.0";
    public bool RequiresElevation => true;
    public CollectionProfile Profiles => CollectionProfile.StandardAndUp;

    private static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public CollectorOutcome Collect(CollectorContext ctx, CancellationToken ct)
    {
        var o = new CollectorOutcome { Source = "live system", Tool = "auditpol.exe + WMI" };
        int stored = 0;
        var dir = ctx.Case.RawDir("LocalSecurityState");
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        // 1. audit policy
        if (!ctx.IsElevated)
            o.Errors.Add("Politica de audit (auditpol): nu a fost salvată, necesită drepturi de administrator");
        else
        {
            var logDir = Path.Combine(ctx.Case.Root, "Logs", "tools");
            try
            {
                var run = _run(ToolRunner.System32("auditpol.exe"), ["/get", "/category:*", "/r"], logDir, "auditpol_get", TimeSpan.FromSeconds(60), ct);
                o.Commands.Add(run.CommandLine);
                o.ExitCode = run.ExitCode;
                string text = run.ExitCode == 0 && File.Exists(run.StdoutPath) ? File.ReadAllText(run.StdoutPath, new UTF8Encoding(false)) : "";
                if (run.ExitCode != 0)
                    o.Errors.Add($"auditpol: exit {run.ExitCode}{(run.TimedOut ? " (timp depășit)" : "")}; politica de audit lipsește din analiză (CFG-AUDIT-* nu pot fi evaluate)");
                else if (!Regex.IsMatch(text, @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}"))
                    o.Errors.Add("auditpol: ieșirea nu conține nicio subcategorie (cu GUID); politica de audit lipsește din analiză");
                else
                {
                    var path = Path.Combine(dir, $"auditpol_{stamp}.csv");
                    File.WriteAllText(path, text, new UTF8Encoding(false));
                    o.Evidence.Add(ctx.Case.RegisterStored(path, "auditpol /get /category:* /r", "auditpol", AuditSourceType, TemporalType.CurrentSnapshot, Name, Version));
                    stored++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                o.Errors.Add($"auditpol: nu a putut fi pornit sau citit ({ex.Message}); politica de audit lipsește din analiză");
            }
        }

        // 2. local accounts and local administrators
        try
        {
            var snap = _read();
            if (snap.Accounts is null) o.Errors.Add($"Conturi locale: {NotEmpty(snap.AccountsError)}");
            if (snap.Administrators is null) o.Errors.Add($"Membrii grupului Administrators (SID {AdministratorsSid}): {NotEmpty(snap.AdministratorsError)}");
            if (snap.Accounts is not null || snap.Administrators is not null)
            {
                var path = Path.Combine(dir, $"local_accounts_{stamp}.json");
                File.WriteAllText(path, ToJson(snap, DateTimeOffset.UtcNow), new UTF8Encoding(false));
                o.Evidence.Add(ctx.Case.RegisterStored(path, "WMI Win32_UserAccount / Win32_GroupUser", "Conturi locale", AccountsSourceType, TemporalType.CurrentSnapshot, Name, Version));
                stored++;
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Security.SecurityException or IOException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            o.Errors.Add($"Conturi locale și administratori: {ex.Message}; CFG-GUEST-ENABLED și CFG-EXTRA-ADMINS nu pot fi evaluate");
        }

        o.Status = stored == 0 ? EvidenceStatus.Failed : o.Errors.Count > 0 ? EvidenceStatus.Partial : EvidenceStatus.Success;
        return o;
    }

    private static string NotEmpty(string s) => s.Length > 0 ? s : "citirea a eșuat fără un motiv raportat";

    /// <summary>
    /// The file: one object per line inside the arrays, so the line the parser quotes (the object's text) is found as it is in the stored file. UTF-8 without BOM, accents kept.
    /// </summary>
    public static string ToJson(LocalSecuritySnapshot s, DateTimeOffset collectedUtc)
    {
        string J(object o) => JsonSerializer.Serialize(o, Compact);
        var sb = new StringBuilder("{\n");
        sb.Append("\"schema\":\"la.local_accounts/1\",\n");
        sb.Append("\"collectedUtc\":").Append(J(collectedUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))).Append(",\n");
        sb.Append("\"machine\":").Append(J(s.Machine)).Append(",\n");
        sb.Append("\"partOfDomain\":").Append(s.PartOfDomain is { } d ? (d ? "true" : "false") : "null").Append(",\n");
        sb.Append("\"interactiveUser\":").Append(J(s.InteractiveUser)).Append(",\n");
        sb.Append("\"accountsError\":").Append(J(s.AccountsError)).Append(",\n");
        sb.Append("\"administratorsError\":").Append(J(s.AdministratorsError)).Append(",\n");
        WriteArray(sb, "accounts", s.Accounts?.Select(a => J(new
        {
            name = a.Name, sid = a.Sid, rid = a.Rid, enabled = a.Enabled,
            lastLogonUtc = a.LastLogonUtc?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), description = a.Description,
        })), true);
        WriteArray(sb, "administrators", s.Administrators?.Select(m => J(new { name = m.Name, domain = m.Domain, sid = m.Sid, kind = m.Kind, scope = m.Scope })), false);
        return sb.Append("}\n").ToString();
    }

    private static void WriteArray(StringBuilder sb, string name, IEnumerable<string>? lines, bool comma)
    {
        sb.Append('"').Append(name).Append("\":");
        if (lines is null) sb.Append("null");
        else sb.Append("[\n").Append(string.Join(",\n", lines)).Append("\n]");
        sb.Append(comma ? ",\n" : "\n");
    }

    // ---- the live read ------------------------------------------------------------------------------------------------

    private static Encoding OemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.InstalledUICulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return new UTF8Encoding(false); }
    }

    private static readonly Regex PathParts = new(@"(?:^|:)(?<cls>Win32_\w+)\.Domain=""(?<d>(?:[^""\\]|\\.)*)"",Name=""(?<n>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private static string Unescape(string s) => Regex.Replace(s, @"\\(.)", "$1");

    /// <summary>The live read: accounts from the same WMI query as the station control; the Administrators members from Win32_GroupUser of the group found by SID.</summary>
    public static LocalSecuritySnapshot ReadLive()
    {
        string machine = Environment.MachineName;
        bool? domain = null; string interactive = "";
        try
        {
            using var cs = new ManagementObjectSearcher("SELECT PartOfDomain, UserName FROM Win32_ComputerSystem");
            foreach (ManagementObject mo in cs.Get()) { domain = mo["PartOfDomain"] as bool?; interactive = mo["UserName"] as string ?? ""; }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        if (interactive.Length == 0) interactive = $@"{Environment.UserDomainName}\{Environment.UserName}";

        List<LocalAccountEntry>? accounts = null; string accountsError = "";
        try
        {
            accounts = StationFactCollector.ReadAccounts().Select(a => new LocalAccountEntry(a.Name, a.Sid, RidOf(a.Sid), !a.Disabled, a.LastLogonUtc, a.Description)).ToList();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException) { accountsError = ex.Message; }

        List<AdminMemberEntry>? admins = null; string adminsError = "";
        try { admins = ReadAdministrators(machine); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException) { adminsError = ex.Message; }
        return new LocalSecuritySnapshot(machine, domain, interactive, accounts, accountsError, admins, adminsError);
    }

    public static int? RidOf(string sid) => sid.LastIndexOf('-') is > 0 and var i && int.TryParse(sid[(i + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var rid) ? rid : null;

    private static List<AdminMemberEntry> ReadAdministrators(string machine)
    {
        var list = new List<AdminMemberEntry>();
        using var g = new ManagementObjectSearcher($"SELECT Name, Domain FROM Win32_Group WHERE LocalAccount = TRUE AND SID = '{AdministratorsSid}'");
        bool found = false;
        foreach (ManagementObject grp in g.Get())
        {
            found = true;
            using var m = new ManagementObjectSearcher(StationFactCollector.GroupUserQuery(grp["Domain"]?.ToString() ?? "", grp["Name"]?.ToString() ?? ""));
            foreach (ManagementObject gu in m.Get())
            {
                var match = PathParts.Match(gu["PartComponent"] as string ?? "");
                if (!match.Success) continue;
                string dom = Unescape(match.Groups["d"].Value), name = Unescape(match.Groups["n"].Value), cls = match.Groups["cls"].Value;
                string kind = cls.Equals("Win32_UserAccount", StringComparison.OrdinalIgnoreCase) ? "user" : cls.Equals("Win32_Group", StringComparison.OrdinalIgnoreCase) ? "group" : "system";
                string scope = dom.Equals(machine, StringComparison.OrdinalIgnoreCase) ? "local"
                             : dom.Equals("NT AUTHORITY", StringComparison.OrdinalIgnoreCase) || dom.Equals("BUILTIN", StringComparison.OrdinalIgnoreCase) || dom.StartsWith("NT SERVICE", StringComparison.OrdinalIgnoreCase) ? "builtin" : "domain";
                list.Add(new AdminMemberEntry(name, dom, SidOf(dom, name), kind, scope));
            }
        }
        if (!found) throw new InvalidOperationException($"grupul cu SID-ul {AdministratorsSid} nu a fost găsit prin WMI");
        return list;
    }

    private static string SidOf(string domain, string name)
    {
        try { return ((SecurityIdentifier)new NTAccount(domain, name).Translate(typeof(SecurityIdentifier))).Value; }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException) { return ""; }   // an unresolvable domain account: the SID stays empty and the finding says so
    }
}
