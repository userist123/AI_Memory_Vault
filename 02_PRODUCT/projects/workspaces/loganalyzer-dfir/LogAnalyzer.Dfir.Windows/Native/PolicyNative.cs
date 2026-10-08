using System.Runtime.InteropServices;

namespace LogAnalyzer.Dfir.Windows.Native;

[Flags]
public enum AuditSetting { None = 0, Success = 1, Failure = 2 }

/// <summary>Effective system audit policy per subcategory (AuditQuerySystemPolicy). Language independent.</summary>
public static class AuditPolicy
{
    public static readonly IReadOnlyDictionary<string, Guid> Subcategories = new Dictionary<string, Guid>
    {
        ["Logon"] = G(0x15), ["Logoff"] = G(0x16), ["Account Lockout"] = G(0x17), ["Special Logon"] = G(0x1B),
        ["Security State Change"] = G(0x10), ["Security System Extension"] = G(0x11),
        ["Filtering Platform Connection"] = G(0x26), ["Other Object Access Events"] = G(0x27),
        ["Process Creation"] = G(0x2B), ["Audit Policy Change"] = G(0x2F),
        ["User Account Management"] = G(0x35), ["Security Group Management"] = G(0x37),
        ["Credential Validation"] = G(0x3F), ["Removable Storage"] = G(0x45), ["Plug and Play Events"] = G(0x48),
    };

    private static Guid G(int b) => new($"0CCE92{b:X2}-69AE-11D9-BED3-505054503030");

    /// <summary>Returns null when the policy cannot be read (typically: not running as administrator).</summary>
    public static IReadOnlyDictionary<string, AuditSetting>? Query()
    {
        var names = Subcategories.Keys.ToArray();
        var guids = names.Select(n => Subcategories[n]).ToArray();
        if (!AuditQuerySystemPolicy(guids, (uint)guids.Length, out var buffer) || buffer == IntPtr.Zero) return null;
        try
        {
            var result = new Dictionary<string, AuditSetting>();
            int size = Marshal.SizeOf<AUDIT_POLICY_INFORMATION>();
            for (int i = 0; i < guids.Length; i++)
            {
                var info = Marshal.PtrToStructure<AUDIT_POLICY_INFORMATION>(buffer + i * size);
                result[names[i]] = (AuditSetting)(info.AuditingInformation & 3);
            }
            return result;
        }
        finally
        {
            AuditFree(buffer);
        }
    }

    /// <summary>Raw AuditingInformation of one subcategory; throws with the Win32 error when it cannot be read.</summary>
    public static uint QueryFlags(Guid subcategory)
    {
        if (!AuditQuerySystemPolicy([subcategory], 1, out var buffer) || buffer == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { return Marshal.PtrToStructure<AUDIT_POLICY_INFORMATION>(buffer).AuditingInformation; }
        finally { AuditFree(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIT_POLICY_INFORMATION
    {
        public Guid AuditSubCategoryGuid;
        public uint AuditingInformation;
        public Guid AuditCategoryGuid;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)] // BOOLEAN (1 byte), not BOOL
    private static extern bool AuditQuerySystemPolicy([In] Guid[] pSubCategoryGuids, uint dwPolicyCount, out IntPtr ppAuditPolicy);

    [DllImport("advapi32.dll")]
    private static extern void AuditFree(IntPtr buffer);
}

public sealed record PasswordPolicy(int MinLength, TimeSpan? MaxAge, TimeSpan MinAge, int History, int LockoutThreshold, TimeSpan LockoutDuration, TimeSpan LockoutWindow);

/// <summary>Local password and lockout policy (NetUserModalsGet levels 0 and 3). No administrator rights needed.</summary>
public static class LocalPasswordPolicy
{
    private const uint TIMEQ_FOREVER = 0xFFFFFFFF;

    public static PasswordPolicy? Query()
    {
        if (NetUserModalsGet(null, 0, out var b0) != 0) return null;
        try
        {
            if (NetUserModalsGet(null, 3, out var b3) != 0) return null;
            try
            {
                uint minLen = (uint)Marshal.ReadInt32(b0, 0), maxAge = (uint)Marshal.ReadInt32(b0, 4), minAge = (uint)Marshal.ReadInt32(b0, 8),
                     hist = (uint)Marshal.ReadInt32(b0, 16);
                uint lockDur = (uint)Marshal.ReadInt32(b3, 0), lockWin = (uint)Marshal.ReadInt32(b3, 4), lockThr = (uint)Marshal.ReadInt32(b3, 8);
                return new PasswordPolicy((int)minLen, maxAge == TIMEQ_FOREVER ? null : TimeSpan.FromSeconds(maxAge), TimeSpan.FromSeconds(minAge),
                    (int)hist, (int)lockThr, lockDur == TIMEQ_FOREVER ? TimeSpan.MaxValue : TimeSpan.FromSeconds(lockDur), TimeSpan.FromSeconds(lockWin));
            }
            finally { NetApiBufferFree(b3); }
        }
        finally { NetApiBufferFree(b0); }
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetUserModalsGet(string? server, uint level, out IntPtr buffer);

    [DllImport("netapi32.dll")]
    private static extern uint NetApiBufferFree(IntPtr buffer);
}
