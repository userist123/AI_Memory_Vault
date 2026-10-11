using LogAnalyzer.Dfir.Devices;

namespace LogAnalyzer.Dfir.Registers;

public enum UsbSessionStatus { Matched, ToVerify }

/// <summary>One connection of a removable storage medium inside the control period, compared with the transfer register.</summary>
public sealed record UsbSessionCheck(DeviceRecord Device, DateTimeOffset Connected, UsbSessionStatus Status, string TransferNumber, string Message)
{
    public string StatusText => Status == UsbSessionStatus.Matched ? "MATCHED" : "DE_VERIFICAT";
    public DateOnly Day => DateOnly.FromDateTime(Connected.ToLocalTime().DateTime);
}

public static class TransferReconciliation
{
    public const string UnregisteredMessage = "stick folosit fără înregistrare în registrul de transferuri";

    /// <summary>
    /// For every connection of a removable storage device (USB stick/disk, memory card) whose local day falls in [<paramref name="from"/>, <paramref name="to"/>],
    /// looks for a transfer row on the same medium (serial or registry number) dated the same day or one day before/after. Other device kinds are ignored.
    /// <paramref name="registrationOf"/> gives the media-register number of a device (empty when unknown) so a transfer written by registry number also matches.
    /// Pure; nothing is written.
    /// </summary>
    public static List<UsbSessionCheck> CheckUsbSessions(IEnumerable<DeviceRecord> devices, IEnumerable<TransferRow> transfers, DateOnly from, DateOnly to,
        Func<DeviceRecord, string>? registrationOf = null)
    {
        var rows = new List<(TransferRow Row, DateOnly Day)>();
        foreach (var t in transfers)
            if (TransferRegister.TryDateTime(t.DateTime, out var w)) rows.Add((t, DateOnly.FromDateTime(w)));

        var result = new List<UsbSessionCheck>();
        foreach (var d in devices)
        {
            if (d.Kind is not (DeviceKind.UsbStorage or DeviceKind.MemoryCard)) continue;
            var serial = MediaRegister.NormalizeSerial(d.Serial);
            var reg = (registrationOf?.Invoke(d) ?? "").Trim();
            foreach (var c in d.Connections.Distinct().OrderBy(c => c))
            {
                var day = DateOnly.FromDateTime(c.ToLocalTime().DateTime);
                if (day < from || day > to) continue;
                var hit = rows.FirstOrDefault(x => Math.Abs(x.Day.DayNumber - day.DayNumber) <= 1 &&
                    ((serial.Length > 0 && MediaRegister.NormalizeSerial(x.Row.MediumSerial) == serial) ||
                     (reg.Length > 0 && x.Row.MediumRegistration.Trim().Equals(reg, StringComparison.OrdinalIgnoreCase))));
                result.Add(hit.Row is null
                    ? new UsbSessionCheck(d, c, UsbSessionStatus.ToVerify, "", UnregisteredMessage)
                    : new UsbSessionCheck(d, c, UsbSessionStatus.Matched, hit.Row.RegistrationNumber, $"transfer {hit.Row.RegistrationNumber}"));
            }
        }
        return result;
    }
}
