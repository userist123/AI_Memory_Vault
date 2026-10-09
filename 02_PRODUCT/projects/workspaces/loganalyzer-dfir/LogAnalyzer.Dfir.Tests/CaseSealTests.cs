using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// WP15a, owner decisions 28 and 30: the chain head hashes are printed in the footer of every case PDF, written in the export manifest and
/// shown at case close; a provisional scope is labelled in every report and export until the operator confirms it.
/// </summary>
[Collection("ReportFooterObserver")]
public sealed class CaseSealTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "la-seal-" + Guid.NewGuid().ToString("N"));
    public CaseSealTests() => Directory.CreateDirectory(_root);
    public void Dispose()
    {
        ReportFooter.Observer.Value = null;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private static CaseScope Provisional()
    {
        var v = TestScopes.Valid();
        return new CaseScope { Purpose = v.Purpose, PeriodFromUtc = v.PeriodFromUtc, PeriodToUtc = v.PeriodToUtc, SystemsInScope = v.SystemsInScope, Approver = "necunoscut (de confirmat)",
                               LegalBasis = v.LegalBasis, Network = v.Network, Classification = v.Classification, Provisional = true };
    }

    private CaseWorkspace NewCase(string name, CaseScope? scope = null) =>
        CaseWorkspace.Create(Path.Combine(_root, name), new CaseInfo { CaseId = "CASE-" + name, Name = name, CreatedAtUtc = DateTimeOffset.UtcNow, Scope = scope ?? TestScopes.Valid() });

    private static List<(string Kind, string Text)> Capture() { var l = new List<(string, string)>(); ReportFooter.Observer.Value = (k, t) => { lock (l) l.Add((k, t)); }; return l; }

    // ---- seal text ----

    [Fact]
    public void Seal_lists_both_chain_heads_in_full_and_the_provisional_scope_label()
    {
        var ws = NewCase("a", Provisional());
        var seal = ReportSeal.For(ws);
        var anchor = ws.Anchor();
        var text = string.Join("\n", seal.Lines());
        Assert.Contains(anchor.CustodyHead, text); Assert.Contains(anchor.AuditHead, text);
        Assert.Contains($"Seq {anchor.AuditSeq}", text);
        Assert.Contains("scop provizoriu, neconfirmat", text);
        Assert.DoesNotContain("scop provizoriu", string.Join("\n", ReportSeal.For(NewCase("b")).Lines()));
    }

    [Fact]
    public void Seal_without_a_case_says_the_anchor_is_unavailable_instead_of_omitting_it()
    {
        Assert.Contains("indisponibilă", string.Join("\n", new ReportSeal(null, null).Lines()));
    }

    // ---- footers ----

    [Fact]
    public void Investigation_report_footer_carries_the_anchor_of_its_case()
    {
        var ws = NewCase("inv");
        var seen = Capture();
        InvestigationReportPdf.Write(new InvestigationResult { Case = ws }, Path.Combine(_root, "inv.pdf"), "test");
        var a = ws.Anchor();
        var footer = Assert.Single(seen, x => x.Kind == "investigation").Text;
        Assert.Contains(a.CustodyHead, footer); Assert.Contains(a.AuditHead, footer);
        Assert.True(File.Exists(Path.Combine(_root, "inv.pdf")));
    }

    [Fact]
    public void Control_report_footer_carries_the_anchor_and_the_provisional_label()
    {
        var ws = NewCase("ctl", Provisional());
        var seen = Capture();
        ControlReportPdf.SaveToCase(ControlEvaluator.Evaluate(new StationFacts { Host = "H", CollectedUtc = DateTimeOffset.UtcNow }), ws, "insp");
        var footer = Assert.Single(seen, x => x.Kind == "control").Text;
        Assert.Matches("[0-9a-f]{64}", footer);
        Assert.Contains("scop provizoriu, neconfirmat", footer);
    }

    [Fact]
    public void Checks_report_footer_carries_the_anchor_when_given_a_seal_and_says_so_when_not()
    {
        var ws = NewCase("chk");
        var seen = Capture();
        ChecksReportPdf.Write(Path.Combine(_root, "c1.pdf"), "t", "s", ReportSeal.For(ws), [], ["n"]);
        ChecksReportPdf.Write(Path.Combine(_root, "c2.pdf"), "t", "s", [], ["n"]);
        var a = ws.Anchor();
        Assert.Contains(a.CustodyHead, seen[0].Text); Assert.Contains(a.AuditHead, seen[0].Text);
        Assert.All(seen, x => Assert.Equal("checks", x.Kind));
        Assert.Contains("indisponibilă", seen[1].Text);
    }

    // ---- export manifest ----

    [Fact]
    public void Export_manifest_carries_the_anchor_and_the_scope_label_while_provisional()
    {
        var ws = NewCase("man", Provisional());
        File.WriteAllText(Path.Combine(ws.Root, "Exports", "a.txt"), "a");
        ws.WriteManifest("Exports/export_manifest.json", ["Exports/a.txt"], "Test", "1");
        var m = JsonDocument.Parse(File.ReadAllText(ws.FullPath("Exports/export_manifest.json"))).RootElement;
        Assert.Equal(64, m.GetProperty("anchor").GetProperty("custodyHead").GetString()!.Length);
        Assert.Contains("scop provizoriu, neconfirmat", m.GetProperty("scopeNote").GetString());

        var ok = NewCase("man2");
        File.WriteAllText(Path.Combine(ok.Root, "Exports", "a.txt"), "a");
        ok.WriteManifest("Exports/export_manifest.json", ["Exports/a.txt"], "Test", "1");
        Assert.Equal("", JsonDocument.Parse(File.ReadAllText(ok.FullPath("Exports/export_manifest.json"))).RootElement.GetProperty("scopeNote").GetString());
    }

    // ---- case close ----

    [Fact]
    public void Closing_a_case_shows_the_final_heads_for_the_custody_register()
    {
        var ws = NewCase("close");
        var closure = CaseClosure.Close(ws, "inspector");
        Assert.Equal(ws.Anchor(), closure.Anchor);   // nothing was written after the heads were read
        Assert.Contains(closure.Anchor.CustodyHead, closure.RegisterText); Assert.Contains(closure.Anchor.AuditHead, closure.RegisterText);
        Assert.Contains("semnătură", closure.RegisterText); Assert.Contains("CASE-close", closure.RegisterText);
        Assert.Contains("case.closed", File.ReadAllText(ws.AppAuditLogPath));
        Assert.True(ws.CheckAnchor(closure.Anchor).Ok);
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Audit.Status);
    }

    [Fact]
    public void Closing_with_a_provisional_scope_says_the_scope_is_unconfirmed()
    {
        var closure = CaseClosure.Close(NewCase("closeP", Provisional()), "inspector");
        Assert.Contains("scop provizoriu, neconfirmat", closure.RegisterText);
    }

    // ---- decision 28: provisional -> confirmed ----

    [Fact]
    public void Confirming_a_provisional_scope_is_audited_persisted_and_removes_the_label()
    {
        var ws = NewCase("conf", Provisional());
        Assert.Contains("scop provizoriu", ws.ScopeNote);
        var confirmed = TestScopes.Valid();
        ws.ConfirmScope(confirmed, "inspector");
        Assert.Null(ws.ScopeNote);
        Assert.False(ws.Info.Scope.Provisional);
        Assert.Equal("approver-1", ws.Info.Scope.Approver);
        var audit = File.ReadAllLines(ws.AuditChainPath).Where(l => l.Contains("case.scope_confirmed")).ToList();
        var line = Assert.Single(audit);
        Assert.Contains("approver-1", line); Assert.Contains("necunoscut (de confirmat)", line);   // before and after, so the replacement is explainable
        var reopened = CaseWorkspace.Open(ws.Root);
        Assert.False(reopened.Info.Scope.Provisional); Assert.Null(reopened.ScopeNote);
        Assert.Equal(ChainStatus.Valid, reopened.VerifyChains().Audit.Status);
    }

    [Fact]
    public void Confirming_refuses_an_incomplete_or_still_provisional_scope()
    {
        var ws = NewCase("conf2", Provisional());
        Assert.Throws<CaseScopeIncompleteException>(() => ws.ConfirmScope(new CaseScope(), "x"));
        Assert.Throws<ArgumentException>(() => ws.ConfirmScope(Provisional(), "x"));
        Assert.True(ws.Info.Scope.Provisional);
        Assert.DoesNotContain("case.scope_confirmed", File.ReadAllText(ws.AppAuditLogPath));
    }

    [Fact]
    public void A_confirmed_scope_can_be_replaced_and_the_replacement_is_audited_too()
    {
        var ws = NewCase("conf3");
        var other = TestScopes.Valid();
        var replaced = new CaseScope { Purpose = "alt scop", PeriodFromUtc = other.PeriodFromUtc, PeriodToUtc = other.PeriodToUtc, SystemsInScope = other.SystemsInScope, Approver = "approver-2",
                                       LegalBasis = other.LegalBasis, Network = other.Network, Classification = other.Classification };
        ws.ConfirmScope(replaced, "inspector");
        Assert.Equal("alt scop", CaseWorkspace.Open(ws.Root).Info.Scope.Purpose);
        Assert.Contains("case.scope_confirmed", File.ReadAllText(ws.AppAuditLogPath));
    }
}

