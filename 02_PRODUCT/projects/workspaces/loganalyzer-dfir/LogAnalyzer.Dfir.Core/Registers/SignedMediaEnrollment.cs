using LogAnalyzer.Dfir.Devices;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// From the station's device inventory to the signed register: which devices are removable media (USB storage and memory cards), the entry a connected
/// medium becomes at enrolment, and the observed identity a control compares with the register.
/// </summary>
public static class SignedMediaEnrollment
{
    public static bool IsRemovableMedium(DeviceRecord d) => d.Kind is DeviceKind.UsbStorage or DeviceKind.MemoryCard or DeviceKind.ExternalDisk;

    /// <summary>"criptat" / "necriptat" / "necunoscut" from the inventory's encryption text ("E: criptat BitLocker", "E: necriptat", …).</summary>
    public static string BitLockerStatus(string encryption)
    {
        var e = (encryption ?? "").ToLowerInvariant();
        if (e.Contains("necriptat")) return "necriptat";
        if (e.Contains("criptat bitlocker")) return "criptat";
        return "necunoscut";
    }

    /// <summary>The register entry for one connected medium; the operator fills in the inventory number, classification and holder.</summary>
    public static SignedMediaEntry Entry(DeviceRecord d, DateTimeOffset nowUtc) => new()
    {
        UsbSerial = MediaRegister.NormalizeSerial(d.Serial), VidPid = (d.VidPid ?? "").ToLowerInvariant(), Vendor = d.Manufacturer, Model = d.Model.Length > 0 ? d.Model : d.Name,
        CapacityBytes = d.CapacityBytes ?? 0, VolumeSerial = string.Join(";", d.VolumeSerials), BitLockerStatus = BitLockerStatus(d.Encryption),
        BitLockerVolumeId = d.BitLockerVolumeId, EnrolledUtc = SignedMediaRegister.FormatUtc(nowUtc), Status = SignedMediaRegister.StatusActive,
    };

    /// <summary>The removable media connected now (enrolment takes all of them at once on the transfer station).</summary>
    public static List<DeviceRecord> Connected(IEnumerable<DeviceRecord> devices) => devices.Where(d => d.ConnectedNow && IsRemovableMedium(d)).ToList();

    /// <summary>Every removable medium the station knows (connected now or in its history), as the control compares it with the register.</summary>
    public static List<SeenMedium> Observed(IEnumerable<DeviceRecord> devices) =>
        devices.Where(IsRemovableMedium).Select(d => new SeenMedium(d.Serial, d.VidPid, d.CapacityBytes, d.BitLockerVolumeId,
                   $"{d.Manufacturer} {(d.Model.Length > 0 ? d.Model : d.Name)}".Trim() is { Length: > 0 } n ? n : d.InstanceId, d.ConnectedNow,
                   [.. d.Connections, .. d.LastConnectedUtc is { } l ? new[] { l } : [], .. d.FirstConnectedUtc is { } f ? new[] { f } : []]))
               .ToList();
}
