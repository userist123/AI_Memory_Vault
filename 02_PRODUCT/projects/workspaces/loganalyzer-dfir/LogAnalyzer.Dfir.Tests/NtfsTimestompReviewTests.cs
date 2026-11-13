using System.Globalization;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Review round: zone independence, files held open, AF08 evidence limits (see NtfsTimestompTests for the helpers).</summary>
public sealed partial class NtfsTimestompTests
{
    [Theory]
    [InlineData("UTC")]
    [InlineData("W. Europe Standard Time")]
    [InlineData("Arabic Standard Time")]
    [InlineData("GTB Standard Time")]
    public void Calibration_is_the_same_whatever_zone_converts_the_usn_times(string zone)
    {
        var (_, scenarios) = Truth();
        var findings = Rule(Mft(Calib("mft.bin")).Concat(Usn(Calib("usn.csv"), zone)));
        foreach (var s in scenarios)
        {
            if (s.Name is "A" or "B" or "C" or "D" or "E") Assert.True(LevelOf(findings, s) == Severity.High, $"{zone} {s.Name}: așteptat High, obținut {LevelOf(findings, s)?.ToString() ?? "nimic"}");
            else Assert.True(LevelOf(findings, s) is null, $"{zone} {s.Name}: așteptat nimic, obținut {LevelOf(findings, s)}");
        }
    }

