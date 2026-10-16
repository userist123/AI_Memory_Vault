using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using LogAnalyzer.Dfir.Devices;

namespace LogAnalyzer.Dfir.Windows.Devices;

/// <summary>
/// The installed driver of each device from WMI Win32_PnPSignedDriver (DriverVersion, DriverProviderName, DriverDate, IsSigned, Signer). Read-only. The full
/// list takes a few seconds, so the monitor reads it once at start and then only for the devices that arrive.
/// </summary>
public static class SignedDriverReader
{
    private const string Fields = "DeviceID, DriverVersion, DriverProviderName, DriverDate, IsSigned, Signer";

    /// <summary>Every device's driver, keyed by Plug and Play device id; empty when WMI is not available.</summary>
    public static Dictionary<string, SignedDriverInfo> All() => Query($"SELECT {Fields} FROM Win32_PnPSignedDriver");

    /// <summary>The drivers of the given devices only (one query per device).</summary>
    public static Dictionary<string, SignedDriverInfo> For(IEnumerable<string> deviceIds)
    {
        var res = new Dictionary<string, SignedDriverInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in deviceIds.Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var kv in Query($"SELECT {Fields} FROM Win32_PnPSignedDriver WHERE DeviceID = '{Escape(id)}'"))
                res[kv.Key] = kv.Value;
        return res;
    }

    /// <summary>A WQL string literal: backslash and quote are escaped.</summary>
    public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    /// <summary>CIM datetime "20230615000000.******+000" → "2023-06-15"; anything else unchanged.</summary>
    public static string Date(string cim) =>
        cim.Length >= 8 && DateTime.TryParseExact(cim[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : cim;

    private static Dictionary<string, SignedDriverInfo> Query(string wql)
    {
        var res = new Dictionary<string, SignedDriverInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (var o in searcher.Get())
            {
                using (o)
                {
                    var id = o["DeviceID"]?.ToString() ?? "";
                    if (id.Length == 0) continue;
                    res[id] = new SignedDriverInfo(o["DriverVersion"]?.ToString() ?? "", o["DriverProviderName"]?.ToString() ?? "", Date(o["DriverDate"]?.ToString() ?? ""),
                        o["IsSigned"] is bool b ? b : null, o["Signer"]?.ToString() ?? "");
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidCastException) { }
        return res;
    }
}
