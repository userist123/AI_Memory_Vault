using System.Globalization;

namespace LogAnalyzer.Dfir.Case;

/// <summary>The start of the period of a one-click control, how many days back it is, and why (one line for the screen).</summary>
public sealed record ControlPeriodPlan(DateTimeOffset StartUtc, int Days, string Basis);

/// <summary>
/// Review of PR #298: where the default period of the one-click control starts. Not from the case folder (anyone who can write there could plant a recent
/// "previous control" and shorten the period), but, in order:
/// 1. the paper anchor code typed by the officer, when the application's log still contains it — the time of that anchored entry;
/// 2. otherwise the last „control.finished” in the sealed audit log, when its plain chain is not broken;
/// 3. otherwise the last 90 days.
/// The result is always at least one day and at most <see cref="MaxDays"/>.
/// </summary>
public static class ControlPeriodStart
{
    public const int DefaultDays = 90, MaxDays = 3650;

    public static ControlPeriodPlan From(string? auditPath, string? paperCode, DateTimeOffset nowUtc)
    {
        string prefix = "";
        if (auditPath is null || !File.Exists(auditPath)) return Default(nowUtc, "jurnalul aplicației nu este disponibil");
        List<AuditAnchor.AuditEvent> events;
        try
        {
            if (new HashChain(auditPath).VerifyPlainChain().Status == ChainStatus.Broken)
                return Default(nowUtc, "jurnalul aplicației are o verigă ruptă, deci nu i se poate da crezare pentru perioadă");
            events = AuditAnchor.Events(auditPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Default(nowUtc, "jurnalul aplicației nu a putut fi citit"); }

        if (!string.IsNullOrWhiteSpace(paperCode))
        {
            var v = AuditAnchor.Verify(auditPath, paperCode);
            if (v.Status == AnchorContinuityStatus.Continuous && v.MatchedSeq is { } seq && events.FirstOrDefault(e => e.Seq == seq) is { } anchored && Time(anchored) is { } t && t <= nowUtc)
                return Plan(nowUtc, t, $"de la controlul ancorat pe procesul-verbal ({t.UtcDateTime:yyyy-MM-dd HH:mm} UTC)");
            prefix = "codul de pe procesul-verbal nu se regăsește în jurnal (verificarea A11 o va spune); ";
        }
        var last = events.Where(e => e.Event == "control.finished").Select(Time).Where(t => t is { } x && x <= nowUtc).Max();
        if (last is { } l) return Plan(nowUtc, l, prefix + $"de la ultimul control din jurnalul sigilat ({l.UtcDateTime:yyyy-MM-dd HH:mm} UTC)");
        return Default(nowUtc, prefix + "niciun control anterior în jurnalul sigilat");
    }

    private static DateTimeOffset? Time(AuditAnchor.AuditEvent e) =>
        DateTimeOffset.TryParse(e.WhenUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    private static ControlPeriodPlan Plan(DateTimeOffset now, DateTimeOffset start, string basis)
    {
        var days = (int)Math.Clamp(Math.Ceiling((now - start).TotalDays), 1, MaxDays);
        return new ControlPeriodPlan(now.AddDays(-days) > start ? now.AddDays(-days) : start, days, basis + $" — {days} {(days == 1 ? "zi" : "zile")}");
    }

    private static ControlPeriodPlan Default(DateTimeOffset now, string why) =>
        new(now.AddDays(-DefaultDays), DefaultDays, $"{why}; se verifică ultimele {DefaultDays} de zile");
}
