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

    private const uint DN_HAS_PROBLEM = 0x400;

    /// <summary>
    /// Every present device of every class, for the real-time device monitor: id, name, setup class, manufacturer, hardware ids, service and the Configuration
    /// Manager problem code (the value Win32_PnPEntity.ConfigManagerErrorCode reports: 0 when the device has no problem, -1 when the status was not readable).
    /// </summary>
    public static List<LogAnalyzer.Dfir.Devices.PnpSnapshotEntry> Snapshot()
    {
        var res = new List<LogAnalyzer.Dfir.Devices.PnpSnapshotEntry>();
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
        if (set == InvalidHandle) throw new IOException("SetupAPI: lista dispozitivelor nu a putut fi deschisă (cod " + Marshal.GetLastWin32Error() + ")");
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var name = Text(set, ref data, SPDRP_FRIENDLYNAME);
                if (name.Length == 0) name = Text(set, ref data, SPDRP_DEVICEDESC);
                int code = CM_Get_DevNode_Status(out var status, out var problem, data.DevInst, 0) == 0 ? ((status & DN_HAS_PROBLEM) != 0 ? (int)problem : 0) : -1;
                res.Add(new(InstanceId(set, ref data), name, Text(set, ref data, SPDRP_CLASS), Text(set, ref data, SPDRP_MFG),
                    string.Join(" ", Multi(set, ref data, SPDRP_HARDWAREID)), Text(set, ref data, SPDRP_SERVICE), code, Container(set, ref data)));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return res;
    }

    private static readonly Guid ContainerKeyFmt = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c");

    /// <summary>
    /// DEVPKEY_Device_ContainerId (pid 2): all the nodes of one physical device share it. DEVPKEY_Device_InLocalMachineContainer (pid 4) true = a component of the
    /// station itself, reported as the local machine container (see <see cref="LogAnalyzer.Dfir.Devices.DeviceScope.LocalMachineContainer"/>). "" = not readable.
    /// </summary>
    private static string Container(IntPtr set, ref SP_DEVINFO_DATA d)
    {
        var inLocal = new DEVPROPKEY { fmtid = ContainerKeyFmt, pid = 4 };
        var buf = new byte[4];
        if (SetupDiGetDevicePropertyW(set, ref d, ref inLocal, out var t, buf, (uint)buf.Length, out _, 0) && t == 0x11 && buf[0] != 0)
            return LogAnalyzer.Dfir.Devices.DeviceScope.LocalMachineContainer;
        var key = new DEVPROPKEY { fmtid = ContainerKeyFmt, pid = 2 };
        var g = new byte[16];
        return SetupDiGetDevicePropertyW(set, ref d, ref key, out t, g, 16, out _, 0) && t == 0x0D ? "{" + new Guid(g).ToString().ToUpperInvariant() + "}" : "";
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint propertyType, byte[]? buffer, uint size, out uint required, uint flags);

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
