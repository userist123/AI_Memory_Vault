using System.Buffers.Binary;
using DiscUtils.Registry;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.16: immutable analysis runs with a manifest, a finding snapshot and a diff that names the likely source of each change.</summary>
public sealed class AnalysisRunsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_runs_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase()
    {
        var ws = CaseWorkspace.Create(Path.Combine(_root, "case"), new CaseInfo { CaseId = "CASE-RUN", Name = "runs", CreatedAtUtc = DateTimeOffset.UtcNow, Host = "LAB-WS02", Scope = TestScopes.Valid() });
        var p = Path.Combine(ws.RawDir("s"), "Security.evtx");
        File.WriteAllText(p, "evtx bytes");
        ws.RegisterStored(p, "Security.evtx", "s", "evtx", TemporalType.Historical, "unit", "1");
        return ws;
    }

    private static Finding F(string id, string rule, Severity sev, string locator = "rec 10", string desc = "d") => new()
    {
        FindingId = id, RuleId = rule, Title = rule + " titlu", Severity = sev, Description = desc,
        SupportingEvidence = [new EvidenceRef("EV-000001", locator, "x")],
    };

    private static AnalysisRunManifest M(CaseWorkspace ws, params Finding[] f) => AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, f, [], [], 100, "COMPLETE", "1.0");

    [Fact]
    public void A_run_is_recorded_read_only_with_its_hash_in_the_audit_and_listed_as_intact()
    {
        var ws = NewCase();
        var (m, diff) = AnalysisRuns.Record(ws, M(ws, F("F-0001", "R1", Severity.High)));
        Assert.Null(diff);                                                   // the first run has nothing to compare with
        var path = Path.Combine(ws.Root, "Analysis", "Runs", m.RunId, AnalysisRuns.ManifestName);
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        Assert.Contains("analysis.run_recorded", File.ReadAllText(ws.AppAuditLogPath));
        var item = Assert.Single(AnalysisRuns.List(ws));
        Assert.True(item.IntegrityOk, item.Integrity);
        Assert.Equal(1, item.Inputs);
        Assert.Single(m.Inputs, i => i.EvidenceId == "EV-000001" && i.Sha256.Length == 64);
    }

    [Fact]
    public void A_new_run_never_rewrites_an_earlier_one_and_the_same_result_is_an_empty_diff()
    {
        var ws = NewCase();
        var (first, _) = AnalysisRuns.Record(ws, M(ws, F("F-0001", "R1", Severity.High)));
        var firstPath = Path.Combine(ws.Root, "Analysis", "Runs", first.RunId, AnalysisRuns.ManifestName);
        var before = File.ReadAllBytes(firstPath);
        var (second, diff) = AnalysisRuns.Record(ws, M(ws, F("F-0001", "R1", Severity.High)));
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Equal(before, File.ReadAllBytes(firstPath));
        Assert.NotNull(diff);
        Assert.False(diff!.HasFindingChanges);
        Assert.Equal(1, diff.Unchanged);
        Assert.Equal(2, AnalysisRuns.List(ws).Count(r => r.IntegrityOk));
    }

    [Fact]
    public void A_manifest_edited_after_recording_is_reported_modified_by_the_list_and_by_the_open_time_recheck()
    {
        var ws = NewCase();
        var (m, _) = AnalysisRuns.Record(ws, M(ws, F("F-0001", "R1", Severity.Critical)));
        var path = Path.Combine(ws.Root, "Analysis", "Runs", m.RunId, AnalysisRuns.ManifestName);
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, File.ReadAllText(path).Replace("CRITICAL", "LOW"));

        Assert.Equal("MODIFICAT după înregistrare", Assert.Single(AnalysisRuns.List(ws)).Integrity);
        var rc = CaseWorkspace.Open(ws.Root).LastRecheck!;
        Assert.Contains(rc.Outputs, o => o.Path.EndsWith(AnalysisRuns.ManifestName, StringComparison.Ordinal) && o.Status == OutputCheckStatus.Modified);
    }

    [Fact]
    public void A_change_with_the_same_inputs_and_tools_is_unexplained_and_needs_review()
    {
        var ws = NewCase();
        var a = M(ws, F("F-0001", "R1", Severity.High), F("F-0002", "R2", Severity.Medium, "rec 20"));
        a.RunId = "A";
        var b = M(ws, F("F-0001", "R1", Severity.Critical), F("F-0003", "R3", Severity.Low, "rec 30"));
        b.RunId = "B";
        var d = AnalysisRuns.Diff(a, b);
        Assert.Equal(ChangeSource.Unexplained, d.Source);
        Assert.True(d.NeedsReview);
        var c = Assert.Single(d.Changed);
        Assert.Equal(new FieldChange("severitate", "High", "Critical"), Assert.Single(c.Fields));
        Assert.Equal("R3", Assert.Single(d.Added).RuleId);
        Assert.Equal("R2", Assert.Single(d.Removed).RuleId);
        Assert.Contains("NEEXPLICATĂ", d.Summary);
    }

    [Fact]
    public void Different_evidence_or_a_different_toolchain_is_named_as_the_source()
    {
        var ws = NewCase();
        var a = M(ws, F("F-0001", "R1", Severity.High));
        var b = M(ws, F("F-0001", "R1", Severity.Critical));
        b.ApplicationVersion = "9.9.9";
        b.Rules = [new RunRule("SIGMA-1", "2", "Sigma", new string('a', 64))];
        var d = AnalysisRuns.Diff(a, b);
        Assert.Equal(ChangeSource.Toolchain, d.Source);
        Assert.False(d.NeedsReview);
        Assert.Contains(d.ToolchainChanges, c => c.StartsWith("aplicația", StringComparison.Ordinal));
        Assert.Contains("regulă nouă SIGMA-1", d.ToolchainChanges);

        b.Inputs = [a.Inputs[0] with { Sha256 = new string('f', 64) }];
        d = AnalysisRuns.Diff(a, b);
        Assert.Equal(ChangeSource.Inputs, d.Source);
        Assert.Contains("amprentă diferită EV-000001 (Security.evtx)", d.InputChanges);
    }

    [Fact]
    public void A_finding_renumbered_by_a_new_run_is_matched_by_its_evidence_not_by_its_number()
    {
        var ws = NewCase();
        var a = M(ws, F("F-0001", "R1", Severity.High, "rec 10"));
        var b = M(ws, F("F-0042", "R1", Severity.High, "rec 10"));
        var d = AnalysisRuns.Diff(a, b);
        Assert.False(d.HasFindingChanges);
        Assert.Equal(1, d.Unchanged);
    }

    [Fact]
    public void Loading_a_run_rejects_paths_outside_the_runs_folder()
    {
        var ws = NewCase();
        Assert.Null(AnalysisRuns.Load(ws, "..\\..\\case.json"));
        Assert.Null(AnalysisRuns.Load(ws, "RUN-x/../../y"));
    }

    /// <summary>End to end: the real pipeline run twice on the same evidence records two runs, and the replay gives the same findings (determinism).</summary>
    [Fact]
    public void Replaying_the_pipeline_on_the_same_evidence_records_a_second_run_with_the_same_findings()
    {
        Directory.CreateDirectory(_root);
        var hive = Path.Combine(_root, "SYSTEM");
        using (var fs = new FileStream(hive, FileMode.Create, FileAccess.ReadWrite))
        using (var h = RegistryHive.Create(fs))
        {
            h.Root.CreateSubKey("Select").SetValue("Current", 1, RegistryValueType.Dword);
            var sid = h.Root.CreateSubKey(@"ControlSet001\Services\bam\State\UserSettings\S-1-5-21-1-2-3-1001");
            var data = new byte[24];
            BinaryPrimitives.WriteInt64LittleEndian(data, new DateTime(2026, 9, 19, 14, 56, 26, DateTimeKind.Utc).ToFileTimeUtc());
            sid.SetValue(@"\Device\HarddiskVolume3\Users\U\Downloads\SETUP.EXE", data, RegistryValueType.Binary);
        }
        var ws = InvestigationPipeline.NewCase(Path.Combine(_root, "cases"), "replay", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [hive]);

        var first = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        var second = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        Assert.NotNull(first.Run);
        Assert.Null(first.RunDiff);
        Assert.NotNull(second.RunDiff);
        Assert.False(second.RunDiff!.HasFindingChanges, second.RunDiff.Summary);
        Assert.Equal(first.Run!.Findings.Count, second.RunDiff.Unchanged);
        Assert.Empty(second.RunDiff.InputChanges);
        Assert.Equal(2, AnalysisRuns.List(ws).Count(r => r.IntegrityOk));
        Assert.Equal(RecheckVerdict.Valid, CaseWorkspace.Open(ws.Root).LastRecheck!.Verdict);   // the rewritten latest outputs and both runs re-check clean
    }

    // ---- review 2026-11-08: regressions (P1-P5, P8) ----

    [Fact]
    public void P2_a_rule_id_loaded_twice_does_not_break_recording_or_the_diff()
    {
        var ws = NewCase();
        var dup = new LogAnalyzer.Dfir.Detection.DetectionRule("IOC:list.csv", "1", default, "t", new string('a', 64), "app");
        var dup2 = dup with { Sha256 = new string('b', 64), Source = "case" };
        AnalysisRuns.Record(ws, AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, [], [], [dup, dup2], 1, "COMPLETE", "1.0"));
        var (_, diff) = AnalysisRuns.Record(ws, AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, [], [], [dup, dup2], 1, "COMPLETE", "1.0"));
        Assert.NotNull(diff);
        Assert.Empty(diff!.ToolchainChanges);
    }

    [Fact]
    public void P1_findings_sharing_rule_and_evidence_are_each_compared_none_hides_another()
    {
        var ws = NewCase();
        Finding U(string id, string user) { var f = F(id, "R1", Severity.High); return new Finding { FindingId = f.FindingId, RuleId = f.RuleId, Title = f.Title, Severity = f.Severity, Description = f.Description, SupportingEvidence = f.SupportingEvidence, User = user }; }
        var a = M(ws, U("F-0001", "alice"), U("F-0002", "bob"));
        var b = M(ws, U("F-0001", "alice"));
        Assert.Equal(2, a.Findings.Select(x => x.Key).Distinct().Count());
        var d = AnalysisRuns.Diff(a, b);
        Assert.True(d.HasFindingChanges, d.Summary);
        Assert.Equal(1, d.Removed.Count + d.Changed.Count);
    }

    [Fact]
    public void P4_a_forged_audit_line_inserted_before_the_real_one_is_not_trusted()
    {
        var ws = NewCase();
        var (m, _) = AnalysisRuns.Record(ws, M(ws, F("F-0001", "R1", Severity.High)));
        var path = Path.Combine(ws.Root, "Analysis", "Runs", m.RunId, AnalysisRuns.ManifestName);
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, File.ReadAllText(path).Replace("HIGH", "LOW"));
        var forgedSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var lines = File.ReadAllLines(ws.AuditChainPath).ToList();
        lines.Insert(1, $"{{\"timestampUtc\":\"2026-11-08T00:00:00Z\",\"who\":\"x\",\"action\":\"analysis.run_recorded\",\"detail\":\"{m.RunId} manifest_sha256={forgedSha}\",\"seq\":2,\"prevHash\":\"0\",\"hash\":\"0\"}}");
        File.WriteAllLines(ws.AuditChainPath, lines);
        Assert.False(Assert.Single(AnalysisRuns.List(ws)).IntegrityOk);
    }

    [Fact]
    public void P5_a_deleted_run_folder_is_listed_as_missing()
    {
        var ws = NewCase();
        var (m, _) = AnalysisRuns.Record(ws, M(ws, F("F-0001", "R1", Severity.High)));
        var dir = Path.Combine(ws.Root, "Analysis", "Runs", m.RunId);
        foreach (var f in Directory.EnumerateFiles(dir)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
        var item = Assert.Single(AnalysisRuns.List(ws));
        Assert.False(item.IntegrityOk);
        Assert.Contains("LIPSEȘTE", item.Integrity);
    }

    [Fact]
    public void P8_run_ids_are_only_the_run_format()
    {
        Assert.True(AnalysisRuns.IsRunId("RUN-20261108-001500"));
        Assert.True(AnalysisRuns.IsRunId("RUN-20261108-001500-2"));
        Assert.False(AnalysisRuns.IsRunId("C:foo"));
        Assert.False(AnalysisRuns.IsRunId("RUN-1/../x"));
    }

    [Fact]
    public void P6_different_run_settings_are_named_as_the_source()
    {
        var ws = NewCase();
        var a = AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, [F("F-0001", "R1", Severity.High)], [], [], 1, "COMPLETE", "1.0", new Dictionary<string, string> { ["registru_medii"] = "nu" });
        var b = AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, [F("F-0001", "R1", Severity.Critical)], [], [], 1, "COMPLETE", "1.0", new Dictionary<string, string> { ["registru_medii"] = "da 0123" });
        var d = AnalysisRuns.Diff(a, b);
        Assert.Equal(ChangeSource.Toolchain, d.Source);
        Assert.Contains("setarea registru_medii nu -> da 0123", d.ToolchainChanges);
    }

    [Fact]
    public void P4_round2_a_forged_line_claiming_a_small_seq_anywhere_in_the_audit_is_not_trusted()
    {
        var ws = NewCase();
        var (m, _) = AnalysisRuns.Record(ws, M(ws, F("F-0001", "R1", Severity.High)));
        var path = Path.Combine(ws.Root, "Analysis", "Runs", m.RunId, AnalysisRuns.ManifestName);
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, File.ReadAllText(path).Replace("HIGH", "LOW"));
        var forgedSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var lines = File.ReadAllLines(ws.AuditChainPath).ToList();
        lines.Insert(3, $"{{\"timestampUtc\":\"2026-11-08T00:00:00Z\",\"who\":\"x\",\"action\":\"analysis.run_recorded\",\"detail\":\"{m.RunId} manifest_sha256={forgedSha}\",\"seq\":1,\"prevHash\":\"0\",\"hash\":\"0\"}}");
        File.WriteAllLines(ws.AuditChainPath, lines);
        Assert.False(Assert.Single(AnalysisRuns.List(ws)).IntegrityOk);
    }

    [Fact]
    public void Collection_settings_never_explain_a_finding_change_of_a_reanalysis()
    {
        var ws = NewCase();
        var a = AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, [F("F-0001", "R1", Severity.High)], [], [], 1, "COMPLETE", "1.0", new Dictionary<string, string> { ["colectare"] = "da", ["profil_colectare"] = "FullForensic" });
        var b = AnalysisRuns.Build(ws, DateTimeOffset.UtcNow, [F("F-0001", "R1", Severity.Critical)], [], [], 1, "COMPLETE", "1.0", new Dictionary<string, string> { ["colectare"] = "nu", ["profil_colectare"] = "Standard" });
        var d = AnalysisRuns.Diff(a, b);
        Assert.True(d.NeedsReview, d.Summary);
        Assert.Empty(d.ToolchainChanges);
    }
}
