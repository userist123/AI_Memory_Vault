using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Review 2026-10-15: every export that is not masked is audited first (intent before the file, refused when the audit cannot be written) and then
/// with the SHA-256 of the written file; an audit failure on any other event is never silent.
/// </summary>
[Collection("Global authentication state")]
public sealed class ExportAuditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-exportaudit-" + Guid.NewGuid().ToString("N"));

    private readonly bool _required = OperatorIdentity.AuthenticationRequired;
    private readonly LogAnalyzer.Dfir.Policy.IPolicyApprovalLedger? _ledger = LogAnalyzer.Dfir.Policy.PolicyApprovals.Ledger;

    public ExportAuditTests()
    {
        Directory.CreateDirectory(_dir);
        AuthApp.Initialize(Path.Combine(_dir, "auth"));
    }

    public void Dispose()
    {
        OperatorIdentity.AuthenticationRequired = _required;                          // Initialize sets process-wide gates: put them back
        LogAnalyzer.Dfir.Policy.PolicyApprovals.Ledger = _ledger;
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_profile_saved_whose_audit_cannot_be_written_says_it_was_saved_and_that_the_audit_failed()
    {
        AuthApp.Record("test.prime", "x");
        using var gate = OperatorIdentity.Scope(null, authenticationRequired: false);  // the profile gate is not under test here
        var provider = new LogAnalyzer.Dfir.Profile.ProfileProvider(Path.Combine(_dir, "procedure_profile.json"));
        var vm = new LogAnalyzer.UI.ViewModels.ProcedureProfileViewModel(provider);
        vm.Tables.Single(t => t.Table == LogAnalyzer.Dfir.Profile.ProfileTable.Zones).Data.Rows.Add("ROSU", "clasificat");
        using (new FileStream(AuthApp.Service.AuditPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            vm.SaveCommand.Execute(null);
        Assert.True(File.Exists(provider.Path), vm.Status + string.Join(" / ", vm.Issues));                           // the profile was written
        Assert.DoesNotContain("nu a putut fi scris", vm.Status);
        Assert.StartsWith("Profil salvat", vm.Status);
        Assert.Contains("jurnalul de audit", vm.Status);
        Assert.True(AuditHealth.Instance.HasWarning);
    }

    private static List<AuthAuditEntry> Audit() => LogAnalyzer.Dfir.IO.Json.ReadLines<AuthAuditEntry>(AuthApp.Service.AuditPath).ToList();

    [Fact]
    public void An_export_records_its_intent_before_the_file_and_its_outcome_with_the_sha256_after()
    {
        var file = Path.Combine(_dir, "out.csv");
        int auditLinesWhenWriting = -1;
        var refused = ExportAudit.Run("CASE-7", "csv", 2, file, () =>
        {
            auditLinesWhenWriting = Audit().Count(e => e.Event == OperationAudit.ExportUnmasked);
            File.WriteAllText(file, "a,b\n1,2\n");
        });
        Assert.Null(refused);
        Assert.Equal(1, auditLinesWhenWriting);                                       // the intent was already in the audit when the file was written
        var lines = Audit().Where(e => e.Event == OperationAudit.ExportUnmasked).ToList();
        Assert.Equal(2, lines.Count);
        Assert.EndsWith("phase=intent", lines[0].Detail);
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        Assert.Contains("sha256=" + sha, lines[1].Detail);
        Assert.Contains("items=2", lines[1].Detail);
        Assert.EndsWith("phase=written", lines[1].Detail);
    }

    [Fact]
    public void When_the_audit_cannot_be_written_the_export_is_refused_and_nothing_is_written()
    {
        AuthApp.Record("test.prime", "x");                                            // the audit file exists
        var file = Path.Combine(_dir, "refused.pdf");
        bool wrote = false;
        string? refused;
        using (new FileStream(AuthApp.Service.AuditPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))   // another process holds the audit
            refused = ExportAudit.Run("CASE-7", "pdf", 1, file, () => { wrote = true; File.WriteAllText(file, "x"); });
        Assert.NotNull(refused);
        Assert.StartsWith("Exportul nu s-a făcut: jurnalul de audit nu poate fi scris — ", refused);
        Assert.False(wrote);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_failed_write_after_the_intent_is_recorded_as_failed_and_rethrown()
    {
        Assert.Throws<IOException>(() => ExportAudit.Run("CASE-7", "zip", 0, Path.Combine(_dir, "x.zip"), () => throw new IOException("disk full")));
        Assert.EndsWith("phase=failed; error=IOException", Audit().Last(e => e.Event == OperationAudit.ExportUnmasked).Detail);
    }

    [Fact]
    public void An_audit_failure_on_another_event_raises_the_persistent_warning()
    {
        AuthApp.Record("test.prime", "x");
        int before = AuditHealth.Instance.Failures;
        bool ok;
        using (new FileStream(AuthApp.Service.AuditPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            ok = AuthApp.Record(OperationAudit.ControlStarted, "edition=test");
        Assert.False(ok);
        Assert.Equal(before + 1, AuditHealth.Instance.Failures);
        Assert.True(AuditHealth.Instance.HasWarning);
        Assert.Contains(OperationAudit.ControlStarted, AuditHealth.Instance.Warning);
        Assert.True(AuthApp.Record(OperationAudit.ControlStarted, "edition=test"));  // writable again: written, and the warning stays (persistent)
        Assert.True(AuditHealth.Instance.HasWarning);
    }

    // ---- guard: every file export chosen through a SaveFileDialog goes through ExportAudit.Run ----

    /// <summary>
    /// Writes through a SaveFileDialog that are not unmasked exports of case data, with the reason. Anything else that opens a SaveFileDialog must
    /// call ExportAudit.Run in the same method.
    /// </summary>
    private static readonly Dictionary<string, string> NotUnmaskedExports = new(StringComparer.Ordinal)
    {
        ["MainViewModel.GenerateHostIsolationScript"] = "response script generated from a template (host name only), no case evidence",
        ["MainViewModel.GenerateKillProcessScript"] = "response script generated from a template, no case evidence",
        ["MainViewModel.SanitizeMediaAndGenerateCertPdfAsync"] = "certificate of a wipe already done, recorded in the provenance ledger; refusing it after the wipe would lose the certificate",
        ["InvestigationViewModel.GenerateRemotePackage"] = "collection script of an authorised request, audited in the case by RemoteCollection.Authorize",
        ["InvestigationViewModel.ExportMaskedPdf"] = "masked export (pseudonyms), audited in the case as report.pdf_masked",
        ["InvestigationViewModel.ExportMaskedTimeline"] = "masked export (pseudonyms), audited in the case as timeline.export_masked",
        ["DomainInvestigationViewModel.SaveScript"] = "mail collection script generated from a template, no case evidence",
        ["ProcedureProfileViewModel.SaveAs"] = "copy of the procedure profile (configuration), audited as profile.edited / profile.classified_folders_changed",
    };

    private static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LogAnalyzer.App", "MainWindow.xaml"))) return Path.Combine(dir.FullName, "LogAnalyzer.App");
        throw new DirectoryNotFoundException("LogAnalyzer.App");
    }

    private static readonly Regex MethodDecl = new(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:private|public|internal|protected|static|async|override|partial)\s+)+[\w<>\[\]?,\s.]+?\s(\w+)\s*\([^;]*$",
        RegexOptions.Multiline);

    [Fact]
    public void Every_file_export_through_a_save_dialog_is_audited_first()
    {
        var problems = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        int dialogs = 0;
        foreach (var file in Directory.GetFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            var decls = MethodDecl.Matches(text).Cast<Match>().ToList();
            var cls = Path.GetFileNameWithoutExtension(file).Split('.')[0];
            foreach (Match m in Regex.Matches(text, @"new\s+(?:Microsoft\.Win32\.)?SaveFileDialog\b"))
            {
                dialogs++;
                var decl = decls.LastOrDefault(d => d.Index < m.Index);
                var next = decls.FirstOrDefault(d => d.Index > m.Index);
                if (decl is null) { problems.Add($"{Path.GetFileName(file)}: SaveFileDialog outside a method"); continue; }
                var key = $"{cls}.{decl.Groups[1].Value}";
                seen.Add(key);
                var body = text[decl.Index..(next?.Index ?? text.Length)];
                if (NotUnmaskedExports.ContainsKey(key)) continue;
                if (!body.Contains("ExportAudit.Run(", StringComparison.Ordinal)) problems.Add($"{key}: writes a file chosen in a SaveFileDialog without ExportAudit.Run");
            }
        }
        Assert.True(dialogs >= 25, $"only {dialogs} SaveFileDialog found: the scan is broken");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        // the allowlist stays exact: an entry for a method that no longer opens a save dialog is removed
        Assert.DoesNotContain(NotUnmaskedExports.Keys, k => !seen.Contains(k));
    }
}
