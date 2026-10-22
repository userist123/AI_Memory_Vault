using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LogAnalyzer.Dfir.AI;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Verification;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// R22.5, WP4, output side: AI text reaches the operator or a file only after the case-level validation, the validation is in the hash-chained audit and
/// bound to the evidence hashes, and an evidence change makes it stale. The registry test enumerates every production type that handles raw AI text;
/// an entry that is missing fails the test, and so does one that no longer applies. Fakes only: no model, no network.
/// </summary>
public sealed class AiClaimValidationReportTests : IDisposable
{
    private readonly AiCaseFixture _c = new();
    public void Dispose() => _c.Dispose();

    private InvestigationResult Result()
    {
        var r = new InvestigationResult { Case = _c.Lab.Ws };
        r.Findings.AddRange(_c.Lab.Findings);
        r.Timeline.AddRange(_c.Lab.Events);
        return r;
    }

    private List<CatalogItem> Catalog() =>
    [
        new CatalogItem("F-0001", "finding", "Proces suspect", _c.Finding.SupportingEvidence),
        new CatalogItem("E1", "event", "Security 4624", [_c.Lab.Ref("a", "rec-1")]),
    ];

    private static AiStatement Statement(string section, string text, params string[] cites) =>
        new(section, text, cites.Select(c => new Citation(c, c.StartsWith('F') ? "finding" : "event", [])).ToList());

    /// <summary>What a model said after the structural checks: one good, one partial, one wrong value, one invented id, one without citation (the last was rejected structurally).</summary>
    private AiReasoning Fake() => new("fake-model", "digest0123456789ab", "http://127.0.0.1:11434", "cat", "prompt", "resp", DateTimeOffset.UtcNow,
        [
            Statement("summary", $"Procesul evil.exe a rulat din {AiCaseFixture.ExePath} la 2025-03-01T10:00:05Z.", "F-0001"),
            Statement("hypotheses", "Poate fi un acces neautorizat.", "F-0001"),
            Statement("summary", "Procesul evil.exe a contactat 8.8.8.8.", "F-0001"),
            Statement("summary", "Fișierul evil.exe a fost găsit.", "EV-999999"),
            Statement("summary", "Logon de la 10.1.2.3 (eveniment 4624).", "E1"),
        ],
        [new RejectedStatement("summary", "Totul este curat.", [], "nu citează nicio probă")], 2, 0, "{\"raw\":true}");

    private static string[] States(AiValidationResult v) => v.Claims.Select(c => c.State.ToSpec()).ToArray();

