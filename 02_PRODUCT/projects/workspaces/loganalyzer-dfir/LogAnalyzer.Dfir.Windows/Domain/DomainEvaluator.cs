using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.Dfir.Windows.Domain;

/// <summary>
/// Domain hygiene checks in the spirit of PingCastle (implemented independently): weaknesses that let an attacker
/// take over accounts, and privileged access that must be justified. Pure function of a <see cref="DomainSnapshot"/>.
/// </summary>
public static class DomainEvaluator
{
    public static List<ControlCheck> Evaluate(DomainSnapshot d, int staleDays = 90)
    {
        var checks = new List<ControlCheck>();
        var now = d.CollectedUtc;
        var enabledUsers = d.Users.Where(u => u.Enabled).ToList();
        var privileged = d.PrivilegedGroups.SelectMany(g => g.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Name(DirectoryAccount a) => $"{a.SamAccountName}{(privileged.Contains(a.SamAccountName) ? " (PRIVILEGIAT)" : "")}";
        void Add(string id, string title, ControlStatus s, string detail, IEnumerable<string>? ev = null, string rec = "", IEnumerable<DirectoryAccount?>? subjects = null) =>
            checks.Add(new ControlCheck(id, "Domeniu", title, s, detail, (ev ?? []).Take(300).ToList(), rec,
                subjects?.OfType<DirectoryAccount>().Select(a => a.SamAccountName).ToList()));

        var kerberoast = enabledUsers.Where(u => u.Spns.Count > 0 && !u.SamAccountName.Equals("krbtgt", StringComparison.OrdinalIgnoreCase)).ToList();
        Add("DM01", "Conturi de utilizator cu SPN (vulnerabile la Kerberoasting)",
            kerberoast.Count == 0 ? ControlStatus.Conform : kerberoast.Any(k => privileged.Contains(k.SamAccountName)) ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            kerberoast.Count == 0 ? "Niciun cont de utilizator activ cu SPN." : $"{kerberoast.Count} conturi: oricine din domeniu poate cere un tichet și poate încerca parola offline.",
            kerberoast.Select(k => $"{Name(k)}: {string.Join(", ", k.Spns.Take(3))}; parola schimbată {k.PasswordLastSetUtc:yyyy-MM-dd}"),
            "Parole de cel puțin 25 de caractere sau gMSA pentru conturile de serviciu; fără SPN pe conturi privilegiate.", kerberoast);

        var asrep = enabledUsers.Where(u => u.Flags.HasFlag(Uac.DontRequirePreauth)).ToList();
        Add("DM02", "Conturi fără pre-autentificare Kerberos (AS-REP roasting)", asrep.Count == 0 ? ControlStatus.Conform : ControlStatus.Neconform,
            asrep.Count == 0 ? "Niciun cont." : $"{asrep.Count} conturi permit obținerea unui hash fără nicio autentificare.", asrep.Select(Name),
            "Eliminați „Do not require Kerberos preauthentication”.", asrep);

        var noPwd = enabledUsers.Where(u => u.Flags.HasFlag(Uac.PasswordNotRequired)).ToList();
        Add("DM03", "Conturi cu PASSWD_NOTREQD", noPwd.Count == 0 ? ControlStatus.Conform : ControlStatus.Neconform,
            noPwd.Count == 0 ? "Niciun cont." : $"{noPwd.Count} conturi pot avea parolă goală.", noPwd.Select(Name), subjects: noPwd);

        var reversible = enabledUsers.Where(u => u.Flags.HasFlag(Uac.ReversibleEncryption)).ToList();
        Add("DM04", "Parole stocate cu criptare reversibilă", reversible.Count == 0 ? ControlStatus.Conform : ControlStatus.Neconform,
            reversible.Count == 0 ? "Niciun cont." : $"{reversible.Count} conturi.", reversible.Select(Name), subjects: reversible);

        var des = enabledUsers.Where(u => u.Flags.HasFlag(Uac.UseDesKeyOnly)).ToList();
        if (des.Count > 0) Add("DM05", "Conturi limitate la DES", ControlStatus.Neconform, $"{des.Count} conturi.", des.Select(Name), subjects: des);

        foreach (var (group, members) in d.PrivilegedGroups.Where(g => g.Value.Count > 0))
        {
            var accounts = members.Select(m => d.Users.Concat(d.Computers).FirstOrDefault(u => u.SamAccountName.Equals(m, StringComparison.OrdinalIgnoreCase))).ToList();
            var bad = accounts.Where(a => a is not null && (!a.Enabled || a.LastLogonUtc is { } l && (now - l).TotalDays > staleDays ||
                                                            a.Flags.HasFlag(Uac.DontExpirePassword))).ToList();
            Add($"DM1{Array.FindIndex(DirectoryCollector.PrivilegedGroupIds, x => x.Name == group)}", $"Membri {group}",
                bad.Count > 0 ? ControlStatus.Neconform : ControlStatus.DeVerificat,
                $"{members.Count} membri (recursiv). " + (bad.Count > 0 ? $"{bad.Count} sunt dezactivați, nefolosiți de peste {staleDays} zile sau au parolă care nu expiră." : "Comparați cu lista aprobată."),
                accounts.Select((a, i) => a is null ? members[i] : $"{a.SamAccountName}: {(a.Enabled ? "activ" : "DEZACTIVAT")}, ultima autentificare {a.LastLogonUtc:yyyy-MM-dd}, parolă {a.PasswordLastSetUtc:yyyy-MM-dd}{(a.Flags.HasFlag(Uac.DontExpirePassword) ? ", NU EXPIRĂ" : "")}"),
                "Minimum de membri; conturi separate pentru administrare; parole care expiră.", bad);
        }

        var stale = enabledUsers.Where(u => u.LastLogonUtc is null ? u.CreatedUtc is { } c && (now - c).TotalDays > staleDays : (now - u.LastLogonUtc.Value).TotalDays > staleDays).ToList();
        Add("DM20", $"Conturi active nefolosite de peste {staleDays} zile", stale.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            $"{stale.Count} conturi active fără autentificare recentă (lastLogonTimestamp are o întârziere de replicare de până la 14 zile).",
            stale.OrderBy(u => u.LastLogonUtc).Select(u => $"{Name(u)}: ultima autentificare {u.LastLogonUtc?.ToString("yyyy-MM-dd") ?? "niciodată"}"),
            "Dezactivați conturile neutilizate.", stale);

        var krbtgt = d.Users.FirstOrDefault(u => u.SamAccountName.Equals("krbtgt", StringComparison.OrdinalIgnoreCase));
        if (krbtgt?.PasswordLastSetUtc is { } kp)
            Add("DM21", "Vechimea parolei krbtgt", (now - kp).TotalDays <= 180 ? ControlStatus.Conform : ControlStatus.Neconform,
                $"Schimbată ultima dată pe {kp:yyyy-MM-dd} ({(now - kp).TotalDays:0} zile). Un tichet „golden” rămâne valid până la schimbare.",
                rec: "Schimbați parola krbtgt de două ori, la interval de replicare, cel puțin o dată la 180 de zile.", subjects: [krbtgt]);

        var unconstrained = d.Users.Concat(d.Computers).Where(a => a.Enabled && a.Flags.HasFlag(Uac.TrustedForDelegation) && !a.IsDomainController).ToList();
        Add("DM22", "Delegare Kerberos neconstrânsă (în afara controlerelor de domeniu)", unconstrained.Count == 0 ? ControlStatus.Conform : ControlStatus.Neconform,
            unconstrained.Count == 0 ? "Niciun cont." : $"{unconstrained.Count} conturi: compromiterea lor expune tichetele oricui se conectează la ele.",
            unconstrained.Select(a => a.SamAccountName), "Folosiți delegare constrânsă sau bazată pe resurse.", unconstrained);

        var orphanAdminCount = enabledUsers.Where(u => u.AdminCount == 1 && !privileged.Contains(u.SamAccountName)).ToList();
        if (orphanAdminCount.Count > 0)
            Add("DM23", "Foste conturi privilegiate (adminCount=1, dar nu mai sunt în grupuri privilegiate)", ControlStatus.DeVerificat,
                $"{orphanAdminCount.Count} conturi păstrează permisiunile protejate (AdminSDHolder).", orphanAdminCount.Select(u => u.SamAccountName), subjects: orphanAdminCount);

        var staleComputers = d.Computers.Where(c => c.Enabled && !c.IsDomainController && c.LastLogonUtc is { } l && (now - l).TotalDays > staleDays).ToList();
        Add("DM24", $"Calculatoare active care nu s-au mai conectat de {staleDays} zile", staleComputers.Count == 0 ? ControlStatus.Conform : ControlStatus.DeVerificat,
            $"{staleComputers.Count} calculatoare.", staleComputers.Select(c => $"{c.SamAccountName} ({c.OperatingSystem}) ultima {c.LastLogonUtc:yyyy-MM-dd}"), subjects: staleComputers);

        var oldOs = d.Computers.Where(c => c.Enabled && (c.OperatingSystem.Contains("2008") || c.OperatingSystem.Contains("2003") || c.OperatingSystem.Contains("Windows 7") ||
                                                          c.OperatingSystem.Contains("Windows XP") || c.OperatingSystem.Contains("2012"))).ToList();
        if (oldOs.Count > 0)
            Add("DM25", "Sisteme de operare fără suport de securitate", ControlStatus.Neconform, $"{oldOs.Count} calculatoare.", oldOs.Select(c => $"{c.SamAccountName}: {c.OperatingSystem}"), subjects: oldOs);

        if (d.Policy is { } p)
        {
            Add("DM30", "Politica de parole a domeniului", p.MinPasswordLength >= 12 ? ControlStatus.Conform : p.MinPasswordLength >= 8 ? ControlStatus.DeVerificat : ControlStatus.Neconform,
                $"Lungime minimă {p.MinPasswordLength}; istoric {p.PasswordHistory}; expirare {(p.MaxPasswordAge is { } a ? a.TotalDays.ToString("0") + " zile" : "niciodată")}.");
            Add("DM31", "Blocarea conturilor în domeniu", p.LockoutThreshold is > 0 and <= 10 ? ControlStatus.Conform : ControlStatus.Neconform,
                p.LockoutThreshold == 0 ? "Conturile nu se blochează niciodată." : $"Blocare după {p.LockoutThreshold} încercări.");
        }
        return checks;
    }
}
