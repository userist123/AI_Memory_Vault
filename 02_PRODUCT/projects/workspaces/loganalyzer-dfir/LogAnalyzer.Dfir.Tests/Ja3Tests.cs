using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.5: JA3 of a TLS ClientHello (GREASE left out, order kept) and matching against "ja3" indicators.</summary>
public sealed class Ja3Tests
{
    /// <summary>A TLS 1.2 record carrying a ClientHello with GREASE in ciphers, extensions and groups.</summary>
    private static byte[] ClientHello()
    {
        var body = new List<byte>();
        void U16(int v) { body.Add((byte)(v >> 8)); body.Add((byte)v); }
        U16(0x0303);                                      // client version 771
        body.AddRange(new byte[32]);                      // random
        body.Add(0);                                      // session id
        U16(6); U16(0x0A0A); U16(0x1301); U16(0x1302);    // ciphers: GREASE, 4865, 4866
        body.Add(1); body.Add(0);                          // compression: null
        var ext = new List<byte>();
        void E16(int v) { ext.Add((byte)(v >> 8)); ext.Add((byte)v); }
        E16(0x1A1A); E16(0);                              // GREASE extension, empty
        var host = Encoding.ASCII.GetBytes("lab.local");
        E16(0); E16(host.Length + 5); E16(host.Length + 3); ext.Add(0); E16(host.Length); ext.AddRange(host);   // server_name
        E16(10); E16(8); E16(6); E16(0x2A2A); E16(29); E16(23);                                              // supported_groups: GREASE, x25519, secp256r1
        E16(11); E16(2); ext.Add(1); ext.Add(0);                                                             // ec_point_formats: uncompressed
        E16(16); E16(5); E16(3); ext.Add(2); ext.AddRange("h2"u8.ToArray());                                // ALPN
        U16(ext.Count); body.AddRange(ext);
        var hs = new List<byte> { 1, (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)body.Count };
        hs.AddRange(body);
        var rec = new List<byte> { 0x16, 3, 1, (byte)(hs.Count >> 8), (byte)hs.Count };
        rec.AddRange(hs);
        return rec.ToArray();
    }

    [Fact]
    public void Ja3_lists_version_ciphers_extensions_groups_and_point_formats_in_order_without_grease()
    {
        var (ja3, md5) = PacketDecoder.Ja3(ClientHello())!.Value;
        Assert.Equal("771,4865-4866,0-10-11-16,29-23,0", ja3);
        Assert.Equal(Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(ja3))), md5);
        Assert.Equal("lab.local", PacketDecoder.ParseTlsClientHello(ClientHello())!.Value.Sni);   // the existing SNI read is unchanged
    }

    [Fact]
    public void A_truncated_or_non_tls_payload_gives_no_fingerprint()
    {
        var full = ClientHello();
        Assert.Null(PacketDecoder.Ja3(full[..40]));
        Assert.Null(PacketDecoder.Ja3("GET / HTTP/1.1\r\n\r\n"u8.ToArray()));
        for (int cut = 43; cut < full.Length; cut += 7) _ = PacketDecoder.Ja3(full[..cut]);     // never throws
    }

    [Fact]
    public void A_ja3_indicator_matches_the_tls_event_and_says_it_names_a_library_not_a_program()
    {
        var (_, md5) = PacketDecoder.Ja3(ClientHello())!.Value;
        var dir = Path.Combine(Path.GetTempPath(), "ladfir_ja3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var csv = Path.Combine(dir, "ja3.csv");
            File.WriteAllText(csv, $"Type,Value,Context\nja3,{md5.ToUpperInvariant()},client TLS de test\n");
            var m = IocMatcher.LoadCsv(csv);
            var e = new TimelineEvent
            {
                Time = Timestamp.FromUtc(new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc), "", "test"), Source = "Network", EvidenceId = "EV-1", Locator = "frame 7",
                Summary = "TLS ClientHello", Fields = { ["JA3Hash"] = md5 },
            };
            var hit = Assert.Single(m.MatchEvents([e]));
            Assert.Contains("bibliotecă TLS, nu un program anume", hit.Match + " " + hit.Explanation);
        }
        finally { Directory.Delete(dir, true); }
    }
}
