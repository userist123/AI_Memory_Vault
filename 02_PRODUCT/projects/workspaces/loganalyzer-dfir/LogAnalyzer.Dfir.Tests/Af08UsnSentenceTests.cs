using System.Globalization;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>AF08 "undetermined": the reasons are separate sentences, none starts with a lower-case letter and none is stated twice (WS01 text, 2026-11-14).</summary>
public sealed class Af08UsnSentenceTests
{
    private static readonly DateTimeOffset Acquired = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    private static TimelineEvent Summary(long chTotal, long counted)
    {
        var s = new TimelineEvent { Time = Timestamp.FromUtc(Acquired.UtcDateTime, "a", "t"), Source = "MFT", EvidenceId = "EV-MFT", Process = "", Path = "", Summary = "s", TimeSemantics = "t", TemporalType = TemporalType.Derived, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "summary" };
        foreach (var (k, v) in new Dictionary<string, string> { ["Category"] = "summary", ["TotalRecords"] = "1000", ["RecentCategory"] = "emisă", ["MismatchTotal"] = counted.ToString(CultureInfo.InvariantCulture), ["MismatchEmitted"] = "0", ["MismatchSystemTrees"] = counted.ToString(CultureInfo.InvariantCulture), ["MismatchDirectories"] = "0", ["RecentTotal"] = "0", ["RecentEmitted"] = "0", ["SystemMismatchRecentTotal"] = chTotal.ToString(CultureInfo.InvariantCulture), ["SystemMismatchRecentEmitted"] = "20000" })
            s.Fields[k] = v;
        return s;
    }

    private static TimelineEvent UsnOp(long usn)
    {
        var e = new TimelineEvent { Time = Timestamp.FromUtc(Acquired.UtcDateTime.AddDays(-1), "r", "t"), Source = "USN", EvidenceId = "EV-USN", Process = "", Path = "\\a.txt", Summary = "u", TimeSemantics = "t", TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "USN#1" };
        e.Fields["Usn"] = usn.ToString(CultureInfo.InvariantCulture); e.Fields["FileName"] = "a.txt"; e.Fields["Reason"] = "Data extend | Close"; e.Fields["FileId"] = "0x0001000000000099";
        return e;
    }

    private static string Af08(params TimelineEvent[] events) => AntiForensics.Evaluate(events, []).Single(c => c.Id == "AF08").Reason;

    [Fact]
    public void Capped_channel_without_usn_gives_two_separate_sentences_the_ws01_combination()
    {
        var reason = Af08(Summary(93_913, 0));
        var channel = $"stație instalată sau actualizată masiv în ultimele 30 de zile: {93_913:N0} fișiere de sistem noi, doar primele {20_000:N0} verificate cu USN.";
        Assert.Contains(channel + " Jurnalul USN nu a fost analizat: ", reason);
        Assert.DoesNotContain("USN. fără jurnal USN", reason);
        Assert.DoesNotContain(". f", reason);
        Assert.DoesNotMatch(new Regex(@"\. \p{Ll}"), reason);
        Assert.Equal(1, Regex.Matches(reason, "Jurnalul USN nu a fost analizat").Count);
    }

    [Fact]
    public void Capped_channel_with_usn_says_the_journal_was_analysed_in_its_own_sentence()
    {
        var reason = Af08(Summary(93_913, 0), UsnOp(10), UsnOp(20));
        Assert.Contains($"doar primele {20_000:N0} verificate cu USN. Jurnalul USN a fost analizat (", reason);
        Assert.DoesNotContain("nu a fost analizat", reason);
        Assert.DoesNotMatch(new Regex(@"\. \p{Ll}"), reason);
    }

    [Fact]
    public void A_missing_journal_reason_that_already_says_so_is_not_repeated()
    {
        var reason = Af08(Summary(0, 900));
        Assert.Contains("fără jurnalul USN nu pot fi verificate", reason);
        Assert.DoesNotContain("Jurnalul USN nu a fost analizat", reason);
        Assert.DoesNotMatch(new Regex(@"\. \p{Ll}"), reason);
    }
}
