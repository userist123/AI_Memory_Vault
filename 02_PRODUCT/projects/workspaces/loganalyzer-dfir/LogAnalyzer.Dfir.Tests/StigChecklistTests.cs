using System.Text.Json;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>The STIG Viewer 3 checklist (.cklb) of the station control: structure, and that no status claims more than the application verified.</summary>
public class StigChecklistTests
{
    private const string Absent = StationFactCollector.Absent;
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 30, 0, TimeSpan.Zero);
    // "not_applicable" is a valid .cklb value but the export never writes it (the application's NU SE APLICĂ is posture, not a STIG NA condition)
    private static readonly string[] Allowed = ["not_a_finding", "open", "not_reviewed"];

    // the rules of the brief, written out independently of the table in StigChecklist
    private static readonly string[] ExpectedVersions =
    [
        "WN11-SO-000145", "WN11-SO-000150", "WN11-SO-000160", "WN11-SO-000165", "WN11-SO-000195", "WN11-SO-000205", "WN11-UR-000015", "WN11-UR-000045", "WN11-UR-000065",
        "WN11-CC-000315", "WN11-00-000145", "WN11-00-000150", "WN11-CC-000330", "WN11-CC-000345", "WN11-CC-000335", "WN11-CC-000350", "WN11-00-000100",
        "WN11-SO-000010", "WN11-00-000135", "WN11-00-000045", "WN11-CC-000038", "WN11-00-000020", "WN11-00-000155", "WN11-CC-000155",
        "WN11-SO-000005", "WN11-SO-000020", "WN11-AC-000035", "WN11-AC-000010", "WN11-AC-000025", "WN11-SO-000270", "WN11-SO-000250", "WN11-00-000030",
        "WN11-00-000160", "WN11-00-000165", "WN11-00-000170", "WN11-SO-000070", "WN11-00-000035", "WN11-CC-000180", "WN11-CC-000185", "WN11-CC-000190",
        "WN11-CC-000005", "WN11-CC-000075", "WN11-CC-000326", "WN11-CC-000327", "WN11-SO-000075", "WN11-SO-000080", "WN11-00-000031", "WN11-00-000010",
        "WN11-CC-000070", "WN11-CC-000145", "WN11-CC-000150", "WN11-AU-000505",
    ];

    private static ControlReport Report(IEnumerable<(string Id, ControlStatus St)>? checks = null, params (string K, string V)[] settings)
    {
        var f = new StationFacts { Host = "LAB-PC", IsAdministrator = true };
        foreach (var (k, v) in settings) f.Settings[k] = v;
        var r = new ControlReport { Facts = f };
        foreach (var (id, st) in checks ?? [])
            r.Checks.Add(new ControlCheck(id, "Politici", "titlu " + id, st, "detaliu " + id, ["dovada 1 " + id, "dovada 2 " + id], "recomandare"));
        return r;
    }

    private static ControlReport AllChecks(ControlStatus st, params (string K, string V)[] settings) =>
        Report(StigChecklist.Rules.Select(x => x.CheckId).Distinct().Select(id => (id, st)), settings);

    private static JsonElement Rule(JsonDocument d, string version) =>
        d.RootElement.GetProperty("stigs")[0].GetProperty("rules").EnumerateArray().Single(x => x.GetProperty("rule_version").GetString() == version);

    private static string Status(ControlReport r, string version)
    {
        using var d = JsonDocument.Parse(StigChecklist.Build(r, Now));
        return Rule(d, version).GetProperty("status").GetString()!;
    }

    private static string[] Keys(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public void Json_parses_with_exact_top_level_and_nested_key_order()
    {
        using var d = JsonDocument.Parse(StigChecklist.Build(AllChecks(ControlStatus.Conform), Now));
        var root = d.RootElement;
        Assert.Equal(["title", "id", "stigs", "active", "mode", "has_path", "target_data", "cklb_version"], Keys(root));
        Assert.Equal("Control stație — LAB-PC — 2026-11-09 12:30 UTC", root.GetProperty("title").GetString());
        Assert.Equal(["target_type", "host_name", "ip_address", "mac_address", "fqdn", "comments", "role", "is_web_database", "technology_area", "web_db_site", "web_db_instance", "classification"],
            Keys(root.GetProperty("target_data")));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("target_data").GetProperty("classification").ValueKind);
        Assert.Equal(2, root.GetProperty("mode").GetInt32());
        Assert.Equal("1.0", root.GetProperty("cklb_version").GetString());
        Assert.Equal(1, root.GetProperty("stigs").GetArrayLength());
        var stig = root.GetProperty("stigs")[0];
        Assert.Equal(["stig_name", "display_name", "stig_id", "release_info", "version", "uuid", "reference_identifier", "size", "rules"], Keys(stig));
        Assert.Equal("Release: 3 (V2R3; date not verified)", stig.GetProperty("release_info").GetString());
        string[] ruleKeys =
        [
            "group_id_src", "group_tree", "group_id", "severity", "group_title", "rule_id_src", "rule_id", "rule_version", "rule_title", "fix_text", "weight", "check_content",
            "check_content_ref", "classification", "discussion", "false_positives", "false_negatives", "documentable", "security_override_guidance", "potential_impacts",
            "third_party_tools", "ia_controls", "responsibility", "mitigations", "mitigation_control", "legacy_ids", "ccis", "reference_identifier", "uuid", "stig_uuid",
            "status", "overrides", "comments", "finding_details", "srg_id",
        ];
        foreach (var rule in stig.GetProperty("rules").EnumerateArray())
        {
            Assert.Equal(ruleKeys, Keys(rule));
            Assert.Equal(["id", "title", "description"], Keys(rule.GetProperty("group_tree")[0]));
            Assert.Equal(["href", "name"], Keys(rule.GetProperty("check_content_ref")));
        }
    }

    [Fact]
    public void Uuids_size_statuses_and_empty_sv_ids()
    {
        using var d = JsonDocument.Parse(StigChecklist.Build(AllChecks(ControlStatus.Conform), Now));
        var stig = d.RootElement.GetProperty("stigs")[0];
        var rules = stig.GetProperty("rules").EnumerateArray().ToList();
        Assert.Equal(rules.Count, stig.GetProperty("size").GetInt32());
        var uuid = stig.GetProperty("uuid").GetString();
        Assert.All(rules, r =>
        {
            Assert.Equal(uuid, r.GetProperty("stig_uuid").GetString());
            Assert.Contains(r.GetProperty("status").GetString(), Allowed);
            Assert.Equal("", r.GetProperty("rule_id").GetString());
            Assert.Equal("", r.GetProperty("rule_id_src").GetString());
            Assert.Equal(r.GetProperty("group_id").GetString(), r.GetProperty("group_tree")[0].GetProperty("id").GetString());
        });
        Assert.Equal(rules.Count, rules.Select(r => r.GetProperty("uuid").GetString()).Distinct().Count());
    }

    [Fact]
    public void Rules_are_exactly_the_listed_ones_each_once_and_nothing_else()
    {
        using var d = JsonDocument.Parse(StigChecklist.Build(AllChecks(ControlStatus.Conform), Now));
        var rules = d.RootElement.GetProperty("stigs")[0].GetProperty("rules").EnumerateArray().ToList();
        var versions = rules.Select(r => r.GetProperty("rule_version").GetString()!).ToList();
        Assert.Equal(ExpectedVersions.OrderBy(x => x), versions.OrderBy(x => x));
        Assert.Equal(versions.Count, versions.Distinct().Count());
        foreach (var t in StigChecklist.Rules) Assert.Single(versions, v => v == t.Version);
        var groupIds = rules.Select(r => r.GetProperty("group_id").GetString()!).ToList();
        Assert.Equal(StigChecklist.Rules.Select(t => t.VId).OrderBy(x => x), groupIds.OrderBy(x => x));
        Assert.Matches(@"^V-\d+$", groupIds[0]);
        Assert.All(StigChecklist.Rules, t => Assert.Contains(t.Severity, new[] { "high", "medium", "low" }));
    }

    [Fact]
    public void A_rules_are_evaluated_one_by_one_from_the_settings()
    {
        var r = AllChecks(ControlStatus.Neconform, ("Stig.RestrictAnonymousSAM", "1"), ("Stig.RestrictAnonymous", "0"), ("Stig.EveryoneIncludesAnonymous", "0"), ("Stig.RestrictNullSessAccess", Absent));
        Assert.Equal("not_a_finding", Status(r, "WN11-SO-000145"));
        Assert.Equal("open", Status(r, "WN11-SO-000150"));
        Assert.Equal("not_a_finding", Status(r, "WN11-SO-000160"));
        // the STIG check text: "does not exist or is not configured as specified, this is a finding" - a Windows default is not the value
        Assert.Equal("open", Status(r, "WN11-SO-000165"));
        foreach (var (key, rule) in new[] { ("Stig.RestrictAnonymousSAM", "WN11-SO-000145"), ("Stig.EveryoneIncludesAnonymous", "WN11-SO-000160"), ("Stig.NoLMHash", "WN11-SO-000195") })
            Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, (key, Absent)), rule));
        using var d = JsonDocument.Parse(StigChecklist.Build(r, Now));
        Assert.Contains("NECONFORM", Rule(d, "WN11-SO-000150").GetProperty("comments").GetString());
        Assert.Contains("RestrictAnonymous = 0", Rule(d, "WN11-SO-000150").GetProperty("comments").GetString());
    }

    [Fact]
    public void A_unreadable_value_is_not_reviewed_even_when_the_check_is_Conform()
    {
        var r = AllChecks(ControlStatus.Conform);   // no settings at all: every value is "" (unreadable)
        foreach (var v in new[] { "WN11-SO-000145", "WN11-SO-000150", "WN11-SO-000160", "WN11-SO-000165", "WN11-SO-000195", "WN11-SO-000205", "WN11-UR-000015", "WN11-UR-000045",
                     "WN11-UR-000065", "WN11-CC-000315", "WN11-00-000145", "WN11-00-000150", "WN11-CC-000330", "WN11-CC-000335", "WN11-CC-000345", "WN11-CC-000350", "WN11-00-000100" })
            Assert.Equal("not_reviewed", Status(r, v));
    }

    [Fact]
    public void A_lm_level_dep_sehop_winrm_and_iis_cases()
    {
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.LmCompatibilityLevel", Absent)), "WN11-SO-000205"));
        Assert.Equal("not_a_finding", Status(AllChecks(ControlStatus.Neconform, ("Stig.LmCompatibilityLevel", "5")), "WN11-SO-000205"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.LmCompatibilityLevel", "3")), "WN11-SO-000205"));
        Assert.Equal("not_a_finding", Status(AllChecks(ControlStatus.Neconform, ("Stig.NoLMHash", "1")), "WN11-SO-000195"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.NoLMHash", "0")), "WN11-SO-000195"));
        Assert.Equal("not_a_finding", Status(AllChecks(ControlStatus.Neconform, ("Stig.DepSupportPolicy", "3")), "WN11-00-000145"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.DepSupportPolicy", "2")), "WN11-00-000145"));
        Assert.Equal("not_a_finding", Status(AllChecks(ControlStatus.Neconform, ("Stig.DisableExceptionChainValidation", "0")), "WN11-00-000150"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.DisableExceptionChainValidation", Absent)), "WN11-00-000150"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.WinRmClientAllowBasic", Absent)), "WN11-CC-000330"));
        Assert.Equal("not_a_finding", Status(AllChecks(ControlStatus.Neconform, ("Stig.WinRmServiceAllowUnencrypted", "0")), "WN11-CC-000350"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.WinRmServiceAllowBasic", "1")), "WN11-CC-000345"));
        Assert.Equal("not_a_finding", Status(AllChecks(ControlStatus.Conform, ("Stig.W3svcKey", "nu"), ("Stig.IisInetStpKey", "nu")), "WN11-00-000100"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.W3svcKey", "nu"), ("Stig.IisInetStpKey", "da")), "WN11-00-000100"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.W3svcKey", "da"), ("Stig.IisInetStpKey", "nu")), "WN11-00-000100"));
    }

    [Fact]
    public void A_user_rights_and_always_install_elevated()
    {
        const string admins = "S-1-5-32-544";
        var r = AllChecks(ControlStatus.Conform, ("UserRights.SeTcbPrivilege", "(niciunul)"), ("UserRights.SeCreateTokenPrivilege", "S-1-5-18"), ("UserRights.SeDebugPrivilege", admins),
            ("Stig.AlwaysInstallElevatedHklm", "0"), ("Stig.AlwaysInstallElevatedHkcu", Absent));
        Assert.Equal("not_a_finding", Status(r, "WN11-UR-000015"));
        Assert.Equal("open", Status(r, "WN11-UR-000045"));
        Assert.Equal("not_a_finding", Status(r, "WN11-UR-000065"));
        Assert.Equal("not_a_finding", Status(r, "WN11-CC-000315"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("UserRights.SeDebugPrivilege", admins + ";S-1-5-21-1-2-3-1001")), "WN11-UR-000065"));
        // the rule is the HKLM policy value only; HKCU is reported in the comment but does not decide it (P43 covers it)
        Assert.Equal("not_a_finding", Status(AllChecks(ControlStatus.Conform, ("Stig.AlwaysInstallElevatedHklm", "0"), ("Stig.AlwaysInstallElevatedHkcu", "1")), "WN11-CC-000315"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.AlwaysInstallElevatedHklm", Absent), ("Stig.AlwaysInstallElevatedHkcu", Absent)), "WN11-CC-000315"));
        Assert.Equal("open", Status(AllChecks(ControlStatus.Conform, ("Stig.AlwaysInstallElevatedHklm", "1"), ("Stig.AlwaysInstallElevatedHkcu", "")), "WN11-CC-000315"));
        Assert.Equal("not_reviewed", Status(AllChecks(ControlStatus.Conform, ("Stig.AlwaysInstallElevatedHklm", "2"), ("Stig.AlwaysInstallElevatedHkcu", "0")), "WN11-CC-000315"));
    }

    [Fact]
    public void B_whole_check_status_is_carried_over()
    {
        Assert.Equal("not_a_finding", Status(Report([("P07", ControlStatus.Conform)]), "WN11-00-000135"));
        Assert.Equal("not_reviewed", Status(Report([("P07", ControlStatus.DeVerificat)]), "WN11-00-000135"));
        Assert.Equal("not_reviewed", Status(Report([("P07", ControlStatus.Nedeterminat)]), "WN11-00-000135"));
        // the application's "nu se aplică" is about the station posture, not one of the STIG's own NA conditions: never carried over as NA
        Assert.Equal("not_reviewed", Status(Report([("P07", ControlStatus.NuSeAplica)]), "WN11-00-000135"));
        Assert.Equal("open", Status(Report([("P15", ControlStatus.Neconform)]), "WN11-00-000020"));
        Assert.Equal("not_a_finding", Status(Report([("P15", ControlStatus.Conform)]), "WN11-00-000020"));
        // review 2026-11-09: C01 (a missing guest account is Conform), P13 (absent and unreadable WDigest both Conform) and P24 (one registry value, not the
        // optional features the STIG checks) are not the STIG criterion, so they are evidence only
        foreach (var (id, rule) in new[] { ("C01", "WN11-SO-000010"), ("P13", "WN11-CC-000038"), ("P24", "WN11-00-000155") })
            Assert.Equal("not_reviewed", Status(Report([(id, ControlStatus.Conform)]), rule));
    }

    [Fact]
    public void Firewall_neconform_is_not_claimed_open_because_another_firewall_could_satisfy_the_rule()
    {
        Assert.Equal("not_reviewed", Status(Report([("P07", ControlStatus.Neconform)]), "WN11-00-000135"));
    }

    [Fact]
    public void C_evidence_only_rules_are_never_decided_and_carry_the_evidence()
    {
        // P08 and N07 are evidence-only too: the application checks Defender only / accepts the non-policy setting
        string[] evidenceChecks = ["C03", "P01", "P02", "P03", "P04", "P11", "P12", "P16", "P17", "P18", "P19", "P22", "P23", "P26", "P32", "P33", "P35", "P36", "A02", "P08", "N07", "C01", "P13", "P24"];
        foreach (var st in new[] { ControlStatus.Conform, ControlStatus.Neconform, ControlStatus.DeVerificat })
        {
            var r = AllChecks(st);
            using var d = JsonDocument.Parse(StigChecklist.Build(r, Now));
            foreach (var t in StigChecklist.Rules.Where(x => evidenceChecks.Contains(x.CheckId)))
            {
                var rule = Rule(d, t.Version);
                Assert.Equal("not_reviewed", rule.GetProperty("status").GetString());
                var details = rule.GetProperty("finding_details").GetString()!;
                Assert.Equal($"detaliu {t.CheckId}\ndovada 1 {t.CheckId}\ndovada 2 {t.CheckId}", details);
            }
        }
        Assert.Equal("not_reviewed", Status(Report([("P11", ControlStatus.NuSeAplica)]), "WN11-00-000030"));
        Assert.Equal("not_reviewed", Status(AllChecks(ControlStatus.NuSeAplica, ("Stig.RestrictAnonymous", "1")), "WN11-SO-000150"));
        Assert.Equal(evidenceChecks.Length, StigChecklist.Rules.Select(x => x.CheckId).Intersect(evidenceChecks).Count());
    }

    [Fact]
    public void Missing_check_gives_not_reviewed_with_the_reason()
    {
        var r = Report([]);
        using var d = JsonDocument.Parse(StigChecklist.Build(r, Now));
        foreach (var t in StigChecklist.Rules)
        {
            var rule = Rule(d, t.Version);
            Assert.Equal("not_reviewed", rule.GetProperty("status").GetString());
            Assert.Contains("verificarea nu a rulat", rule.GetProperty("comments").GetString());
            Assert.Equal("", rule.GetProperty("finding_details").GetString());
        }
    }

    [Fact]
    public void Comment_has_check_id_romanian_status_and_details_are_capped()
    {
        var r = Report([("P07", ControlStatus.Conform)]);
        r.Checks[0] = r.Checks[0] with { Detail = new string('x', 9000) };
        using var d = JsonDocument.Parse(StigChecklist.Build(r, Now));
        var rule = Rule(d, "WN11-00-000135");
        Assert.StartsWith("LogAnalyzer P07: CONFORM — ", rule.GetProperty("comments").GetString());
        Assert.Equal(8000, rule.GetProperty("finding_details").GetString()!.Length);
    }

    [Fact]
    public void The_cap_never_splits_a_surrogate_pair_and_no_status_is_ever_not_applicable()
    {
        var r = Report([("P07", ControlStatus.Conform)]);
        r.Checks[0] = r.Checks[0] with { Detail = new string('x', 7999) + "😀" + new string('y', 50) };   // the emoji straddles the 8000-character cap
        using var d = JsonDocument.Parse(StigChecklist.Build(r, Now));
        Assert.Equal(new string('x', 7999), Rule(d, "WN11-00-000135").GetProperty("finding_details").GetString());
        foreach (var st in Enum.GetValues<ControlStatus>())
        {
            using var all = JsonDocument.Parse(StigChecklist.Build(AllChecks(st), Now));
            Assert.DoesNotContain(all.RootElement.GetProperty("stigs")[0].GetProperty("rules").EnumerateArray(), x => x.GetProperty("status").GetString() == "not_applicable");
        }
    }

    [Fact]
    public void FileName_is_safe_and_has_the_cklb_extension()
    {
        var n = StigChecklist.FileName("PC/01:x", Now);
        Assert.EndsWith(".cklb", n);
        Assert.Equal(".cklb", StigChecklist.FileExtension);
        Assert.Equal(-1, n.IndexOfAny(Path.GetInvalidFileNameChars()));
    }
}
