using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Phase R2: licence format LA2 (module, capabilities, holder). One test per probe P1..P9 of the threat model; keys are ephemeral, in memory.</summary>
public sealed class LicenseV2Tests : IDisposable
{
    private const string Hwid = "ABCD1234EFAB5678";
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Expiry = new(2027, 11, 4);
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-lic2-" + Guid.NewGuid().ToString("N"));

    public LicenseV2Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _key.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Pub => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    private LicenseService Edition(StationPosture p) => new([Pub], null, null, p);
    private static readonly StationPosture[] Postures = [StationPosture.Standalone, StationPosture.IsolatedNetwork, StationPosture.Internet];
    private static string Id(StationPosture p) => LicenseFeatures.ModuleId(p);

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Unb64(string s) { var t = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(t + new string('=', (4 - t.Length % 4) % 4)); }

    /// <summary>Signs arbitrary payload text as an LA2 licence (what a buggy or hostile issuer holding the key could do).</summary>
    private string SignRaw(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return "LA2." + B64(bytes) + "." + B64(_key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    private static string Json(string mod = "insula", string feat = "[\"control.station\",\"case.local\"]", string holder = "Unitatea X", string hw = Hwid,
        string exp = "2027-11-04", string product = "LogAnalyzer", string v = "2", string id = "ID1") =>
        $"{{\"v\":{v},\"id\":\"{id}\",\"hw\":\"{hw}\",\"exp\":\"{exp}\",\"product\":\"{product}\",\"mod\":\"{mod}\",\"feat\":{feat},\"holder\":\"{holder}\"}}";

    private string Issue(string module, string holder = "Unitatea X", IEnumerable<string>? feats = null) =>
        LicenseService.SignV2(_key, Hwid, Expiry, module, holder, feats, "LIC0001");

    // ---- happy path ----

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void Valid_la2_for_the_compiled_module_exposes_module_features_holder(StationPosture p)
    {
        var lic = Issue(Id(p), "Unitatea X");
        Assert.StartsWith("LA2.", lic, StringComparison.Ordinal);
        Assert.Equal(LicenseCheck.Valid, Edition(p).Check(Hwid, lic, Now, out var c));
        Assert.False(c!.IsLegacy);
        Assert.Equal(Id(p), c.Module);
        Assert.Equal("Unitatea X", c.Holder);
        Assert.Equal("LIC0001", c.LicenseId);
        Assert.Equal(Expiry, c.ExpiresUtcDate);
        Assert.Equal(LicenseFeatures.DefaultFor(Id(p)), c.Features);
        Assert.All(LicenseFeatures.DefaultFor(Id(p)), f => Assert.True(c.Has(f)));
    }

    [Fact]
    public void Catalogue_and_module_base_sets_are_as_specified()
    {
        Assert.Equal(["control.station", "case.local", "report.pv", "registers", "collect.lan", "auth.ad", "intel.online", "update.online", "response.host"], LicenseFeatures.All);
        Assert.Equal(["control.station", "case.local", "report.pv", "registers"], LicenseFeatures.DefaultFor("insula"));
        Assert.Equal(["control.station", "case.local", "report.pv", "registers", "collect.lan", "auth.ad", "response.host"], LicenseFeatures.DefaultFor("arhipelag"));
        Assert.Equal(LicenseFeatures.All, LicenseFeatures.DefaultFor("continent"));
        Assert.Equal("insula", LicenseFeatures.ModuleId(StationPosture.Standalone));
        Assert.Equal("arhipelag", LicenseFeatures.ModuleId(StationPosture.IsolatedNetwork));
        Assert.Equal("continent", LicenseFeatures.ModuleId(StationPosture.Internet));
    }

    [Fact]
    public void A_licence_may_carry_a_subset_of_the_module_base_set()
    {
        var lic = Issue("arhipelag", "X", ["case.local", "auth.ad"]);
        Assert.Equal(LicenseCheck.Valid, Edition(StationPosture.IsolatedNetwork).Check(Hwid, lic, Now, out var c));
        Assert.True(c!.Has("auth.ad"));
        Assert.False(c.Has("report.pv"));
    }

    // ---- P1 ----

    [Theory]
    [InlineData("\"mod\":\"insula\"", "\"mod\":\"continent\"")]
    [InlineData("\"feat\":[\"control.station\",\"case.local\",\"report.pv\",\"registers\"]", "\"feat\":[\"control.station\",\"case.local\",\"report.pv\",\"registers\",\"intel.online\"]")]
    public void P1_changing_mod_or_feat_without_resigning_is_BadSignature(string from, string to)
    {
        var parts = Issue("insula").Split('.');
        var json = Encoding.UTF8.GetString(Unb64(parts[1]));
        Assert.Contains(from, json, StringComparison.Ordinal);
        var tampered = $"LA2.{B64(Encoding.UTF8.GetBytes(json.Replace(from, to, StringComparison.Ordinal)))}.{parts[2]}";
        foreach (var p in Postures) Assert.Equal(LicenseCheck.BadSignature, Edition(p).Check(Hwid, tampered, Now, out _));
    }

    [Fact]
    public void P1_a_signed_la1_cannot_be_relabelled_as_la2()
    {
        var la1 = LicenseService.Sign(_key, Hwid, Expiry, "X");
        var relabelled = "LA2" + la1[3..];
        Assert.NotEqual(LicenseCheck.Valid, Edition(StationPosture.Standalone).Check(Hwid, relabelled, Now, out _));
    }

    // ---- P2 ----

    [Fact]
    public void P2_licence_signed_for_another_module_is_WrongModule_for_every_pair()
    {
        foreach (var lic in Postures)
            foreach (var exe in Postures.Where(e => e != lic))
            {
                var r = Edition(exe).Check(Hwid, Issue(Id(lic)), Now, out var c);
                Assert.Equal(LicenseCheck.WrongModule, r);
                Assert.Equal(Id(lic), c!.Module);
            }
    }

    [Fact]
    public void P2_wrong_module_message_names_both_modules()
    {
        var exe = StationPosture.Standalone;
        var r = Edition(exe).Check(Hwid, Issue("continent"), Now, out var c);
        Assert.Equal("Licența este pentru modulul Continent; acest program este modulul Insula.", LicenseService.Explain(r, c, exe));
    }

    // ---- P3 ----

    [Theory]
    [InlineData("insula", "intel.online")]       // known, not allowed for the module
    [InlineData("insula", "collect.lan")]
    [InlineData("arhipelag", "update.online")]
    [InlineData("arhipelag", "intel.online")]
    public void P3_capability_not_allowed_for_the_module_is_refused(string module, string feature)
    {
        var lic = SignRaw(Json(module, $"[\"control.station\",\"{feature}\"]"));
        var p = LicenseFeatures.PostureOf(module)!.Value;
        var r = Edition(p).Check(Hwid, lic, Now, out _);
        Assert.Equal(LicenseCheck.FeatureNotAllowed, r);
        Assert.NotEqual(LicenseCheck.Valid, r);
        Assert.False(string.IsNullOrWhiteSpace(LicenseService.Explain(r)));
    }

    [Theory]
    [InlineData("[\"control.station\",\"root.shell\"]")]   // unknown
    [InlineData("[\"Control.Station\"]")]                    // case differs from the catalogue
    [InlineData("[]")]                                       // nothing
    [InlineData("[\"case.local\",\"case.local\"]")]          // duplicate
    [InlineData("[1]")]                                      // wrong type
    public void P3_unknown_empty_duplicate_or_mistyped_capabilities_are_refused(string feat)
    {
        var r = Edition(StationPosture.Internet).Check(Hwid, SignRaw(Json("continent", feat)), Now, out _);
        Assert.NotEqual(LicenseCheck.Valid, r);
        Assert.Equal(LicenseCheck.Malformed, r);
    }

    [Fact]
    public void P3_issuing_refuses_what_the_verifier_would_refuse()
    {
        Assert.Throws<ArgumentException>(() => LicenseService.SignV2(_key, Hwid, Expiry, "insula", "X", ["intel.online"]));
        Assert.Throws<ArgumentException>(() => LicenseService.SignV2(_key, Hwid, Expiry, "continent", "X", ["nope"]));
        Assert.Throws<ArgumentException>(() => LicenseService.SignV2(_key, Hwid, Expiry, "galaxie", "X"));
    }

    // ---- P4 ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void P4_empty_holder_is_refused(string holder)
    {
        Assert.NotEqual(LicenseCheck.Valid, Edition(StationPosture.Standalone).Check(Hwid, SignRaw(Json(holder: holder)), Now, out _));
        Assert.Throws<ArgumentException>(() => LicenseService.SignV2(_key, Hwid, Expiry, "insula", holder));
    }

    [Fact]
    public void P4_holder_of_121_characters_is_refused_and_120_is_accepted()
    {
        var ed = Edition(StationPosture.Standalone);
        Assert.Equal(LicenseCheck.Malformed, ed.Check(Hwid, SignRaw(Json(holder: new string('a', 121))), Now, out _));
        Assert.Equal(LicenseCheck.Valid, ed.Check(Hwid, SignRaw(Json(holder: new string('a', 120))), Now, out _));
        Assert.Throws<ArgumentException>(() => LicenseService.SignV2(_key, Hwid, Expiry, "insula", new string('a', 121)));
    }

    // ---- P5 ----

    [Theory]
    [InlineData(StationPosture.Standalone)]
    [InlineData(StationPosture.IsolatedNetwork)]
    [InlineData(StationPosture.Internet)]
    public void P5_existing_la1_stays_valid_in_every_module_as_legacy_with_the_module_base_set(StationPosture p)
    {
        var la1 = LicenseService.Sign(_key, Hwid, Expiry, "LEG1");
        Assert.StartsWith("LA1.", la1, StringComparison.Ordinal);
        Assert.Equal(LicenseCheck.Valid, Edition(p).Check(Hwid, la1, Now, out var c));
        Assert.True(c!.IsLegacy);
        Assert.Equal(Id(p), c.Module);
        Assert.Equal(LicenseFeatures.DefaultFor(Id(p)), c.Features);
        Assert.Equal("", c.Holder);
    }

    [Fact]
    public void P5_default_constructor_signature_and_la1_payload_are_unchanged()
    {
        var la1 = LicenseService.Sign(_key, Hwid, Expiry, "LEG1");
        var json = Encoding.UTF8.GetString(Unb64(la1.Split('.')[1]));
        Assert.Equal("{\"v\":1,\"id\":\"LEG1\",\"hw\":\"" + Hwid + "\",\"exp\":\"2027-11-04\",\"product\":\"LogAnalyzer\"}", json);
        Assert.Equal(LicenseCheck.Valid, new LicenseService([Pub]).Check(Hwid, la1, Now, out _));
    }

    // ---- P6 ----

    public static IEnumerable<object[]> MalformedPayloads() =>
    [
        ["not json"],
        ["{"],
        ["[]"],
        ["null"],
        ["{\"v\":2}"],
        [Json(v: "\"2\"")],                                                       // wrong type
        [Json(v: "3")],                                                           // wrong version
        [Json().Replace("\"holder\":\"Unitatea X\"", "\"holder\":5")],
        [Json().Replace("\"mod\":\"insula\"", "\"mod\":[\"insula\"]")],
        [Json().Replace("\"feat\":[\"control.station\",\"case.local\"]", "\"feat\":\"control.station\"")],
        [Json() + " x"],                                                          // trailing content
        [Json().Replace("\"v\":2,", "\"v\":2,\"v\":2,")],                         // duplicated key
        [Json().Replace("\"mod\":\"insula\"", "\"mod\":\"insula\",\"mod\":\"continent\"")],
        [Json().Replace("\"holder\":", "\"extra\":1,\"holder\":")],               // unknown key
        [Json(mod: "galaxie")],
        [Json(exp: "04.11.2027")],
        [Json().Replace("\"id\":\"ID1\",", "")],                                  // missing key
        [Json().Replace("{\"v\":2,", "{/*c*/\"v\":2,")],                         // comment (review of R2)
        [Json().Replace("\"mod\":", "\"Mod\":")],                                 // case-variant key
        [Json().Replace("\"holder\":", "\"HOLDER\":")],
        [Json().Replace("\"exp\":\"2027-11-04\"", "\"exp\":20271104")],          // number instead of a date string
        [Json().Replace("\"hw\":\"" + Hwid + "\"", "\"hw\":1234567890")],
    ];

    [Theory]
    [MemberData(nameof(MalformedPayloads))]
    public void P6_malformed_payloads_are_Malformed_without_an_exception(string json)
    {
        var r = Edition(StationPosture.Standalone).Check(Hwid, SignRaw(json), Now, out _);
        Assert.Equal(LicenseCheck.Malformed, r);
    }

    [Fact]
    public void P6_payload_over_8_KB_is_Malformed_even_when_correctly_signed()
    {
        // review of R2: the size limit is the only rule a long id meets (the holder is limited to 120 characters by its own rule)
        var big = Json().Replace("\"id\":\"ID1\"", "\"id\":\"" + new string('a', 9000) + "\"");
        Assert.True(Encoding.UTF8.GetByteCount(big) > 8192);
        Assert.Equal(LicenseCheck.Malformed, Edition(StationPosture.Standalone).Check(Hwid, SignRaw(big), Now, out _));
        var huge = SignRaw(Json().Replace("\"id\":\"ID1\"", "\"id\":\"" + new string('a', 200_000) + "\""));
        Assert.Equal(LicenseCheck.Malformed, Edition(StationPosture.Standalone).Check(Hwid, huge, Now, out _));
    }

    [Theory]
    [InlineData("LA2")]
    [InlineData("LA2.")]
    [InlineData("LA2..")]
    [InlineData("LA2.!!!.???")]
    [InlineData("LA3.AAAA.AAAA")]
    [InlineData("LA2.AAAA.AAAA.AAAA")]
    public void P6_garbage_la2_text_is_Malformed_or_BadSignature_never_an_exception(string text)
    {
        var r = Edition(StationPosture.Standalone).Check(Hwid, text, Now, out _);
        Assert.True(r is LicenseCheck.Malformed or LicenseCheck.BadSignature, r.ToString());
    }

    [Fact]
    public void P6_deeply_nested_payload_does_not_throw()
    {
        // review of R2: the nesting is inside a known key, so the depth limit is what refuses it (an unknown key would be refused first)
        var nested = Json().Replace("\"feat\":[\"control.station\",\"case.local\"]", "\"feat\":" + new string('[', 500) + new string(']', 500));
        Assert.Equal(LicenseCheck.Malformed, Edition(StationPosture.Standalone).Check(Hwid, SignRaw(nested), Now, out _));
    }

    // ---- P7 ----

    [Fact]
    public void P7_request_file_has_exactly_the_four_keys_and_no_machine_or_user_name()
    {
        var text = LicenseRequest.Build(Hwid, "arhipelag", Now);
        using var doc = JsonDocument.Parse(text);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "hw", "module", "product", "requestedUtc" }, keys);
        Assert.Equal("LogAnalyzer", doc.RootElement.GetProperty("product").GetString());
        Assert.Equal("arhipelag", doc.RootElement.GetProperty("module").GetString());
        Assert.Equal(Hwid, doc.RootElement.GetProperty("hw").GetString());
        Assert.Equal("2026-11-04T12:00:00Z", doc.RootElement.GetProperty("requestedUtc").GetString());
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("cerere_licenta_" + Hwid + ".json", LicenseRequest.FileName(Hwid.ToLowerInvariant()));
    }

