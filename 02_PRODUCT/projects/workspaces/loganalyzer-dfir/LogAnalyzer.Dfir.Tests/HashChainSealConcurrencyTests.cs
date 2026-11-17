using LogAnalyzer.Dfir.Case;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>A sealed chain shared by several processes: verification reads one consistent snapshot, and a head that cannot be moved never leaves the log ahead of its seal.</summary>
public sealed class HashChainSealConcurrencyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-sealrace-" + Guid.NewGuid().ToString("N"));
    public HashChainSealConcurrencyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string SealedLog()
    {
        var log = Path.Combine(_dir, "auth_audit.jsonl");
        HashChain.RegisterHeadSeal(log, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        new HashChain(log).Append(new { Event = "first" });
        return log;
    }

    [Fact]
    public void A_reader_holding_the_head_file_does_not_leave_the_seal_behind_an_appended_entry()
    {
        var log = SealedLog();
        var before = File.ReadAllBytes(log);
        // the old way of reading: FileShare.Read, no Delete -> the writer's replace of .head cannot succeed while this is open
        var reader = new FileStream(HashChain.HeadPath(log), FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(() => { Thread.Sleep(400); reader.Dispose(); });
        new HashChain(log).Append(new { Event = "second" });
        release.Wait();
        Assert.True(File.ReadAllBytes(log).Length > before.Length);
        Assert.Equal(ChainStatus.Valid, new HashChain(log).Verify().Status);
    }

    [Fact]
    public void A_sealed_log_verified_while_another_writer_appends_is_never_reported_broken()
    {
        var log = SealedLog();
        var writer = new HashChain(log);       // a second object on the same path = a second process
        var verifier = new HashChain(log);
        var done = false;
        var task = Task.Run(() =>
        {
            for (int i = 0; i < 300; i++) writer.Append(new { Event = "e", I = i });
            done = true;
        });
        int checks = 0;
        while (!Volatile.Read(ref done) || checks < 5)
        {
            var v = verifier.Verify();
            Assert.True(v.Status != ChainStatus.Broken, v.Message);
            var (_, rv) = verifier.ReadVerified();
            Assert.True(rv.Status != ChainStatus.Broken, rv.Message);
            checks++;
        }
        task.Wait();
        Assert.Equal(ChainStatus.Valid, verifier.Verify().Status);
    }

    [Fact]
    public void A_read_only_head_fails_the_append_at_once_and_leaves_the_log_as_it_was()
    {
        // review 2026-11-17: a persistent "access denied" on the head must not hold the log (and every other instance's sign-in) for ~19 s
        var log = SealedLog();
        var before = File.ReadAllBytes(log);
        var head = HashChain.HeadPath(log);
        File.SetAttributes(head, File.GetAttributes(head) | FileAttributes.ReadOnly);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { Assert.ThrowsAny<UnauthorizedAccessException>(() => new HashChain(log).Append(new { Event = "second" })); }
        finally { File.SetAttributes(head, File.GetAttributes(head) & ~FileAttributes.ReadOnly); }
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
        Assert.Equal(before, File.ReadAllBytes(log));
        Assert.Equal(ChainStatus.Valid, new HashChain(log).Verify().Status);
    }

    [Fact]
    public void A_failed_head_move_leaves_the_log_as_it_was()
    {
        var log = SealedLog();
        var before = File.ReadAllBytes(log);
        var headBefore = File.ReadAllText(HashChain.HeadPath(log));
        var tmp = HashChain.HeadPath(log) + ".tmp";
        Directory.CreateDirectory(tmp);        // the temporary head cannot be written: a persistent, non-sharing failure
        try
        {
            Assert.ThrowsAny<Exception>(() => new HashChain(log).Append(new { Event = "second" }));
        }
        finally { Directory.Delete(tmp); }
        Assert.Equal(before, File.ReadAllBytes(log));
        Assert.Equal(headBefore, File.ReadAllText(HashChain.HeadPath(log)));
        Assert.Equal(ChainStatus.Valid, new HashChain(log).Verify().Status);
        new HashChain(log).Append(new { Event = "third" });                  // and the log is still usable afterwards
        Assert.Equal(ChainStatus.Valid, new HashChain(log).Verify().Status);
    }
}
