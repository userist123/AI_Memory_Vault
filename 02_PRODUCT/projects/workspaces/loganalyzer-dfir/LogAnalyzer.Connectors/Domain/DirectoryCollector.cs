using System.DirectoryServices;
using System.Security.Principal;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Domain;

/// <summary>
/// Read-only LDAP inventory of the domain the station belongs to (current Windows credentials). Network access:
/// gated by <see cref="NetworkPolicy"/>, so it never runs in AirGapped mode.
/// </summary>
public static class DirectoryCollector
{
    private static readonly string[] AccountAttributes =
    [
        "sAMAccountName", "displayName", "distinguishedName", "objectSid", "userAccountControl", "lastLogonTimestamp", "pwdLastSet",
        "whenCreated", "adminCount", "servicePrincipalName", "memberOf", "mail", "description", "operatingSystem",
    ];

    public static readonly (string Name, string SidSuffixOrSid)[] PrivilegedGroupIds = PrivilegedGroupCatalog.Ids;

    public static bool IsDomainJoined(out string domain)
    {
        domain = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName;
        return !string.IsNullOrEmpty(domain);
    }

    public static DomainSnapshot Collect(string? server = null, CancellationToken ct = default)
    {
        NetworkPolicy.EnsureAllowed("Interogare Active Directory (LDAP)");
        if (!IsDomainJoined(out var domainName) && server is null)
            throw new InvalidOperationException("Stația nu face parte dintr-un domeniu Active Directory. Indicați un controler de domeniu.");

        using var rootDse = new DirectoryEntry($"LDAP://{(server is null ? "" : server + "/")}RootDSE");
        var baseDn = rootDse.Properties["defaultNamingContext"].Value as string ?? throw new InvalidOperationException("RootDSE fără defaultNamingContext.");
        var dnsHost = rootDse.Properties["dnsHostName"].Value as string ?? server ?? "";
        using var root = new DirectoryEntry($"LDAP://{(server is null ? "" : server + "/")}{baseDn}");
        var domainSid = root.Properties["objectSid"].Value is byte[] ds ? new SecurityIdentifier(ds, 0).Value : "";

        var snap = new DomainSnapshot { DomainName = domainName.Length > 0 ? domainName : baseDn, DomainSid = domainSid, Server = dnsHost };
        snap.Policy = new DomainPolicy(
            Int(root, "minPwdLength"), Int(root, "lockoutThreshold"),
            root.Properties["maxPwdAge"].Value is { } mpa ? Interval(mpa) : null, Int(root, "pwdHistoryLength"));

        foreach (var a in Search(root, "(&(objectCategory=person)(objectClass=user))", ct)) snap.Users.Add(a);
        ct.ThrowIfCancellationRequested();
        foreach (var a in Search(root, "(objectCategory=computer)", ct)) snap.Computers.Add(a);

        foreach (var (name, id) in PrivilegedGroupIds)
        {
            var sid = id.StartsWith("S-1-", StringComparison.Ordinal) ? id : domainSid + id;
            var groupDn = FindDnBySid(root, sid);
            if (groupDn is null) continue;
            var members = Search(root, $"(memberOf:1.2.840.113556.1.4.1941:={Escape(groupDn)})", ct).Select(m => m.SamAccountName).ToList();
            snap.PrivilegedGroups[name] = members;
        }
        return snap;
    }

    private static IEnumerable<DirectoryAccount> Search(DirectoryEntry root, string filter, CancellationToken ct)
    {
        using var s = new DirectorySearcher(root, filter, AccountAttributes, SearchScope.Subtree) { PageSize = 1000 };
        using var results = s.FindAll();
        foreach (SearchResult r in results)
        {
            ct.ThrowIfCancellationRequested();
            var p = r.Properties;
            string S(string k) => p[k].Count > 0 ? p[k][0]?.ToString() ?? "" : "";
            DateTimeOffset? FT(string k) => p[k].Count > 0 && p[k][0] is long ft && ft > 0 && ft != long.MaxValue ? DateTimeOffset.FromFileTime(ft).ToUniversalTime() : null;
            yield return new DirectoryAccount(
                S("sAMAccountName"), S("displayName"), S("distinguishedName"),
                p["objectSid"].Count > 0 && p["objectSid"][0] is byte[] sid ? new SecurityIdentifier(sid, 0).Value : "",
                (Uac)(p["userAccountControl"].Count > 0 ? Convert.ToInt32(p["userAccountControl"][0]) : 0),
                FT("lastLogonTimestamp"), FT("pwdLastSet"),
                p["whenCreated"].Count > 0 && p["whenCreated"][0] is DateTime wc ? new DateTimeOffset(DateTime.SpecifyKind(wc, DateTimeKind.Utc)) : null,
                p["adminCount"].Count > 0 ? Convert.ToInt32(p["adminCount"][0]) : 0,
                p["servicePrincipalName"].Cast<object>().Select(o => o.ToString()!).ToList(),
                p["memberOf"].Cast<object>().Select(o => o.ToString()!).ToList(),
                S("mail"), S("description"), S("operatingSystem").Length > 0 || filter.Contains("computer"), S("operatingSystem"));
        }
    }

    private static string? FindDnBySid(DirectoryEntry root, string sid)
    {
        using var s = new DirectorySearcher(root, $"(objectSid={sid})", ["distinguishedName"]);
        return s.FindOne()?.Properties["distinguishedName"][0]?.ToString();
    }

    private static int Int(DirectoryEntry e, string attr) => e.Properties[attr].Value is { } v ? Convert.ToInt32(v) : 0;

    /// <summary>Large-integer interval (negative 100-ns ticks) from ADSI.</summary>
    private static TimeSpan? Interval(object v)
    {
        try
        {
            dynamic li = v;
            long ticks = ((long)(int)li.HighPart << 32) | (uint)(int)li.LowPart;
            return ticks == long.MinValue || ticks == 0 ? null : TimeSpan.FromTicks(-ticks);
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return null; }
    }

    /// <summary>RFC 4515 escaping for values inserted in LDAP filters.</summary>
    public static string Escape(string value)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in value)
            sb.Append(c switch { '\\' => @"\5c", '*' => @"\2a", '(' => @"\28", ')' => @"\29", '\0' => @"\00", _ => c.ToString() });
        return sb.ToString();
    }
}
