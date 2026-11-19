using System.Security.Cryptography;
using LogAnalyzer.Dfir.Case;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// CI 2026-11-19: the turnstile timing test measured 7-9 s only on loaded runners while the other collections ran in parallel (it passed in between);
/// the 5 s bound is kept, the class runs alone so the measurement is about the chain, not about the runner's CPU.
/// </summary>
[CollectionDefinition("ChainTiming", DisableParallelization = true)]
public sealed class ChainTimingCollection { }

/// <summary>Anti-rollback witness of a sealed log: an older valid copy of log + .head is caught, a lagging witness is normal, a failing witness never loses an entry.</summary>
[Collection("ChainTiming")]
public sealed class HeadWitnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-witness-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    public HeadWitnessTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class MemWitness : IHeadWitness
    {
        public (long Seq, string Hash, string Mac)? Value;
        public int FailWrites;          // next N writes throw
        public bool FailAlways;
        public bool FailReads;          // present but unreadable (a holder, access denied)
        public (long Seq, string Hash, string Mac)? Read() { lock (this) { if (FailReads) throw new IOException("witness locked"); return Value; } }
        public void Write(long seq, string hash, string mac)
        {
            lock (this)
            {
                if (FailAlways || FailWrites-- > 0) throw new IOException("witness write refused");
                Value = (seq, hash, mac);
            }
        }
    }

    private string Log(MemWitness? w, string name = "a.jsonl")
    {
        var log = Path.Combine(_dir, name);
        HashChain.RegisterHeadSeal(log, _key, w);
        return log;
    }

    private static void Add(string log, int n, string tag = "e") { var c = new HashChain(log); for (int i = 0; i < n; i++) c.Append(new { Event = tag, I = i }); }
    private static ChainVerification V(string log) => new HashChain(log).Verify();

    private static (byte[] Log, byte[] Head) Snapshot(string log) => (File.ReadAllBytes(log), File.ReadAllBytes(HashChain.HeadPath(log)));
    private static void Restore(string log, (byte[] Log, byte[] Head) s) { File.WriteAllBytes(log, s.Log); File.WriteAllBytes(HashChain.HeadPath(log), s.Head); }

    [Fact]
    public void P1_rollback_to_an_older_valid_copy_is_broken()
    {
        var w = new MemWitness(); var log = Log(w);
        Add(log, 3); var old = Snapshot(log); Add(log, 3);
        Assert.Equal(ChainStatus.Valid, V(log).Status);
        Restore(log, old);
        var v = V(log);
        Assert.Equal(ChainStatus.Broken, v.Status);
        Assert.Contains("copie mai veche", v.Reason);
        Assert.Contains("Seq 6", v.Reason); Assert.Contains("Seq 3", v.Reason);
        Assert.Equal(ChainStatus.Broken, new HashChain(log).Verify(File.ReadAllBytes(log)).Status);
        Assert.Equal(ChainStatus.Broken, new HashChain(log).ReadVerified().Verification.Status);
    }

    [Fact]
    public void P2_fork_after_rollback_is_broken_first_as_older_then_as_rewritten()
    {
        var w = new MemWitness(); var log = Log(w);
        Add(log, 2); var old = Snapshot(log); Add(log, 3);   // witness at Seq 5
        Restore(log, old);
        Add(log, 2, "fork");                                  // a key holder continues the old copy: Seq 3, 4
        Assert.Contains("copie mai veche", V(log).Reason);
        Add(log, 1, "fork");                                  // Seq 5 with a different hash
        Assert.Contains("rescris", V(log).Reason);
        Add(log, 1, "fork");
        Assert.Contains("rescris", V(log).Reason);
        Assert.Equal(5, w.Value!.Value.Seq);                  // the witness never moved down
    }

    [Fact]
    public void P3_crash_between_head_move_and_witness_write_leaves_a_lagging_witness_that_the_next_append_moves()
    {
        var w = new MemWitness(); var log = Log(w);
        Add(log, 2);
        w.FailWrites = 1;
        Add(log, 1);
        Assert.Equal(2, w.Value!.Value.Seq);
        Assert.Equal(ChainStatus.Valid, V(log).Status);
        Add(log, 1);
        Assert.Equal(4, w.Value!.Value.Seq);
        Assert.Equal(ChainStatus.Valid, V(log).Status);
    }

    [Fact]
    public void P4_tampered_witness_is_broken_and_a_deleted_one_is_valid()
    {
        var w = new MemWitness(); var log = Log(w);
        Add(log, 3);
        var good = w.Value!.Value;
        w.Value = (good.Seq, good.Hash, new string('0', 64));
        var v = V(log);
        Assert.Equal(ChainStatus.Broken, v.Status);
        Assert.Contains("martorul anti-revenire", v.Reason);
        w.Value = (good.Seq + 5, good.Hash, good.Mac);   // raised without the key
        Assert.Contains("martorul anti-revenire", V(log).Reason);
        Add(log, 1);                                      // an append does not heal a tampered witness
        Assert.Contains("martorul anti-revenire", V(log).Reason);
        w.Value = null;                                   // residual: an administrator can delete it
        Assert.Equal(ChainStatus.Valid, V(log).Status);
    }

    [Fact]
    public void P5_a_failing_witness_never_loses_or_rolls_back_an_entry()
    {
        var w = new MemWitness { FailAlways = true }; var log = Log(w);
        Add(log, 4);
        Assert.Equal(4, new HashChain(log).Head().Seq);
        Assert.Equal(ChainStatus.Valid, V(log).Status);
        Assert.Null(w.Value);
    }

    [Fact]
    public void P6_reseal_resets_the_witness_downwards_and_the_restored_log_is_valid()
    {
        var w = new MemWitness(); var log = Log(w);
        Add(log, 2); var old = Snapshot(log); Add(log, 3);
        Restore(log, old);
        Assert.Equal(ChainStatus.Broken, V(log).Status);
        new HashChain(log).Reseal();
        Assert.Equal(2, w.Value!.Value.Seq);
        Assert.Equal(ChainStatus.Valid, V(log).Status);
        Add(log, 1);
        Assert.Equal(ChainStatus.Valid, V(log).Status);
    }

    [Fact]
    public void P7_a_log_without_a_witness_behaves_as_before()
    {
        var log = Log(null, "plain.jsonl");
        Add(log, 3); var old = Snapshot(log); Add(log, 3);
        Restore(log, old);
        Assert.Equal(ChainStatus.Valid, V(log).Status);   // the known residual this witness exists to close
    }

    [Fact]
    public void P8_two_writers_never_move_the_witness_backwards()
    {
        var w = new MemWitness(); var log = Log(w);
        Add(log, 1);
        long last = 0; bool backwards = false, done = false;
        var a = Task.Run(() => Add(log, 60, "a"));
        var b = Task.Run(() => Add(log, 60, "b"));
        var watcher = Task.Run(() => { while (!Volatile.Read(ref done)) { var c = w.Read(); if (c is { } x) { if (x.Seq < last) backwards = true; last = x.Seq; } } });
        Task.WaitAll(a, b); done = true; watcher.Wait();
        Assert.False(backwards);
        Assert.Equal(121, w.Value!.Value.Seq);
        Assert.Equal(ChainStatus.Valid, V(log).Status);
    }

    [Fact]
    public void The_file_witness_outside_the_log_folder_round_trips_and_catches_an_older_copy_of_the_folder()
    {
        var logDir = Path.Combine(_dir, "auth"); Directory.CreateDirectory(logDir);
        var witnessDir = Path.Combine(_dir, "witness");
        var log = Path.Combine(logDir, "auth_audit.jsonl");
        var w = new LogAnalyzer.Dfir.Auth.FileHeadWitness(witnessDir, log);
        Assert.Null(w.Read());
        HashChain.RegisterHeadSeal(log, _key, w);
        Add(log, 2); var old = Snapshot(log); Add(log, 2);
        Assert.Equal(4, w.Read()!.Value.Seq);
        Assert.False(File.Exists(w.FilePath + ".tmp"));
        Assert.Equal(ChainStatus.Valid, V(log).Status);
        Restore(log, old);                                                          // the auth folder put back from an older copy
        Assert.Equal(ChainStatus.Broken, V(log).Status);
        Assert.Contains("copie mai veche", V(log).Reason);
        Assert.Null(new LogAnalyzer.Dfir.Auth.FileHeadWitness(witnessDir, Path.Combine(logDir, "other.jsonl")).Read());   // one witness per log
        File.WriteAllText(w.FilePath, "{not json");                                  // a damaged witness reads as invalid, never as absent
        Assert.Equal(-1, w.Read()!.Value.Seq);
    }

    [Fact]
    public void A_witness_that_is_present_but_cannot_be_read_is_reported_never_silently_valid()
    {
        // review 2026-11-18: an unreadable witness (a holder, access denied) was treated as absent -> Valid
        var w = new MemWitness(); var log = Log(w); Add(log, 3);
        w.FailReads = true;
        var v = V(log);
        Assert.Equal(ChainStatus.Broken, v.Status);
        Assert.Contains("nu poate fi citit", v.Reason);
        w.FailReads = false;
        Assert.Equal(ChainStatus.Valid, V(log).Status);
    }

    [Fact]
    public void A_verify_running_while_another_writer_appends_and_moves_the_witness_is_never_falsely_broken()
    {
        // review 2026-11-18: the witness was read after the log handle was released -> a concurrent append gave "copie mai veche"
        var w = new MemWitness(); var log = Log(w); Add(log, 1);
        var done = false;
        var writer = Task.Run(() => { var c = new HashChain(log); for (int i = 0; i < 300; i++) c.Append(new { Event = "w", I = i }); Volatile.Write(ref done, true); });
        int checks = 0;
        while (!Volatile.Read(ref done) || checks < 5)
        {
            var (_, v) = new HashChain(log).ReadVerified();
            Assert.True(v.Status == ChainStatus.Valid, v.Reason);
            checks++;
        }
        writer.Wait();
    }

    [Fact]
    public void A_reader_gets_in_within_seconds_while_four_writers_append_back_to_back()
    {
        // CI 2026-11-18: writers re-took the exclusive handle right after releasing it and a verifier starved for 20 s (sharing violation); the turnstile lets it in
        var w = new MemWitness(); var log = Log(w); Add(log, 1);
        int running = 4;
        var writers = Enumerable.Range(0, 4).Select(k => Task.Factory.StartNew(() =>
        {
            var c = new HashChain(log);
            for (int i = 0; i < 150; i++) c.Append(new { Event = "w" + k, I = i });
            Interlocked.Decrement(ref running);
        }, TaskCreationOptions.LongRunning)).ToArray();   // own threads: four blocked pool threads would starve other tests of the suite
        var slowest = TimeSpan.Zero; int checks = 0;
        while (Volatile.Read(ref running) > 0 || checks < 3)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (_, v) = new HashChain(log).ReadVerified();
            sw.Stop();
            Assert.True(v.Status == ChainStatus.Valid, v.Reason);
            if (sw.Elapsed > slowest) slowest = sw.Elapsed;
            checks++;
        }
        Task.WaitAll(writers);
        Assert.True(slowest < TimeSpan.FromSeconds(5), $"cea mai lentă verificare a așteptat {slowest.TotalSeconds:0.0} s");
        Assert.Equal(601, new HashChain(log).Head().Seq);
    }

    [Fact]
    public void A_held_turnstile_costs_one_short_wait_then_is_skipped_and_the_log_still_works()
    {
        // review 2026-11-18: the mutex name is predictable; a process holding it must not stall every open of the log
        var log = Path.Combine(_dir, "held.jsonl");
        Add(log, 1);
        var name = @"Local\LogAnalyzer-chain-" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(log).ToLowerInvariant())))[..32];
        using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { using var m = new Mutex(true, name); held.Set(); release.Wait(); m.ReleaseMutex(); });
        holder.Start(); held.Wait();
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Add(log, 1);                                             // waits once, then the old path
            var first = sw.Elapsed; sw.Restart();
            Add(log, 3); Assert.Equal(ChainStatus.Valid, V(log).Status);
            var rest = sw.Elapsed;
            // a holder that makes no progress costs Wait + one Slice (2 + 2 s, CI 2026-11-18 design), then the turnstile is skipped
            Assert.True(first < HashChain.Turnstile.Wait + HashChain.Turnstile.Slice + TimeSpan.FromSeconds(3), $"prima operație a așteptat {first.TotalSeconds:0.0} s");
            Assert.True(first >= HashChain.Turnstile.Wait, $"prima operație nu a așteptat turnichetul ({first.TotalSeconds:0.0} s)");
            Assert.True(rest < TimeSpan.FromSeconds(2), $"operațiile următoare au așteptat {rest.TotalSeconds:0.0} s");
            Assert.Equal(5, new HashChain(log).Head().Seq);
        }
        finally { release.Set(); holder.Join(); }
    }

    [Fact]
    public void A_witness_folder_found_without_protection_is_moved_aside_and_recreated_protected()
    {
        // review 2026-11-18: a standard user can pre-create the folder in ProgramData and own the witness; production checks the DACL (injected here)
        var witnessDir = Path.Combine(_dir, "witness");
        Directory.CreateDirectory(witnessDir);
        File.WriteAllText(Path.Combine(witnessDir, "planted.witness"), "{}");
        var protectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var log = Path.Combine(_dir, "p.jsonl");
        var fw = new LogAnalyzer.Dfir.Auth.FileHeadWitness(witnessDir, log, d => protectedDirs.Contains(d), d => { Directory.CreateDirectory(d); protectedDirs.Add(d); });   // production creates the folder with its DACL
        HashChain.RegisterHeadSeal(log, _key, fw);
        Add(log, 2);
        Assert.NotNull(fw.MovedAside);
        Assert.True(File.Exists(Path.Combine(fw.MovedAside!, "planted.witness")));   // kept as found, not deleted
        Assert.Contains(witnessDir, protectedDirs);
        Assert.Equal(2, fw.Read()!.Value.Seq);
        Assert.Equal(ChainStatus.Valid, V(log).Status);
    }
}