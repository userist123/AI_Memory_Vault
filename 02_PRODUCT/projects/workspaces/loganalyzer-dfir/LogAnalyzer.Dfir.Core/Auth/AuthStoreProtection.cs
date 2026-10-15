using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>Whether only Administrators / SYSTEM can change the auth store. <see cref="Problems"/> says, in Romanian, who else can.</summary>
public sealed record StoreAclResult(bool Protected, IReadOnlyList<string> Problems)
{
    public static readonly StoreAclResult Ok = new(true, []);
    public const string StateName = "magazia de conturi neprotejată";

    public string Message => Protected
        ? "Magazia de conturi este protejată: doar Administratorii și SYSTEM o pot modifica."
        : $"Atenție: {StateName}. Pe lângă Administratori și SYSTEM, și alte conturi pot modifica folderul de autentificare, deci pot adăuga un administrator " +
          "sau pot rescrie auditul. Autentificarea este refuzată; un administrator poate continua doar după o confirmare explicită, care se trece în audit. " +
          "Remediere (IT, o singură dată): comanda icacls din docs/dfir/AUTHENTICATION.md, secțiunea 2b. Detalii: " + string.Join("; ", Problems.Take(5));
}

/// <summary>
/// What the auth store needs from the operating system (security audit #4): a check of the folder's DACL / owner, and the HMAC key that seals the head of the
/// auth audit chain. <see cref="None"/> (tests, tools with an explicit folder) checks nothing and keys nothing, as before.
/// </summary>
public sealed class AuthStoreSecurity
{
    public required Func<string, StoreAclResult> CheckAcl { get; init; }
    /// <summary>Returns the chain key for the auth folder, creating it when absent (<c>created</c> = true); null when it cannot be read.</summary>
    public required Func<string, (byte[]? Key, bool Created)> ChainKey { get; init; }

    public static readonly AuthStoreSecurity None = new() { CheckAcl = _ => StoreAclResult.Ok, ChainKey = _ => (null, false) };

    /// <summary>Production: real DACL check and a DPAPI (LocalMachine) protected key in an Administrators/SYSTEM-only file.</summary>
    [SupportedOSPlatform("windows")]
    public static AuthStoreSecurity Windows() => new() { CheckAcl = AuthStoreProtection.CheckDirectory, ChainKey = AuthStoreProtection.LoadOrCreateMachineKey };

    /// <summary>Tests: a fixed ACL verdict and an in-memory key.</summary>
    public static AuthStoreSecurity ForTests(StoreAclResult acl, byte[]? key) => new() { CheckAcl = _ => acl, ChainKey = _ => (key, false) };
}

public static class AuthStoreProtection
{
    public const string KeyFileName = "audit_chain.key";

    private const string Administrators = "S-1-5-32-544", LocalSystem = "S-1-5-18", CreatorOwner = "S-1-3-0";
    private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    /// <summary>
    /// Rights that let a holder change, add, delete or re-permission content: WriteData 0x2, AppendData 0x4, WriteExtendedAttributes 0x10,
    /// DeleteSubdirectoriesAndFiles 0x40, WriteAttributes 0x100, Delete 0x10000, ChangePermissions 0x40000, TakeOwnership 0x80000, plus the
    /// generic GENERIC_WRITE / GENERIC_ALL bits that inherit-only ACEs carry.
    /// </summary>
    private const int WriteMask = 0x2 | 0x4 | 0x10 | 0x40 | 0x100 | 0x10000 | 0x40000 | 0x80000 | 0x40000000 | 0x10000000;

    public static bool IsTrusted(string sid) => sid == Administrators || sid == LocalSystem || sid == TrustedInstaller;

    /// <summary>Pure evaluation of one object's owner and allow-ACEs (sid, rights mask). CREATOR OWNER is a placeholder and is not counted.</summary>
    public static IReadOnlyList<string> Evaluate(string objectName, string ownerSid, IEnumerable<(string Sid, int Rights)> allowAces)
    {
        var problems = new List<string>();
        if (!IsTrusted(ownerSid)) problems.Add($"{objectName}: proprietar {Name(ownerSid)} (trebuie Administrators sau SYSTEM)");
        foreach (var (sid, rights) in allowAces)
        {
            if (sid == CreatorOwner || IsTrusted(sid) || (rights & WriteMask) == 0) continue;
            problems.Add($"{objectName}: {Name(sid)} poate scrie");
        }
        return problems.Distinct().ToList();
    }

    private static string Name(string sid)
    {
        if (!OperatingSystem.IsWindows()) return sid;
        try { return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException) { return sid; }
    }

    /// <summary>Checks the auth folder and everything in it. A missing folder is not yet a store and is reported as such.</summary>
    [SupportedOSPlatform("windows")]
    public static StoreAclResult CheckDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return new StoreAclResult(false, [$"folderul {dir} nu există încă"]);
        var problems = new List<string>();
        try
        {
            var root = new DirectoryInfo(dir);
            problems.AddRange(Check(root.Name, root.GetAccessControl()));
            foreach (var d in root.EnumerateDirectories("*", SearchOption.AllDirectories))
                problems.AddRange(Check(Path.GetRelativePath(dir, d.FullName), d.GetAccessControl()));
            foreach (var f in root.EnumerateFiles("*", SearchOption.AllDirectories))
                problems.AddRange(Check(Path.GetRelativePath(dir, f.FullName), f.GetAccessControl()));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PrivilegeNotHeldException or SystemException)
        {
            problems.Add("drepturile folderului nu pot fi citite: " + ex.Message);
        }
        return new StoreAclResult(problems.Count == 0, problems);
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> Check(string name, FileSystemSecurity sec)
    {
        var owner = sec.GetOwner(typeof(SecurityIdentifier))?.Value ?? "";
        var aces = sec.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .Select(r => (((SecurityIdentifier)r.IdentityReference).Value, (int)r.FileSystemRights));
        return Evaluate(name, owner, aces);
    }

    /// <summary>
    /// The HMAC key for the auth audit head: 32 random bytes, DPAPI LocalMachine protected, in a file whose DACL grants only Administrators and SYSTEM
    /// (so a standard user cannot read it even if the folder ACL is weak). Created on first use.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static (byte[]? Key, bool Created) LoadOrCreateMachineKey(string dir)
    {
        var path = Path.Combine(dir, KeyFileName);
        try
        {
            if (File.Exists(path))
                return (ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.LocalMachine), false);
            Directory.CreateDirectory(dir);
            var key = RandomNumberGenerator.GetBytes(32);
            var sec = new FileSecurity();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(Administrators), FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(LocalSystem), FileSystemRights.FullControl, AccessControlType.Allow));
            using (var fs = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadAttributes, FileShare.None, 4096, FileOptions.None, sec))
                fs.Write(ProtectedData.Protect(key, Entropy, DataProtectionScope.LocalMachine));
            return (key, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { return (null, false); }
    }

    private static readonly byte[] Entropy = "LogAnalyzer-auth-audit-head-v1"u8.ToArray();
}