    [Fact]
    public void P7_request_round_trips_and_the_parser_refuses_extra_missing_or_duplicate_keys()
    {
        var ok = LicenseRequest.Parse(LicenseRequest.Build(Hwid, "continent", Now));
        Assert.Equal(new LicenseRequest.Content("LogAnalyzer", "continent", Hwid, "2026-11-04T12:00:00Z"), ok);
        const string b = "\"product\":\"LogAnalyzer\",\"module\":\"insula\",\"hw\":\"" + Hwid + "\",\"requestedUtc\":\"x\"";
        Assert.NotNull(LicenseRequest.Parse("{" + b + "}"));
        Assert.Null(LicenseRequest.Parse("{" + b + ",\"user\":\"u\"}"));
        Assert.Null(LicenseRequest.Parse("{\"product\":\"LogAnalyzer\",\"module\":\"insula\",\"hw\":\"" + Hwid + "\"}"));
        Assert.Null(LicenseRequest.Parse("{" + b + ",\"hw\":\"" + Hwid + "\"}"));
        Assert.Null(LicenseRequest.Parse("{" + b.Replace("insula", "galaxie") + "}"));
        Assert.Null(LicenseRequest.Parse("{" + b.Replace(Hwid, "XYZ") + "}"));
        Assert.Null(LicenseRequest.Parse("nu e json"));
        Assert.Null(LicenseRequest.Parse(new string(' ', 5000) + "{" + b + "}"));
    }

