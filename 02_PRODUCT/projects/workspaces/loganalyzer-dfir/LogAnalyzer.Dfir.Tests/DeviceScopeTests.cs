using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The scope of the device monitor (owner decision 2026-10-16): storage media and externally connected devices only. One pure classifier, three users (the
/// monitor page, check D05, report section 3c). Fixtures are neutral: LAB-PC era fake ids, FAKESN serials, invented VID/PID and container ids.
/// </summary>
public sealed class DeviceScopeTests
{
    private const string Local = DeviceScope.LocalMachineContainer;
    private const string C1 = "{AAAAAAAA-0000-0000-0000-000000000001}", C2 = "{AAAAAAAA-0000-0000-0000-000000000002}", C3 = "{AAAAAAAA-0000-0000-0000-000000000003}",
                         C4 = "{AAAAAAAA-0000-0000-0000-000000000004}", C5 = "{AAAAAAAA-0000-0000-0000-000000000005}", C6 = "{AAAAAAAA-0000-0000-0000-000000000006}",
                         C7 = "{AAAAAAAA-0000-0000-0000-000000000007}", C8 = "{AAAAAAAA-0000-0000-0000-000000000008}", C9 = "{AAAAAAAA-0000-0000-0000-000000000009}";

    private static PnpSnapshotEntry E(string id, string name, string cls, string container, string hw = "", int code = 0, string mfr = "Fake") =>
        new(id, name, cls, mfr, hw, "", code, container);

    private static PnpSnapshotEntry Vol(string diskId) =>
        E(@"STORAGE\VOLUME\_??_" + diskId.Replace('\\', '#'), "Volume", "Volume", Local, @"STORAGE\Volume");

    private static ScopedDevice Single(IReadOnlyList<PnpSnapshotEntry> snapshot, DeviceClass kind)
    {
        var d = DeviceScope.Classify(snapshot);
        return Assert.Single(d, x => x.Kind == kind);
    }

    // ---- in scope, every kind ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_usb_stick_is_one_row_and_its_usb_parent_is_not_a_second_one()
    {
        var stick = E(@"USBSTOR\Disk&Ven_Fake&Prod_Stick_3.0&Rev_1.00\FAKESN0001&0", "Fake Stick USB Device", "DiskDrive", C1, @"USBSTOR\DiskFake____Stick_3.0");
        var parent = E(@"USB\VID_1234&PID_5678\FAKESN0001", "USB Mass Storage Device", "USB", C1, @"USB\VID_1234&PID_5678");
        var all = DeviceScope.Classify([stick, parent]);
        var d = Assert.Single(all);
        Assert.Equal(DeviceClass.UsbStorage, d.Kind);
        Assert.True(d.IsMedium);
        Assert.Equal("Stick USB", DeviceMonitorRules.ClassText(d.Kind));
    }

    [Fact]
    public void A_portable_disk_is_an_external_disk_by_name_or_by_capacity()
    {
        var named = E(@"USBSTOR\Disk&Ven_Fake&Prod_Portable_SSD&Rev_1.00\FAKESN0002&0", "Fake Portable SSD USB Device", "DiskDrive", C2, "");
        Assert.Equal(DeviceClass.ExternalDisk, Single([named], DeviceClass.ExternalDisk).Kind);
        var plain = E(@"USBSTOR\Disk&Ven_Fake&Prod_Drive&Rev_1.00\FAKESN0003&0", "Fake Drive USB Device", "DiskDrive", C3, "");
        Assert.Equal(DeviceClass.UsbStorage, Single([plain], DeviceClass.UsbStorage).Kind);
        var big = DeviceScope.Classify([plain], new Dictionary<string, MediumInfo> { [plain.InstanceId] = new("AAAA0001", 2_000_000_000_000, "E:", "BACKUP") });
        Assert.Equal(DeviceClass.ExternalDisk, Assert.Single(big).Kind);
        Assert.Equal("Disc extern", DeviceMonitorRules.ClassText(DeviceClass.ExternalDisk));
    }

    [Fact]
    public void A_usb_card_reader_is_a_row_and_the_inserted_card_is_a_row_of_its_own()
    {
        var parent = E(@"USB\VID_058F&PID_6366\FAKEREADER1", "USB Mass Storage Device", "USB", C4, @"USB\VID_058F&PID_6366");
        var slot = E(@"USBSTOR\Disk&Ven_Generic-&Prod_SD_MMC&Rev_1.00\FAKEREADER1&0", "Generic- SD/MMC USB Device", "DiskDrive", C4, @"USBSTOR\DiskGeneric-_SD/MMC");
        var empty = DeviceScope.Classify([parent, slot]);
        var reader = Assert.Single(empty);
        Assert.Equal(DeviceClass.CardReader, reader.Kind);
        Assert.False(reader.IsMedium);
        Assert.Equal(parent.InstanceId, reader.IdentityEntry.InstanceId);                 // the reader's serial and VID/PID come from the USB node

        var withCard = DeviceScope.Classify([parent, slot, Vol(slot.InstanceId)], new Dictionary<string, MediumInfo> { [slot.InstanceId] = new("BEEF0001", 32_000_000_000, "F:", "CARD") });
        Assert.Equal(2, withCard.Count);
        Assert.Contains(withCard, d => d.Kind == DeviceClass.CardReader);
        var card = Assert.Single(withCard, d => d.Kind == DeviceClass.MemoryCard);
        Assert.True(card.IsMedium);
        Assert.Equal("BEEF0001", card.Medium!.VolumeSerial);
        Assert.Equal("Cititor de carduri", DeviceMonitorRules.ClassText(DeviceClass.CardReader));
        Assert.Equal("Card de memorie", DeviceMonitorRules.ClassText(DeviceClass.MemoryCard));
    }

    [Fact]
    public void An_integrated_card_reader_is_out_of_scope_but_the_card_in_it_still_counts()
    {
        var host = E(@"PCI\VEN_10EC&DEV_5229\4&11111111&0&00E5", "Realtek PCIE Card Reader", "SDHost", Local);
        var slot = E(@"SD\VID_AA&OID_BBBB&PID_FAKE&REV_3.0\4&22222222&0&00", "SD Memory Card", "DiskDrive", Local, "");
        Assert.Empty(DeviceScope.Classify([host, slot]));                                  // empty slot: nothing
        var card = Assert.Single(DeviceScope.Classify([host, slot, Vol(slot.InstanceId)]));
        Assert.Equal(DeviceClass.MemoryCard, card.Kind);
    }

    [Fact]
    public void A_phone_seen_as_WPD_composite_and_ADB_is_one_row_with_the_usb_identity()
    {
        var composite = E(@"USB\VID_04E8&PID_6860\FAKEPHONE01", "USB Composite Device", "USB", C5, @"USB\VID_04E8&PID_6860");
        var wpd = E(@"SWD\WPDBUSENUM\{11111111-2222-3333-4444-555555555555}#0000000000100000", "Fake Phone", "WPD", C5);
        var adb = E(@"USB\VID_04E8&PID_6860&MI_01\7&3A4B5C6D&0&0001", "Fake Android ADB Interface", "AndroidUsbDeviceClass", C5);
        var d = Assert.Single(DeviceScope.Classify([composite, wpd, adb]));
        Assert.Equal(DeviceClass.Phone, d.Kind);
        Assert.Equal(composite.InstanceId, d.IdentityEntry.InstanceId);
        Assert.Equal(3, d.Members.Count);
        Assert.Equal("Telefon / tabletă", DeviceMonitorRules.ClassText(d.Kind));
    }

    [Fact]
    public void A_wpd_view_of_a_disk_is_not_a_phone()
    {
        var view = E(@"SWD\WPDBUSENUM\_??_USBSTOR#DISK&VEN_FAKE&PROD_STICK&REV_1.00#FAKESN0001&0#{53F56307-B6BF-11D0-94F2-00A0C91EFB8B}", "E:\\", "WPD", C1);
        Assert.Empty(DeviceScope.Classify([view]));
    }

    [Fact]
    public void A_camera_a_scanner_and_a_printer_are_recognised_and_an_integrated_webcam_is_not()
    {
        var cam = E(@"USB\VID_046D&PID_085B\FAKECAM0001", "Fake USB Webcam", "Camera", C6);
        var scan = E(@"USB\VID_04B8&PID_0142\FAKESCAN001", "Fake Flatbed Scanner", "Image", C7);
        var printer = E(@"USBPRINT\FAKEMAKER\FAKELASER\7&33333333&0&USB001", "Fake LaserJet", "Printer", C8);
        var internalCam = E(@"USB\VID_13D3&PID_5666\5&44444444&0&6", "Integrated Camera", "Camera", Local);
        var d = DeviceScope.Classify([cam, scan, printer, internalCam]);
        Assert.Equal(3, d.Count);
        Assert.Contains(d, x => x.Kind == DeviceClass.Camera && x.Primary.InstanceId == cam.InstanceId);
        Assert.Contains(d, x => x.Kind == DeviceClass.Scanner);
        Assert.Contains(d, x => x.Kind == DeviceClass.Printer);
        Assert.Equal(["Cameră foto/video", "Scaner", "Imprimantă"], new[] { DeviceClass.Camera, DeviceClass.Scanner, DeviceClass.Printer }.Select(DeviceMonitorRules.ClassText));
    }

    [Fact]
    public void Print_queues_and_the_pdf_printer_are_not_printers()
    {
        Assert.Empty(DeviceScope.Classify([
            E(@"SWD\PRINTENUM\{FAKE-1}", "Microsoft Print to PDF", "PrintQueue", Local),
            E(@"SWD\PRINTENUM\{FAKE-2}", "Fake Network Queue", "Printer", Local),
            E(@"ROOT\PRINTQUEUE\0000", "Root Print Queue", "PrintQueue", Local)]));
    }

    [Fact]
    public void Usb_network_wifi_and_modem_adapters_are_in_scope_and_pci_virtual_and_vpn_adapters_are_not()
    {
        var nic = E(@"USB\VID_0BDA&PID_8153\FAKENIC0001", "Realtek USB GbE Family Controller", "Net", C1);
        var wifi = E(@"USB\VID_0BDA&PID_B812\FAKEWIFI001", "Fake Wireless 802.11ac USB Adapter", "Net", C2);
        var modem = E(@"USB\VID_12D1&PID_1506\FAKELTE0001", "Fake LTE Mobile Broadband Adapter", "Net", C3);
        var d = DeviceScope.Classify([nic, wifi, modem]);
        Assert.Contains(d, x => x.Kind == DeviceClass.NetworkAdapter && x.Primary.InstanceId == nic.InstanceId);
        Assert.Contains(d, x => x.Kind == DeviceClass.WiFi && x.Primary.InstanceId == wifi.InstanceId);
        Assert.Contains(d, x => x.Kind == DeviceClass.Modem && x.Primary.InstanceId == modem.InstanceId);
        Assert.Equal(["Adaptor de rețea USB", "Adaptor Wi-Fi USB", "Modem USB"], new[] { DeviceClass.NetworkAdapter, DeviceClass.WiFi, DeviceClass.Modem }.Select(DeviceMonitorRules.ClassText));

        Assert.Empty(DeviceScope.Classify([
            E(@"PCI\VEN_10EC&DEV_8168\4&22222222&0&00E4", "Realtek PCIe GbE Family Controller", "Net", Local),
            E(@"PCI\VEN_8086&DEV_2723\4&11111111&0&00E0", "Intel(R) Wi-Fi 6 AX200 160MHz", "Net", Local),
            E(@"ROOT\VMS_MP\0000", "Hyper-V Virtual Ethernet Adapter", "Net", Local),
            E(@"ROOT\NET\0001", "TAP-Windows Adapter V9", "Net", Local),
            E(@"SWD\IP_TUNNEL_VBUS\ISATAP_0", "Microsoft ISATAP Adapter", "Net", Local),
            E(@"ROOT\NET\0002", "WAN Miniport (L2TP)", "Net", Local),
            E(@"ROOT\NET\0003", "Fake VPN Virtual Adapter", "Net", Local),
            E(@"BTH\MS_BTHPAN\6&1A2B3C4D&0&2", "Bluetooth Device (Personal Area Network)", "Net", Local)]));
    }

    [Fact]
    public void A_bluetooth_device_is_one_row_and_the_radio_adapter_itself_is_not_in_scope()
    {
        var dev = E(@"BTHENUM\DEV_001122334455\8&1A2B3C4D&0&BLUETOOTHDEVICE_001122334455", "Fake Headset", "Bluetooth", C9);
        var service = E(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&200E\8&1A2B3C4D&0&001122334455_C00000000", "Fake Headset Stereo", "MEDIA", C9);
        var radio = E(@"USB\VID_8087&PID_0AAA\5&55555555&0&10", "Intel(R) Wireless Bluetooth(R)", "Bluetooth", Local);
        var enumerator = E(@"BTH\MS_BTHBRB\6&1A2B3C4D&0&1", "Microsoft Bluetooth Enumerator", "Bluetooth", Local);
        var d = DeviceScope.Classify([dev, service, radio, enumerator]);
        var one = Assert.Single(d);
        Assert.Equal(DeviceClass.Bluetooth, one.Kind);
        Assert.Equal("Dispozitiv Bluetooth", DeviceMonitorRules.ClassText(one.Kind));
    }

    [Fact]
    public void An_optical_drive_is_in_scope_and_a_virtual_dvd_is_not()
    {
        var dvd = E(@"SCSI\CDROM&VEN_FAKE&PROD_DVD-RW\4&66666666&0&000100", "Fake DVD-RW", "CDROM", Local);
        Assert.Equal(DeviceClass.Optical, Single([dvd], DeviceClass.Optical).Kind);
        Assert.Empty(DeviceScope.Classify([E(@"SCSI\CDROM&VEN_MSFT&PROD_VIRTUAL_DVD-ROM\2&1F4ADFCF&0&000002", "Microsoft Virtual DVD-ROM", "CDROM", Local, "SCSI\\CDROM&VEN_MSFT&PROD_VIRTUAL_DVD-ROM")]));
        Assert.Equal("Unitate optică", DeviceMonitorRules.ClassText(DeviceClass.Optical));
    }

    [Fact]
    public void An_external_keyboard_and_mouse_collapse_their_hid_children_and_internal_input_devices_are_out()
    {
        var parent = E(@"USB\VID_046D&PID_C534\FAKERECV0001", "USB Composite Device", "USB", C1);
        var kb = E(@"HID\VID_046D&PID_C534&MI_00\7&2B3C4D5E&0&0000", "HID Keyboard Device", "Keyboard", C1);
        var ms = E(@"HID\VID_046D&PID_C534&MI_01\7&2B3C4D5E&0&0001", "HID-compliant mouse", "Mouse", C1);
        var consumer = E(@"HID\VID_046D&PID_C534&MI_01&COL01\8&1A2B3C4D&0&0000", "HID-compliant consumer control device", "HIDClass", C1);
        var vendor = E(@"HID\VID_046D&PID_C534&MI_02\7&2B3C4D5E&0&0002", "USB Input Device", "HIDClass", C1);
        var combo = DeviceScope.Classify([parent, kb, ms, consumer, vendor]);
        var one = Assert.Single(combo);
        Assert.Equal(DeviceClass.Keyboard, one.Kind);                                       // keyboard + mouse of one receiver: one row
        Assert.Equal(parent.InstanceId, one.IdentityEntry.InstanceId);

        var mouseOnly = DeviceScope.Classify([E(@"HID\VID_046D&PID_C077\7&1A2B3C4D&0&0000", "HID-compliant mouse", "Mouse", C2)]);
        Assert.Equal(DeviceClass.Mouse, Assert.Single(mouseOnly).Kind);
        Assert.Equal(["Tastatură externă", "Mouse extern"], new[] { DeviceClass.Keyboard, DeviceClass.Mouse }.Select(DeviceMonitorRules.ClassText));

        Assert.Empty(DeviceScope.Classify([
            E(@"ACPI\PNP0303\4&1A2B3C4D&0", "Standard PS/2 Keyboard", "Keyboard", Local),
            E(@"HID\ELAN0001&COL01\5&3A4B5C6D&0&0000", "HID-compliant touch pad", "Mouse", Local),
            E(@"HID\VID_0C45&PID_6A10&MI_00\7&1A2B3C4D&0&0000", "Integrated Keyboard", "Keyboard", Local)]));
    }

    [Fact]
    public void A_smart_card_reader_is_in_scope()
    {
        var sc = E(@"USB\VID_04E6&PID_5790\FAKESC000001", "Fake USB Smart Card Reader", "SmartCardReader", C7);
        Assert.Equal(DeviceClass.SmartCardReader, Single([sc], DeviceClass.SmartCardReader).Kind);
        Assert.Equal("Cititor de carduri smart", DeviceMonitorRules.ClassText(DeviceClass.SmartCardReader));
        Assert.Empty(DeviceScope.Classify([E(@"USB\VID_04E6&PID_5790\5&66666666&0&2", "Integrated Smart Card Reader", "SmartCardReader", Local)]));
    }

    // ---- out of scope ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Hubs_host_controllers_and_composite_parents_are_out_of_scope()
    {
        Assert.Empty(DeviceScope.Classify([
            E(@"USB\ROOT_HUB30\4&1A2B3C4D&0&0", "USB Root Hub (USB 3.0)", "USB", Local),
            E(@"USB\VID_05E3&PID_0610\FAKEHUB0001", "Generic USB Hub", "USB", C1),
            E(@"PCI\VEN_8086&DEV_A36D\3&11583659&0&A0", "USB xHCI Compliant Host Controller", "USB", Local),
            E(@"USB\VID_04E8&PID_6860\FAKEPHONE01", "USB Composite Device", "USB", C2)]));
    }

    [Fact]
    public void Software_virtual_acpi_and_system_devices_are_out_of_scope()
    {
        Assert.Empty(DeviceScope.Classify([
            E(@"ROOT\SYSTEM\0001", "Microsoft Hyper-V Virtualization Infrastructure Driver", "System", Local),
            E(@"SWD\MMDEVAPI\{0.0.0.00000000}.{FAKE}", "Speakers (Fake Audio)", "AudioEndpoint", Local),
            E(@"SWD\DRIVERENUM\1&1A2B3C4D&0", "Fake Software Component", "SoftwareComponent", Local),
            E(@"ACPI\PNP0C0A\1", "Microsoft ACPI-Compliant Control Method Battery", "Battery", Local),
            E(@"ACPI\GenuineIntel_-_Fake\_0", "Fake Processor", "Processor", Local),
            E(@"HDAUDIO\FUNC_01&VEN_10EC\4&55555555&0&0001", "Realtek High Definition Audio", "MEDIA", Local),
            E(@"STORAGE\VOLUME\{FAKE-VOLUME}", "Volume", "Volume", Local)]));
    }

    [Fact]
    public void Internal_and_virtual_disks_are_out_of_scope()
    {
        Assert.Empty(DeviceScope.Classify([
            E(@"SCSI\Disk&Ven_NVMe&Prod_Fake_SSD_1TB\4&77777777&0&000000", "NVMe Fake SSD 1TB", "DiskDrive", Local),
            E(@"SCSI\Disk&Ven_Fake&Prod_SATA_HDD\4&88888888&0&010000", "Fake SATA HDD", "DiskDrive", Local),
            E(@"SCSI\Disk&Ven_Msft&Prod_Virtual_Disk\2&1F4ADFCF&0&000001", "Microsoft Virtual Disk", "DiskDrive", Local, @"SCSI\DiskMsft____Virtual_Disk____1.0")]));
    }

    [Fact]
    public void Without_a_container_id_the_bus_of_the_instance_id_decides()
    {
        // fixtures and old snapshots carry no container id: USB, Bluetooth and USB-HID nodes are external, ACPI / PCI / ROOT are not
        var usbMouse = E(@"HID\VID_046D&PID_C077\7&1A2B3C4D&0&0000", "HID-compliant mouse", "Mouse", "");
        var ps2 = E(@"ACPI\PNP0F13\4&1A2B3C4D&0", "PS/2 Compatible Mouse", "Mouse", "");
        var pciNic = E(@"PCI\VEN_10EC&DEV_8168\4&22222222&0&00E4", "Realtek PCIe GbE Family Controller", "Net", "");
        var d = DeviceScope.Classify([usbMouse, ps2, pciNic]);
        Assert.Equal(DeviceClass.Mouse, Assert.Single(d).Kind);
    }

    [Fact]
    public void The_scope_predicate_is_the_same_for_a_class_and_for_a_journal_row()
    {
        foreach (var c in Enum.GetValues<DeviceClass>())
        {
            bool expected = c is not (DeviceClass.Hid or DeviceClass.Audio or DeviceClass.Controller or DeviceClass.InternalDisk or DeviceClass.VirtualDisk or DeviceClass.Internal or DeviceClass.Other);
            Assert.Equal(expected, DeviceScope.IsInScope(c));
            Assert.Equal(expected, DeviceScope.IsInScope(new DeviceJournalEntry { Identity = "x", Class = c }));
        }
        Assert.Equal(17, Enum.GetValues<DeviceClass>().Count(DeviceScope.IsInScope));
    }

    [Fact]
    public void Journal_and_report_list_exactly_the_in_scope_devices()
    {
        var j = new DeviceJournal();
        var t = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        j.Devices.Add(new DeviceJournalEntry { Identity = "a", Class = DeviceClass.UsbStorage, Name = "Fake Stick", Present = true, FirstSeenUtc = t });
        j.Devices.Add(new DeviceJournalEntry { Identity = "b", Class = DeviceClass.Internal, Name = "Old internal row", Present = true, FirstSeenUtc = t });
        j.Devices.Add(new DeviceJournalEntry { Identity = "c", Class = DeviceClass.Controller, Name = "Old hub row", Present = true, FirstSeenUtc = t });
        var s = DeviceMonitorReport.Build(j, t.AddDays(-1), t.AddDays(1), isolated: true);
        Assert.Equal("Fake Stick", Assert.Single(s.Devices).Name);
    }
}
