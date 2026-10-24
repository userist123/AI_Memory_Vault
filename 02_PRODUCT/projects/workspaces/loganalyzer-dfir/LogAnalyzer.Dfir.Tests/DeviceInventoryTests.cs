using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Dfir.Windows.Devices;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Owner 2026-10-11: every storage medium and connectable device, with all its data, compared with the media register; research §4.</summary>
public class DeviceInventoryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(@"USBSTOR\Disk&Ven_Kingston&Prod_DataTraveler_3.0&Rev_PMAP\60A44C3FAE22EEA0C9A2&0", "DiskDrive", "", "", DeviceKind.UsbStorage)]
    [InlineData(@"USBSTOR\Disk&Ven_Generic-&Prod_SD/MMC&Rev_1.00\20120926571200000&0", "DiskDrive", "", "Generic- SD/MMC USB Device", DeviceKind.MemoryCard)]
    [InlineData(@"SCSI\Disk&Ven_NVMe&Prod_Samsung_SSD_980\5&2f1b5c2&0&000000", "DiskDrive", "", "", DeviceKind.InternalDisk)]
    [InlineData(@"SCSI\Disk&Ven_Samsung&Prod_PSSD_T7\7&1a2b&0&000000", "DiskDrive", @"USB\VID_04E8&PID_4001\S5TNNS0R123", "", DeviceKind.UsbStorage)]
    [InlineData(@"SCSI\Disk&Ven_Msft&Prod_Virtual_Disk\2&1f4adffe&0&000001", "DiskDrive", "", "Microsoft Virtual Disk", DeviceKind.VirtualDisk)]
    [InlineData(@"SCSI\CdRom&Ven_HL-DT-ST&Prod_DVDRAM\4&1&0", "CDROM", "", "", DeviceKind.Optical)]
    [InlineData(@"USB\VID_04E8&PID_6860\R58M123ABC", "WPD", "", "Galaxy S21", DeviceKind.PortableDevice)]
    [InlineData(@"BTHENUM\DEV_0C1539A1B2C3\7&2d4f&0&BLUETOOTHDEVICE_0C1539A1B2C3", "Bluetooth", "", "Telefon", DeviceKind.Bluetooth)]
    public void Devices_are_classified_by_what_they_are(string id, string cls, string hw, string name, DeviceKind expected) =>
        Assert.Equal(expected, DeviceRules.Classify(id, cls, hw, name));

    [Theory]
    [InlineData(DeviceKind.VirtualDisk, "C: D:", @"C:\", DeviceKind.InternalDisk)]   // Hyper-V guest: the boot disk is MSFT Virtual Disk
    [InlineData(DeviceKind.VirtualDisk, "c:", @"C:\", DeviceKind.InternalDisk)]
    [InlineData(DeviceKind.VirtualDisk, "E:", @"C:\", DeviceKind.VirtualDisk)]        // a VHD mounted as E: stays a mounted image
    [InlineData(DeviceKind.VirtualDisk, "", @"C:\", DeviceKind.VirtualDisk)]
    [InlineData(DeviceKind.UsbStorage, "C:", @"C:\", DeviceKind.UsbStorage)]         // only virtual disks are reconsidered
    public void A_virtual_disk_holding_the_system_volume_is_the_station_disk(DeviceKind kind, string letters, string systemDrive, DeviceKind expected) =>
        Assert.Equal(expected, DeviceRules.WithSystemVolume(kind, letters.Split(' ', StringSplitOptions.RemoveEmptyEntries), systemDrive));

    [Theory]
    [InlineData(@"HID\VID_046D&PID_C52B\7&1", "HIDClass")]                                                             // a mouse
    [InlineData(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_LOCALMFG&0002\7&1", "Bluetooth")]                   // a Bluetooth service entry, not a device
    [InlineData(@"SWD\WPDBUSENUM\_??_USBSTOR#DISK&VEN_KINGSTON#60A4&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}", "WPD")] // the volume of a stick already listed
    public void Things_that_are_not_media_are_left_out(string id, string cls) => Assert.Null(DeviceRules.Classify(id, cls, "", ""));

    [Fact]
    public void Serials_lose_the_windows_suffix_and_generated_ids_are_not_serials()
    {
        Assert.Equal("60A44C3FAE22EEA0C9A2", DeviceRules.Serial(@"USBSTOR\Disk&Ven_Kingston&Prod_DT&Rev_1\60A44C3FAE22EEA0C9A2&0"));
        Assert.Equal("", DeviceRules.Serial(@"USBSTOR\Disk&Ven_Cheap&Prod_Stick&Rev_1\7&2a4b1c&0"));   // second char '&': Windows made it up
        Assert.Equal("0951:1666", DeviceRules.VidPid(@"USB\VID_0951&PID_1666\60A4"));
        Assert.Equal(("Kingston", "DataTraveler 3.0"), DeviceRules.VendorProduct(@"USBSTOR\Disk&Ven_Kingston&Prod_DataTraveler_3.0&Rev_PMAP\x"));
    }

    private static MediaRegister Reg(params MediaRow[] rows) => new() { Rows = rows.ToList() };

    [Fact]
    public void The_register_verdict_says_what_to_do()
    {
        var stick = new DeviceRecord { Kind = DeviceKind.UsbStorage, InstanceId = "x", Serial = "AA11", ConnectedNow = true };
        var today = new DateOnly(2026, 10, 11);
        var none = DeviceRules.Assess(stick, Reg(), today, true);
        Assert.Equal(RegisterMatch.NotRegistered, none.Match);
        Assert.Contains("Deconectați-l", none.WhatToDo);
        Assert.True(none.NeedsAttention);

        var ok = DeviceRules.Assess(stick, Reg(new MediaRow { RegistrationNumber = "1234-56S", Serial = "aa11", Status = "activ" }), today, true);
        Assert.Equal(RegisterMatch.Registered, ok.Match);
        Assert.Equal("1234-56S", ok.RegistrationNumber);

        Assert.Equal(RegisterMatch.Withdrawn, DeviceRules.Assess(stick, Reg(new MediaRow { RegistrationNumber = "R", Serial = "AA11", Status = "distrus" }), today, true).Match);
        Assert.Equal(RegisterMatch.Expired, DeviceRules.Assess(stick, Reg(new MediaRow { RegistrationNumber = "R", Serial = "AA11", Status = "activ", ValidTo = "2026-01-01" }), today, true).Match);

        var bt = DeviceRules.Assess(new DeviceRecord { Kind = DeviceKind.Bluetooth, InstanceId = "b" }, Reg(), today, true);
        Assert.Equal(RegisterMatch.NotApplicable, bt.Match);
        Assert.Contains("stație izolată", bt.Verdict);
    }

    [Fact]
    public void Windows_protections_are_read_and_unprotected_is_said_plainly()
    {
        var values = new Dictionary<string, object>
        {
            [@"SYSTEM\CurrentControlSet\Services\USBSTOR|Start"] = 4,
            [@"SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56308-b6bf-11d0-94f2-00a0c91efb8b}|Deny_Write"] = 1,
        };
        var r = DevicePolicyCheck.Read((k, n) => values.GetValueOrDefault(k + "|" + n),
                                       () => new Dictionary<string, string> { ["Removable Storage"] = "No Auditing", ["Plug and Play Events"] = "Success" });
        Assert.Contains(r, p => p.Title.Contains("USBSTOR") && p.Protected && p.State == "dezactivată");
        Assert.Contains(r, p => p.Title == "Acces la CD/DVD" && p.State == "scriere interzisă" && p.Protected);
        Assert.Contains(r, p => p.Title.StartsWith("Acces la discuri amovibile") && !p.Protected);
        Assert.Contains(r, p => p.Title.StartsWith("Înregistrarea copierii") && p.State == "INACTIVĂ" && p.Meaning.Contains("NU se înregistrează"));
        Assert.Contains(r, p => p.Title.StartsWith("Înregistrarea conectării") && p.Protected);
    }

    [Fact]
    public void The_real_station_lists_its_internal_disk_with_data()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = DeviceInventory.Collect();
        foreach (var d in result.Devices.Take(40))
            output.WriteLine($"{d.KindText,-40} {(d.ConnectedNow ? "ACUM" : "    ")} {d.Name,-40} SN={d.Serial} {d.VidPid} {d.CapacityText} {d.DriveLetters} {d.Encryption} first={d.FirstConnectedUtc:yyyy-MM-dd} last={d.LastConnectedUtc:yyyy-MM-dd HH:mm} removed={d.LastRemovedUtc:yyyy-MM-dd HH:mm}");
        foreach (var p in result.Problems) output.WriteLine("PROBLEMĂ: " + p);
        Assert.Contains(result.Devices, d => d.Kind == DeviceKind.InternalDisk && d.ConnectedNow && d.CapacityBytes > 0);
    }
}
