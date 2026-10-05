using LogAnalyzer.Dfir.Graph;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.Dfir.Windows.Domain;

/// <summary>
/// Puts a directory inventory into the Evidence Graph: accounts PART_OF the AD domain, MEMBER_OF privileged groups (recursive
/// membership as LDAP returned it) and VIOLATES the domain checks that named them as NECONFORM. Every edge points at the
/// stored inventory (EvidenceId) and the object's distinguished name or the LDAP query (Locator). Computer accounts become
/// Host entities with the same id the case graph uses for a host name, so the two graphs join on it.
/// </summary>
public static class DomainGraph
{
    public static EvidenceGraph Build(DomainSnapshot d, IReadOnlyList<ControlCheck> checks, string inventoryEvidenceId, EvidenceGraph? into = null)
    {
        if (inventoryEvidenceId.Length == 0) throw new ArgumentException("Inventarul trebuie să fie o probă înregistrată (EvidenceId).");
        var g = into ?? new EvidenceGraph();
        var time = Timestamp.FromUtc(d.CollectedUtc.UtcDateTime, d.CollectedUtc.ToString("O"), "ora colectării LDAP");
        var domain = g.Add("AdDomain", d.DomainName);

        var bySam = new Dictionary<string, (Entity Entity, DirectoryAccount? Account)>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in d.Users.Concat(d.Computers))
        {
            var e = a.IsComputer ? g.Add("Host", a.SamAccountName.TrimEnd('$'), $"{a.SamAccountName.TrimEnd('$')} ({a.OperatingSystem})")
                                 : g.Add("Account", $@"{d.DomainName}\{a.SamAccountName}", a.DisplayName.Length > 0 ? $"{a.SamAccountName} — {a.DisplayName}" : a.SamAccountName);
            bySam[a.SamAccountName] = (e, a);
            g.LinkStored(e, RelationType.PartOf, domain, time, inventoryEvidenceId, "LDAP " + a.DistinguishedName,
                reason: a.IsComputer ? "cont de calculator în domeniu" : "cont de utilizator în domeniu");
        }

        foreach (var (group, members) in d.PrivilegedGroups)
        {
            var ge = g.Add("Group", $@"{d.DomainName}\{group}", group);
            g.LinkStored(ge, RelationType.PartOf, domain, time, inventoryEvidenceId, $"LDAP grup {group}", reason: "grup privilegiat al domeniului");
            foreach (var m in members)
            {
                // A member LDAP returned but the inventory does not hold (e.g. a nested group) is still drawn, as an account.
                var me = bySam.TryGetValue(m, out var x) ? x.Entity : g.Add("Account", $@"{d.DomainName}\{m}");
                g.LinkStored(me, RelationType.MemberOf, ge, time, inventoryEvidenceId, $"LDAP memberOf:1.2.840.113556.1.4.1941:={group}",
                    reason: "membru (recursiv) al grupului privilegiat");
            }
        }

        foreach (var c in checks.Where(c => c.Status == ControlStatus.Neconform && c.Subjects is { Count: > 0 }))
        {
            var control = g.Add("Control", c.Id, $"{c.Id}: {c.Title}");
            foreach (var s in c.Subjects!.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!bySam.TryGetValue(s, out var x)) continue;
                g.LinkStored(x.Entity, RelationType.Violates, control, time, inventoryEvidenceId, "LDAP " + x.Account!.DistinguishedName,
                    derivation: $"DomainEvaluator {c.Id}", reason: c.Detail);
            }
        }
        return g;
    }
}