[CollectionDefinition("ReportFooterObserver", DisableParallelization = true)]
public sealed class ReportFooterObserverCollection { }

public class ScopeFormTests
{
    [Fact]
    public void Empty_form_lists_every_missing_field_and_builds_nothing_usable()
    {
        Assert.False(new ScopeForm().TryBuild(out var scope, out var missing));
        Assert.Equal(8, missing.Count);
        Assert.False(scope.IsConfirmed);
    }

    [Fact]
    public void Complete_form_builds_a_confirmed_non_provisional_scope()
    {
        var f = new ScopeForm { Purpose = "control anual", Approver = "col. Ionescu", Systems = "ST-01; ST-02,ST-03", PeriodFrom = new DateTime(2026, 1, 1), PeriodTo = new DateTime(2026, 12, 31),
                                LegalBasisIndex = 2, NetworkIndex = 0, ClassificationIndex = 0 };
        Assert.True(f.TryBuild(out var s, out var missing)); Assert.Empty(missing);
        Assert.True(s.IsConfirmed); Assert.False(s.Provisional);
        Assert.Equal(["ST-01", "ST-02", "ST-03"], s.SystemsInScope);
        Assert.Equal(LegalBasis.Control, s.LegalBasis); Assert.Equal(NetworkCategory.AirGappedNetwork, s.Network); Assert.Equal(ClassificationLevel.Classified, s.Classification);
        Assert.Equal(new DateTime(2026, 12, 31), s.PeriodToUtc!.Value.UtcDateTime.Date);   // the whole last day is in scope
    }

    [Fact]
    public void Form_from_a_provisional_scope_does_not_carry_the_placeholders_over()
    {
        var prov = new CaseScope { Purpose = "Caz LIVE", Approver = "necunoscut (de confirmat)", SystemsInScope = ["PC-9"], PeriodFromUtc = DateTimeOffset.UtcNow, PeriodToUtc = DateTimeOffset.UtcNow.AddYears(1),
                                   LegalBasis = LegalBasis.Control, Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified, Provisional = true };
        var f = ScopeForm.FromScope(prov);
        Assert.Equal("PC-9", f.Systems); Assert.Equal("", f.Approver); Assert.Null(f.PeriodFrom); Assert.Equal(-1, f.NetworkIndex);
        Assert.False(f.TryBuild(out _, out var missing)); Assert.Contains("approver", missing);
        Assert.False(prov.IsConfirmed);
    }
}
