using System.IO;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Coverage;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Planner #12 B3: audit policy (auditpol CSV), Guest and local Administrators saved as hashed evidence, parsed (GUID match, English and Romanian), judged as facts with the raw
/// line as the quote, and checkable by hand. Fake runner and fake account source only; nothing here touches the real machine.
/// </summary>
public sealed class LocalSecurityStateTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private const string GProcess = "0CCE922B-69AE-11D9-BED3-505054503030", GLogon = "0CCE9215-69AE-11D9-BED3-505054503030", GCred = "0CCE923F-69AE-11D9-BED3-505054503030";
    private const string DomSid = "S-1-5-21-111-222-333";

    private static string Csv(string process, string logon, string cred, bool ro = false)
    {
        string head = ro ? "Nume computer,Țintă politică,Subcategorie,GUID subcategorie,Setare incluziune,Setare excludere" : "Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting";
        string n(string en, string rom) => ro ? rom : en;
        return "\r\n" + head + "\r\n" +
               $"WS-ALFA-07,System,{n("Security System Extension", "Extensie de sistem de securitate")},{{0CCE9211-69AE-11D9-BED3-505054503030}},{n("No Auditing", "Fără auditare")},\r\n" +
               $"WS-ALFA-07,System,{n("Logon", "Conectare")},{{{GLogon}}},{logon},\r\n" +
               $"WS-ALFA-07,System,{n("Process Creation", "Creare proces")},{{{GProcess}}},{process},\r\n" +
               $"WS-ALFA-07,System,{n("Credential Validation", "Validare acreditări")},{{{GCred}}},{cred},\r\n";
    }

    private static LocalSecuritySnapshot Snap(IReadOnlyList<LocalAccountEntry>? accounts = null, IReadOnlyList<AdminMemberEntry>? admins = null, bool domain = false,
                                              string interactive = @"WS-ALFA-07\Ana", string accountsError = "", string adminsError = "") =>
        new("WS-ALFA-07", domain, interactive, accounts, accountsError, admins, adminsError);

    private static readonly LocalAccountEntry Admin500 = new("Administrator", "S-1-5-21-1-2-3-500", 500, false, null, "");
    private static readonly LocalAccountEntry Ana = new("Ana", "S-1-5-21-1-2-3-1001", 1001, true, new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero), "");
    private static AdminMemberEntry Member(string name, string sid, string kind = "user", string scope = "local", string? dom = null) => new(name, dom ?? (scope == "local" ? "WS-ALFA-07" : "CORP"), sid, kind, scope);
    private static readonly AdminMemberEntry BuiltIn = Member("Administrator", "S-1-5-21-1-2-3-500");
    private static readonly AdminMemberEntry AnaMember = Member("Ana", "S-1-5-21-1-2-3-1001");

    private static ToolRun Run(string exe, IReadOnlyList<string> args, int exit, string stdout) =>
        new(exe, args, exit, stdout, "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

    private static ToolRunFunc Fake(string csv, int exit = 0, List<IReadOnlyList<string>>? calls = null) => (exe, args, logDir, label, _, _) =>
    {
        calls?.Add(args);
        Directory.CreateDirectory(logDir);
        var p = Path.Combine(logDir, label + ".stdout.txt");
        File.WriteAllText(p, csv);
        return Run(exe, args, exit, p);
    };

    private CollectorOutcome Collect(ToolRunFunc run, LocalSecuritySnapshot snap, bool elevated, out CaseWorkspace ws)
    {
        ws = _k.Case();
        return new LocalSecurityStateCollector(run, () => snap).Collect(new CollectorContext { Case = ws, IsElevated = elevated }, default);
    }

    /// <summary>Collects, then parses every stored item with its own parser and returns the timeline with the case.</summary>
    private (List<TimelineEvent> Rows, CaseWorkspace Ws, List<ParseResult> Parsed) Pipeline(string csv, LocalSecuritySnapshot snap, bool elevated = true)
    {
        var o = Collect(Fake(csv), snap, elevated, out var ws);
        var sink = new ListSink();
        var parsed = new List<ParseResult>();
        foreach (var e in o.Evidence)
        {
            IEvidenceParserLike p = e.SourceType == "auditpol_csv" ? new Wrap(new AuditPolicyCsvParser()) : new Wrap(new LocalAccountsParser());
            parsed.Add(p.Parse(e, ws.FullPath(e.StoredPath), sink));
        }
        return (sink.Events, ws, parsed);
    }

    private interface IEvidenceParserLike { ParseResult Parse(EvidenceItem e, string full, ListSink sink); }
    private sealed class Wrap(LogAnalyzer.Dfir.Parsing.IEvidenceParser p) : IEvidenceParserLike
    {
        public ParseResult Parse(EvidenceItem e, string full, ListSink sink) => p.Parse(e, full, sink, default);
    }

    private static ConfigAuditResult Judge(List<TimelineEvent> rows) { int n = 0; return LocalSecurityAudit.Evaluate(rows, "WS-ALFA-07", () => $"F-{++n:D4}"); }

    // ---- collector ----

    [Fact]
    public void Collector_registers_both_evidence_types_hashed_with_the_exact_auditpol_command()
    {
        var calls = new List<IReadOnlyList<string>>();
        var csv = Csv("No Auditing", "Success and Failure", "Success and Failure");
        var ws0 = _k.Case();
        var o = new LocalSecurityStateCollector(Fake(csv, 0, calls), () => Snap([Admin500, Ana], [BuiltIn, AnaMember])).Collect(new CollectorContext { Case = ws0, IsElevated = true }, default);
        Assert.Equal(["/get", "/category:*", "/r"], Assert.Single(calls));
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Contains("auditpol.exe", o.Commands[0]);
        Assert.Equal(["auditpol_csv", "local_accounts_json"], o.Evidence.Select(e => e.SourceType));
        Assert.All(o.Evidence, e => { Assert.False(string.IsNullOrEmpty(e.Sha256)); Assert.Equal(TemporalType.CurrentSnapshot, e.TemporalType); });
        Assert.Equal(csv, File.ReadAllText(ws0.FullPath(o.Evidence[0].StoredPath)));
        Assert.Equal(EvidenceFingerprint.Detect(ws0.FullPath(o.Evidence[0].StoredPath)), "auditpol_csv");
        Assert.Equal("json", EvidenceFingerprint.Detect(ws0.FullPath(o.Evidence[1].StoredPath)));
    }

    [Fact]
    public void Not_elevated_keeps_the_accounts_snapshot_and_reports_auditpol_as_a_gap_reason()
    {
        var calls = new List<IReadOnlyList<string>>();
        var o = Collect(Fake("x", 0, calls), Snap([Ana], [AnaMember]), false, out _);
        Assert.Empty(calls);
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Equal("local_accounts_json", Assert.Single(o.Evidence).SourceType);
        Assert.Contains("drepturi de administrator", Assert.Single(o.Errors));
    }

    [Fact]
    public void Failing_auditpol_or_unreadable_accounts_are_errors_never_silent()
    {
        var o = Collect(Fake("", 1), Snap([Ana], [AnaMember]), true, out _);
        Assert.Equal(EvidenceStatus.Partial, o.Status);
        Assert.Contains("auditpol: exit 1", Assert.Single(o.Errors));
        Assert.Equal("local_accounts_json", Assert.Single(o.Evidence).SourceType);

        var o2 = Collect(Fake("", 1), Snap(null, null, accountsError: "WMI refuzat", adminsError: "WMI refuzat"), true, out _);
        Assert.Equal(EvidenceStatus.Failed, o2.Status);
        Assert.Empty(o2.Evidence);
        Assert.Equal(3, o2.Errors.Count);
        Assert.Contains(o2.Errors, e => e.Contains("Conturi locale: WMI refuzat"));

        var o3 = Collect((_, _, _, _, _, _) => throw new IOException("disc plin"), Snap([Ana], [AnaMember]), true, out _);
        Assert.Contains(o3.Errors, e => e.Contains("disc plin"));

        var o4 = new LocalSecurityStateCollector(Fake(Csv("No Auditing", "No Auditing", "No Auditing")), () => throw new InvalidOperationException("WMI oprit"));
        var ws = _k.Case();
        var r4 = o4.Collect(new CollectorContext { Case = ws, IsElevated = true }, default);
        Assert.Equal(EvidenceStatus.Partial, r4.Status);
        Assert.Contains(r4.Errors, e => e.Contains("WMI oprit"));
    }

    [Fact]
    public void Garbage_auditpol_output_is_not_stored_as_evidence()
    {
        var o = Collect(Fake("Error 0x00000522 A required privilege is not held"), Snap([Ana], [AnaMember]), true, out _);
        Assert.Equal("local_accounts_json", Assert.Single(o.Evidence).SourceType);
        Assert.Contains(o.Errors, e => e.Contains("nicio subcategorie"));
    }

    [Fact]
    public void Collector_profile_pipeline_registration_and_parsers_are_wired()
    {
        var c = new LocalSecurityStateCollector();
        Assert.Equal(CollectionProfile.QuickAndUp, c.Profiles);   // planner #13 D1: small and fast, so the Quick triage also saves the audit policy and the accounts
        Assert.True(c.RequiresElevation);
        Assert.Single(InvestigationPipeline.AllCollectors, x => x is LocalSecurityStateCollector);
        Assert.Contains(InvestigationPipeline.AllCollectors.Where(x => (x.Profiles & CollectionProfile.Quick) != 0), x => x is LocalSecurityStateCollector);
        Assert.Contains(WindowsParsers.Registry.Descriptors, d => d.ParserId == "AuditPolicyCsvParser");
        Assert.Contains(WindowsParsers.Registry.Descriptors, d => d.ParserId == "LocalAccountsParser");
        Assert.NotNull(ParserCapabilities.For("AuditPolicyCsvParser"));
        Assert.NotNull(ParserCapabilities.For("LocalAccountsParser"));
        var row = CoverageMatrix.Build(new CoverageInputs([], [], [new CollectorRun("LocalSecurityStateCollector", EvidenceStatus.Failed, 0, "auditpol: exit 1")], [], WindowsParsers.Registry.Descriptors.ToList())).Rows.Single(r => r.FamilyId == "local_security");
        Assert.Contains("LocalSecurityStateCollector", row.Reason);
        foreach (var id in new[] { "CFG-AUDIT-PROCESS", "CFG-AUDIT-LOGON", "CFG-AUDIT-CREDVAL", "CFG-GUEST-ENABLED", "CFG-EXTRA-ADMINS" })
            Assert.NotNull(RuleContracts.For(id));
    }

    // ---- parsers ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Auditpol_parser_gives_one_row_per_subcategory_matched_by_guid_in_english_and_romanian(bool ro)
    {
        string none = ro ? "Fără auditare" : "No Auditing", both = ro ? "Succes și eșec" : "Success and Failure", ok = ro ? "Succes" : "Success";
        var csv = Csv(none, ok, both, ro);
        var (rows, _, parsed) = Pipeline(csv, Snap([Ana], [AnaMember]));
        var audit = rows.Where(r => r.Source == "auditpol").ToList();
        Assert.Equal(4, audit.Count);
        Assert.Equal(EvidenceStatus.Success, parsed[0].Status);
        var proc = audit.Single(r => r.Locator == "auditpol:" + GProcess);
        Assert.Equal("nu", proc.Fields["AuditSuccess"]);
        Assert.Equal("nu", proc.Fields["AuditFailure"]);
        Assert.Equal(ro ? "Creare proces" : "Process Creation", proc.Fields["Subcategory"]);
        Assert.Contains($"{{{GProcess}}}", proc.Fields["RawLine"]);
        Assert.Contains(proc.Fields["RawLine"], csv.Split("\r\n"));   // RawLine is the CSV line as stored
        var logon = audit.Single(r => r.Locator == "auditpol:" + GLogon);
        Assert.Equal("da", logon.Fields["AuditSuccess"]);
        Assert.Equal("nu", logon.Fields["AuditFailure"]);
        Assert.Equal("da", audit.Single(r => r.Locator == "auditpol:" + GCred).Fields["AuditFailure"]);
        Assert.All(audit, r => Assert.Equal(SemanticType.Configuration, r.SemanticType));
    }

    [Fact]
    public void Accounts_parser_gives_rows_with_locators_and_the_json_object_text_as_raw_line()
    {
        var (rows, ws, parsed) = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"), Snap([Admin500, Ana], [BuiltIn, AnaMember]));
        Assert.All(parsed, p => Assert.Equal(EvidenceStatus.Success, p.Status));
        var stored = File.ReadAllText(ws.FullPath(ws.LoadEvidence().Single(e => e.SourceType == "local_accounts_json").StoredPath));
        var acc = rows.Where(r => r.Source == "Conturi locale" && r.Locator.StartsWith("LocalAccounts[")).ToList();
        Assert.Equal(["LocalAccounts[Administrator]", "LocalAccounts[Ana]"], acc.Select(r => r.Locator));
        var ana = acc[1];
        Assert.Equal("da", ana.Fields["Enabled"]);
        Assert.Equal("1001", ana.Fields["Rid"]);
        Assert.StartsWith("{\"name\":\"Ana\"", ana.Fields["RawLine"]);
        Assert.Contains(ana.Fields["RawLine"], stored.Split('\n').Select(l => l.TrimEnd(',', '\r')));   // the quote is a line of the stored file
        var adm = rows.Where(r => r.Locator.StartsWith("LocalAdministrators[")).ToList();
        Assert.Equal([@"LocalAdministrators[WS-ALFA-07\Administrator]", @"LocalAdministrators[WS-ALFA-07\Ana]"], adm.Select(r => r.Locator));
        Assert.Equal(@"WS-ALFA-07\Ana", adm[1].Fields["Member"]);
    }

    [Fact]
    public void Accounts_parser_reports_a_part_that_was_not_read_as_a_gap_not_an_empty_list()
    {
        var (rows, _, parsed) = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"), Snap([Ana], null, adminsError: "Win32_GroupUser: acces refuzat"));
        Assert.DoesNotContain(rows, r => r.Locator.StartsWith("LocalAdministrators["));
        var gap = Assert.Single(parsed.Single(p => p.Parser == "LocalAccountsParser").Gaps);
        Assert.Contains("acces refuzat", gap.Reason);
        Assert.Empty(Judge(rows).Findings);
    }

    // ---- findings ----

    [Fact]
    public void Unsafe_states_give_the_five_findings_with_severities_quote_time_source_and_channel()
    {
        var snap = Snap([Admin500, Ana, new LocalAccountEntry("Oaspete", "S-1-5-21-1-2-3-501", 501, true, null, "")],
                        [BuiltIn, AnaMember, Member("Bogdan", "S-1-5-21-1-2-3-1005")]);
        var (rows, _, _) = Pipeline(Csv("No Auditing", "Success", "Failure"), snap);
        var res = Judge(rows);
        Assert.Empty(res.Gaps);
        Assert.Equal(["CFG-AUDIT-PROCESS", "CFG-AUDIT-LOGON", "CFG-AUDIT-CREDVAL", "CFG-GUEST-ENABLED", "CFG-EXTRA-ADMINS"], res.Findings.Select(f => f.RuleId));
        Assert.Equal([Severity.Medium, Severity.Medium, Severity.Medium, Severity.High, Severity.Medium], res.Findings.Select(f => f.Severity));
        Assert.All(res.Findings, f => { Assert.Equal(SemanticType.Configuration, f.SemanticType); Assert.Equal("Configurație", f.Category); });

        var proc = res.Findings[0];
        var e = Assert.Single(proc.SupportingEvidence);
        Assert.Equal("auditpol:" + GProcess, e.Locator);
        Assert.Contains($"{{{GProcess}}}", e.Quote);
        Assert.Equal("Instantaneu la colectare", e.TimeSource);
        Assert.Equal("auditpol", e.Channel);
        Assert.Contains("4688", proc.Description);
        Assert.Contains("lipsește auditarea pentru eșec", res.Findings[1].Description);      // Logon audits Success only: Failure is what is missing
        Assert.Contains("lipsește auditarea pentru succes", res.Findings[2].Description);    // Credential Validation audits Failure only

        var guest = res.Findings[3];
        Assert.Contains("Oaspete", guest.Title);
        var ge = Assert.Single(guest.SupportingEvidence);
        Assert.Equal("LocalAccounts[Oaspete]", ge.Locator);
        Assert.StartsWith("{\"name\":\"Oaspete\"", ge.Quote);
        Assert.Equal("Conturi locale", ge.Channel);
        Assert.Equal("Instantaneu la colectare", ge.TimeSource);

        var admins = res.Findings[4];
        Assert.Contains(@"WS-ALFA-07\Ana", admins.Description);
        Assert.Contains(@"WS-ALFA-07\Bogdan", admins.Description);
        Assert.DoesNotContain("Administrator (", admins.Description);
        Assert.Contains("verificați dacă fiecare cont are nevoie de drepturi de administrator", admins.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, admins.SupportingEvidence.Count);
        Assert.All(admins.SupportingEvidence, r => Assert.Equal("Conturi locale", r.Channel));
        Assert.All(res.Findings, f => Assert.DoesNotContain(new[] { "neconform", "conform" }, w => (f.Title + f.Description).Contains(w, StringComparison.OrdinalIgnoreCase)));

        // the binder rebinds from the same row and must give the very same quote / time source / channel
        var index = EvidenceQuote.Index(rows);
        var refs = proc.SupportingEvidence.ToList();
        EvidenceQuote.Bind(refs, index);
        Assert.Equal(e.Quote, refs[0].Quote); Assert.Equal(e.TimeSource, refs[0].TimeSource); Assert.Equal(e.Channel, refs[0].Channel);
    }

    [Fact]
    public void Safe_states_give_no_findings_and_no_gaps()
    {
        var domainAdmins = Member("Domain Admins", DomSid + "-512", "group", "domain");
        var (rows, _, _) = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"),
            Snap([Admin500, Ana, new LocalAccountEntry("Guest", "S-1-5-21-1-2-3-501", 501, false, null, "")], [BuiltIn, domainAdmins], domain: true));
        var res = Judge(rows);
        Assert.Empty(res.Findings);
        Assert.Empty(res.Gaps);
        // Process Creation with Success only is enough for 4688 (Failure is not required)
        Assert.DoesNotContain(res.Findings, f => f.RuleId == "CFG-AUDIT-PROCESS");
    }

    [Fact]
    public void Renamed_builtin_administrator_is_recognised_by_rid_500_not_by_name()
    {
        var renamed = Member("Admin-local", "S-1-5-21-1-2-3-500");
        var (rows, _, _) = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"), Snap([Ana], [renamed]));
        Assert.Empty(Judge(rows).Findings);
    }

    [Fact]
    public void The_interactive_user_as_the_only_extra_administrator_is_low_otherwise_medium()
    {
        var (rows, _, _) = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"), Snap([Ana], [BuiltIn, AnaMember]));
        var f = Assert.Single(Judge(rows).Findings);
        Assert.Equal("CFG-EXTRA-ADMINS", f.RuleId);
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Contains("verificați dacă fiecare cont are nevoie de drepturi de administrator", f.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(f.AlternativeExplanations, a => a.Contains("normală"));

        // another single member is not the interactive user: Medium
        var (rows2, _, _) = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"), Snap([Ana], [BuiltIn, Member("Bogdan", "S-1-5-21-1-2-3-1005")]));
        Assert.Equal(Severity.Medium, Assert.Single(Judge(rows2).Findings).Severity);

        // a domain account that is the interactive user
        var (rows3, _, _) = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"), Snap([Ana], [BuiltIn, Member("ana.pop", DomSid + "-1104", "user", "domain")], domain: true, interactive: @"CORP\ana.pop"));
        Assert.Equal(Severity.Low, Assert.Single(Judge(rows3).Findings).Severity);

        // a domain PC: a group other than Domain Admins is listed
        var (rows4, _, _) = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"), Snap([Ana], [BuiltIn, Member("Domain Admins", DomSid + "-512", "group", "domain"), Member("Helpdesk", DomSid + "-2001", "group", "domain")], domain: true));
        var f4 = Assert.Single(Judge(rows4).Findings);
        Assert.Contains("Helpdesk", f4.Description);
        Assert.DoesNotContain("Domain Admins (", f4.Description);
    }

    [Fact]
    public void An_unreadable_or_missing_setting_is_a_gap_never_a_finding()
    {
        var csv = "Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting\r\n" +
                  $"H,System,Logon,{{{GLogon}}},Aucun audit configurat,\r\n" +
                  $"H,System,Process Creation,{{{GProcess}}},Success,\r\n";
        var (rows, _, _) = Pipeline(csv, Snap([Ana], [BuiltIn]));
        var res = Judge(rows);
        Assert.Empty(res.Findings);
        Assert.Equal(2, res.Gaps.Count);
        Assert.Contains(res.Gaps, g => g.Artifact.Contains("Logon") && g.Reason.Contains("nu a putut fi interpretat"));
        Assert.Contains(res.Gaps, g => g.Artifact.Contains("Credential Validation") && g.Reason.Contains("nu apare"));
    }

    [Fact]
    public void Several_snapshots_judge_only_the_newest()
    {
        var old = Pipeline(Csv("No Auditing", "No Auditing", "No Auditing"), Snap([Ana], [BuiltIn])).Rows;
        var cur = Pipeline(Csv("Success", "Success and Failure", "Success and Failure"), Snap([Ana], [BuiltIn])).Rows;
        var older = old.Select(r => new TimelineEvent
        {
            Time = Timestamp.FromUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "", "test"), Source = r.Source, EvidenceId = "OLD-1", Locator = r.Locator, Summary = r.Summary, Fields = r.Fields,
        }).ToList();
        Assert.NotEmpty(Judge(older).Findings);
        Assert.Empty(Judge([.. older, .. cur]).Findings);
    }

    // ---- manual verification ----

    [Fact]
    public void Manual_verification_steps_use_auditpol_net_user_and_the_sid_based_group_commands()
    {
        var snap = Snap([Admin500, new LocalAccountEntry("Oaspete", "S-1-5-21-1-2-3-501", 501, true, null, "")], [BuiltIn, Member("Bogdan", "S-1-5-21-1-2-3-1005")]);
        var (rows, ws, _) = Pipeline(Csv("No Auditing", "Success and Failure", "Success and Failure", ro: true), snap);
        var res = Judge(rows);
        var items = ws.LoadEvidence();

        EvidenceItem Item(EvidenceRef r) => items.Single(i => i.EvidenceId == r.EvidenceId);
        TimelineEvent Row(EvidenceRef r) => rows.Single(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator);

        var audit = res.Findings.Single(f => f.RuleId == "CFG-AUDIT-PROCESS").SupportingEvidence[0];
        var steps = ManualVerification.For(audit, Item(audit), ws.Root, Row(audit));
        Assert.Equal("auditpol /get /subcategory:\"Creare proces\"", steps.First(s => s.IsCommand).Action);   // the localized name of this station
        Assert.Contains(steps, s => s.Tool == "Explicație");
        Assert.DoesNotContain(steps, s => s.Tool == ManualVerification.SpecialistTool);
        var byGuid = ManualVerification.For(audit, Item(audit), ws.Root);   // without the row: the GUID is used
        Assert.Equal($"auditpol /get /subcategory:\"{{{GProcess}}}\"", byGuid.First(s => s.IsCommand).Action);

        var guest = res.Findings.Single(f => f.RuleId == "CFG-GUEST-ENABLED").SupportingEvidence[0];
        var gs = ManualVerification.For(guest, Item(guest), ws.Root, Row(guest));
        Assert.Equal("net user \"Oaspete\"", gs.First(s => s.IsCommand).Action);
        Assert.Contains("Account active", gs.First(s => s.IsCommand).Compare);
        Assert.Contains(gs, s => s.Action.Contains("-501"));

        var adm = res.Findings.Single(f => f.RuleId == "CFG-EXTRA-ADMINS").SupportingEvidence[0];
        var asteps = ManualVerification.For(adm, Item(adm), ws.Root, Row(adm));
        Assert.Equal("Get-LocalGroupMember -SID S-1-5-32-544 | Format-Table Name,SID,ObjectClass,PrincipalSource", asteps.First(s => s.IsCommand).Action);
        Assert.Contains(asteps, s => s.Action == "net localgroup Administrators");
        Assert.All(new[] { steps, gs, asteps }, list => Assert.Contains(list, s => s.Action.StartsWith("Get-FileHash")));
    }
}