    [Fact]
    public void The_model_output_is_validated_before_anything_is_written_and_a_NESUSTINUT_statement_is_never_accepted()
    {
        var r = Result();
        var ai = AiCaseAnalysis.ValidateAndStore(r, Fake(), Catalog());

        Assert.NotNull(ai.Validation);
        Assert.Equal(["SUSTINUT", "PARTIAL", "NESUSTINUT", "NESUSTINUT", "SUSTINUT", "NESUSTINUT"], States(ai.Validation!));
        Assert.Equal(3, ai.Accepted.Count);
        Assert.All(ai.Accepted, s => Assert.NotNull(s.Validation));
        Assert.DoesNotContain(ai.Accepted, s => s.Validation!.State == ClaimState.Nesustinut);
        Assert.Contains(ai.Rejected, x => x.Text.Contains("8.8.8.8") && x.Reason.StartsWith("validare la nivel de caz"));
        Assert.Contains(ai.Rejected, x => x.Text.Contains("EV-999999") || x.Cites.Contains("EV-999999"));
        Assert.Equal(ClaimState.Partial, ai.Accepted.Single(s => s.Section == "hypotheses").Validation!.State);

        // what is on disk is the same: no NESUSTINUT in the accepted list of ai_reasoning.json
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(r.Case.Root, "Analysis", "ai_reasoning.json")));
        var accepted = doc.RootElement.GetProperty("Accepted").EnumerateArray().ToList();
        Assert.Equal(3, accepted.Count);
        Assert.All(accepted, a => Assert.NotEqual("NESUSTINUT", a.GetProperty("Validation").GetProperty("State").GetString()));
        Assert.DoesNotContain("8.8.8.8", string.Join("", doc.RootElement.GetProperty("Accepted").EnumerateArray().Select(a => a.GetProperty("Text").GetString())));
    }

    [Fact]
    public void The_validation_is_written_to_the_case_bound_to_the_evidence_and_recorded_in_the_hash_chained_audit()
    {
        var r = Result();
        var ai = AiCaseAnalysis.ValidateAndStore(r, Fake(), Catalog());
        var path = AiValidationStore.PathIn(r.Case.Root);
        Assert.True(File.Exists(path));
        var saved = AiValidationStore.TryRead(r.Case.Root, out var err)!;
        Assert.Equal("", err);
        Assert.Equal(ai.Validation!.EvidenceBindingSha256, saved.EvidenceBindingSha256);
        Assert.Equal(AiCaseFacts.Binding(r.Case.Info.CaseId, r.Case.LoadEvidence()), saved.EvidenceBindingSha256);
        Assert.Equal(r.Case.Info.CaseId, saved.CaseId);
        Assert.Equal(6, saved.Claims.Count);
        Assert.Equal(States(ai.Validation!), States(saved));

        var audit = File.ReadAllText(r.Case.AuditChainPath);
        Assert.Contains("ai.claim_validation", audit);
        Assert.Contains($"binding={saved.EvidenceBindingSha256}", audit);
        Assert.Contains($"text={saved.TextSha256}", audit);
        Assert.Contains("NESUSTINUT=3", audit);
        var chains = r.Case.VerifyChains();
        Assert.Equal(ChainStatus.Valid, chains.Audit.Status);
        Assert.Equal(ChainStatus.Valid, chains.Custody.Status);

        // registered as a case output that depends on every evidence item
        var custody = File.ReadAllText(r.Case.CustodyJsonlPath);
        Assert.Contains("Analysis/ai_validation.json", custody);
        Assert.Contains("depends=" + _c.EvidenceId, custody);
    }

    [Fact]
    public void Evidence_that_changes_after_validation_marks_the_validation_stale_and_every_claim_NEVERIFICABIL()
    {
        var r = Result();
        AiCaseAnalysis.ValidateAndStore(r, Fake(), Catalog());
        var fresh = AiValidationText.Lines(r.Case.Root, AiCaseFactsFactory.ForResult(r));
        Assert.DoesNotContain(fresh, l => l.Text.Contains("ÎNVECHITĂ", StringComparison.OrdinalIgnoreCase));

        _c.Lab.Evidence("late");   // a new evidence item: the evidence set the validation was bound to is no longer the case's
        var stale = AiValidationStore.TryRead(r.Case.Root, out _)!.CheckAgainst(AiCaseFactsFactory.ForResult(r));
        Assert.True(stale.Stale);
        Assert.False(stale.AllSustained);
        Assert.All(stale.Claims, c => Assert.Equal(ClaimState.Neverificabil, c.State));
        Assert.Contains(stale.Claims, c => c.StateAtValidation == ClaimState.Sustinut);
        Assert.Contains("probele cazului s-au schimbat", stale.StaleReason);

        var lines = AiValidationText.Lines(r.Case.Root, AiCaseFactsFactory.ForResult(r));
        Assert.Contains(lines, l => l.Heading && l.Text.Contains("ÎNVECHITĂ"));
        Assert.DoesNotContain(lines, l => l.Text.StartsWith("[SUSTINUT]"));
    }

    [Fact]
    public void A_validation_made_for_another_case_or_with_a_changed_hash_is_stale()
    {
        var r = Result();
        var v = AiCaseAnalysis.ValidateAndStore(r, Fake(), Catalog()).Validation!;
        var other = new AiCaseFacts("CASE-ALTUL", v.EvidenceBindingSha256);
        Assert.True(v.CheckAgainst(other).Stale);
        var changedHash = new AiCaseFacts(v.CaseId, "00" + v.EvidenceBindingSha256[2..]);
        Assert.True(v.CheckAgainst(changedHash).Stale);
        Assert.False(v.CheckAgainst(new AiCaseFacts(v.CaseId, v.EvidenceBindingSha256)).Stale);
    }

    [Fact]
    public void Report_lines_mark_every_state_and_only_SUSTINUT_has_no_warning()
    {
        var r = Result();
        AiCaseAnalysis.ValidateAndStore(r, Fake(), Catalog());
        var lines = AiValidationText.Lines(r.Case.Root, AiCaseFactsFactory.ForResult(r));
        var head = lines.First();
        Assert.True(head.Heading);
        Assert.Contains("6 afirmații", head.Text);
        Assert.Contains("2 SUSTINUT", head.Text);
        Assert.Contains("1 PARTIAL", head.Text);
        Assert.Contains("3 NESUSTINUT", head.Text);
        Assert.True(head.Warning);   // not all sustained

        var statements = lines.Where(l => l.State is not null && !l.Heading).ToList();
        Assert.Equal(6, statements.Count);
        Assert.All(statements.Where(l => l.State == ClaimState.Sustinut), l => { Assert.False(l.Warning); Assert.False(l.Struck); Assert.StartsWith("[SUSTINUT]", l.Text); });
        Assert.All(statements.Where(l => l.State != ClaimState.Sustinut), l => Assert.True(l.Warning));
        var struck = statements.Where(l => l.Struck).ToList();
        Assert.Equal(3, struck.Count);
        Assert.All(struck, l => { Assert.StartsWith("[NESUSTINUT — nu este constatare]", l.Text); Assert.Contains("motiv:", l.Text); });
        Assert.Contains(statements, l => l.State == ClaimState.Partial && l.Text.Contains("avertisment"));
    }

    [Fact]
    public void Raw_AI_output_without_a_validation_prints_no_statement_and_says_why()
    {
        var r = Result();
        var dir = Path.Combine(r.Case.Root, "Analysis");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ai_reasoning.json"), "{\"Accepted\":[{\"Section\":\"summary\",\"Text\":\"Totul este curat.\"}]}");
        var lines = AiValidationText.Lines(r.Case.Root, AiCaseFactsFactory.ForResult(r));
        var line = Assert.Single(lines);
        Assert.Contains("fără validare la nivel de caz", line.Text);
        Assert.Contains("nicio afirmație AI nu este tipărită", line.Text);
        Assert.DoesNotContain("Totul este curat", line.Text);
        Assert.Equal(ClaimState.Neverificabil, line.State);
    }

    [Fact]
    public void An_unreadable_validation_file_prints_no_statement_and_says_why()
    {
        var r = Result();
        Directory.CreateDirectory(Path.Combine(r.Case.Root, "Analysis"));
        File.WriteAllText(AiValidationStore.PathIn(r.Case.Root), "{ not json");
        var line = Assert.Single(AiValidationText.Lines(r.Case.Root, AiCaseFactsFactory.ForResult(r)));
        Assert.Contains("nu a putut fi citită", line.Text);
        Assert.Equal(ClaimState.Neverificabil, line.State);
    }

    [Fact]
    public void A_case_without_AI_says_so_and_prints_nothing_else()
    {
        var line = Assert.Single(AiValidationText.Lines(Result().Case.Root, AiCaseFactsFactory.ForResult(Result())));
        Assert.Contains("nerulată", line.Text);
        Assert.Null(line.State);
    }

    [Fact]
    public void The_investigation_PDF_is_written_with_the_validated_AI_section()
    {
        var r = Result();
        AiCaseAnalysis.ValidateAndStore(r, Fake(), Catalog());
        var pdf = Path.Combine(_c.Lab.Dir, "inv.pdf");
        InvestigationReportPdf.Write(r, pdf, "test");
        Assert.True(new FileInfo(pdf).Length > 1000);
        // the report path takes its AI lines from the validator only (see the registry test for the IL guarantee)
        Assert.Contains(AiValidationText.Lines(r.Case.Root, AiCaseFactsFactory.ForResult(r)), l => l.Struck);
    }

    [Fact]
    public void The_verification_module_carries_the_claim_state_of_each_AI_statement()
    {
        var r = Result();
        AiCaseAnalysis.ValidateAndStore(r, Fake(), Catalog());
        var report = CaseVerifier.Evaluate(r.Case, new VerificationOptions());
        Assert.Equal(3, report.AiStatements.Count);
        Assert.Contains(report.AiStatements, s => s.ClaimValidation == "SUSTINUT");
        Assert.Contains(report.AiStatements, s => s.ClaimValidation == "PARTIAL");
        Assert.DoesNotContain(report.AiStatements, s => s.ClaimValidation == "NESUSTINUT");
        Assert.DoesNotContain(report.AiStatements, s => s.Text.Contains("8.8.8.8"));
    }

    [Fact]
    public void A_legacy_ai_reasoning_file_without_validation_is_carried_as_NEVERIFICABIL_by_the_verifier()
    {
        var r = Result();
        Directory.CreateDirectory(Path.Combine(r.Case.Root, "Analysis"));
        File.WriteAllText(Path.Combine(r.Case.Root, "Analysis", "ai_reasoning.json"),
            "{\"Accepted\":[{\"Section\":\"summary\",\"Text\":\"x\",\"Citations\":[{\"Item\":\"F-0001\",\"Kind\":\"finding\",\"Evidence\":[]}]}],\"Rejected\":[]}");
        var s = Assert.Single(CaseVerifier.Evaluate(r.Case, new VerificationOptions()).AiStatements);
        Assert.Equal("NEVERIFICABIL", s.ClaimValidation);
    }

    // ---------------------------------------------------------------- no network

    [Fact]
    public void The_validator_has_no_network_path()
    {
        var types = new[] { typeof(AiClaimValidator), typeof(AiCaseFacts), typeof(AiValidationStore), typeof(AiValidationText), typeof(AiValidationResult) };
        foreach (var t in types)
            foreach (var m in AllMethods(t))
                foreach (var (member, _) in Operands(m))
                    Assert.False(member?.DeclaringType?.Namespace?.StartsWith("System.Net") == true, $"{t.Name}.{m.Name} reaches {member?.DeclaringType?.FullName}");
    }

    // ---------------------------------------------------------------- registry of AI text paths

    private static readonly string[] ProductionAssemblies =
        ["LogAnalyzer.Ai", "LogAnalyzer.Dfir.Core", "LogAnalyzer.Dfir.Windows", "LogAnalyzer.Verification", "LogAnalyzer.Core", "LogAnalyzer.Response",
         "LogAnalyzer.Infrastructure", "LogAnalyzer.Connectors"];

    /// <summary>The types that carry raw AI text, and the file names that hold it.</summary>
    private static readonly string[] RawAiTypes =
        ["LogAnalyzer.Dfir.AI.AiStatement", "LogAnalyzer.Dfir.AI.AiReasoning", "LogAnalyzer.Dfir.AI.RejectedStatement", "LogAnalyzer.Verification.AiStatementVerdict"];
    private static readonly string[] RawAiFiles = ["ai_reasoning", "ai_raw_response", "ai_catalog"];
    private static readonly string[] Gate = ["LogAnalyzer.Dfir.AI.AiClaimValidator", "LogAnalyzer.Dfir.AI.AiValidationStore", "LogAnalyzer.Dfir.AI.AiValidationText"];

    /// <summary>
    /// Every production type that handles raw AI text (model statements, the verification verdicts on them, or the AI files), each with why it is allowed.
    /// A new type that touches raw AI text, for any output, fails the registry test until it is added here with its reason; a GATE entry must call the validator.
    /// The WPF screen (AiAnalysisViewModel) is covered in LogAnalyzer.App.Tests (AiClaimValidationUiTests).
    /// </summary>
    private static readonly Dictionary<string, (bool CallsValidator, string Why)> Registry = new()
    {
        ["LogAnalyzer.Dfir.AI.EvidenceReasoner"] = (false, "producer: structural checks of the model JSON; its output is consumed only by AiCaseAnalysis"),
        ["LogAnalyzer.Dfir.AI.LocalModelClient"] = (false, "producer: local model call over numeric loopback; returns raw statements to AiCaseAnalysis"),
        ["LogAnalyzer.Dfir.Windows.Investigation.AiCaseAnalysis"] = (true, "GATE: validates against the case, then writes ai_reasoning.json / ai_validation.json and the audit"),
        ["LogAnalyzer.Dfir.AI.AiValidationText"] = (false, "the report text itself; reads only the validation file, checks that ai_reasoning.json exists"),
        ["LogAnalyzer.Verification.AiClaims"] = (false, "reads the already validated ai_reasoning.json and carries each statement's claim state"),
        ["LogAnalyzer.Verification.CaseFacts"] = (false, "reads ai_reasoning.json for the verifier"),
        ["LogAnalyzer.Verification.CaseVerifier"] = (false, "writes verification.json; its AI statements come from AiClaims (validated file)"),
    };

    /// <summary>Every writer of a report, PDF or export, listed explicitly. The ones that print AI text must go through AiValidationText; the others must not touch raw AI text at all.</summary>
    private static readonly Dictionary<string, bool> OutputWriters = new()
    {
        ["LogAnalyzer.Dfir.Windows.Investigation.InvestigationReportPdf"] = true,
        ["LogAnalyzer.Dfir.Windows.Investigation.InvestigationReportText"] = false,
        ["LogAnalyzer.Dfir.Windows.Audit.ControlReportPdf"] = false,
        ["LogAnalyzer.Dfir.Windows.Audit.ChecksReportPdf"] = false,
        ["LogAnalyzer.Dfir.Windows.Audit.CrossStationReportPdf"] = false,
        ["LogAnalyzer.Dfir.Windows.Audit.OfficerRequestPdf"] = false,
        ["LogAnalyzer.Dfir.Windows.Audit.OfficerExport"] = false,
        ["LogAnalyzer.Dfir.Memory.VaultExport"] = false,
        ["LogAnalyzer.Dfir.Case.AnchorExport"] = false,
        ["LogAnalyzer.Dfir.Language.ReportText"] = false,
        ["LogAnalyzer.Dfir.Registers.MediaHistoryExport"] = false,
        ["LogAnalyzer.Dfir.Windows.Native.EventLogExport"] = false,
        ["LogAnalyzer.Dfir.Windows.Registers.TransferReportPdf"] = false,
    };

    private static List<Assembly> Load()
    {
        var loaded = new List<Assembly>(); var missing = new List<string>();
        foreach (var n in ProductionAssemblies)
        {
            try { loaded.Add(Assembly.Load(n)); } catch (FileNotFoundException ex) { missing.Add($"{n} ({ex.Message})"); }
        }
        Assert.True(missing.Count == 0, "Ansambluri de producție nescanate: " + string.Join("; ", missing));
        return loaded;
    }

    private static readonly Dictionary<short, OpCode> Ops =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    private static IEnumerable<MethodBase> AllMethods(Type t)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all))) yield return m;
        foreach (var n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) foreach (var m in AllMethods(n)) yield return m;
    }

    /// <summary>Every member, type or string an IL body refers to (decoded with the real opcode table, not byte matching).</summary>
    private static IEnumerable<(MemberInfo? Member, string? Str)> Operands(MethodBase m)
    {
        byte[]? il;
        try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch (Exception) { yield break; }
        if (il is null) yield break;
        var module = m.Module;
        Type[]? ta = m.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        Type[]? ma = m is MethodInfo { IsGenericMethod: true } mi ? mi.GetGenericArguments() : null;
        int i = 0;
        while (i < il.Length)
        {
            short code = il[i++];
            if (code == 0xFE) code = (short)(0xFE00 | il[i++]);
            if (!Ops.TryGetValue(code, out var op)) yield break;
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
            if (i + size > il.Length) yield break;
            switch (op.OperandType)
            {
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType:
                    MemberInfo? member = null;
                    try { member = module.ResolveMember(BitConverter.ToInt32(il, i), ta, ma); } catch (Exception) { }
                    yield return (member, null);
                    break;
                case OperandType.InlineString:
                    string? s = null;
                    try { s = module.ResolveString(BitConverter.ToInt32(il, i)); } catch (Exception) { }
                    yield return (null, s);
                    break;
            }
            i += size;
        }
    }

    private static Type Outer(Type t)
    {
        while ((t.IsDefined(typeof(CompilerGeneratedAttribute)) || t.Name.StartsWith('<')) && t.DeclaringType is not null) t = t.DeclaringType;
        while (t.DeclaringType is not null && t.IsNestedPrivate && t.IsDefined(typeof(CompilerGeneratedAttribute))) t = t.DeclaringType;
        return t;
    }

    private sealed record Touch(bool Raw, bool Gate);

    private static Dictionary<string, Touch> Scan()
    {
        var map = new Dictionary<string, Touch>();
        foreach (var asm in Load())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
            foreach (var t in types.Where(t => t.DeclaringType is null && !t.Name.StartsWith('<')))
            {
                bool raw = false, gate = false;
                foreach (var m in AllMethods(t))
                    foreach (var (member, str) in Operands(m))
                    {
                        var dt = member as Type ?? member?.DeclaringType;
                        if (dt?.FullName is { } n)
                        {
                            if (RawAiTypes.Contains(n) && Outer(dt) != t) raw = true;
                            if (Gate.Contains(n) && Outer(dt) != t) gate = true;
                        }
                        if (str is not null && RawAiFiles.Any(f => str.Contains(f, StringComparison.OrdinalIgnoreCase))) raw = true;
                    }
                if (raw || gate) map[t.FullName!] = new Touch(raw, gate);
            }
        }
        return map;
    }

    [Fact]
    public void Every_production_type_that_handles_raw_AI_text_is_in_the_explicit_registry()
    {
        var scan = Scan();
        var raw = scan.Where(kv => kv.Value.Raw).Select(kv => kv.Key).ToList();
        var missing = raw.Where(k => !Registry.ContainsKey(k) && !RawAiTypes.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "Tipuri care ating text AI brut fără a fi în registrul de ieșiri (și fără validator):\n" + string.Join("\n", missing));
        // the registry stays honest: an entry that no longer touches raw AI text is stale
        var stale = Registry.Keys.Where(k => !raw.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(stale.Count == 0, "Intrări vechi în registru (nu mai ating text AI brut):\n" + string.Join("\n", stale));
        // a gate entry must really call the validator
        foreach (var (type, entry) in Registry.Where(kv => kv.Value.CallsValidator))
            Assert.True(scan.TryGetValue(type, out var t) && t.Gate, $"{type} este poarta, dar nu apelează validatorul");
    }

    [Fact]
    public void The_registry_scan_really_finds_the_known_paths_so_an_empty_result_cannot_pass()
    {
        var scan = Scan();
        Assert.True(scan["LogAnalyzer.Dfir.Windows.Investigation.AiCaseAnalysis"].Raw);
        Assert.True(scan["LogAnalyzer.Dfir.Windows.Investigation.AiCaseAnalysis"].Gate);
        Assert.True(scan["LogAnalyzer.Verification.AiClaims"].Raw);
        Assert.True(scan["LogAnalyzer.Dfir.AI.LocalModelClient"].Raw);
        Assert.True(scan["LogAnalyzer.Dfir.Windows.Investigation.InvestigationReportPdf"].Gate);
        Assert.True(Registry.Count >= 7);
    }

    [Fact]
    public void Every_report_PDF_and_export_writer_either_goes_through_the_validator_or_never_touches_raw_AI_text()
    {
        var scan = Scan();
        var all = Load().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; } })
            .Where(t => t!.DeclaringType is null).Select(t => t!.FullName!).ToHashSet();
        foreach (var (writer, printsAi) in OutputWriters)
        {
            Assert.True(all.Contains(writer), $"Scriitor listat dar inexistent: {writer}");
            var touch = scan.GetValueOrDefault(writer);
            Assert.False(touch?.Raw == true, $"{writer} atinge text AI brut");
            Assert.Equal(printsAi, touch?.Gate == true);
        }
        // and nothing else that looks like a report/PDF/export writer is missing from the explicit list
        var unlisted = all.Where(n => (n.EndsWith("ReportPdf") || n.EndsWith("Export") || n.EndsWith("ReportText") || n.EndsWith("RequestPdf")) && !OutputWriters.ContainsKey(n)).OrderBy(n => n).ToList();
        Assert.True(unlisted.Count == 0, "Scriitori de rapoarte/exporturi care nu sunt în lista explicită (adăugați-i cu decizia lor privind textul AI):\n" + string.Join("\n", unlisted));
    }
}
