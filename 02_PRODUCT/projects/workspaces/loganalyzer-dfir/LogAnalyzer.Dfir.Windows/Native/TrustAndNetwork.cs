using System.Net;
using System.Runtime.InteropServices;

namespace LogAnalyzer.Dfir.Windows.Native;

public sealed record SignatureInfo(bool IsSigned, bool IsValid, string Kind, string Status, string Signer);

/// <summary>
/// Authenticode verification through WinVerifyTrust, for embedded signatures and for catalog-signed system files
/// (most of Windows' own binaries carry no embedded signature). Revocation is not checked and URL retrieval is
/// cache-only, so verification never generates network traffic.
/// </summary>
public static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_CHOICE_CATALOG = 2;
    private const uint WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2, WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003), TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);

    public static SignatureInfo Verify(string path)
    {
        int embedded = VerifyEmbedded(path);
        // Not a signable format / no provider for it: same as "no embedded signature".
        if (embedded is TRUST_E_SUBJECT_FORM_UNKNOWN or TRUST_E_PROVIDER_UNKNOWN) embedded = TRUST_E_NOSIGNATURE;
        if (embedded != TRUST_E_NOSIGNATURE)
            return new SignatureInfo(true, embedded == 0, "embedded", Describe(embedded), SignerSubject(path));

        var (catalogResult, catalogName) = VerifyCatalog(path);
        if (catalogResult is int r)
            return new SignatureInfo(true, r == 0, $"catalog ({catalogName})", Describe(r), "Windows catalog");

        return new SignatureInfo(false, false, "none", "fără semnătură digitală", "");
    }

    private static string Describe(int hr) => hr switch
    {
        0 => "valid",
        TRUST_E_NOSIGNATURE => "fără semnătură",
        unchecked((int)0x800B0101) => "certificat expirat",
        unchecked((int)0x800B010A) => "lanț de certificate neîncrezător",
        unchecked((int)0x80096010) => "semnătură invalidă (fișier modificat după semnare)",
        unchecked((int)0x800B0111) => "semnatar explicit neîncrezător",
        _ => $"eroare 0x{hr:X8}"
    };

    private static string SignerSubject(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return "";
        }
    }

    private static int VerifyEmbedded(string path)
    {
        var file = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(file, pFile, false);
            var data = NewData(WTD_CHOICE_FILE, pFile);
            var action = GenericVerifyV2;
            int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(pFile);
        }
    }

    private static (int? result, string catalog) VerifyCatalog(string path)
    {
        if (!CryptCATAdminAcquireContext2(out var admin, IntPtr.Zero, "SHA256", IntPtr.Zero, 0))
            return (null, "");
        IntPtr catInfo = IntPtr.Zero;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var handle = fs.SafeFileHandle.DangerousGetHandle();
            uint size = 0;
            CryptCATAdminCalcHashFromFileHandle2(admin, handle, ref size, null, 0);
            if (size == 0) return (null, "");
            var hash = new byte[size];
            if (!CryptCATAdminCalcHashFromFileHandle2(admin, handle, ref size, hash, 0)) return (null, "");

            catInfo = CryptCATAdminEnumCatalogFromHash(admin, hash, size, 0, IntPtr.Zero);
            if (catInfo == IntPtr.Zero) return (null, "");

            var info = new CATALOG_INFO { cbStruct = (uint)Marshal.SizeOf<CATALOG_INFO>() };
            if (!CryptCATCatalogInfoFromContext(catInfo, ref info, 0)) return (null, "");

            var member = Convert.ToHexString(hash);
            var cat = new WINTRUST_CATALOG_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_CATALOG_INFO>(),
                pcwszCatalogFilePath = info.wszCatalogFile,
                pcwszMemberTag = member,
                pcwszMemberFilePath = path,
                hMemberFile = handle,
                pbCalculatedFileHash = IntPtr.Zero,
                cbCalculatedFileHash = 0,
                hCatAdmin = admin,
            };
            IntPtr pHash = Marshal.AllocHGlobal(hash.Length);
            IntPtr pCat = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_CATALOG_INFO>());
            try
            {
                Marshal.Copy(hash, 0, pHash, hash.Length);
                cat.pbCalculatedFileHash = pHash;
                cat.cbCalculatedFileHash = (uint)hash.Length;
                Marshal.StructureToPtr(cat, pCat, false);
                var data = NewData(WTD_CHOICE_CATALOG, pCat);
                var action = GenericVerifyV2;
                int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                return (result, Path.GetFileName(info.wszCatalogFile));
            }
            finally
            {
                Marshal.FreeHGlobal(pHash);
                Marshal.FreeHGlobal(pCat);
            }
        }
        catch (IOException)
        {
            return (null, "");
        }
        finally
        {
            if (catInfo != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(admin, catInfo, 0);
            CryptCATAdminReleaseContext(admin, 0);
        }
    }

    private static WINTRUST_DATA NewData(uint choice, IntPtr info) => new()
    {
        cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
        dwUIChoice = WTD_UI_NONE,
        fdwRevocationChecks = WTD_REVOKE_NONE,
        dwUnionChoice = choice,
        pInfo = info,
        dwStateAction = WTD_STATEACTION_VERIFY,
        dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_CATALOG_INFO
    {
        public uint cbStruct;
        public uint dwCatalogVersion;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszCatalogFilePath;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberTag;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberFilePath;
        public IntPtr hMemberFile;
        public IntPtr pbCalculatedFileHash;
        public uint cbCalculatedFileHash;
        public IntPtr pcCatalogContext;
        public IntPtr hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pInfo;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CATALOG_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string wszCatalogFile;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptCATAdminAcquireContext2(out IntPtr phCatAdmin, IntPtr pgSubsystem, string pwszHashAlgorithm, IntPtr pStrongHashPolicy, uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr hCatAdmin, IntPtr hFile, ref uint pcbHash, byte[]? pbHash, uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin, byte[] pbHash, uint cbHash, uint dwFlags, IntPtr phPrevCatInfo);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptCATCatalogInfoFromContext(IntPtr hCatInfo, ref CATALOG_INFO psCatInfo, uint dwFlags);

    [DllImport("wintrust.dll")]
    private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);

    [DllImport("wintrust.dll")]
    private static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);
}

