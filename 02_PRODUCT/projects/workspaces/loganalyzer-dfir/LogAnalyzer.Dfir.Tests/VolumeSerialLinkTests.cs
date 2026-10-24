using System.Buffers.Binary;
using System.Text;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// E2: the volume serial read from the volume boot record logged in Partition/Diagnostic 1006 (Vbr0) joins the serial of LNK / Jump List files, so a file
/// link becomes "opened from this medium" (strong). Boot records are synthetic, one per file system.
/// </summary>
public sealed class VolumeSerialLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_vbr_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private const uint Serial = 0xA1B2C3D4;
    private static readonly DateTimeOffset Base = new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);

    // ---- synthetic boot sectors ----

    private static byte[] Sector(string oemAt3 = "", Action<byte[]>? fill = null)
    {
        var b = new byte[512];
        b[510] = 0x55; b[511] = 0xAA;
        if (oemAt3.Length > 0) Encoding.ASCII.GetBytes(oemAt3).CopyTo(b, 3);
        fill?.Invoke(b);
        return b;
    }

    internal static byte[] Ntfs(ulong serial64) => Sector("NTFS    ", b => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x48), serial64));
    internal static byte[] ExFat(uint serial) => Sector("EXFAT   ", b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x64), serial));
    internal static byte[] Fat32(uint serial) => Sector("MSDOS5.0", b => { b[0x42] = 0x29; BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x43), serial); Encoding.ASCII.GetBytes("FAT32   ").CopyTo(b, 0x52); });
    internal static byte[] Fat16(uint serial) => Sector("MSDOS5.0", b => { b[0x26] = 0x29; BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x27), serial); Encoding.ASCII.GetBytes("FAT16   ").CopyTo(b, 0x36); });

    [Fact]
    public void Ntfs_serial_is_the_low_32_bits_of_the_64_bit_serial()
    {
        var v = VolumeBootRecord.TryParse(Ntfs(0x1122334400000000UL | Serial));
        Assert.NotNull(v);
        Assert.Equal(VbrFileSystem.Ntfs, v!.FileSystem);
        Assert.Equal("A1B2C3D4", v.Serial);
        Assert.Equal("NTFS", v.FileSystemName);
    }

    [Fact]
    public void ExFat_serial_is_read_at_offset_0x64()
    {
        var v = VolumeBootRecord.TryParse(ExFat(Serial));
        Assert.Equal(new VolumeBootRecord.Volume(VbrFileSystem.ExFat, "A1B2C3D4"), v);
    }

    [Fact]
    public void Fat32_serial_is_read_at_offset_0x43_after_the_extended_signature()
    {
        var v = VolumeBootRecord.TryParse(Fat32(Serial));
        Assert.Equal(new VolumeBootRecord.Volume(VbrFileSystem.Fat32, "A1B2C3D4"), v);
    }

    [Fact]
    public void Fat16_serial_is_read_at_offset_0x27()
    {
        var v = VolumeBootRecord.TryParse(Fat16(Serial));
        Assert.Equal(new VolumeBootRecord.Volume(VbrFileSystem.Fat16, "A1B2C3D4"), v);
    }

    [Fact]
    public void Low_serial_is_zero_padded_to_eight_digits_like_the_LNK_parser_prints_it()
    {
        Assert.Equal("00000ABC", VolumeBootRecord.TryParse(ExFat(0xABC))!.Serial);
    }

    [Fact]
    public void Sectors_that_are_not_a_recognised_boot_record_give_nothing_instead_of_a_guess()
    {
        Assert.Null(VolumeBootRecord.TryParse(new byte[512]));                              // no boot signature
        Assert.Null(VolumeBootRecord.TryParse(Sector("OTHERFS ")));                         // signature but unknown layout
        Assert.Null(VolumeBootRecord.TryParse(Ntfs(1)[..300]));                             // truncated
        var noExt = Fat32(Serial); noExt[0x42] = 0;                                         // FAT32 label without the extended boot signature
        Assert.Null(VolumeBootRecord.TryParse(noExt));
    }

    [Theory]
    [InlineData("EB5290")]
    [InlineData("EB 52 90")]
    [InlineData("0xEB5290")]
    [InlineData("eb-52-90")]
    public void Logged_hex_is_decoded_whatever_the_separators(string text) => Assert.Equal(new byte[] { 0xEB, 0x52, 0x90 }, VolumeBootRecord.FromHex(text));

    [Theory]
    [InlineData("")]
    [InlineData("EB5")]          // odd number of digits
    [InlineData("EB5G90")]       // not hex
    public void Invalid_hex_is_rejected(string text) => Assert.Null(VolumeBootRecord.FromHex(text));

    [Fact]
    public void An_event_with_Vbr0_to_Vbr3_gives_one_serial_per_recognised_volume()
    {
        var fields = new Dictionary<string, string>
        {
            ["Vbr0"] = Convert.ToHexString(Ntfs(Serial)), ["Vbr1"] = Convert.ToHexString(Fat32(0x01020304)), ["Vbr2"] = "", ["Vbr3"] = "ZZ",
        };
        var volumes = VolumeBootRecord.FromEvent(n => fields.GetValueOrDefault(n, ""));
        Assert.Equal(["A1B2C3D4", "01020304"], volumes.Select(v => v.Serial));
    }

    // ---- join with the LNK / Jump List serial ----

    /// <summary>The pseudo fields "_EventId" and "_Path" set the event's own properties and are not kept as fields.</summary>
    private static TimelineEvent Row(string source, DateTimeOffset t, string locator, params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(t.UtcDateTime, "", "test"), Source = source, EvidenceId = "EV-V", Locator = locator, Summary = source,
        EventId = f.FirstOrDefault(x => x.K == "_EventId").V ?? "", Path = f.FirstOrDefault(x => x.K == "_Path").V ?? "",
        Fields = f.Where(x => !x.K.StartsWith('_')).ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    private static TimelineEvent Partition1006(DateTimeOffset t, string vbr0) => Row("EventLog:Microsoft-Windows-Partition/Diagnostic", t, "r-" + t.Ticks,
        ("SerialNumber", "STICK0001"), ("BusType", "7"), ("Capacity", "32000000000"), ("Manufacturer", "Acme"), ("Model", "Stick"), ("Vbr0", vbr0), ("_EventId", "1006"));

    private static TimelineEvent UsbRow() => Row("USB", Base, "u1", ("Serial", "STICK0001"), ("VidPid", "VID_1234&PID_5678"), ("FriendlyName", "Acme Stick"), ("DriveLetter", "E"));

    private static List<Finding> Usage(params TimelineEvent[] events)
    {
        int n = 0;
        var input = new Wp14Input { Scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Unclassified } };
        return Wp14Rules.Run(events, () => $"F-{++n:D4}", input).Where(f => f.RuleId == "MEDIA-USAGE").ToList();
    }

    private TimelineEvent Lnk(string name, uint serial, int driveType, DateTimeOffset written)
    {
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, name + ".lnk");
        File.WriteAllBytes(p, LnkParserTests.BuildLnk(@"E:\docs\raport.docx", "", "", "ws", serial, driveType));
        File.SetLastWriteTimeUtc(p, written.UtcDateTime);
        var sink = new ListSink();
        new LnkParser().Parse(new EvidenceItem { EvidenceId = "EV-L-" + name, CaseId = "C", Source = "lnk", SourceType = "lnk", StoredPath = p }, p, sink, default);
        return Assert.Single(sink.Events);
    }

    [Theory]
    [InlineData("ntfs")]
    [InlineData("exfat")]
    [InlineData("fat32")]
    public void A_removable_LNK_with_the_serial_of_the_logged_boot_record_is_opened_from_the_medium(string fs)
    {
        var vbr = fs switch { "ntfs" => Ntfs(Serial), "exfat" => ExFat(Serial), _ => Fat32(Serial) };
        var lnk = Lnk("a", Serial, 2, Base.AddMinutes(5));
        Assert.Equal("A1B2C3D4", lnk.Fields["VolumeSerial"]);     // the LNK parser prints the same form as the boot record reader

        var f = Assert.Single(Usage(Partition1006(Base, Convert.ToHexString(vbr)), UsbRow(), lnk));
        Assert.Contains("1 deschise de pe acest suport", f.Description);
        Assert.Contains("A1B2C3D4", f.Description);
        Assert.DoesNotContain("nu este citită din sursele analizate", f.Description);
        Assert.Contains(f.SupportingEvidence, r => r.EvidenceId == "EV-L-a");
    }

    [Fact]
    public void A_Jump_List_entry_with_the_serial_is_a_strong_link_too()
    {
        var jl = Row("JumpList", Base.AddMinutes(3), "dl-1", ("VolumeSerial", "A1B2C3D4"), ("DriveType", "REMOVABLE"), ("LnkTarget", @"E:\docs\plan.xlsx"), ("_Path", @"E:\docs\plan.xlsx"));
        var f = Assert.Single(Usage(Partition1006(Base, Convert.ToHexString(Ntfs(Serial))), UsbRow(), jl));
        Assert.Contains("1 deschise de pe acest suport", f.Description);
    }

    [Fact]
    public void A_link_with_another_serial_stays_a_letter_hint_and_says_the_medium_serial_is_known()
    {
        var lnk = Lnk("b", 0x0BADF00D, 2, Base.AddMinutes(5));
        var f = Assert.Single(Usage(Partition1006(Base, Convert.ToHexString(Ntfs(Serial))), UsbRow(), lnk));
        Assert.Contains("0 deschise de pe acest suport", f.Description);
        Assert.Contains("1 doar aceeași literă", f.Description);
        Assert.Contains("dar nicio scurtătură nu o poartă", f.Description);
    }

    [Fact]
    public void A_fixed_disk_link_with_the_same_serial_is_not_linked_to_the_medium()
    {
        var lnk = Lnk("c", Serial, 3, Base.AddMinutes(5));
        var findings = Usage(Partition1006(Base, Convert.ToHexString(Ntfs(Serial))), UsbRow(), lnk);
        Assert.All(findings, f => Assert.DoesNotContain("1 deschise de pe acest suport", f.Description));
    }

    [Fact]
    public void A_link_written_outside_every_connection_window_is_not_linked_even_with_the_serial()
    {
        var lnk = Lnk("d", Serial, 2, Base.AddDays(-3));
        var findings = Usage(Partition1006(Base, Convert.ToHexString(Ntfs(Serial))), UsbRow(), lnk);
        Assert.All(findings, f => Assert.DoesNotContain("1 deschise de pe acest suport", f.Description));
    }

    [Fact]
    public void Without_a_logged_boot_record_only_the_letter_hint_is_possible_and_the_description_says_why()
    {
        var lnk = Lnk("e", Serial, 2, Base.AddMinutes(5));
        var f = Assert.Single(Usage(Partition1006(Base, ""), UsbRow(), lnk));
        Assert.Contains("0 deschise de pe acest suport", f.Description);
        Assert.Contains("nu este citită din sursele analizate", f.Description);
    }

    [Fact]
    public void A_reformatted_medium_keeps_both_serials_so_links_to_either_volume_count()
    {
        var first = Partition1006(Base, Convert.ToHexString(Fat32(0x11111111)));
        var second = Partition1006(Base.AddHours(2), Convert.ToHexString(ExFat(Serial)));
        var lnk = Lnk("f", 0x11111111, 2, Base.AddMinutes(10));
        var f = Assert.Single(Usage(first, second, UsbRow(), lnk));
        Assert.Contains("11111111", f.Description);
        Assert.Contains("A1B2C3D4", f.Description);
        Assert.Contains("1 deschise de pe acest suport", f.Description);
    }

    [Fact]
    public void Several_volumes_logged_in_one_event_all_become_medium_serials()
    {
        var e = Partition1006(Base, Convert.ToHexString(Ntfs(Serial)));
        e.Fields["Vbr1"] = Convert.ToHexString(Fat32(0x22222222));
        var lnk = Lnk("g", 0x22222222, 2, Base.AddMinutes(5));
        var f = Assert.Single(Usage(e, UsbRow(), lnk));
        Assert.Contains("1 deschise de pe acest suport", f.Description);
    }
}
