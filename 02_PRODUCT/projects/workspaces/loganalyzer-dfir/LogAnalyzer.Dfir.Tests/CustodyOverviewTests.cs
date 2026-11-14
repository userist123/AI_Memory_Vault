using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Tab 9 "Istoricul probelor": the custody chain of the case, read for display with its chain verdict.</summary>
public sealed class CustodyOverviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_custov_" + Guid.NewGuid().ToString("N"));
    public CustodyOverviewTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private CaseWorkspace NewCase() => InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "cust", TestScopes.Valid());

    private static EvidenceItem Store(CaseWorkspace ws, string name, string content)
    {
        var path = Path.Combine(ws.RawDir("cust"), name);
        File.WriteAllText(path, content);
        return ws.RegisterStored(path, @"C:\orig\" + name, "cust", "txt", TemporalType.Historical, "col", "1");
    }

    [Fact]
    public void Collected_evidence_is_listed_newest_first_with_a_romanian_action_and_a_verified_chain()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var b = Store(ws, "b.txt", "beta");
        var ledger = CustodyOverview.Load(ws);
        Assert.False(ledger.ChainProblem);
        Assert.StartsWith("Lanț de custodie verificat:", ledger.ChainLine);
        var mine = ledger.Rows.Where(r => r.EvidenceId == a.EvidenceId || r.EvidenceId == b.EvidenceId).ToList();
        Assert.Equal([b.EvidenceId, a.EvidenceId], mine.Select(r => r.EvidenceId));
        Assert.All(mine, r => Assert.Equal("colectată", r.ActionText));
        Assert.Equal(a.Sha256, mine[1].Sha256);
        Assert.Equal(a.Sha256[..12], mine[1].ShaShort);
        Assert.Contains("→", mine[1].FromTo);
        Assert.True(ledger.Rows[0].Seq > ledger.Rows[^1].Seq);
    }

    [Fact]
    public void An_edited_line_breaks_the_chain_and_is_flagged()
    {
        var ws = NewCase();
        Store(ws, "a.txt", "alpha");
        Store(ws, "b.txt", "beta");
        var lines = File.ReadAllLines(ws.CustodyJsonlPath);
        var o = JsonNode.Parse(lines[0])!.AsObject();
        o["who"] = "altcineva";
        lines[0] = o.ToJsonString();
        File.WriteAllLines(ws.CustodyJsonlPath, lines);
        var ledger = CustodyOverview.Load(ws);
        Assert.True(ledger.ChainProblem);
        Assert.StartsWith("PROBLEMĂ: lanțul de custodie este rupt", ledger.ChainLine);
    }

    [Fact]
    public void A_line_that_is_not_json_is_shown_as_unreadable_not_skipped()
    {
        var ws = NewCase();
        Store(ws, "a.txt", "alpha");
        File.AppendAllText(ws.CustodyJsonlPath, "{ nu este json\n");
        var ledger = CustodyOverview.Load(ws);
        Assert.True(ledger.ChainProblem);
        var bad = Assert.Single(ledger.Rows, r => r.Unreadable);
        Assert.Same(bad, ledger.Rows[0]);
        Assert.Equal("linie ilizibilă", bad.ActionText);
    }

    [Fact]
    public void Text_from_the_log_is_cleaned_and_a_field_that_is_not_a_hash_is_not_shown_as_one()
    {
        var ws = NewCase();
        ws.Custody(new CustodyEntry(DateTimeOffset.UtcNow, "op\u202Eerat\nor", "custom\u200B.action", "E-1", "C:\\a\u0000b", "", "nu-e-amprentă", "unealtă\r\n2", "", "", "notă\u2028nouă"));
        var row = CustodyOverview.Load(ws).Rows[0];
        Assert.Equal("op erat or", row.Who);
        Assert.Equal("custom .action", row.ActionText);
        Assert.Equal("C:\\a b", row.FromTo);
        Assert.Equal("", row.Sha256);
        Assert.Equal("—", row.ShaShort);
        Assert.Equal("unealtă 2", row.Tool);
        Assert.StartsWith("notă nouă", row.Details);
        Assert.DoesNotContain(row.Details, ch => char.IsControl(ch) || ch == '\u2028');
    }

    [Fact]
    public void A_case_without_a_custody_log_says_the_chain_is_empty()
    {
        var ws = NewCase();
        File.Delete(ws.CustodyJsonlPath);
        if (File.Exists(ws.CustodyCsvPath)) File.Delete(ws.CustodyCsvPath);
        var ledger = CustodyOverview.Load(ws);
        Assert.Empty(ledger.Rows);
        Assert.Equal("Lanțul de custodie al cazului este gol", ledger.ChainLine);
        Assert.False(ledger.ChainProblem);
    }

    [Fact]
    public void A_case_created_before_the_chain_reads_as_unverifiable_not_valid()
    {
        var ws = NewCase();
        Store(ws, "a.txt", "alpha");
        File.Delete(ws.CustodyJsonlPath);
        var ledger = CustodyOverview.Load(ws);
        Assert.True(ledger.ChainProblem);
        Assert.StartsWith("Lanț de custodie neverificabil", ledger.ChainLine);
    }

    [Fact]
    public void Only_the_newest_rows_are_kept_and_the_line_says_so()
    {
        var text = CustodyOverview.ChainText(new ChainVerification("custody", ChainStatus.Valid, 25_000, 0, null, "", ""), 25_000, CustodyOverview.MaxRows);
        Assert.Contains("se afișează cele mai noi 20.000 din 25.000 linii", text);
    }

    [Fact]
    public void Cancellation_propagates()
    {
        var ws = NewCase();
        Store(ws, "a.txt", "alpha");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => CustodyOverview.Load(ws, cts.Token));
    }

    [Fact]
    public void Review1_a_line_planted_in_front_of_a_valid_chain_is_a_problem_and_shows_no_hash()
    {
        var ws = NewCase();
        Store(ws, "a.txt", "alpha");
        var planted = new JsonObject { ["timestampUtc"] = "2026-01-01T00:00:00Z", ["who"] = "ALT_OPERATOR", ["action"] = "acquired", ["evidenceId"] = "E-FALS", ["sha256"] = new string('a', 64) }.ToJsonString();
        File.WriteAllText(ws.CustodyJsonlPath, planted + "\n" + File.ReadAllText(ws.CustodyJsonlPath));
        var ledger = CustodyOverview.Load(ws);
        Assert.True(ledger.ChainProblem);
        Assert.StartsWith("PROBLEMĂ: 1 linie fără lanț", ledger.ChainLine);
        var row = Assert.Single(ledger.Rows, r => r.EvidenceId == "E-FALS");
        Assert.Equal("", row.Sha256);
        Assert.Equal("—", row.ShaShort);
        Assert.StartsWith(CustodyOverview.UnverifiedPrefix.TrimEnd(), row.Details);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[1]")]
    [InlineData("5")]
    public void Review1_a_line_that_is_json_but_not_an_object_breaks_the_chain_without_throwing(string line)
    {
        var ws = NewCase();
        Store(ws, "a.txt", "alpha");
        File.AppendAllText(ws.CustodyJsonlPath, line + "\n");
        var ledger = CustodyOverview.Load(ws);
        Assert.True(ledger.ChainProblem);
        Assert.StartsWith("PROBLEMĂ: lanțul de custodie este rupt", ledger.ChainLine);
        Assert.True(ledger.Rows[0].Unreadable);
        Assert.Equal(ChainStatus.Broken, ws.VerifyChains().Custody.Status);   // the integrity re-check reads it the same way, no NullReferenceException
    }

    [Fact]
    public void Review1_load_keeps_only_the_newest_rows_newest_first()
    {
        var ws = NewCase();
        File.Delete(ws.CustodyJsonlPath);
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= CustodyOverview.MaxRows + 5; i++) sb.Append("{\"action\":\"acquired\",\"evidenceId\":\"E-").Append(i).Append("\"}\n");
        File.WriteAllText(ws.CustodyJsonlPath, sb.ToString());
        var ledger = CustodyOverview.Load(ws);
        Assert.Equal(CustodyOverview.MaxRows, ledger.Rows.Count);
        Assert.Equal(CustodyOverview.MaxRows + 5, ledger.TotalLines);
        Assert.True(ledger.Truncated);
        Assert.Equal($"E-{CustodyOverview.MaxRows + 5}", ledger.Rows[0].EvidenceId);
        Assert.Equal("E-6", ledger.Rows[^1].EvidenceId);
        Assert.Contains("se afișează cele mai noi", ledger.ChainLine);
    }

    [Fact]
    public void Review1_a_run_without_collection_fills_the_custody_with_every_entry_of_the_run()
    {
        var ws = NewCase();
        Store(ws, "a.txt", "alpha");
        var r = new InvestigationPipeline().Run(ws, LogAnalyzer.Dfir.Windows.Acquisition.CollectionProfile.Quick, collect: false);
        Assert.Equal("", r.CustodyError);
        Assert.NotNull(r.Custody);
        Assert.Equal(File.ReadAllLines(ws.CustodyJsonlPath).Count(l => l.Length > 0), r.Custody!.TotalLines);
        Assert.Contains(r.Custody.Rows, x => x.ActionText == "rezultat înregistrat");
    }

    [Fact]
    public void Review2_a_bom_and_a_lone_carriage_return_read_the_same_as_the_integrity_recheck()
    {
        var ws = NewCase();
        Store(ws, "a.txt", "alpha");
        var bytes = File.ReadAllBytes(ws.CustodyJsonlPath);
        File.WriteAllBytes(ws.CustodyJsonlPath, [0xEF, 0xBB, 0xBF, .. bytes]);
        var ledger = CustodyOverview.Load(ws);
        Assert.Equal(ChainStatus.Valid, ws.VerifyChains().Custody.Status);
        Assert.False(ledger.ChainProblem);
        Assert.DoesNotContain(ledger.Rows, r => r.Unreadable);

        var text = File.ReadAllText(ws.CustodyJsonlPath);
        var i = text.IndexOf(",\"", StringComparison.Ordinal);
        File.WriteAllText(ws.CustodyJsonlPath, text[..(i + 1)] + "\r" + text[(i + 1)..]);   // '\r' is JSON whitespace inside one line
        var file = ws.VerifyChains().Custody;
        ledger = CustodyOverview.Load(ws);
        Assert.Equal(file.Status != ChainStatus.Valid, ledger.ChainProblem);
        Assert.Equal(File.ReadLines(ws.CustodyJsonlPath).Count(l => l.Trim().Length > 0), ledger.TotalLines);
        Assert.True(ledger.ChainProblem);
    }

    [Fact]
    public void Opening_a_case_fills_the_custody_of_the_result()
    {
        var ws = NewCase();
        var a = Store(ws, "a.txt", "alpha");
        var res = new InvestigationResult { Case = ws };
        res.LoadCustody(ws);
        Assert.Equal("", res.CustodyError);
        Assert.Contains(res.Custody!.Rows, r => r.EvidenceId == a.EvidenceId);
    }
}
