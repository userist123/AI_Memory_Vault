using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Dfir.Case;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.App.Tests;

[CollectionDefinition("TurnstileStarvation", DisableParallelization = true)]
public sealed class TurnstileStarvationCollection;

/// <summary>
/// CI 2026-11-19 (runs 44559083457, 44575459133): MultiProcessChainTests failed with "being used by another process" out of ReadVerified. Cause: a turnstile wait that
/// ended without the mutex switched the turnstile off for 60 s (Backoff) in that process; the reader then competed raw with three writers that re-take the exclusive
/// handle right after releasing it, and exhausted its sharing-retry budget. The Dfir test DLL cannot run on the dev machine, so this reproduces it from App.Tests.
/// </summary>
[Collection("TurnstileStarvation")]
public sealed class TurnstileStarvationTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-turn-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string MutexName(string log) => @"Local\LogAnalyzer-chain-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(log).ToLowerInvariant())))[..32];

    private static string WorkerExe()
    {
        foreach (var cfg in new[] { "Debug", "Release" })
        {
            var exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LogAnalyzer.Dfir.ChainWorker", "bin", cfg, "net10.0", "LogAnalyzer.Dfir.ChainWorker.exe"));
            if (File.Exists(exe)) return exe;
        }
        throw new FileNotFoundException("ChainWorker exe not found next to the solution output");
    }

    /// <summary>Holds the turnstile mutex with no progress on the log for longer than Wait + Slice, so the first open gives up and the backoff starts; then releases it.</summary>
    private static void ForceBackoff(HashChain chain, string log)
    {
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { using var m = new Mutex(true, MutexName(log)); held.Set(); release.Wait(); m.ReleaseMutex(); });
        holder.Start(); held.Wait();
        try { chain.ReadVerified(); }   // costs Wait + one Slice, then SkipUntil[log] = now + Backoff
        finally { release.Set(); holder.Join(); }
    }

    [Fact]
    public async Task A_reader_in_backoff_is_not_starved_by_three_writer_processes()
    {
        Directory.CreateDirectory(_dir);
        var key = RandomNumberGenerator.GetBytes(32);
        var log = Path.Combine(_dir, "audit.jsonl");
        HashChain.RegisterHeadSeal(log, key);
        var chain = new HashChain(log);
        chain.Append(new { Tag = "seed" });
        var savedAttempts = HashChain.SharingAttempts;
        HashChain.SharingAttempts = 120;   // review: ~1 s of raw retries, then the turnstile, then enough budget for one slow append (fsync + head move + witness) on a slow runner
        try
        {
            ForceBackoff(chain, log);
            var procs = Enumerable.Range(0, 3).Select(i =>
            {
                var psi = new ProcessStartInfo(WorkerExe()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in new[] { "append", log, Convert.ToHexString(key), "200", "W" + i }) psi.ArgumentList.Add(a);
                return Process.Start(psi)!;
            }).ToList();
            var all = Task.WhenAll(procs.Select(p => p.WaitForExitAsync()));
            long slowest = 0; int checks = 0;
            Exception? failure = null;
            var sw = new Stopwatch();
            while (!all.IsCompleted)
            {
                sw.Restart();
                try { var (_, v) = chain.ReadVerified(); Assert.Equal(ChainStatus.Valid, v.Status); }
                catch (IOException e) { failure = e; break; }
                slowest = Math.Max(slowest, sw.ElapsedMilliseconds); checks++;
                await Task.Delay(5);
            }
            if (failure is not null) { foreach (var p in procs) { try { p.Kill(true); } catch { } } }
            await all;
            output.WriteLine($"checks={checks} slowestMs={slowest} failure={failure?.Message}");
            Assert.True(failure is null, "ReadVerified threw: " + failure?.Message);
            Assert.True(checks >= 1);
            Assert.Equal(601, new HashChain(log).Head().Seq);
        }
        finally { HashChain.SharingAttempts = savedAttempts; }
    }

    /// <summary>
    /// Deterministic emulation of a slow runner: three writers that honour the turnstile exactly as <c>OpenExclusive</c> does (mutex only while opening, never while
    /// holding the file) but hand the file over without any pause, so a raw poller almost never finds it free. With the turnstile skipped (backoff) the reader exhausts
    /// its sharing-retry budget; through the turnstile it is let in right after the current writer.
    /// </summary>
    [Fact]
    public void A_reader_in_backoff_gets_through_writers_that_hand_the_log_over_without_a_gap()
    {
        Directory.CreateDirectory(_dir);
        var log = Path.Combine(_dir, "audit.jsonl");
        var chain = new HashChain(log);
        chain.Append(new { Tag = "seed" });
        var savedAttempts = HashChain.SharingAttempts;
        HashChain.SharingAttempts = 120;   // review: ~1 s of raw retries, then the turnstile, then enough budget for one slow append (fsync + head move + witness) on a slow runner
        using var stop = new CancellationTokenSource();
        var writers = new List<Thread>();
        try
        {
            ForceBackoff(chain, log);
            for (int w = 0; w < 3; w++)
            {
                var t = new Thread(() =>
                {
                    using var m = new Mutex(false, MutexName(log));
                    while (!stop.IsCancellationRequested)
                    {
                        FileStream? fs = null;
                        m.WaitOne();
                        try { while (fs is null && !stop.IsCancellationRequested) { try { fs = new FileStream(log, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException) { Thread.Yield(); } } }
                        finally { m.ReleaseMutex(); }
                        if (fs is null) break;
                        using (fs) { var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 50; while (Stopwatch.GetTimestamp() < until) Thread.SpinWait(50); }   // 20 ms inside the file
                    }
                }) { IsBackground = true };
                t.Start(); writers.Add(t);
            }
            Thread.Sleep(200);
            var sw = Stopwatch.StartNew();
            IOException? failure = null;
            try { chain.ReadVerified(); } catch (IOException e) { failure = e; }
            output.WriteLine($"read took {sw.ElapsedMilliseconds} ms, failure={failure?.Message}");
            Assert.True(failure is null, "ReadVerified threw: " + failure?.Message);
        }
        finally { stop.Cancel(); foreach (var t in writers) t.Join(); HashChain.SharingAttempts = savedAttempts; }
    }

    [Fact]
    public void Backoff_is_cleared_once_the_mutex_is_obtained_and_re_armed_while_a_holder_is_stuck()
    {
        Directory.CreateDirectory(_dir);
        var log = Path.Combine(_dir, "audit.jsonl");
        var chain = new HashChain(log);
        chain.Append(new { Tag = "seed" });
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { using var m = new Mutex(true, MutexName(log)); held.Set(); release.Wait(); m.ReleaseMutex(); });
        holder.Start(); held.Wait();
        try
        {
            using (HashChain.Turnstile.Enter(log)) { }                       // stuck holder, no progress: gives up after Wait + Slice and starts the backoff
            var sw = Stopwatch.StartNew();
            using (var skipped = HashChain.Turnstile.Enter(log)) { Assert.Null(skipped); }
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), "the backoff did not skip the turnstile");
            using (var again = HashChain.Turnstile.Enter(log, ignoreBackoff: true)) { Assert.Null(again); }   // still stuck: bounded wait, no turnstile, backoff re-armed
            sw.Restart();
            using (var skippedAgain = HashChain.Turnstile.Enter(log)) { Assert.Null(skippedAgain); }
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), "the backoff was not re-armed after the stuck holder was tried again");
        }
        finally { release.Set(); holder.Join(); }
        using (var turn = HashChain.Turnstile.Enter(log, ignoreBackoff: true)) { Assert.NotNull(turn); }   // holder gone: obtained, backoff cleared
        var sw2 = Stopwatch.StartNew();
        using (var normal = HashChain.Turnstile.Enter(log)) { Assert.NotNull(normal); }                    // the turnstile is in use again for every open
        Assert.True(sw2.Elapsed < TimeSpan.FromSeconds(1));
    }
}
