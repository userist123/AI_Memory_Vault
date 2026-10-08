using System.Security.Principal;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Windows.Domain;

[Flags]
public enum Uac
{
    AccountDisabled = 0x2, PasswordNotRequired = 0x20, ReversibleEncryption = 0x80, NormalAccount = 0x200,
    WorkstationTrust = 0x1000, ServerTrust = 0x2000, DontExpirePassword = 0x10000, SmartcardRequired = 0x40000,
    TrustedForDelegation = 0x80000, NotDelegated = 0x100000, UseDesKeyOnly = 0x200000, DontRequirePreauth = 0x400000,
    TrustedToAuthForDelegation = 0x1000000,
}

public sealed record DirectoryAccount(
    string SamAccountName, string DisplayName, string DistinguishedName, string Sid, Uac Flags,
    DateTimeOffset? LastLogonUtc, DateTimeOffset? PasswordLastSetUtc, DateTimeOffset? CreatedUtc,
    int AdminCount, IReadOnlyList<string> Spns, IReadOnlyList<string> MemberOf, string Mail, string Description, bool IsComputer, string OperatingSystem)
{
    public bool Enabled => !Flags.HasFlag(Uac.AccountDisabled);
    public bool IsDomainController => Flags.HasFlag(Uac.ServerTrust);
}

public sealed record DomainPolicy(int MinPasswordLength, int LockoutThreshold, TimeSpan? MaxPasswordAge, int PasswordHistory);

public sealed class DomainSnapshot
{
    public string DomainName { get; init; } = "";
    public string DomainSid { get; init; } = "";
    public string Server { get; init; } = "";
    public DateTimeOffset CollectedUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<DirectoryAccount> Users { get; init; } = [];
    public List<DirectoryAccount> Computers { get; init; } = [];
    /// <summary>Privileged group name → recursive members (sAMAccountName).</summary>
    public Dictionary<string, List<string>> PrivilegedGroups { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public DomainPolicy? Policy { get; set; }
    public List<EvidenceGap> Gaps { get; init; } = [];
}

public static class PrivilegedGroupCatalog
{
    /// <summary>Well-known privileged groups: domain-relative RIDs and builtin SIDs.</summary>
    public static readonly (string Name, string SidSuffixOrSid)[] Ids =
    [
        ("Domain Admins", "-512"), ("Enterprise Admins", "-519"), ("Schema Admins", "-518"), ("Group Policy Creator Owners", "-520"),
        ("Administrators", "S-1-5-32-544"), ("Account Operators", "S-1-5-32-548"), ("Server Operators", "S-1-5-32-549"),
        ("Print Operators", "S-1-5-32-550"), ("Backup Operators", "S-1-5-32-551"),
    ];
}
