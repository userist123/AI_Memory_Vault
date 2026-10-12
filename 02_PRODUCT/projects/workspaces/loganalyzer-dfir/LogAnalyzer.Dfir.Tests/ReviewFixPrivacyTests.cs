using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Privacy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Regression tests for the independent review of the PII masker: IPv6, device serials, DNS suffixes and every free-text field. Synthetic values only.</summary>
public class ReviewFixPrivacyTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    private static TimelineEvent Row(string summary = "t", string service = "", string host = "", string user = "", string dns = "", string locator = "row=1", params (string K, string V)[] f) => new()
    {
        Time = Timestamp.FromUtc(T0, "", "test"), Source = "EventLog:System", EvidenceId = "EV-1", Summary = summary, Service = service, Host = host, User = user, Dns = dns,
        Locator = locator, Fields = f.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase),
    };

    [Theory]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334")]
    [InlineData("2001:db8:85a3::8a2e:370:7334")]
    [InlineData("2001:db8::1")]
    [InlineData("fe80::1c2b:3d4e:5f60:7182%12")]
    [InlineData("fe80::a00:27ff:fe4e:66a1")]
    public void IPv6_addresses_in_every_notation_are_masked(string address)
    {
        var m = new PiiMasker();
        var s = m.Mask($"Conexiune către {address} pe portul 443");
        Assert.DoesNotContain(address, s);
        Assert.DoesNotContain(address.Split('%')[0], s);
        Assert.Matches(@"IP-\d+", s);
    }

    [Fact]
    public void The_same_IPv6_address_in_full_and_compressed_form_is_one_pseudonym_and_clock_times_and_macs_are_left_alone()
    {
        var m = new PiiMasker();
        var a = m.Mask("2001:0db8:0000:0000:0000:0000:0000:0001");
        var b = m.Mask("2001:db8::1");
        Assert.Equal(a, b);
        Assert.Equal("la 10:23:45 și 00:11:22:33:44:55 și ::1 și [System.Convert]::Add", m.Mask("la 10:23:45 și 00:11:22:33:44:55 și ::1 și [System.Convert]::Add"));
    }

    [Fact]
    public void Device_serials_from_USBSTOR_and_USB_instance_ids_are_masked_consistently()
    {
        var m = new PiiMasker();
        var s1 = m.Mask(@"USBSTOR\Disk&Ven_Vendor&Prod_Stick&Rev_1.00\0123456789ABCDEF&0 a fost conectat");
        var s2 = m.Mask(@"USB\VID_0951&PID_1666\AA00000000000489");
        var s3 = m.Mask(@"USBSTOR\Disk&Ven_Vendor&Prod_Stick&Rev_1.00\0123456789ABCDEF&0");
        Assert.DoesNotContain("0123456789ABCDEF", s1);
        Assert.DoesNotContain("AA00000000000489", s2);
        Assert.StartsWith(@"USB\VID_0951&PID_1666\Serial-", s2);
        Assert.Contains(@"USBSTOR\Disk&Ven_Vendor&Prod_Stick&Rev_1.00\Serial-1", s1);   // vendor / product stay: they are not personal
        Assert.Equal(s1.Split(' ')[0], s3);
    }

    [Fact]
    public void A_free_standing_hex_serial_is_masked_but_hashes_guids_and_short_hex_are_not()
    {
        var m = new PiiMasker();
        const string sha = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        const string guid = "{4d36e972-e325-11ce-bfc1-08002be10318}";
        Assert.DoesNotContain("A1B2C3D4E5F60789", m.Mask("serie A1B2C3D4E5F60789 detectată"));
        Assert.Equal($"sha {sha} {guid} 0x7FFE1234ABCD5678 cod 1A2B", m.Mask($"sha {sha} {guid} 0x7FFE1234ABCD5678 cod 1A2B"));
    }

    [Fact]
    public void A_known_hosts_FQDN_is_masked_whole_and_consistently_with_the_short_name_and_the_domain_is_learned()
    {
        var m = new PiiMasker().KnownHost("wks01.corp.example.com");
        var a = m.Mask("Rezolvare wks01.corp.example.com spre server");
        var b = m.Mask("Gazda wks01 (\\\\WKS01.corp.example.com\\partajare)");
        Assert.DoesNotContain("corp.example.com", a);
        Assert.DoesNotContain("corp.example.com", b);
        var pseudo = m.Host("wks01");
        Assert.Contains(pseudo, a);
        Assert.Contains(pseudo, b);
        // another host under the learned domain and the bare domain
        var c = m.Mask("fileserver.corp.example.com și corp.example.com");
        Assert.DoesNotContain("corp.example.com", c);
        Assert.DoesNotContain("fileserver", c);
        // public domains and file names are not names of the station
        Assert.Contains("microsoft.com", m.Mask("microsoft.com și powershell.exe"));
        Assert.Contains("powershell.exe", m.Mask("microsoft.com și powershell.exe"));
        Assert.Contains("wks01.log", m.Mask("wks01.log").Replace(pseudo, "wks01"));
    }

    [Fact]
    public void The_domain_suffix_of_a_timeline_host_is_learned_from_the_rows()
    {
        var rows = new[] { Row(host: "srv7.dep.example.org", dns: "srv7.dep.example.org") };
        var m = PiiMasker.FromTimeline(rows);
        var masked = m.Mask(rows[0]);
        Assert.DoesNotContain("example.org", masked.Dns);
        Assert.DoesNotContain("example.org", masked.Host);
        Assert.Equal(masked.Host, masked.Dns);
        Assert.DoesNotContain("example.org", m.Mask("alt-server.dep.example.org"));
    }

    [Fact]
    public void Every_free_text_field_is_masked_Service_Summary_Locator_Task_Notes_and_Fields_values()
    {
        var e = Row(summary: "Adresa 2001:db8::5 din 10.1.2.3", service: @"serviciu-utilizator-test pe C:\Users\ionescu\app", locator: @"\\wks09\c$\Users\ionescu\x.evtx", user: "ionescu", host: "wks09",
            f: [("Description", "contul ionescu de pe wks09, serie USB\\VID_0951&PID_1666\\AA00000000000489"), ("SerialNumber", "XYZ123"), ("Anything", "10.9.8.7")]);
        var m = PiiMasker.FromTimeline([e]);
        var x = m.Mask(e);
        foreach (var text in new[] { x.Summary, x.Service, x.Locator, x.Host, x.User, x.Fields["Description"], x.Fields["SerialNumber"], x.Fields["Anything"] })
        {
            Assert.DoesNotContain("ionescu", text);
            Assert.DoesNotContain("wks09", text);
            Assert.DoesNotContain("10.1.2.3", text);
            Assert.DoesNotContain("2001:db8::5", text);
            Assert.DoesNotContain("AA00000000000489", text);
            Assert.DoesNotContain("XYZ123", text);
            Assert.DoesNotContain("10.9.8.7", text);
        }
        Assert.DoesNotContain("ionescu", x.Service);
    }

    [Fact]
    public void The_same_value_gets_the_same_pseudonym_in_the_report_text_and_in_the_CSV()
    {
        var e = Row(summary: "către 2001:db8::77 de la srv7.dep.example.org serie AA00000000000489", host: "srv7.dep.example.org", dns: "srv7.dep.example.org",
            f: [("SerialNumber", "AA00000000000489")]);
        var m = PiiMasker.FromTimeline([e]);
        var reportText = m.Mask("Raport: 2001:db8::77, srv7.dep.example.org, AA00000000000489");
        var csv = Path.Combine(Path.GetTempPath(), "la-pii-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            TimelineCsv.Write(csv, [m.Mask(e)]);
            var text = File.ReadAllText(csv);
            var ip = m.Mask("2001:db8::77"); var host = m.Host("srv7"); var serial = m.Mask("AA00000000000489");
            Assert.Matches(@"^IP-\d+$", ip);
            Assert.Matches(@"^Serial-\d+$", serial);
            Assert.Contains(ip, reportText); Assert.Contains(host, reportText); Assert.Contains(serial, reportText);
            Assert.Contains(ip, text); Assert.Contains(host, text); Assert.Contains(serial, text);
            Assert.DoesNotContain("example.org", text);
            Assert.DoesNotContain("AA00000000000489", text);
            Assert.DoesNotContain("2001:db8::77", text);
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void Masking_is_idempotent_for_the_new_pseudonyms()
    {
        var m = new PiiMasker().KnownHost("a1.corp.example.com");
        var once = m.Mask("2001:db8::9 USB\\VID_0951&PID_1666\\AA00000000000489 a1.corp.example.com corp.example.com");
        Assert.Equal(once, m.Mask(once));
    }
}
