using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Case;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Real multi-PROCESS tests (planner #8, item 1, phase A) for the sealed HashChain and SharedAppend, using the helper exe LogAnalyzer.Dfir.ChainWorker
/// (copied to &lt;test output&gt;\chainworker\ by the test project). Fault injection (HashChain.FaultInjection) is switched on in the worker only.
/// </summary>
public sealed class MultiProcessChainTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-mp-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private sealed record Run(int ExitCode, string Stdout, string Stderr);

    private static string WorkerExe()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "chainworker");
        var exe = Path.Combine(dir, "LogAnalyzer.Dfir.ChainWorker.exe");
        if (File.Exists(exe)) return exe;
        throw new FileNotFoundException("ChainWorker not found (is the ProjectReference/copy target in the test csproj broken?): " + exe);
    }

    private static Process Start(IEnumerable<string> args, string? fault = null)
    {
        var psi = new ProcessStartInfo(WorkerExe()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment.Remove("LA_CHAIN_FAULT");
        if (fault is not null) psi.Environment["LA_CHAIN_FAULT"] = fault;
        return Process.Start(psi)!;
    }

    private static async Task<Run> Finish(Process p)
    {
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new Run(p.ExitCode, await o, await e);
    }

    private string NewLog(out byte[] key)
    {
        Directory.CreateDirectory(_dir);
        key = RandomNumberGenerator.GetBytes(32);
        var log = Path.Combine(_dir, "audit.jsonl");
        HashChain.RegisterHeadSeal(log, key);
        return log;
    }

    [Fact]
    public async Task Three_processes_appending_to_one_sealed_log_never_leave_it_broken_for_a_verifier()
    {
        var log = NewLog(out var key);
        var chain = new HashChain(log);
        chain.Append(new { Tag = "seed", I = 0 });
        var hex = Convert.ToHexString(key);
        var procs = new[] { "A", "B", "C" }.Select(t => Start(["append", log, hex, "200", t])).ToList();
        var runs = procs.Select(Finish).ToList();
        var all = Task.WhenAll(runs);
        int checks = 0;
        var broken = new List<string>();
        while (!all.IsCompleted)
        {
            var v = chain.ReadVerified().Verification;
            checks++;
            if (v.Status == ChainStatus.Broken) broken.Add(v.Message);
            await Task.Delay(5);
        }
        var results = await all;
        output.WriteLine($"verifier checks while appending: {checks}");
        Assert.All(results, r => Assert.True(r.ExitCode == 0, $"worker exit {r.ExitCode}: {r.Stderr}"));
        Assert.True(broken.Count == 0, "Broken seen while appending: " + string.Join(" | ", broken.Distinct()));
        var final = chain.Verify();
        Assert.Equal(ChainStatus.Valid, final.Status);
        Assert.Equal(601, final.Entries);
    }

    [Fact]
    public async Task Three_processes_writing_through_SharedAppend_produce_exactly_600_whole_lines()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "shared.log");
        var results = await Task.WhenAll(new[] { "A", "B", "C" }.Select(t => Finish(Start(["sharedappend", path, "200", t]))));
        Assert.All(results, r => Assert.True(r.ExitCode == 0, $"worker exit {r.ExitCode}: {r.Stderr}"));
        var text = File.ReadAllText(path);
        Assert.EndsWith("\n", text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(600, lines.Length);
        var pad = new string('x', 200);
        foreach (var l in lines)
        {
            var parts = l.Split('|');
            Assert.True(parts.Length == 3 && parts[0] is "A" or "B" or "C" && int.TryParse(parts[1], out _) && parts[2] == pad, "interleaved or partial line: " + l);
        }
        foreach (var t in new[] { "A", "B", "C" })
            Assert.Equal(Enumerable.Range(1, 200), lines.Where(l => l.StartsWith(t + "|")).Select(l => int.Parse(l.Split('|')[1])).ToList());
    }

    private string Describe(string log)
    {
        long headSeq = -1;
        var hp = HashChain.HeadPath(log);
        if (File.Exists(hp)) headSeq = JsonNode.Parse(File.ReadAllText(hp))!["seq"]!.GetValue<long>();
        var lastLine = File.ReadAllLines(log).Last(l => l.Length > 0);
        long lastSeq = JsonNode.Parse(lastLine)!["seq"]!.GetValue<long>();
        return $".head seq={headSeq}, last line seq={lastSeq}, .head.tmp present={File.Exists(hp + ".tmp")}";
    }

    private void CrashThenAppend(string point, out ChainVerification afterCrash, out ChainVerification afterNext, int at = 3)
    {
        var log = NewLog(out var key);
        using var p = Start(["append", log, Convert.ToHexString(key), "5", "W"], fault: point + ":" + at);
        var run = Finish(p).GetAwaiter().GetResult();
        output.WriteLine($"[{point}] worker exit={run.ExitCode} (FailFast expected, not 0)");
        Assert.NotEqual(0, run.ExitCode);
        var chain = new HashChain(log);
        afterCrash = chain.Verify();
        output.WriteLine($"[{point}] after crash: {afterCrash.Status} entries={afterCrash.Entries} | {afterCrash.Message} | {Describe(log)}");
        chain.Append(new { Tag = "after", I = 99 });
        afterNext = chain.Verify();
        output.WriteLine($"[{point}] after next append: {afterNext.Status} entries={afterNext.Entries} | {afterNext.Message} | {Describe(log)}");
    }

    [Fact]
    public void Crash_after_line_flush_is_recovered_by_the_next_append()
    {
        // before the fix: Broken "intrări adăugate după ultimul cap sigilat (Seq 2) fără cheia de sigilare" after the crash and after the next append
        CrashThenAppend("after-line-flush", out var crash, out var next);
        Assert.Equal(ChainStatus.Valid, crash.Status);   // the key holder's own entry, announced in the MACed intent: seal move pending, not tampering
        Assert.Equal(3, crash.Entries);
        Assert.Equal(ChainStatus.Valid, next.Status);
        Assert.Equal(4, next.Entries);
    }

    [Fact]
    public void Crash_after_head_tmp_is_recovered_by_the_next_append()
    {
        CrashThenAppend("after-head-tmp", out var crash, out var next);
        Assert.Equal(ChainStatus.Valid, crash.Status);
        Assert.Equal(3, crash.Entries);
        Assert.Equal(ChainStatus.Valid, next.Status);
        Assert.Equal(4, next.Entries);
    }

    [Fact]
    public void Crash_after_head_move_leaves_a_valid_log_and_the_next_append_continues_it()
    {
        CrashThenAppend("after-head-move", out var crash, out var next);
        Assert.Equal(ChainStatus.Valid, crash.Status);
        Assert.Equal(3, crash.Entries);
        Assert.Equal(ChainStatus.Valid, next.Status);
        Assert.Equal(4, next.Entries);
    }

    [Fact]
    public void Crash_after_the_intent_and_before_the_line_leaves_a_valid_log()
    {
        CrashThenAppend("after-intent", out var crash, out var next);
        Assert.Equal(ChainStatus.Valid, crash.Status);
        Assert.Equal(2, crash.Entries);
        Assert.Equal(ChainStatus.Valid, next.Status);
        Assert.Equal(3, next.Entries);
    }

    [Fact]
    public void An_entry_appended_without_the_key_stays_broken_even_with_an_old_intent_of_the_key_holder()
    {
        // the intent only covers the one entry the key holder announced after that sealed head: it never launders another line
        var log = NewLog(out var key);
        using (var p = Start(["append", log, Convert.ToHexString(key), "5", "W"], fault: "after-line-flush:3")) Finish(p).GetAwaiter().GetResult();
        var oldIntent = File.ReadAllBytes(HashChain.IntentPath(log));
        new HashChain(log).Append(new { Tag = "after", I = 99 });                 // recovers the crashed entry, seals 4
        HashChain.RegisterHeadSeal(log, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        new HashChain(log).Append(new { Tag = "forged", I = 1 });                 // another key: the seal does not match, no intent is written
        HashChain.RegisterHeadSeal(log, key);
        File.WriteAllBytes(HashChain.IntentPath(log), oldIntent);                // replay of the key holder's old intent
        var v = new HashChain(log).Verify();
        Assert.Equal(ChainStatus.Broken, v.Status);
        Assert.Contains("fără cheia de sigilare", v.Message);
        new HashChain(log).Append(new { Tag = "next", I = 1 });                   // and the next append of the key holder does not seal it either
        Assert.Equal(ChainStatus.Broken, new HashChain(log).Verify().Status);
    }

    [Theory]
    [InlineData("after-line-flush")]
    [InlineData("after-head-tmp")]
    public void A_crash_in_the_very_first_sealed_append_is_recovered_too(string point)
    {
        // review 2026-11-17: there is no .head before the first sealed append; the intent covers the empty chain as predecessor
        CrashThenAppend(point, out var crash, out var next, at: 1);
        Assert.Equal(ChainStatus.Valid, crash.Status);
        Assert.Equal(1, crash.Entries);
        Assert.Equal(ChainStatus.Valid, next.Status);
        Assert.Equal(2, next.Entries);
    }

    [Fact]
    public void The_intent_does_not_reveal_the_hash_of_the_entry_it_announces()
    {
        var log = NewLog(out var key);
        using (var p = Start(["append", log, Convert.ToHexString(key), "5", "W"], fault: "after-line-flush:3")) Finish(p).GetAwaiter().GetResult();
        var intent = File.ReadAllText(HashChain.IntentPath(log));
        var lastHash = JsonNode.Parse(File.ReadAllLines(log).Last(l => l.Length > 0))!["hash"]!.GetValue<string>();
        Assert.DoesNotContain(lastHash, intent, StringComparison.OrdinalIgnoreCase);
        Assert.Null(JsonNode.Parse(intent)!["hash"]);
    }

    [Fact]
    public void Another_line_put_after_an_announced_but_unwritten_entry_stays_broken()
    {
        // a crash after the intent and before the line leaves an intent for seq N+1; a line built by someone without the key for seq N+1 is not the announced one
        var log = NewLog(out var key);
        using (var p = Start(["append", log, Convert.ToHexString(key), "5", "W"], fault: "after-intent:3")) Finish(p).GetAwaiter().GetResult();
        Assert.True(File.Exists(HashChain.IntentPath(log)));
        HashChain.RegisterHeadSeal(log, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        new HashChain(log).Append(new { Tag = "forged", I = 3 });                 // a correctly chained line of seq 3 appended without the key
        HashChain.RegisterHeadSeal(log, key);
        var v = new HashChain(log).Verify();
        Assert.Equal(ChainStatus.Broken, v.Status);
        Assert.Contains("fără cheia de sigilare", v.Message);
    }
}