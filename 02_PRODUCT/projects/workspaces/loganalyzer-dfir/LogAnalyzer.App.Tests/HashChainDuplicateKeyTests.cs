using System.IO;
using LogAnalyzer.Dfir.Case;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// A JSON object with a duplicate key ("seq" twice) parses, then throws ArgumentException on the first lookup. Review of 2026-11-18 (L1): every
/// reader of a chained log, its sealed head and its seal intent treats such a line as invalid (fail closed), never as a crash of the verification.
/// </summary>
public sealed class HashChainDuplicateKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-dupkey-" + Guid.NewGuid().ToString("N"));
    public HashChainDuplicateKeyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const string DuplicateKeyLine = "{\"seq\":2,\"seq\":3,\"prevHash\":\"x\",\"hash\":\"y\"}";

    private string Log(bool sealedLog)
    {
        var log = Path.Combine(_dir, Guid.NewGuid().ToString("N")[..6] + ".jsonl");
        if (sealedLog) HashChain.RegisterHeadSeal(log, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        new HashChain(log).Append(new { Event = "first" });
        return log;
    }

    [Fact]
    public void The_runtime_really_throws_on_a_duplicate_key_lookup()
    {
        // guards the premise: if a future runtime stops throwing, the tests below still pass but no longer prove the catch
        var o = System.Text.Json.Nodes.JsonNode.Parse(DuplicateKeyLine)!.AsObject();
        Assert.Throws<ArgumentException>(() => o.ContainsKey("seq"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_duplicate_key_last_line_breaks_the_chain_without_throwing(bool sealedLog)
    {
        var log = Log(sealedLog);
        File.AppendAllText(log, DuplicateKeyLine + "\n");
        var chain = new HashChain(log);
        var v = chain.Verify();
        Assert.Equal(ChainStatus.Broken, v.Status);
        Assert.Equal(ChainStatus.Broken, chain.Verify(File.ReadAllBytes(log)).Status);
        Assert.Equal(ChainStatus.Broken, chain.VerifyPlainChain().Status);
        var head = chain.Head();   // the last line is unreadable: no head from it, no exception
        Assert.NotEqual(2, head.Seq);
        Assert.False(chain.Contains(2, "y"));
    }

    [Fact]
    public void A_sealed_head_file_with_a_duplicate_key_is_an_invalid_seal_not_a_crash()
    {
        var log = Log(sealedLog: true);
        var chain = new HashChain(log);
        Assert.Equal(ChainStatus.Valid, chain.Verify().Status);
        var (seq, hash) = chain.Head();
        File.WriteAllText(HashChain.HeadPath(log), $"{{\"seq\":{seq},\"seq\":{seq},\"hash\":\"{hash}\",\"mac\":\"00\"}}");
        Assert.Equal(ChainStatus.Broken, chain.Verify().Status);
    }

    [Fact]
    public void A_seal_intent_with_a_duplicate_key_is_ignored_not_a_crash()
    {
        var log = Log(sealedLog: true);
        File.WriteAllText(HashChain.IntentPath(log), "{\"prevSeq\":1,\"prevSeq\":1,\"prevHash\":\"x\",\"seq\":2,\"mac\":\"00\"}");
        Assert.Equal(ChainStatus.Valid, new HashChain(log).Verify().Status);   // the head still matches: the unreadable intent covers nothing
    }

    [Fact]
    public void A_witness_file_with_a_duplicate_key_is_an_invalid_witness_not_a_crash()
    {
        var log = Path.Combine(_dir, "witnessed.jsonl");
        var witnessDir = Path.Combine(_dir, "witness");
        Directory.CreateDirectory(witnessDir);
        var w = new LogAnalyzer.Dfir.Auth.FileHeadWitness(witnessDir, log);
        HashChain.RegisterHeadSeal(log, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), w);
        new HashChain(log).Append(new { Event = "first" });
        Assert.Equal(ChainStatus.Valid, new HashChain(log).Verify().Status);
        File.WriteAllText(w.FilePath, "{\"seq\":1,\"seq\":1,\"hash\":\"x\",\"mac\":\"00\"}");
        Assert.Equal((-1L, "", ""), w.Read());
        var v = new HashChain(log).Verify();
        Assert.Equal(ChainStatus.Broken, v.Status);
        Assert.Contains("martorul anti-revenire", v.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custody_line_with_a_duplicate_key_is_an_unreadable_row_not_a_crash()
    {
        var row = CustodyOverviewRows("{\"seq\":1,\"seq\":2,\"hash\":\"y\"}");
        Assert.NotNull(row);
        Assert.Contains("linie ilizibilă", row!.ToString(), StringComparison.Ordinal);
    }

    private static object? CustodyOverviewRows(string line)
    {
        // CustodyOverview.Row is private; reach it through reflection only to prove it does not throw on this line
        var m = typeof(CustodyOverview).GetMethod("Row", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return m.Invoke(null, [line, 1]);
    }
}