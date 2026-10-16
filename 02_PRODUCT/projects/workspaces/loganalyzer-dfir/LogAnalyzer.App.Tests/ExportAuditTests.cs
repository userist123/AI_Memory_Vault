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
    public void A_bundle_export_lists_every_file_before_writing_and_records_each_written_file_with_its_sha256()
    {
        var dest = Path.Combine(_dir, "bundle");
        int linesWhenWriting = -1;
        var refused = ExportAudit.RunBundle("CASE-9", "pentru-ofiter", dest, ["raport.pdf", "raport.pdf.sig", "citeste_ma.txt"], () =>
        {
            linesWhenWriting = Audit().Count(e => e.Event == OperationAudit.ExportUnmasked);
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "raport.pdf"), "pdf");
            File.WriteAllText(Path.Combine(dest, "raport.pdf.sig"), "sig");
            File.WriteAllText(Path.Combine(dest, "citeste_ma.txt"), "readme");
            return Directory.GetFiles(dest);
        });
        Assert.Null(refused);
        var lines = Audit().Where(e => e.Event == OperationAudit.ExportUnmasked).ToList();
        Assert.Equal(1, linesWhenWriting);                                            // the intent, with the whole list, was written first
        Assert.EndsWith("phase=intent", lines[0].Detail);
        foreach (var f in new[] { "raport.pdf", "raport.pdf.sig", "citeste_ma.txt" }) Assert.Contains(f, lines[0].Detail);
        Assert.Equal(4, lines.Count);                                                 // then one record per written file
        foreach (var f in Directory.GetFiles(dest))
        {
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)));
            Assert.Contains(lines.Skip(1), l => l.Detail.Contains("sha256=" + sha) && l.Detail.Contains(Path.GetFileName(f)) && l.Detail.EndsWith("phase=written"));
        }
    }

    [Fact]
    public void A_bundle_export_into_a_chosen_folder_records_only_the_files_it_wrote()
    {
        var dest = Path.Combine(_dir, "officer-stick");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "alt_document.txt"), "not ours");          // already on the medium: not part of this export
        var refused = ExportAudit.RunBundle("CASE-9", "istoric-medii", dest, ["istoric.json", "istoric.json.p7s"], () =>
        {
            File.WriteAllText(Path.Combine(dest, "istoric.json"), "{}");
            File.WriteAllText(Path.Combine(dest, "istoric.json.p7s"), "sig");
            return [Path.Combine(dest, "istoric.json"), Path.Combine(dest, "istoric.json.p7s")];
        });
        Assert.Null(refused);
        var written = Audit().Where(e => e.Event == OperationAudit.ExportUnmasked && e.Detail.EndsWith("phase=written")).ToList();
        Assert.Equal(2, written.Count);
        Assert.DoesNotContain(written, l => l.Detail.Contains("alt_document.txt"));
    }

    [Fact]
    public void A_bundle_export_whose_audit_cannot_be_written_writes_no_file_at_all()
    {
        AuthApp.Record("test.prime", "x");
        var dest = Path.Combine(_dir, "bundle-refused");
        bool wrote = false;
        string? refused;
        using (new FileStream(AuthApp.Service.AuditPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            refused = ExportAudit.RunBundle("CASE-9", "pentru-ofiter", dest, ["raport.pdf"], () => { wrote = true; Directory.CreateDirectory(dest); return []; });
        Assert.NotNull(refused);
        Assert.False(wrote);
        Assert.False(Directory.Exists(dest));
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

    /// <summary>
    /// Methods that open a folder picker or read a remembered folder but write nothing outside the case (they only read, or only choose), with the reason.
    /// Anything else that takes a destination from a folder picker or from <c>FolderMemory</c> must call ExportAudit.Run / RunBundle in the same method.
    /// </summary>
    private static readonly Dictionary<string, string> ReadOnlyFolderPickers = new(StringComparer.Ordinal)
    {
        ["HomeViewModel.PickFolderWithDialog"] = "opens an existing case folder (read; the case loader re-verifies it)",
        ["DomainInvestigationViewModel.ImportMail"] = "reads exported mail CSV files",
        ["InvestigationViewModel.AddFiles"] = "chooses evidence files to import (read)",
        ["InvestigationViewModel.AddFolder"] = "chooses an evidence folder to import (read)",
        ["InvestigationViewModel.IntakePickFolder"] = "chooses the folder of brought evidence (read)",
        ["InvestigationViewModel.ImportRemotePackage"] = "reads a collected package (manifest verified)",
        ["InvestigationViewModel.AddHistoricalFolder"] = "reads archived logs / Windows.old (read)",
        ["PolicyViewModel.OpenFolder"] = "reads a GPO backup folder",
        ["TransferViewModel.VerifyFolder"] = "reads the medium to check; it is never modified",
        ["StationControlViewModel.VerifySignedReport"] = "opens a signed report to verify (read)",
        ["StationControlViewModel.PickFolder"] = "the picker helper itself: returns the folder; every caller that writes is checked here",
        ["StationControlViewModel.ChooseOfficerExportFolder"] = "only chooses and remembers the folder; writes nothing",
        ["MainViewModel.LoadFolderAsync"] = "reads a folder of logs or a storage medium (read)",
        ["MainViewModel.SelectCollectionOutputDir"] = "only chooses the folder; the writes of the legacy collector and of the syslog listener are audited in StartCollectionAsync / StartSyslog (ExportAudit.RunAsync / Run, finding B3)",
    };

    /// <summary>
    /// Where a folder is taken from: a folder dialog, the shared picker helper — also class- or namespace-qualified (re-review of PR #298), but not its
    /// declaration — or the folder memory.
    /// </summary>
    private static readonly Regex FolderPicker = new(@"new\s+(?:Microsoft\.Win32\.)?OpenFolderDialog\b|FolderBrowserDialog\b|(?<!(?:string\??|void|static|internal|private|public|protected)\s+)\b(?:[A-Za-z_]\w*\.)*PickFolder\(|FolderMemory\.Shared\.Get\(");

    [Fact]
    public void Every_write_to_a_folder_from_a_picker_or_from_the_folder_memory_is_audited_first()
    {
        var problems = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        int pickers = 0;
        foreach (var file in Directory.GetFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            var decls = MethodDecl.Matches(text).Cast<Match>().ToList();
            var cls = Path.GetFileNameWithoutExtension(file).Split('.')[0];
            foreach (Match m in FolderPicker.Matches(text))
            {
                pickers++;
                var decl = decls.LastOrDefault(d => d.Index < m.Index);
                var next = decls.FirstOrDefault(d => d.Index > m.Index);
                if (decl is null) { problems.Add($"{Path.GetFileName(file)}: folder picker outside a method"); continue; }
                var key = $"{cls}.{decl.Groups[1].Value}";
                seen.Add(key);
                var body = text[decl.Index..(next?.Index ?? text.Length)];
                if (ReadOnlyFolderPickers.ContainsKey(key)) continue;
                if (!Regex.IsMatch(body, @"ExportAudit\.Run(Bundle|Async)?\(")) problems.Add($"{key}: writes to a folder from a picker or from FolderMemory without ExportAudit.Run/RunBundle/RunAsync");
            }
        }
        Assert.True(pickers >= 10, $"only {pickers} folder pickers found: the scan is broken");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
        Assert.DoesNotContain(ReadOnlyFolderPickers.Keys, k => !seen.Contains(k));   // the allowlist stays exact
    }

    [Theory]
    [InlineData("var f = PickFolder(\"t\", k);")]
    [InlineData("var f = StationControlViewModel.PickFolder(\"t\", k);")]
    [InlineData("var f = LogAnalyzer.UI.ViewModels.StationControlViewModel.PickFolder(\"t\", k);")]
    [InlineData("var d = new OpenFolderDialog { Title = \"t\" };")]
    [InlineData("var d = new Microsoft.Win32.OpenFolderDialog();")]
    [InlineData("using var d = new System.Windows.Forms.FolderBrowserDialog();")]
    [InlineData("var last = FolderMemory.Shared.Get(FolderMemory.Evidence);")]
    [InlineData("var last = LogAnalyzer.Dfir.Language.FolderMemory.Shared.Get(k);")]
    public void The_folder_picker_guard_recognises_every_form_of_a_picker_call(string code) => Assert.Matches(FolderPicker, code);

    [Fact]
    public void The_folder_picker_guard_does_not_match_the_declaration_of_the_helper_or_other_names()
    {
        Assert.DoesNotMatch(FolderPicker, "internal static string? PickFolder(string title, string kind)");
        Assert.DoesNotMatch(FolderPicker, "await IntakePickFolder();");
    }

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

    // ---- finding B4: any raw file write in the application is audited, allow-listed with a reason, or goes to an app-owned path ----

    /// <summary>A write call of the file API that creates or changes a file (reads are not matched).</summary>
    internal static readonly Regex RawWrite = new(
        @"\bFile\.(?:Copy|Move|Replace|Create|OpenWrite|Write\w*|AppendAll\w*)\s*\(|new\s+(?:System\.IO\.)?(?:FileStream|StreamWriter)\s*\((?![^;\r\n]*FileAccess\.Read)|\bFile\.Open\s*\((?![^;\r\n]*FileAccess\.Read)");

    /// <summary>
    /// Methods with a raw file write that is neither a user export nor in need of ExportAudit.Run, with the reason. Everything else must call
    /// ExportAudit.Run / RunBundle / RunAsync in the same method. App-owned paths: LiveCase.Root, application data / settings, temp.
    /// </summary>
    private static readonly Dictionary<string, string> AppOwnedOrAuditedElsewhere = new(StringComparer.Ordinal)
    {
        ["SignedPolicyStartup.Load"] = "app data: the high-water mark of the signed policy version",
        ["ChainOfCustodyService.Append"] = "app data: the chain-of-custody log of the application itself",
        ["ProtectedSecretStore.Protect"] = "app data: DPAPI-protected secret in the application folder (temp file then move)",
        ["StartupLog.Append"] = "app data: the redacted startup log",
        ["DomainInvestigationViewModel.SaveText"] = "LiveCase.Root: evidence stored in the case and registered with its hash",
        ["MediaCenterViewModel.Log"] = "LiveCase.Root: media_live.jsonl inside the case",
        ["MediaHistoryFlows.Export"] = "audit-first through the audit callback (media_history.exported) before any byte is written",
        ["MediaHistoryFlows.ExportReport"] = "audit-first through the audit callback (report.generated, media_history.reconciled) before any byte is written",
        ["MediaRegisterFlows.Export"] = "helper called only from SignedMediaRegisterViewModel inside ExportAudit.Run",
        ["OfficerFlows.SignedPathFor"] = "OfficerFlows.SignReport (the scan attributes it to the preceding method): signed copy next to a report generated by the application, audited as report.officer_signed by the caller",
        ["StationControlViewModel.CopyWithSignature"] = "helper called only inside ExportAudit.Run / RunBundle",
        ["StationControlViewModel.ManageOfficers"] = "writes the approval into the application's own officer-requests folder (app data)",
    };

    [Fact]
    public void Every_raw_file_write_in_the_application_is_audited_or_allow_listed_with_a_reason()
    {
        var problems = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        int writes = 0;
        foreach (var file in Directory.GetFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = StripComments(File.ReadAllText(file));
            var decls = MethodDecl.Matches(text).Cast<Match>().ToList();
            var cls = Path.GetFileNameWithoutExtension(file).Split('.')[0];
            foreach (Match m in RawWrite.Matches(text))
            {
                writes++;
                var decl = decls.LastOrDefault(d => d.Index < m.Index);
                var next = decls.FirstOrDefault(d => d.Index > m.Index);
                if (decl is null) { problems.Add($"{Path.GetFileName(file)}: raw file write outside a method"); continue; }
                var key = $"{cls}.{decl.Groups[1].Value}";
                seen.Add(key);
                var body = text[decl.Index..(next?.Index ?? text.Length)];
                if (AppOwnedOrAuditedElsewhere.ContainsKey(key) || NotUnmaskedExports.ContainsKey(key)) continue;
                if (!Regex.IsMatch(body, @"ExportAudit\.Run(Bundle|Async)?\(")) problems.Add($"{key}: raw file write ({m.Value.Trim()}) without ExportAudit.Run/RunBundle/RunAsync and not allow-listed");
            }
        }
        Assert.True(writes >= 20, $"only {writes} raw writes found: the scan is broken");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
        Assert.DoesNotContain(AppOwnedOrAuditedElsewhere.Keys, k => !seen.Contains(k));   // the allowlist stays exact
    }

    private static string StripComments(string text) => Regex.Replace(text, @"^\s*//.*$", "", RegexOptions.Multiline);

    [Theory]
    [InlineData("File.WriteAllText(p, x);")]
    [InlineData("System.IO.File.WriteAllBytes(p, x);")]
    [InlineData("File.AppendAllText(p, x);")]
    [InlineData("File.Copy(a, b, true);")]
    [InlineData("File.Move(a, b);")]
    [InlineData("using var f = File.Create(p);")]
    [InlineData("using var f = File.OpenWrite(p);")]
    [InlineData("using var f = new FileStream(p, FileMode.Create);")]
    [InlineData("using var w = new StreamWriter(p);")]
    [InlineData("using var f = File.Open(p, FileMode.Create, FileAccess.Write);")]
    public void The_raw_write_guard_recognises_every_form_of_a_file_write(string code) => Assert.Matches(RawWrite, code);

    [Theory]
    [InlineData("var t = File.ReadAllText(p);")]
    [InlineData("using var f = new FileStream(p, FileMode.Open, FileAccess.Read);")]
    [InlineData("var ok = File.Exists(p);")]
    public void The_raw_write_guard_does_not_match_reads(string code) => Assert.DoesNotMatch(RawWrite, code);

    [Fact]
    public void The_raw_write_guard_flags_a_new_unaudited_write_in_a_method()
    {
        const string code = "private void Leak(string userPath)\n{\n    File.WriteAllText(userPath, data);\n}\n";
        Assert.True(MethodDecl.Match(code).Success);
        Assert.Matches(RawWrite, code);
        Assert.DoesNotMatch(new Regex(@"ExportAudit\.Run(Bundle|Async)?\("), code);
    }

    [Fact]
    public void The_legacy_collector_and_the_syslog_listener_write_through_the_export_audit()
    {
        var text = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        var decls = MethodDecl.Matches(text).Cast<Match>().ToList();
        foreach (var (name, call) in new[] { ("StartCollectionAsync", "ExportAudit.RunAsync("), ("StartSyslog", "ExportAudit.Run(") })
        {
            var decl = decls.Single(d => d.Groups[1].Value == name);
            var next = decls.First(d => d.Index > decl.Index);
            Assert.Contains(call, text[decl.Index..next.Index], StringComparison.Ordinal);
        }
    }

    // ---- finding B1: single items copied unmasked to the clipboard are audited (kind, length, SHA-256), never masked and never with the text ----

    /// <summary>Copies that carry no case data (explanations, anchor codes, hardware id): the reason they are not audited.</summary>
    private static readonly Dictionary<string, string> ClipboardWithoutCaseData = new(StringComparer.Ordinal)
    {
        ["PolicyViewModel.CopyExplanation"] = "explanation of a setting, no case data",
        ["StationControlViewModel.CopyExplanation"] = "explanation of a check, no case data",
        ["StationControlViewModel.CopyAnchorCode"] = "anchor code of the application audit, no case data",
        ["ActivationWindow.CopyHwid_Click"] = "hardware id of the station for activation, no case data",
    };

    [Fact]
    public void Every_clipboard_copy_of_case_data_goes_through_ExportAudit_Clipboard()
    {
        var problems = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        int copies = 0;
        foreach (var file in Directory.GetFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = StripComments(File.ReadAllText(file));
            var decls = MethodDecl.Matches(text).Cast<Match>().ToList();
            var cls = Path.GetFileNameWithoutExtension(file).Split('.')[0];
            foreach (Match m in Regex.Matches(text, @"\bClipboard\.Set\w+\("))
            {
                if (cls == "ExportAudit") continue;                  // the one place that really sets the clipboard
                copies++;
                var decl = decls.LastOrDefault(d => d.Index < m.Index);
                var key = decl is null ? file : $"{cls}.{decl.Groups[1].Value}";
                seen.Add(key);
                if (!ClipboardWithoutCaseData.ContainsKey(key)) problems.Add($"{key}: Clipboard.Set* called directly; use ExportAudit.Clipboard (audit) or allow-list with a reason");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
        Assert.DoesNotContain(ClipboardWithoutCaseData.Keys, k => !seen.Contains(k));
        Assert.True(copies >= 3, "the scan is broken");
        foreach (var f in new[] { Path.Combine("ViewModels", "MainViewModel.cs"), Path.Combine("ViewModels", "InvestigationViewModel.cs"), Path.Combine("ViewModels", "MediaCenterViewModel.cs"), Path.Combine("Views", "GenericDetailWindow.xaml.cs") })
            Assert.Contains("ExportAudit.Clipboard(", File.ReadAllText(Path.Combine(AppDir(), f)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_clipboard_copy_is_audited_with_length_and_hash_and_never_with_the_text()
    {
        AuthApp.Record("test.prime", "x");
        string? set = null;
        var old = ExportAudit.ClipboardSetter;
        ExportAudit.ClipboardSetter = t => set = t;
        try
        {
            const string secret = "S3CRET-case-value-\u00e9";
            Assert.Null(ExportAudit.Clipboard("CASE-1", "row_json", secret));
            Assert.Equal(secret, set);
            var entry = Audit().Single(e => e.Event == OperationAudit.ClipboardCopy);
            Assert.Contains("kind=row_json", entry.Detail);
            Assert.Contains($"length={secret.Length}", entry.Detail);
            Assert.Contains(OperationAudit.Sha256Hex(secret), entry.Detail);
            Assert.DoesNotContain("S3CRET", File.ReadAllText(AuthApp.Service.AuditPath));
        }
        finally { ExportAudit.ClipboardSetter = old; }
    }

    [Fact]
    public void A_clipboard_copy_is_refused_and_nothing_is_copied_when_the_audit_cannot_be_written()
    {
        AuthApp.Record("test.prime", "x");
        string? set = null;
        var old = ExportAudit.ClipboardSetter;
        ExportAudit.ClipboardSetter = t => set = t;
        try
        {
            string? refused;
            using (new FileStream(AuthApp.Service.AuditPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                refused = ExportAudit.Clipboard("CASE-1", "raw_event", "value");
            Assert.NotNull(refused);
            Assert.StartsWith(ExportAudit.RefusedPrefix, refused);
            Assert.Null(set);
        }
        finally { ExportAudit.ClipboardSetter = old; }
    }
}
