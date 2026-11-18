using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Case;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>
/// Anti-rollback witness of one sealed log, kept in a file OUTSIDE the log's own folder (production: %ProgramFiles%\LogAnalyzer\witness), so a copy of an older
/// auth folder put back over the current one is caught: the witness stays at the newer head (planner #8 item 2, 2026-11-18).
/// The registry would survive a folder restore better, but the Standalone (isolated) edition must not write the registry (Edition guard
/// StandaloneEditionBuildTests: no RegistryKey::SetValue / CreateSubKey); that variant is an owner decision. Whoever also restores this folder, or (as an
/// administrator) deletes the witness, is not caught by it (the paper anchor code and the CD-R copy remain the reference).
/// The folder is Administrators/SYSTEM only (review 2026-11-18: ProgramData lets standard users create files and folders, so a user could pre-create the folder
/// or a ".tmp" and own the witness). A folder found without that protection is moved aside as "untrusted" (kept, not deleted) and recreated protected.
/// File name = first 32 hex chars of SHA-256 of the lower-cased full log path; content = compact JSON {seq,hash,mac}. An unreadable file reads as an invalid witness.
/// </summary>
public sealed class FileHeadWitness : IHeadWitness
{
    private readonly string _dir;
    private readonly string _path;
    private readonly Func<string, bool>? _isProtected;
    private readonly Action<string>? _protect;

    /// <param name="isProtected">Whether a folder admits only Administrators/SYSTEM (null = no ACL handling: tests on a temp folder).</param>
    /// <param name="protect">Applies the Administrators/SYSTEM-only DACL and owner to a folder.</param>
    public FileHeadWitness(string witnessDir, string logPath, Func<string, bool>? isProtected = null, Action<string>? protect = null)
    {
        _dir = witnessDir;
        var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(logPath).ToLowerInvariant())))[..32];
        _path = Path.Combine(witnessDir, name + ".witness");
        _isProtected = isProtected;
        _protect = protect;
    }

    /// <summary>
    /// %ProgramFiles%\LogAnalyzer\witness: outside the auth folder, and in a tree where standard users cannot create anything (review round 2, 2026-11-18: in
    /// ProgramData a standard user could pre-create the folder, hold a file in it and make every check fail -> card sign-in refused). The app runs elevated
    /// (requireAdministrator), so it can create it.
    /// </summary>
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LogAnalyzer", "witness");

    [SupportedOSPlatform("windows")]
    public static FileHeadWitness ForLog(string logPath) =>
        new(DefaultDir, logPath, d => AuthStoreProtection.CheckDirectory(d).Protected && OnlyAdminsCanRead(d), ApplyAdminOnlyAcl);

    public string FilePath => _path;

    /// <summary>The untrusted folder moved aside by the last check, if any (for the audit / the operator).</summary>
    public string? MovedAside { get; private set; }

    public (long Seq, string Hash, string Mac)? Read()
    {
        EnsureProtectedFolder(create: false);
        string text;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                text = sr.ReadToEnd();
                break;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
            catch (IOException e) when ((e.HResult & 0xFFFF) is 32 or 33 && attempt < 200) { Thread.Sleep(Math.Min(5 + attempt, 50)); }
            // anything else (access denied, a holder that never lets go) propagates: the caller reports an unreadable witness, never "absent"
        }
        try
        {
            var o = JsonNode.Parse(text) as JsonObject;
            if (o?["seq"] is { } s && o["hash"] is { } h && o["mac"] is { } m) return (s.GetValue<long>(), h.GetValue<string>(), m.GetValue<string>());
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
        return (-1, "", "");   // present but unreadable: reported as an invalid witness (the MAC cannot match)
    }

    /// <summary>Durable replace: temp file flushed to disk, then moved over the witness (a crash leaves the old or the new witness, never half of one).</summary>
    public void Write(long seq, string hash, string mac)
    {
        EnsureProtectedFolder(create: true);
        var bytes = Encoding.UTF8.GetBytes(new JsonObject { ["seq"] = seq, ["hash"] = hash, ["mac"] = mac }.ToJsonString());
        var tmp = _path + ".tmp";
        using (var t = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            t.Write(bytes);
            t.Flush(true);
        }
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>
    /// Production only (ACL handling given): a missing folder is created Administrators/SYSTEM-only; an existing folder without that protection was writable by
    /// others, so its content is not trusted: it is moved aside (kept as found) and a protected folder takes its place. Failures propagate (visible as Broken).
    /// </summary>
    private void EnsureProtectedFolder(bool create)
    {
        if (_isProtected is null || _protect is null) { if (create) Directory.CreateDirectory(_dir); return; }
        if (Directory.Exists(_dir))
        {
            // a junction / symbolic link is never trusted (its target could be retargeted later); otherwise the DACL decides
            if (!new DirectoryInfo(_dir).Attributes.HasFlag(FileAttributes.ReparsePoint) && _isProtected(_dir)) return;
            var aside = _dir + ".untrusted-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(_dir, aside);
            MovedAside = aside;
        }
        else if (!create) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_dir)!);
        _protect(_dir);   // creates the folder WITH its DACL in one step (no window in which an inherited ACE lets others plant a file)
        if (!Directory.Exists(_dir) || !_isProtected(_dir)) throw new UnauthorizedAccessException($"Folderul martorului anti-revenire nu a putut fi restricționat la Administrators și SYSTEM: {_dir}");
    }

    [SupportedOSPlatform("windows")]
    private static bool OnlyAdminsCanRead(string dir)
    {
        var acl = new DirectoryInfo(dir).GetAccessControl();
        foreach (FileSystemAccessRule r in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (r.AccessControlType == AccessControlType.Allow && !AuthStoreProtection.IsTrusted(r.IdentityReference.Value)
                && r.IdentityReference.Value != "S-1-3-0" && ((r.FileSystemRights & FileSystemRights.ReadData) != 0 || ((int)r.FileSystemRights & unchecked((int)0x80000000)) != 0)) return false;   // ReadData or GENERIC_READ
        return true;
    }

    /// <summary>Creates the folder (or re-permissions an existing one) with Administrators and SYSTEM full control, nothing inherited, owner Administrators
    /// (no read for standard users: they cannot lock the file either).</summary>
    [SupportedOSPlatform("windows")]
    private static void ApplyAdminOnlyAcl(string dir)
    {
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
        sec.SetOwner(admins);
        var info = new DirectoryInfo(dir);
        if (info.Exists) info.SetAccessControl(sec); else info.Create(sec);
    }
}
