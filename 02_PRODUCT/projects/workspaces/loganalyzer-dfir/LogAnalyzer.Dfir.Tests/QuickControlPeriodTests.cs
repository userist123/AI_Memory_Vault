using LogAnalyzer.Dfir.Case;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review of PR #298: the one-click control's default period does not come from the case folder (a planted "previous control" folder could shorten it)
/// but from the paper anchor code, else from the sealed audit's last „control.finished”, else 90 days; and it is always said in words.
/// </summary>
public sealed class QuickControlPeriodTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-quickperiod-" + Guid.NewGuid().ToString("N"));
    public QuickControlPeriodTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Log(params (string Event, DateTimeOffset When)[] entries)
    {
        var p = Path.Combine(_dir, "auth_audit.jsonl");
        var c = new HashChain(p);
        foreach (var (ev, when) in entries) c.Append(new { Event = ev, Detail = "d", WhenUtc = when.UtcDateTime.ToString("o") });
        return p;
    }

    [Fact]
    public void Without_an_audit_the_period_is_the_last_90_days_and_says_so()
    {
        var p = ControlPeriodStart.From(Path.Combine(_dir, "missing.jsonl"), "", Now);
        Assert.Equal(90, p.Days);
        Assert.Contains("90 de zile", p.Basis);
    }

    [Fact]
    public void Without_a_paper_code_the_period_starts_at_the_last_control_finished_in_the_sealed_audit()
    {
        var log = Log(("control.started", Now.AddDays(-45)), ("control.finished", Now.AddDays(-44)), ("control.finished", Now.AddDays(-20)), ("login", Now.AddDays(-1)));
        var p = ControlPeriodStart.From(log, "", Now);
        Assert.Equal(20, p.Days);
        Assert.Equal(Now.AddDays(-20), p.StartUtc);
        Assert.Contains("jurnalul sigilat", p.Basis);
        // 2026-10-22 PC walkthrough: local time in the Romanian format, not UTC in ISO format
        Assert.Matches(@"\(\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}, ora stației\)", p.Basis);
        Assert.DoesNotContain("UTC", p.Basis);
    }

    [Fact]
    public void With_the_paper_code_the_period_starts_at_the_anchored_control_even_if_a_later_control_exists()
    {
        var log = Log(("control.finished", Now.AddDays(-41)), ("audit.anchor_state", Now.AddDays(-40)));
        var code = AuditAnchor.Current(log)!.Code;
        new HashChain(log).Append(new { Event = "control.finished", Detail = "d", WhenUtc = Now.AddDays(-3).UtcDateTime.ToString("o") });
        var p = ControlPeriodStart.From(log, code, Now);
        Assert.Equal(40, p.Days);
        Assert.Contains("procesul-verbal", p.Basis);
    }

    [Fact]
    public void A_paper_code_that_the_log_no_longer_contains_falls_back_to_the_sealed_audit_and_says_why()
    {
        var log = Log(("control.finished", Now.AddDays(-12)));
        var p = ControlPeriodStart.From(log, "AAAAA-BBBBB-CCCCC-DDDDD", Now);
        Assert.Equal(12, p.Days);
        Assert.Contains("nu", p.Basis);
    }

    [Fact]
    public void A_broken_chain_is_not_trusted_for_the_period()
    {
        var log = Log(("control.finished", Now.AddDays(-5)), ("login", Now.AddDays(-4)));
        var lines = File.ReadAllLines(log);
        lines[0] = System.Text.RegularExpressions.Regex.Replace(lines[0], @"(""[Dd]etail"":)""d""", "$1\"x\"");   // an entry changed after it was written
        Assert.NotEqual(File.ReadAllLines(log)[0], lines[0]);
        File.WriteAllLines(log, lines);
        var p = ControlPeriodStart.From(log, "", Now);
        Assert.Equal(90, p.Days);
    }

    [Fact]
    public void A_control_finished_appended_without_the_seal_key_does_not_shorten_the_period()
    {
        // re-review of PR #298: someone without the key appends a plain-valid „control.finished” dated yesterday; the sealed check refuses it
        var log = Path.Combine(_dir, "sealed_audit.jsonl");
        HashChain.RegisterHeadSeal(log, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var sealedChain = new HashChain(log);
        sealedChain.Append(new { Event = "control.finished", Detail = "d", WhenUtc = Now.AddDays(-30).UtcDateTime.ToString("o") });
        Assert.Equal(30, ControlPeriodStart.From(log, "", Now).Days);              // sealed and intact: the last control counts

        var outside = Path.Combine(_dir, "copy_without_key.jsonl");                  // the same file, written by a process that has no key
        File.Copy(log, outside);
        new HashChain(outside).Append(new { Event = "control.finished", Detail = "d", WhenUtc = Now.AddDays(-1).UtcDateTime.ToString("o") });
        File.Copy(outside, log, overwrite: true);
        Assert.Equal(ChainStatus.Valid, new HashChain(log).VerifyPlainChain().Status);   // the plain chain alone cannot see it

        var p = ControlPeriodStart.From(log, "", Now);
        Assert.Equal(90, p.Days);
        Assert.Contains("jurnalul nu poate fi verificat (sigiliu) — perioada implicită 90 de zile", p.Basis);
    }
}
