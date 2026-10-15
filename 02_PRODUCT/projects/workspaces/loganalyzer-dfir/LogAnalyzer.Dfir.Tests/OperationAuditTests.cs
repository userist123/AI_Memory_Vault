using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Profile;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Operator operations that must appear in the sealed auth audit chain (control run, unmasked export, case removal, profile edits).</summary>
public sealed class OperationAuditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "opaudit_" + Guid.NewGuid().ToString("N"));
    public OperationAuditTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private AuthService Svc() => new(Path.Combine(_dir, "auth"));
    private static List<AuthAuditEntry> Entries(AuthService s) => Json.ReadLines<AuthAuditEntry>(s.AuditPath).ToList();

    [Fact]
    public void Control_started_and_finished_are_appended_with_their_facts()
    {
        var s = Svc();
        s.AuditOperation(null, OperationAudit.ControlStarted, OperationAudit.ControlStartedDetail("Standalone", "Standalone", true, 30, 2));
        s.AuditOperation(null, OperationAudit.ControlFinished, OperationAudit.ControlFinishedDetail("WS01", true, 40, 3, 2, 1));
        var e = Entries(s);
        var st = Assert.Single(e, x => x.Event == "control.started");
        Assert.Contains("edition=Standalone", st.Detail); Assert.Contains("elevated=da", st.Detail); Assert.Contains("30 zile", st.Detail);
        var fin = Assert.Single(e, x => x.Event == "control.finished");
        Assert.Contains("neconform=3", fin.Detail);
        Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, s.VerifyAudit().Status);
    }

    [Fact]
    public void Unmasked_export_records_case_format_count_and_sha256_of_the_output()
    {
        var s = Svc();
        var file = Path.Combine(_dir, "out.csv");
        File.WriteAllText(file, "a,b\n1,2\n");
        s.AuditOperation(null, OperationAudit.ExportUnmasked, OperationAudit.ExportUnmaskedDetail("CASE-1", "csv", 1, file));
        var x = Assert.Single(Entries(s), e => e.Event == "export.unmasked");
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        Assert.Contains("case=CASE-1", x.Detail); Assert.Contains("format=csv", x.Detail); Assert.Contains("items=1", x.Detail); Assert.Contains("sha256=" + sha, x.Detail);
    }

    [Fact]
    public void Case_deleted_is_appended()
    {
        var s = Svc();
        s.AuditOperation(null, OperationAudit.CaseDeleted, OperationAudit.CaseDeletedDetail("CASE-9", "removed_from_recent_list"));
        Assert.Contains("case=CASE-9", Assert.Single(Entries(s), e => e.Event == "case.deleted").Detail);
    }

    [Fact]
    public void Profile_edit_appends_before_after_hash_and_folder_change_only_when_the_table_changed()
    {
        var s = Svc();
        var before = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = "D:/A", Note = "" }] };
        var sameFolders = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = "D:/A", Note = "n" }] };
        var written = OperationAudit.AuditProfileSave(s, null, before, sameFolders, "p.json");
        Assert.Equal(["profile.edited"], written);

        var moved = new ProcedureProfile { ClassifiedFolders = [new ClassifiedFolderRow { Path = "D:/B", Note = "" }] };
        written = OperationAudit.AuditProfileSave(s, null, before, moved, "p.json");
        Assert.Equal(["profile.edited", "profile.classified_folders_changed"], written);

        Assert.Empty(OperationAudit.AuditProfileSave(s, null, before, before, "p.json"));   // nothing changed, nothing written
        var all = Entries(s);
        var ed = all.First(e => e.Event == "profile.edited");
        Assert.Contains("before_sha256=", ed.Detail); Assert.Contains("after_sha256=", ed.Detail);
        Assert.Contains("old_sha256=", all.Single(e => e.Event == "profile.classified_folders_changed").Detail);
        Assert.Equal(LogAnalyzer.Dfir.Case.ChainStatus.Valid, s.VerifyAudit().Status);
    }
}
