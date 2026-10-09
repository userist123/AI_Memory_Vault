using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WP15a on the production path: the pipeline uses the profile, copies it into the case and records its SHA-256 in custody.</summary>
public sealed class ProfileInCaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ladfir_wp15_{Guid.NewGuid():N}");
    public ProfileInCaseTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private CaseWorkspace NewCaseWithSample()
    {
        var sample = Path.Combine(_dir, "sample.bin");
        File.WriteAllText(sample, "MZ not really a program");
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "wp15", TestScopes.Valid());
        InvestigationPipeline.Import(ws, [sample]);
        return ws;
    }

    private static ProcedureProfile Profile()
    {
        var p = new ProcedureProfile();
        ProfileTables.SetCells(p, ProfileTable.ApprovedAccounts, [[@"CORP\alice", ""]]);
        ProfileTables.SetCells(p, ProfileTable.MaintenanceWindows, [["weekly", "", "Mon", "", "08:00", "10:00", ""]]);
        return p;
    }

    private static List<JsonElement> Outputs(CaseWorkspace ws) =>
        File.ReadAllLines(ws.CustodyJsonlPath).Select(l => JsonDocument.Parse(l).RootElement.Clone()).Where(e => e.GetProperty("action").GetString() == "output.written").ToList();

    [Fact]
    public void Run_with_a_profile_copies_it_into_the_case_and_records_the_sha256_in_custody()
    {
        var ws = NewCaseWithSample();
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, procedureProfile: Profile());
        var path = ws.FullPath(ProfileSnapshot.RelPath);
        Assert.True(File.Exists(path));
        var sha = Hashing.Sha256File(path);
        Assert.Equal(sha, r.ProcedureProfileSha256);
        var entry = Outputs(ws).Last(e => e.GetProperty("to").GetString() == ProfileSnapshot.RelPath);
        Assert.Equal(sha, entry.GetProperty("sha256").GetString());
        Assert.Contains(File.ReadAllLines(ws.AuditChainPath), l => l.Contains("profile.used") && l.Contains(sha));
        // The snapshot is the profile that was used.
        Assert.Equal(["Mon"], ProfileImport.FromJson(File.ReadAllText(path)).Profile!.MaintenanceWindows.Select(w => w.Day));
        Assert.NotNull(r.Procedure);
    }

    [Fact]
    public void Run_without_a_profile_writes_no_snapshot_and_audits_no_profile()
    {
        var ws = NewCaseWithSample();
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        Assert.False(File.Exists(ws.FullPath(ProfileSnapshot.RelPath)));
        Assert.Null(r.Procedure);
        Assert.DoesNotContain(File.ReadAllLines(ws.AuditChainPath), l => l.Contains("profile.used"));
    }

    [Fact]
    public void Maintenance_policy_for_a_case_uses_the_case_time_zone_and_the_evidence_span()
    {
        var t = new DateTimeOffset(2026, 7, 6, 5, 30, 0, TimeSpan.Zero);   // Monday 08:30 in Bucharest (UTC+3 in July)
        var policy = ProfileSnapshot.MaintenancePolicyFor(Profile(), [t], "Europe/Bucharest")!;
        Assert.Equal(LogClearLifecycle.Routine, LogClearAssessment.Assess([new("Security", t, "alice", "CORP")], policy, [])[0].Lifecycle);
        Assert.Equal(LogClearLifecycle.Unexpected, LogClearAssessment.Assess([new("Security", t.AddHours(-3), "alice", "CORP")], policy, [])[0].Lifecycle);
        Assert.Null(ProfileSnapshot.MaintenancePolicyFor(null, [t], "UTC"));
        Assert.Null(ProfileSnapshot.MaintenancePolicyFor(new ProcedureProfile(), [t], "UTC"));
    }
}

public sealed class ProfileProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-prov-" + Guid.NewGuid().ToString("N"));
    public ProfileProviderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Without_a_file_everything_is_null_and_unchanged()
    {
        var p = new ProfileProvider(Path.Combine(_dir, "none.json"));
        Assert.Null(p.Current); Assert.Null(p.WorkingHours);
        Assert.Null(p.MaintenancePolicyFor([DateTimeOffset.UtcNow]));
    }

    [Fact]
    public void Saved_profile_is_picked_up_by_Replace_and_Reload()
    {
        var path = Path.Combine(_dir, "p.json");
        var prov = new ProfileProvider(path);
        Assert.Null(prov.Current);
        var prof = new ProcedureProfile();
        ProfileTables.SetCells(prof, ProfileTable.WorkingHours, [["Mon", "08:00", "16:00"]]);
        ProfileTables.SetCells(prof, ProfileTable.ApprovedAccounts, [["alice", ""]]);
        ProfileTables.SetCells(prof, ProfileTable.MaintenanceWindows, [["weekly", "", "Mon", "", "08:00", "10:00", ""]]);
        Assert.DoesNotContain(ProfileStore.Save(prof, path, out _), i => i.IsError);
        prov.Replace(prof);
        Assert.Equal("08:00-16:00", prov.WorkingHours!.Describe());
        var t = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var policy = prov.MaintenancePolicyFor([t]);
        Assert.NotNull(policy); Assert.Same(policy, prov.MaintenancePolicyFor([t.AddMinutes(5)]));   // same day: cached
        var fresh = new ProfileProvider(path);
        Assert.Equal(["alice"], fresh.Current!.ApprovedAccounts.Select(a => a.Account));
    }
}