public sealed record TcpConnection(int Pid, IPEndPoint Local, IPEndPoint Remote, string State);

/// <summary>Current TCP table with owning PIDs (GetExtendedTcpTable), IPv4 and IPv6. Read-only.</summary>
public static class TcpTable
{
    private const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_ALL = 5;

    public static IReadOnlyList<TcpConnection> ForProcess(int pid) => All().Where(c => c.Pid == pid).ToList();

    public static IReadOnlyList<TcpConnection> All()
    {
        var list = new List<TcpConnection>();
        Read(AF_INET, 24, list, (p, l) =>
        {
            uint state = (uint)Marshal.ReadInt32(p, 0);
            var local = new IPEndPoint(new IPAddress((uint)Marshal.ReadInt32(p, 4)), Port(p, 8));
            var remote = new IPEndPoint(new IPAddress((uint)Marshal.ReadInt32(p, 12)), Port(p, 16));
            l.Add(new TcpConnection(Marshal.ReadInt32(p, 20), local, remote, StateName(state)));
        });
        Read(AF_INET6, 56, list, (p, l) =>
        {
            var lAddr = new byte[16]; Marshal.Copy(p, lAddr, 0, 16);
            var rAddr = new byte[16]; Marshal.Copy(p + 24, rAddr, 0, 16);
            var local = new IPEndPoint(new IPAddress(lAddr, (uint)Marshal.ReadInt32(p, 16)), Port(p, 20));
            var remote = new IPEndPoint(new IPAddress(rAddr, (uint)Marshal.ReadInt32(p, 40)), Port(p, 44));
            uint state = (uint)Marshal.ReadInt32(p, 48);
            l.Add(new TcpConnection(Marshal.ReadInt32(p, 52), local, remote, StateName(state)));
        });
        return list;
    }

    private static int Port(IntPtr p, int offset)
    {
        var b = new byte[2];
        Marshal.Copy(p + offset, b, 0, 2);
        return (b[0] << 8) | b[1];
    }

    private static void Read(int family, int rowSize, List<TcpConnection> list, Action<IntPtr, List<TcpConnection>> parse)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0) != 0) return;
            int count = Marshal.ReadInt32(buf);
            for (int i = 0; i < count; i++)
                parse(buf + 4 + i * rowSize, list);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string StateName(uint s) => s switch
    {
        1 => "CLOSED", 2 => "LISTEN", 3 => "SYN_SENT", 4 => "SYN_RCVD", 5 => "ESTABLISHED", 6 => "FIN_WAIT1",
        7 => "FIN_WAIT2", 8 => "CLOSE_WAIT", 9 => "CLOSING", 10 => "LAST_ACK", 11 => "TIME_WAIT", 12 => "DELETE_TCB",
        _ => s.ToString()
    };

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);
}
