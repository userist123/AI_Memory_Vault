using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// Read-only reading of "user rights assignments" through the Local Security Authority (LsaEnumerateAccountsWithUserRight): who holds a privilege such as
/// SeTcbPrivilege. Nothing is changed. A right that cannot be read (no administrator rights, access denied) is "" and never an empty list.
/// </summary>
internal static class UserRights
{
    /// <summary>Value stored when the right is held by nobody.</summary>
    public const string Nobody = "(niciunul)";

    private const uint PolicyViewLocalInformation = 0x00000001;
    private const uint PolicyLookupNames = 0x00000800;
    private const uint StatusSuccess = 0x00000000;
    private const uint StatusNoMoreEntries = 0x8000001A;

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaEnumerationInformation
    {
        public IntPtr Sid;
    }

    [DllImport("advapi32.dll")]
    private static extern uint LsaOpenPolicy(IntPtr systemName, ref LsaObjectAttributes objectAttributes, uint desiredAccess, out IntPtr policyHandle);

    [DllImport("advapi32.dll")]
    private static extern uint LsaEnumerateAccountsWithUserRight(IntPtr policyHandle, ref LsaUnicodeString userRight, out IntPtr buffer, out uint countReturned);

    [DllImport("advapi32.dll")]
    private static extern uint LsaFreeMemory(IntPtr buffer);

    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr objectHandle);

    /// <summary>
    /// The holders of each right: "S-1-5-...;S-1-5-..." (string SIDs), <see cref="Nobody"/>, or "" when unreadable. Also the readable names of the SIDs
    /// found: "S-1-5-32-544=BUILTIN\Administrators;..." (a SID that does not translate is absent from it).
    /// </summary>
    public static (Dictionary<string, string> Holders, string Names) Read(IEnumerable<string> rights)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var all = rights.ToList();
        foreach (var r in all) result[r] = "";
        var attributes = new LsaObjectAttributes { Length = Marshal.SizeOf<LsaObjectAttributes>() };
        if (LsaOpenPolicy(IntPtr.Zero, ref attributes, PolicyLookupNames | PolicyViewLocalInformation, out var policy) != StatusSuccess || policy == IntPtr.Zero)
            return (result, "");
        try
        {
            foreach (var right in all) result[right] = Holders(policy, right);
        }
        finally { _ = LsaClose(policy); }

        var names = new List<string>();
        foreach (var sid in result.Values.SelectMany(v => v.Split(';', StringSplitOptions.RemoveEmptyEntries)).Where(v => v.StartsWith("S-", StringComparison.Ordinal)).Distinct())
        {
            try { names.Add(sid + "=" + new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value); }
            catch (SystemException) { }   // IdentityNotMappedException, ArgumentException, SystemException: the SID stays as it is
        }
        return (result, string.Join(";", names));
    }

    private static string Holders(IntPtr policy, string right)
    {
        var name = Marshal.StringToHGlobalUni(right);
        var buffer = IntPtr.Zero;
        try
        {
            var us = new LsaUnicodeString { Length = (ushort)(right.Length * 2), MaximumLength = (ushort)((right.Length + 1) * 2), Buffer = name };
            var status = LsaEnumerateAccountsWithUserRight(policy, ref us, out buffer, out var count);
            if (status == StatusNoMoreEntries) return Nobody;
            if (status != StatusSuccess) return "";
            if (count == 0) return Nobody;
            var size = Marshal.SizeOf<LsaEnumerationInformation>();
            var sids = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var sidPtr = Marshal.ReadIntPtr(buffer + i * size);
                if (sidPtr != IntPtr.Zero) sids.Add(new SecurityIdentifier(sidPtr).Value);
            }
            return sids.Count == 0 ? Nobody : string.Join(";", sids);
        }
        catch (ArgumentException) { return ""; }
        finally
        {
            if (buffer != IntPtr.Zero) _ = LsaFreeMemory(buffer);
            Marshal.FreeHGlobal(name);
        }
    }
}