    // ---- P8 ----

    [Fact]
    public void P8_expired_and_wrong_hardware_follow_the_la1_order()
    {
        var ed = Edition(StationPosture.Standalone);
        var lic = Issue("insula");
        Assert.Equal(LicenseCheck.Valid, ed.Check(Hwid, lic, Expiry.AddDays(1).AddTicks(-1), out _));
        Assert.Equal(LicenseCheck.Expired, ed.Check(Hwid, lic, Expiry.AddDays(1), out _));
        Assert.Equal(LicenseCheck.WrongHardware, ed.Check("0000000000000000", lic, Now, out _));
        // wrong hardware wins over expired (LA1 order), and both win over a module mismatch
        Assert.Equal(LicenseCheck.WrongHardware, ed.Check("0000000000000000", lic, Expiry.AddDays(5), out _));
        var other = Issue("continent");
        Assert.Equal(LicenseCheck.Expired, ed.Check(Hwid, other, Expiry.AddDays(5), out _));
        Assert.Equal(LicenseCheck.WrongHardware, ed.Check("0000000000000000", other, Now, out _));
        Assert.Equal(LicenseCheck.WrongProduct, ed.Check(Hwid, SignRaw(Json(product: "Other")), Now, out _));
    }

    [Fact]
    public void Another_key_does_not_vouch_for_an_la2()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ed = new LicenseService([Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())], null, null, StationPosture.Standalone);
        Assert.Equal(LicenseCheck.BadSignature, ed.Check(Hwid, Issue("insula"), Now, out _));
    }

    // ---- saved licence / Has ----

    [Fact]
    public void Saved_licence_is_exposed_through_CurrentLicense_and_Has_and_a_module_mismatch_is_not_saved()
    {
        var path = Path.Combine(_dir, "license.lic");
        var ed = new LicenseService([Pub], path, () => Hwid, StationPosture.IsolatedNetwork);
        Assert.Null(ed.CurrentLicense());
        Assert.False(ed.Has("auth.ad"));
        Assert.False(ed.ValidateAndSaveKey(Issue("continent"), out var r));
        Assert.Equal(LicenseCheck.WrongModule, r);
        Assert.False(File.Exists(path));
        Assert.True(ed.ValidateAndSaveKey(Issue("arhipelag", "Unitatea Y", ["case.local", "auth.ad"]), out r));
        Assert.Equal(LicenseCheck.Valid, r);
        Assert.Equal("Unitatea Y", ed.CurrentLicense()!.Holder);
        Assert.True(ed.Has("auth.ad"));
        Assert.False(ed.Has("response.host"));
        Assert.True(ed.IsActivated());
    }

    // ---- KeyGen CLI ----

    private (string Priv, string Pub) CliKey()
    {
        var priv = Path.Combine(_dir, "k.key");
        var pub = Path.Combine(_dir, "k.pub");
        Assert.Equal(0, LicenseCli.Keygen(["license-keygen", priv, pub], new StringWriter()));
        return (priv, File.ReadAllText(pub).Trim());
    }

    private static string Future => DateTime.UtcNow.Date.AddYears(1).ToString("yyyy-MM-dd");

    [Fact]
    public void Cli_with_module_issues_an_la2_the_module_accepts_and_without_module_still_issues_la1()
    {
        var (priv, pub) = CliKey();
        var outFile = Path.Combine(_dir, "l2.lic");
        Assert.Equal(0, LicenseCli.Sign(["license-sign", priv, "--hardware-id", Hwid, "--expires", Future, "--module", "arhipelag", "--holder", "Unitatea Z",
            "--feature", "case.local", "--feature", "auth.ad", "--out", outFile], new StringWriter()));
        var lic = File.ReadAllText(outFile).Trim();
        Assert.StartsWith("LA2.", lic, StringComparison.Ordinal);
        Assert.Equal(LicenseCheck.Valid, new LicenseService([pub], null, null, StationPosture.IsolatedNetwork).Check(Hwid, lic, DateTime.UtcNow, out var c));
        Assert.Equal(["case.local", "auth.ad"], c!.Features);
        Assert.Equal("Unitatea Z", c.Holder);
        Assert.Equal(LicenseCheck.WrongModule, new LicenseService([pub], null, null, StationPosture.Standalone).Check(Hwid, lic, DateTime.UtcNow, out _));

        var out1 = Path.Combine(_dir, "l1.lic");
        Assert.Equal(0, LicenseCli.Sign(["license-sign", priv, "--hardware-id", Hwid, "--expires", Future, "--out", out1], new StringWriter()));
        Assert.StartsWith("LA1.", File.ReadAllText(out1), StringComparison.Ordinal);

        // no --feature = the base set of the module
        var out3 = Path.Combine(_dir, "l3.lic");
        Assert.Equal(0, LicenseCli.Sign(["license-sign", priv, "--hardware-id", Hwid, "--expires", Future, "--module", "insula", "--holder", "H", "--out", out3], new StringWriter()));
        new LicenseService([pub], null, null, StationPosture.Standalone).Check(Hwid, File.ReadAllText(out3), DateTime.UtcNow, out var c3);
        Assert.Equal(LicenseFeatures.DefaultFor("insula"), c3!.Features);
    }

    [Theory]
    [InlineData("--module", "insula", "--feature", "intel.online", "--holder", "H")]     // not allowed
    [InlineData("--module", "insula", "--feature", "nope", "--holder", "H")]             // unknown
    [InlineData("--module", "galaxie", "--holder", "H")]                                  // unknown module
    [InlineData("--module", "insula")]                                                    // holder missing
    [InlineData("--holder", "H")]                                                         // holder without module
    [InlineData("--feature", "case.local")]                                               // feature without module
    public void Cli_refuses_bad_la2_arguments_and_prints_no_licence(params string[] extra)
    {
        var (priv, _) = CliKey();
        var o = new StringWriter();
        Assert.Equal(1, LicenseCli.Sign(["license-sign", priv, "--hardware-id", Hwid, "--expires", Future, .. extra], o));
        Assert.DoesNotContain("LA2.", o.ToString(), StringComparison.Ordinal);
    }

    // ---- F: messages ----

    [Fact]
    public void New_results_have_their_own_romanian_explanations()
    {
        var generic = LicenseService.Explain((LicenseCheck)999);
        foreach (var r in new[] { LicenseCheck.WrongModule, LicenseCheck.FeatureNotAllowed })
        {
            var t = LicenseService.Explain(r);
            Assert.NotEqual(generic, t);
            Assert.Contains("Licența", t, StringComparison.Ordinal);
        }
    }

    // ---- P9 ----

    [Fact]
    public void P9_new_licence_code_opens_no_network_connection()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "LogAnalyzer.Core", "Services"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        Assert.NotNull(root);
        var files = new[]
        {
            "LogAnalyzer.Core/Services/LicenseService.cs", "LogAnalyzer.Core/Services/LicenseFeatures.cs", "LogAnalyzer.Core/Services/LicenseRequest.cs",
            "LogAnalyzer.KeyGen/LicenseCli.cs", "LogAnalyzer.LicenseManager/MainWindow.xaml.cs", "LogAnalyzer.App/Views/ActivationWindow.xaml.cs",
        };
        var banned = new[] { "HttpClient", "Socket", "WebRequest", "WebClient", "TcpClient", "UdpClient", "System.Net.Http", "System.Net.Sockets", "NamedPipe", "Process.Start" };
        foreach (var f in files)
        {
            var text = File.ReadAllText(Path.Combine(root!, f.Replace('/', Path.DirectorySeparatorChar)));
            // LicenseManager opens Explorer on its own ledger folder (pre-existing, local); every other banned token is a failure.
            foreach (var b in banned.Where(b => !(b == "Process.Start" && f.Contains("LicenseManager", StringComparison.Ordinal))))
                Assert.False(text.Contains(b, StringComparison.Ordinal), $"{f} contains {b}");
        }
    }
}
