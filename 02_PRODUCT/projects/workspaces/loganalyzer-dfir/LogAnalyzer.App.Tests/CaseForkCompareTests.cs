using System.IO;
using System.IO.Compression;
using System.Text;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.KeyGen;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #8 item 5: two copies of one case compared log by log (identical / older copy / forked at Seq N), folders and archives. Synthetic data only.</summary>
public sealed class CaseForkCompareTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private const string AuditLog = "Logs/audit_chain.jsonl";

    private CaseWorkspace NewCase() => _k.Case(_k.File("in/" + Guid.NewGuid().ToString("N")[..6] + ".bin", "alpha"));

    private string CopyOf(CaseWorkspace ws)
    {
        var dst = Path.Combine(_k.Dir, "copies", Guid.NewGuid().ToString("N")[..6]);
        foreach (var f in Directory.EnumerateFiles(ws.Root, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(dst, Path.GetRelativePath(ws.Root, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
            File.SetAttributes(to, FileAttributes.Normal);
        }
        return dst;
    }

    private static void AddAudit(string root, params string[] actions)
    {
        var ws = CaseWorkspace.Open(root, recheck: false);
        foreach (var a in actions) ws.Audit(a, "test");
    }

    private static int AuditEntries(string root) => new HashChain(Path.Combine(root, "Logs", "audit_chain.jsonl")).VerifyPlainChain().Entries;

    private static LogForkComparison Audit(CaseForkComparison r) => Assert.Single(r.Logs, l => l.Log.Equals(AuditLog, StringComparison.OrdinalIgnoreCase));

    private (int Code, string Text) Cli(string a, string b)
    {
        var o = new StringWriter();
        var code = ReportCli.CompareCase(["compare-case", a, b], o);
        return (code, o.ToString());
    }

    [Fact]
    public void A_plain_copy_is_identical_in_every_chained_log()
    {
        var ws = NewCase();
        var copy = CopyOf(ws);
        var r = CaseForkCompare.CompareCases(ws.Root, copy);
        Assert.NotEmpty(r.Logs);
        Assert.Contains(r.Logs, l => l.Log.Equals(AuditLog, StringComparison.OrdinalIgnoreCase));
        Assert.All(r.Logs, l => Assert.Equal(ForkRelation.Identical, l.Relation));
        Assert.Equal(CaseForkVerdict.Identical, r.Verdict);
        Assert.All(r.Logs, l => Assert.True(l.SameBytes));
        Assert.True(r.SameCaseId);
        var (code, text) = Cli(ws.Root, copy);
        Assert.Equal(0, code);
        Assert.StartsWith("IDENTICE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_older_copy_is_reported_as_a_prefix_in_either_order()
    {
        var ws = NewCase();
        var older = CopyOf(ws);
        var before = AuditEntries(ws.Root);
        AddAudit(ws.Root, "test.later_1", "test.later_2");
        var after = AuditEntries(ws.Root);
        Assert.True(after >= before + 2);   // opening the case also records itself

        var r = Audit(CaseForkCompare.CompareCases(ws.Root, older));
        Assert.Equal(ForkRelation.SecondIsPrefix, r.Relation);
        Assert.Equal(before, r.ComparedSecond);
        Assert.Equal(after, r.ComparedFirst);
        Assert.Null(r.ForkAtSeq);
        Assert.Contains("copie mai veche", r.Message, StringComparison.Ordinal);

        Assert.Equal(ForkRelation.FirstIsPrefix, Audit(CaseForkCompare.CompareCases(older, ws.Root)).Relation);
        var (code, text) = Cli(older, ws.Root);
        Assert.Equal(3, code);
        Assert.StartsWith("O COPIE ESTE MAI VECHE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_copies_written_after_a_common_point_fork_at_the_first_differing_seq()
    {
        var ws = NewCase();
        var other = CopyOf(ws);
        var common = AuditEntries(ws.Root);
        AddAudit(ws.Root, "test.only_in_first");
        AddAudit(other, "test.only_in_second", "test.second_again");

        var r = Audit(CaseForkCompare.CompareCases(ws.Root, other));
        Assert.Equal(ForkRelation.Forked, r.Relation);
        Assert.Equal(common + 1, r.ForkAtSeq);
        Assert.Equal(64, r.FirstHashAtFork.Length);
        Assert.Equal(64, r.SecondHashAtFork.Length);
        Assert.NotEqual(r.FirstHashAtFork, r.SecondHashAtFork);
        Assert.Contains($"Seq 1-{common}", r.Message, StringComparison.Ordinal);
        var (code, text) = Cli(ws.Root, other);
        Assert.Equal(4, code);
        Assert.StartsWith("RAMIFICATE", text, StringComparison.Ordinal);
        Assert.Contains(r.FirstHashAtFork, text, StringComparison.Ordinal);
        Assert.Contains(r.SecondHashAtFork, text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_archive_compares_like_its_folder()
    {
        var ws = NewCase();
        var other = CopyOf(ws);
        var common = AuditEntries(ws.Root);
        var zip = Path.Combine(_k.Dir, "out", "a.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        CaseArchive.Export(ws, zip);   // records "case.archive" (inside the zip) and "case.archive_done" (after it) in the folder

        var r = CaseForkCompare.CompareCases(zip, ws.Root);
        Assert.True(r.SameCaseId);
        Assert.Equal(ForkRelation.FirstIsPrefix, Audit(r).Relation);   // the folder moved on after the zip was written
        Assert.Equal(ForkRelation.SecondIsPrefix, Audit(CaseForkCompare.CompareCases(ws.Root, zip)).Relation);
        Assert.Equal(ForkRelation.FirstIsPrefix, Audit(CaseForkCompare.CompareCases(other, zip)).Relation);   // a copy taken before the export is older

        AddAudit(other, "test.second_branch");   // the earlier copy is worked on: the histories split right after the common part
        var forked = Audit(CaseForkCompare.CompareCases(zip, other));
        Assert.Equal(ForkRelation.Forked, forked.Relation);
        Assert.Equal(common + 1, forked.ForkAtSeq);
    }

    [Fact]
    public void A_broken_chain_is_reported_and_compared_only_up_to_its_break()
    {
        var ws = NewCase();
        var other = CopyOf(ws);
        AddAudit(ws.Root, "test.one", "test.two");
        AddAudit(other, "test.one", "test.two");
        // rewrite the action of the LAST line of the copy without recomputing its hash
        var log = Path.Combine(other, "Logs", "audit_chain.jsonl");
        var lines = File.ReadAllLines(log).Where(l => l.Length > 0).ToArray();
        Assert.Contains("test.two", lines[^1], StringComparison.Ordinal);
        lines[^1] = lines[^1].Replace("test.two", "test.TWO", StringComparison.Ordinal);
        File.WriteAllLines(log, lines);

        var r = CaseForkCompare.CompareCases(ws.Root, other);
        var a = Audit(r);
        Assert.Equal(ChainStatus.Broken, a.Second!.Status);
        Assert.True(r.AnyBroken);
        Assert.Equal(CaseForkVerdict.Broken, r.Verdict);
        Assert.Equal(lines.Length - 1, a.ComparedSecond);   // the forged entry is not compared
        Assert.Contains("rupt", a.Message, StringComparison.Ordinal);
        var (code, text) = Cli(ws.Root, other);
        Assert.Equal(2, code);
        Assert.StartsWith("LANȚ RUPT", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_archive_with_a_duplicate_log_entry_is_not_compared()
    {
        var ws = NewCase();
        var zip = Path.Combine(_k.Dir, "out", "dup.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        CaseArchive.Export(ws, zip);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            using var w = new StreamWriter(z.CreateEntry("case/LOGS/AUDIT_CHAIN.JSONL").Open(), new UTF8Encoding(false));
            w.Write("{}");
        }
        Assert.Throws<InvalidDataException>(() => CaseForkCompare.CompareCases(zip, ws.Root));
        var (code, text) = Cli(zip, ws.Root);
        Assert.Equal(2, code);
        Assert.StartsWith("NECOMPARABIL", text, StringComparison.Ordinal);
        Assert.Contains("de două ori", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_case_or_wrong_arguments_are_refused()
    {
        var ws = NewCase();
        Assert.Equal(2, Cli(ws.Root, Path.Combine(_k.Dir, "nu-exista")).Code);
        var o = new StringWriter();
        Assert.Equal(1, ReportCli.CompareCase(["compare-case", ws.Root], o));
    }

    [Fact]
    public void Different_cases_are_flagged_and_their_histories_share_no_point()
    {
        var a = NewCase();
        var b = NewCase();
        var r = CaseForkCompare.CompareCases(a.Root, b.Root);
        var audit = Audit(r);
        Assert.Equal(ForkRelation.Forked, audit.Relation);
        Assert.Equal(1, audit.ForkAtSeq);
        Assert.Contains("fără punct comun", audit.Message, StringComparison.Ordinal);
        // the case id alone does not tell two cases apart (same name, same id): the history does, and the CLI says so
        Assert.Equal(CaseForkVerdict.Unrelated, r.Verdict);
        var (code, text) = Cli(a.Root, b.Root);
        Assert.Equal(4, code);
        Assert.StartsWith("FĂRĂ ISTORIE COMUNĂ", text, StringComparison.Ordinal);
        Assert.Contains("fără punct comun", text, StringComparison.Ordinal);
        if (!r.SameCaseId) Assert.Contains("nu au același id de caz", text, StringComparison.Ordinal);
    }

    private static void DropLastEntry(string root, string log)
    {
        var path = Path.Combine(root, log.Replace('/', Path.DirectorySeparatorChar));
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        Assert.NotEmpty(lines);
        File.WriteAllLines(path, lines[..^1]);   // a shorter copy of a valid chain is still a valid chain
    }

    [Fact]
    public void One_log_older_in_each_copy_is_a_fork_of_the_case_not_an_older_copy()
    {
        var ws = NewCase();
        var a = CopyOf(ws);
        var b = CopyOf(ws);
        DropLastEntry(a, "Logs/chain_of_custody.jsonl");   // copy 1 lacks the last custody entry
        DropLastEntry(b, AuditLog);                        // copy 2 lacks the last audit entry

        var r = CaseForkCompare.CompareCases(a, b);
        Assert.Contains(r.Logs, l => l.Relation == ForkRelation.FirstIsPrefix);
        Assert.Contains(r.Logs, l => l.Relation == ForkRelation.SecondIsPrefix);
        Assert.Equal(CaseForkVerdict.Forked, r.Verdict);
        var (code, text) = Cli(a, b);
        Assert.Equal(4, code);
        Assert.StartsWith("RAMIFICATE", text, StringComparison.Ordinal);
        Assert.Contains("Ramificare între jurnale", text, StringComparison.Ordinal);

        // the same drop in ONE copy only is an older copy
        var c = CopyOf(ws);
        DropLastEntry(c, "Logs/chain_of_custody.jsonl");
        DropLastEntry(c, AuditLog);
        Assert.Equal(CaseForkVerdict.FirstOlder, CaseForkCompare.CompareCases(c, ws.Root).Verdict);
        Assert.Equal(CaseForkVerdict.SecondOlder, CaseForkCompare.CompareCases(ws.Root, c).Verdict);
    }

    [Fact]
    public void A_line_or_case_file_with_a_duplicate_json_key_is_reported_not_a_crash()
    {
        var ws = NewCase();
        var b = CopyOf(ws);
        var log = Path.Combine(b, "Logs", "audit_chain.jsonl");
        var lines = File.ReadAllLines(log).Where(l => l.Length > 0).ToList();
        lines.Add("{\"seq\":" + (lines.Count + 1) + ",\"seq\":1,\"prevHash\":\"x\",\"hash\":\"y\"}");
        File.WriteAllLines(log, lines);
        File.WriteAllText(Path.Combine(b, "case.json"), "{\"caseId\":\"a\",\"caseId\":\"b\"}");

        var r = CaseForkCompare.CompareCases(ws.Root, b);
        Assert.Equal(ChainStatus.Broken, Audit(r).Second!.Status);
        Assert.Equal("", r.SecondCaseId);
        Assert.Equal(CaseForkVerdict.Broken, r.Verdict);
        Assert.Equal(2, Cli(ws.Root, b).Code);
        Assert.Equal(ChainStatus.Broken, HashChain.VerifyPlainChain("x.jsonl", Encoding.UTF8.GetBytes("{\"seq\":1,\"seq\":2}\n")).Status);
    }

    [Fact]
    public void Identical_chains_with_different_bytes_outside_the_chain_are_not_called_identical()
    {
        var ws = NewCase();
        var b = CopyOf(ws);
        var log = Path.Combine(b, "Logs", "audit_chain.jsonl");
        File.WriteAllText(log, "{\"action\":\"linie veche adăugată\"}\n" + File.ReadAllText(log));   // a plain line before the chain

        var r = CaseForkCompare.CompareCases(ws.Root, b);
        var audit = Audit(r);
        Assert.Equal(ForkRelation.Identical, audit.Relation);
        Assert.False(audit.SameBytes);
        Assert.Contains("fișierele diferă", audit.Message, StringComparison.Ordinal);
        Assert.Equal(CaseForkVerdict.IdenticalChainsDifferentFiles, r.Verdict);
        var (code, text) = Cli(ws.Root, b);
        Assert.Equal(5, code);
        Assert.StartsWith("LANȚURI IDENTICE, FIȘIERE DIFERITE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_log_edge_cases()
    {
        Assert.Equal(ForkRelation.NotComparable, CaseForkCompare.CompareLog("Logs/x.jsonl", null, null).Relation);
        var legacy = Encoding.UTF8.GetBytes("{\"action\":\"old\"}\n{\"action\":\"older\"}\n");
        Assert.Equal(ForkRelation.NotComparable, CaseForkCompare.CompareLog("Logs/x.jsonl", legacy, legacy).Relation);
        var only = CaseForkCompare.CompareLog("Logs/x.jsonl", legacy, null);
        Assert.Equal(ForkRelation.OnlyInOne, only.Relation);
        Assert.Contains("doar în prima", only.Message, StringComparison.Ordinal);
    }
}