    private static IEnumerable<TimelineEvent> HeldOpen(string id, string siCreated, string fnCreated, string chg, bool rename)
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 10, 5, 0, TimeSpan.Zero);   // the Close of the creation operation: 5 min after the file was created
        yield return MftEv(id, "held.log", siCreated, fnCreated, chg, category: "recent");
        yield return UsnEv(id[2..], "Data extend | File create | Close", t0, 100, "held.log");
        yield return UsnEv(id[2..], "Basic info change | Close", t0.AddMinutes(1), 200, "held.log");
        if (rename) yield return UsnEv(id[2..], "Rename: new name | Close", t0.AddMinutes(2), 300, "held.log");
    }

    [Fact]
    public void Held_open_file_with_a_later_attribute_change_is_silent()
    {
        var id = Id(1, 0x40);
        // created 10:00, kept open until 10:05 (the USN Close), then curl -R / attrib: $SI == $FN, nothing was changed
        Assert.Empty(Rule(HeldOpen(id, "2026-11-10T10:00:00.0000000Z", "2026-11-10T10:00:00.0000000Z", "2026-11-10T10:06:00.0000000Z", rename: false)));
        // the same with a rename after the attribute change and $SI 10 min before the Close: under 24 h, still silent
        Assert.Empty(Rule(HeldOpen(id, "2026-11-10T09:55:00.0000000Z", "2026-11-10T09:55:00.0000000Z", "2026-11-10T10:06:00.0000000Z", rename: true)));
    }

    [Fact]
    public void Rename_path_needs_creation_then_separate_basic_info_change_then_rename_in_usn_number_order_and_more_than_24_hours()
    {
        var id = Id(1, 0x41);
        var si = "2019-03-14T09:26:53.0000000Z";
        Assert.Equal(Severity.High, Assert.Single(Rule(HeldOpen(id, si, si, "2026-11-10T10:06:00.0000000Z", rename: true))).Severity);
        Assert.Empty(Rule(HeldOpen(id, si, si, "2026-11-10T10:06:00.0000000Z", rename: false)));   // no rename: nothing in the MFT shows it either
        // order comes from the Usn number, never from the time: a rename numbered before the attribute change does not count
        var wrongOrder = new[] { MftEv(id, "x.txt", si, si, "2026-11-10T10:06:00.0000000Z", category: "recent"),
            UsnEv(id[2..], "Data extend | File create | Close", Create, 100), UsnEv(id[2..], "Rename: new name | Close", Create.AddSeconds(5), 200), UsnEv(id[2..], "Basic info change | Close", Create.AddSeconds(2), 300) };
        Assert.Empty(Rule(wrongOrder));
    }

    [Fact]
    public void Mismatch_with_usn_but_no_separate_basic_info_change_stays_medium()
    {
        var id = Id(1, 0x42);
        var f = Assert.Single(Rule([MftEv(id, "x.txt", Si2019, Fn2026, Chg2026), UsnEv(id[2..], "Data extend | File create | Basic info change | Close", Create, 100)]));
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void Mismatch_high_does_not_depend_on_the_usn_times()
    {
        var id = Id(1, 0x43);
        // USN times ten hours off in either direction (wrong zone): the order is by Usn number and the comparison is $SI against $FN
        foreach (var shift in new[] { -10, 10 })
        {
            var usn = UsnStomp(id[2..]).Select(e => UsnEv(id[2..], F(e, "Reason"), e.Time.Utc!.Value.AddHours(shift), long.Parse(F(e, "Usn"), CultureInfo.InvariantCulture))).ToList();
            var f = Assert.Single(Rule([MftEv(id, "x.txt", Si2019, Fn2026, Chg2026), .. usn]));
            Assert.Equal(Severity.High, f.Severity);
        }
    }

    private static TimelineEvent Summary(string recent = "emisă", long mismatchTotal = 0, long mismatchEmitted = 0, DateTimeOffset? acquired = null)
    {
        var e = new TimelineEvent
        {
            Time = acquired is { } a ? Timestamp.FromUtc(a.UtcDateTime, "a", "t") : Timestamp.Unknown(), Source = "MFT", EvidenceId = "EV-MFT", Path = "", Process = "", Summary = "s", TimeSemantics = "t",
            TemporalType = TemporalType.Derived, Classification = Classification.Direct, Confidence = Confidence.High, Locator = "summary",
        };
        e.Fields["Category"] = "summary"; e.Fields["TotalRecords"] = "100"; e.Fields["InUse"] = "90"; e.Fields["RecentCategory"] = recent;
        e.Fields["MismatchTotal"] = mismatchTotal.ToString(CultureInfo.InvariantCulture); e.Fields["MismatchEmitted"] = mismatchEmitted.ToString(CultureInfo.InvariantCulture);
        e.Fields["RecentTotal"] = "0"; e.Fields["RecentEmitted"] = "0";
        return e;
    }

    [Fact]
    public void Af08_not_detected_needs_recent_emitted_no_cap_and_usn_inside_the_30_day_window()
    {
        var acq = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        var id = Id(1, 0x50);
        var usnInside = UsnEv(id[2..], "Data extend | File create | Close", acq.AddDays(-2), 100);
        Assert.Equal(AntiForensicResult.NotDetected, Af08([Summary(acquired: acq), usnInside]).Result);
        Assert.Equal(AntiForensicResult.NotDetected, Af08([Summary(acquired: acq)]).Result);   // no USN: the limit is in the text
        // P4b: the MFT had no acquisition date, so "recent" was not emitted
        var noRecent = Af08([Summary(recent: "omisă: proba nu are data achiziției"), usnInside]);
        Assert.Equal(AntiForensicResult.Undetermined, noRecent.Result);
        Assert.Contains("recente", noRecent.Reason);
        // P4d: the USN is older than the window of 30 days before acquisition
        var old = Af08([Summary(acquired: acq.AddDays(60)), usnInside]);
        Assert.Equal(AntiForensicResult.Undetermined, old.Result);
        Assert.Contains("30 de zile", old.Reason);
        // a cap cut mismatch records
        var capped = Af08([Summary(acquired: acq, mismatchTotal: 900, mismatchEmitted: 500)]);
        Assert.Equal(AntiForensicResult.Undetermined, capped.Result);
        Assert.Contains("plafoane", capped.Reason);
    }

    [Fact]
    public void Af08_not_detected_lists_the_limits()
    {
        var af = Af08([Summary(acquired: new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero))]);
        Assert.Contains("redenumire", af.Reason);
        Assert.Contains("ora de schimbare", af.Reason);
    }

    [Fact]
    public void Usn_parser_uses_the_zone_of_the_evidence_item_and_records_the_time_source()
    {
        var path = Calib("usn.csv");
        List<TimelineEvent> Run(string tz)
        {
            var sink = new ListSink();
            new UsnJournalParser(TimeZoneInfo.Utc).Parse(new EvidenceItem { EvidenceId = "EV-USN", CaseId = "C", Source = "t", SourceType = "usn_journal", StoredPath = path, Timezone = tz }, path, sink, default);
            return sink.Events.ToList();
        }
        var gtb = Run("GTB Standard Time").First(); var none = Run("").First(); var bogus = Run("Nu există").First();
        Assert.Equal(gtb.Time.Utc!.Value.AddHours(2), none.Time.Utc!.Value);   // November: GTB = UTC+2, so the same local text is 2 h later when read as UTC
        Assert.Equal(none.Time.Utc, bogus.Time.Utc);                           // an unknown zone id is ignored, not guessed
        Assert.Contains("GTB Standard Time", F(gtb, "TimeSource"));
        Assert.Contains("UTC", F(none, "TimeSource"));
    }
}
