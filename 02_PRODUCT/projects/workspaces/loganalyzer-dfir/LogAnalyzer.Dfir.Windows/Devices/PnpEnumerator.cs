using System.Runtime.InteropServices;
using System.Text;

namespace LogAnalyzer.Dfir.Windows.Devices;

/// <summary>
/// Present Plug and Play devices of chosen setup classes, read through SetupAPI (the WMI class Win32_PnPEntity takes tens of seconds on a normal station; this takes
/// milliseconds). Read-only: it only reads device properties. "Disabled" is the Configuration Manager problem code 22 (CM_PROB_DISABLED).
/// </summary>
public static class PnpEnumerator
{
    public sealed record Node(string InstanceId, string Name, string SetupClass, string Service, string Manufacturer, IReadOnlyList<string> HardwareIds, IReadOnlyList<string> CompatibleIds, bool Disabled);

    private const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
    private const uint SPDRP_DEVICEDESC = 0x0, SPDRP_HARDWAREID = 0x1, SPDRP_COMPATIBLEIDS = 0x2, SPDRP_CLASS = 0x7, SPDRP_SERVICE = 0x4, SPDRP_MFG = 0xB, SPDRP_FRIENDLYNAME = 0xC;
    private const uint CM_PROB_DISABLED = 22;
    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>Every present device whose setup class name (Net, Camera, Image, MEDIA, AudioEndpoint, ...) is in <paramref name="classNames"/>.</summary>
    public static List<Node> Present(params string[] classNames)
    {
        var wanted = new HashSet<string>(classNames, StringComparer.OrdinalIgnoreCase);
        var res = new List<Node>();
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
        if (set == InvalidHandle) throw new IOException("SetupAPI: lista dispozitivelor nu a putut fi deschisă (cod " + Marshal.GetLastWin32Error() + ")");
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var cls = Text(set, ref data, SPDRP_CLASS);
                if (!wanted.Contains(cls)) continue;
                var name = Text(set, ref data, SPDRP_FRIENDLYNAME);
                if (name.Length == 0) name = Text(set, ref data, SPDRP_DEVICEDESC);
                bool disabled = CM_Get_DevNode_Status(out _, out var problem, data.DevInst, 0) == 0 && problem == CM_PROB_DISABLED;
                res.Add(new Node(InstanceId(set, ref data), name, cls, Text(set, ref data, SPDRP_SERVICE), Text(set, ref data, SPDRP_MFG), Multi(set, ref data, SPDRP_HARDWAREID), Multi(set, ref data, SPDRP_COMPATIBLEIDS), disabled));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return res;
    }

    private static byte[]? Raw(IntPtr set, ref SP_DEVINFO_DATA d, uint prop)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref d, prop, out _, null, 0, out var size);
        if (size == 0) return null;
        var buf = new byte[size];
        return SetupDiGetDeviceRegistryPropertyW(set, ref d, prop, out _, buf, size, out _) ? buf : null;
    }

    private static string Text(IntPtr set, ref SP_DEVINFO_DATA d, uint prop) => Raw(set, ref d, prop) is { } b ? Encoding.Unicode.GetString(b).TrimEnd('\0') : "";

    private static string[] Multi(IntPtr set, ref SP_DEVINFO_DATA d, uint prop) =>
        Raw(set, ref d, prop) is { } b ? Encoding.Unicode.GetString(b).Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];

    private static string InstanceId(IntPtr set, ref SP_DEVINFO_DATA d)
    {
        var sb = new StringBuilder(512);
        return SetupDiGetDeviceInstanceIdW(set, ref d, sb, (uint)sb.Capacity, out _) ? sb.ToString() : "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, uint property, out uint regType, byte[]? buffer, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder? buffer, uint size, out uint required);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
}
