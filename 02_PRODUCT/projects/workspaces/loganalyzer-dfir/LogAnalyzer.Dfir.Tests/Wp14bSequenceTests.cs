using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Registers;
using LogAnalyzer.Verification;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;
using static LogAnalyzer.Dfir.Tests.W14;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Synthetic timelines for the WP14b combined sequences (control gap + media, SMB + staging + USB, portable software + archive + USB).</summary>
internal static class Wp14b
{
    public static DateTimeOffset At(double minute) => new(W11.T0.AddMinutes(minute), TimeSpan.Zero);

    public static CaseScope Scope(bool classified = true, bool period = true) => new()
    {
        Network = NetworkCategory.AirGappedNetwork, Classification = classified ? ClassificationLevel.Classified : ClassificationLevel.Unclassified,
        PeriodFromUtc = period ? At(-24 * 60) : null, PeriodToUtc = period ? At(24 * 60) : null,
    };

    /// <summary>A POLICY-CONTROL-GAP finding as PolicyTimeline writes it: the control title in quotes, the window as first / last seen.</summary>
    public static Finding Gap(string control, double start, double? end, string id = "F-9001") => new()
    {
        FindingId = id, RuleId = "POLICY-CONTROL-GAP", Title = $"Decalaj de control: „{control}” este configurat, dar nivelul „enforced” nu este observat", Severity = Severity.Medium,
        Category = "Control gap", Classification = Classification.Correlated, FirstSeenUtc = start >= 0 ? At(start) : null, LastSeenUtc = end is { } e ? At(e) : null,
        Description = $"„{control}” cere activat. Se rupe la nivelul „enforced”.", SupportingEvidence = [new("EV-1", "EventRecordID=1", "politică")],
    };

    public static (List<TimelineEvent> Events, List<Finding> Existing) World(IEnumerable<TimelineEvent> events, CaseScope? scope = null, MediaRegister? media = null, params Finding[] extra)
    {
        var ev = events.ToList(); int n = 0;
        var wp14 = Wp14Rules.Run(ev, () => $"F-{++n:D4}", new Wp14Input { Scope = scope ?? Scope(), Media = media });
        return (ev, wp14.Concat(extra).ToList());
    }

    public static List<Finding> Seq(IEnumerable<TimelineEvent> events, CaseScope? scope = null, MediaRegister? media = null, SequenceData? data = null, params Finding[] extra)
    {
        var (ev, existing) = World(events, scope, media, extra); int m = 100;
        return SequenceRules.Run(ev, existing, () => $"F-{++m:D4}", new SequenceInput { Scope = scope ?? Scope(), Data = data });
    }

    public static TimelineEvent Share(double minute, string target = "plan.xlsx", string user = "ion", string ip = "10.1.2.3", string share = @"\\*\Docs", int id = 5145) =>
        Sec(id, minute, ("ShareName", share), ("RelativeTargetName", target), ("SubjectUserName", user), ("SubjectDomainName", "CORP"), ("IpAddress", ip));

    public static TimelineEvent Usn(double minute, string path, string reason = "FILE_CREATE|CLOSE") =>
        Row("USN", minute, path: path, f: [("Reason", reason), ("FileName", path[(path.LastIndexOf('\\') + 1)..])]);

    public static TimelineEvent MediaWrite(double minute, string path, string user = "ion") =>
        Sec(4663, minute, ("ObjectName", path), ("AccessMask", "0x2"), ("SubjectUserName", user), ("SubjectDomainName", "CORP"));

    public static TimelineEvent Prefetch(double minute, string path, string process) => Row("Prefetch", minute, process: process, path: path);

    public static TimelineEvent Lnk(double minute, string path, long size) => Row("LNK", minute, path: path, f: [("TargetSize", size.ToString()), ("DriveType", "FIXED")]);

    public static Finding One(List<Finding> found, string ruleId) => Assert.Single(found, f => f.RuleId == ruleId);

    /// <summary>The three scenarios that each produce a finding, for the checks that apply to all of them.</summary>
    public static List<Finding> AllPositive()
    {
        var all = new List<Finding>();
        all.AddRange(Seq([Usb("AA001", 30, letter: "E:")], extra: Gap("Audit dispozitive amovibile", 10, 120)));
        all.AddRange(Seq([Share(0), Usn(10, @"C:\Staging\plan.xlsx"), Usb("AA001", 20, letter: "E:"), MediaWrite(30, @"E:\plan.xlsx")]));
        all.AddRange(Seq([Usb("AA001", 0, letter: "E:"), Prefetch(5, @"C:\Users\ion\Downloads\7zG.exe", "7zG.exe"), Usn(20, @"C:\Users\ion\Documents\data.zip"),
                          Lnk(21, @"C:\Users\ion\Documents\data.zip", 200L * 1024 * 1024), MediaWrite(40, @"E:\data.zip")]));
        return all;
    }
}

public class Wp14bEngineTests
{
    [Fact]
    public void Follows_is_inclusive_at_the_window_and_never_joins_a_missing_or_earlier_time()
    {
        var a = Wp14b.At(0); var w = TimeSpan.FromMinutes(30);
        Assert.True(SequenceEngine.Follows(a, Wp14b.At(30), w));
        Assert.False(SequenceEngine.Follows(a, Wp14b.At(30).AddSeconds(1), w));
        Assert.True(SequenceEngine.Follows(a, a, w));
        Assert.False(SequenceEngine.Follows(a, Wp14b.At(-1), w));
        Assert.False(SequenceEngine.Follows(null, a, w));
        Assert.False(SequenceEngine.Follows(a, null, w));
    }

    [Fact]
    public void Within_is_inclusive_at_both_ends()
    {
        Assert.True(SequenceEngine.Within(Wp14b.At(10), Wp14b.At(10), Wp14b.At(20)));
        Assert.True(SequenceEngine.Within(Wp14b.At(20), Wp14b.At(10), Wp14b.At(20)));
        Assert.False(SequenceEngine.Within(Wp14b.At(21), Wp14b.At(10), Wp14b.At(20)));
        Assert.False(SequenceEngine.Within(null, Wp14b.At(10), Wp14b.At(20)));
    }

    [Theory]
    [InlineData(@"CORP\ion", "ion", JoinResult.Same)]
    [InlineData(@"CORP\ion", @"corp\ION", JoinResult.Same)]
    [InlineData(@"CORP\ion", @"CORP\mary", JoinResult.Different)]
    [InlineData(@"CORP\ion", @"OTHER\ion", JoinResult.Different)]
    [InlineData(@"CORP\ion", "", JoinResult.Unknown)]
    [InlineData("S-1-5-21-1-2-3-1001", @"CORP\ion", JoinResult.Unknown)]
    [InlineData("-", "ion", JoinResult.Unknown)]
    public void Accounts_join_only_when_both_are_known_names(string a, string b, JoinResult expected) => Assert.Equal(expected, SequenceEngine.JoinAccount(a, b));

    [Fact]
    public void Hosts_and_volumes_join_by_equality_and_never_when_unknown()
    {
        Assert.Equal(JoinResult.Same, SequenceEngine.JoinHost("PC1", "pc1"));
        Assert.Equal(JoinResult.Different, SequenceEngine.JoinHost("PC1", "PC2"));
        Assert.Equal(JoinResult.Unknown, SequenceEngine.JoinHost("", "PC2"));
        Assert.Equal(JoinResult.Same, SequenceEngine.JoinVolume("E:", "e"));
        Assert.Equal(JoinResult.Different, SequenceEngine.JoinVolume("E:", "F:"));
        Assert.Equal(JoinResult.Unknown, SequenceEngine.JoinVolume("", "F:"));
    }

    [Fact]
    public void Order_puts_observed_steps_on_the_timeline_and_the_unobserved_ones_after_them()
    {
        SeqStep S(string n, double min) => new(n, "s", Wp14b.At(min), "", "", "d", [], []);
        var ordered = SequenceEngine.Order([S("c", 30), SequenceEngine.Missing("x", "Prefetch", false), S("a", 10), S("b", 20)]);
        Assert.Equal(["a", "b", "c", "x"], ordered.Select(s => s.Name));
        Assert.False(ordered[3].Observed);
    }

    [Fact]
    public void A_missing_step_names_the_source_and_never_says_absent()
    {
        var unavailable = SequenceEngine.NotObserved("Prefetch", sourceCollected: false);
        var empty = SequenceEngine.NotObserved("Prefetch", sourceCollected: true);
        Assert.Contains("pas neobservat", unavailable); Assert.Contains("indisponibilă", unavailable);
        Assert.Contains("pas neobservat", empty); Assert.Contains("nu dovedește", empty);
        foreach (var t in new[] { unavailable, empty }) { Assert.DoesNotContain("absent", t, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("conform", t, StringComparison.OrdinalIgnoreCase); }
    }

    [Fact]
    public void The_input_has_no_field_for_the_signed_in_application_user()
    {
        var names = typeof(SequenceInput).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        Assert.DoesNotContain(names, n => n.Contains("user") || n.Contains("examiner") || n.Contains("operator") || n.Contains("signed"));
    }

    [Fact]
    public void The_shipped_data_holds_the_windows_the_rules_use()
    {
        var d = SequenceData.Default;
        Assert.True(d.ControlGapMedia.MediaAfterGapMinutes > 0 && d.ControlGapMedia.ControlWords.Count > 0);
        Assert.True(d.SmbStagingUsb.SmbToStagingMinutes > 0 && d.SmbStagingUsb.StagingToMediaMinutes > 0 && d.SmbStagingUsb.MinObservedSteps >= 2);
        Assert.True(d.PortableUsbArchive.ArchiveMinBytes > 0 && d.PortableUsbArchive.ArchiveExtensions.Contains(".zip") && d.PortableUsbArchive.ArchiverTools.Count > 0);
    }
}

public class Wp14bControlGapMediaTests
{
    private const string Rule = "SEQ-CONTROL-GAP-MEDIA";

    [Fact]
    public void A_medium_inside_the_gap_gives_a_sequence_with_the_facts_in_order_and_the_restoration_not_observed()
    {
        var f = Wp14b.One(Wp14b.Seq([Usb("AA001", 30, letter: "E:")], extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)), Rule);
        Assert.Equal(SequenceRules.Category, f.Category);
        Assert.Contains("Audit dispozitive amovibile", f.Title);
        Assert.Contains("dezactivat de la 2026-10-01 10:10 UTC", f.Title);
        Assert.Contains("observat la 2026-10-01 10:30 UTC", f.Title);
        Assert.Contains("restabilirea controlului: pas neobservat", f.Title);
        Assert.Equal(SemanticType.Correlation, f.SemanticType);
        var steps = f.Sequence!.Steps;
        Assert.Equal(3, steps.Count);
        Assert.True(steps[0].Observed && steps[1].Observed && !steps[2].Observed);
        Assert.Contains("pas neobservat", steps[2].Note);
        Assert.Contains("F-9001", f.RelatedFindingIds);
        Assert.Contains(f.RelatedFindingIds, id => id != "F-9001");
        Assert.Contains(f.MissingEvidence, m => m.Contains("pas neobservat"));
    }

    [Fact]
    public void The_sequence_lists_the_constituent_findings_so_the_view_can_navigate_to_them()
    {
        var (ev, existing) = Wp14b.World([Usb("AA001", 30, letter: "E:")], null, null, Wp14b.Gap("Defender protecție în timp real", 10, 120));
        var f = One(SequenceRules.Run(ev, existing, () => "F-0900", new SequenceInput { Scope = Wp14b.Scope() }));
        foreach (var id in f.RelatedFindingIds) Assert.Contains(existing, x => x.FindingId == id);
        Assert.Contains(existing.Where(x => x.RuleId.StartsWith("MEDIA-")), x => f.RelatedFindingIds.Contains(x.FindingId));
    }

    private static Finding One(List<Finding> l) => Assert.Single(l, x => x.RuleId == Rule);

    [Fact]
    public void A_medium_before_the_gap_or_long_after_it_gives_no_sequence()
    {
        Assert.Empty(Wp14b.Seq([Usb("AA001", 5, letter: "E:")], extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)));
        Assert.Empty(Wp14b.Seq([Usb("AA001", 241, letter: "E:")], extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)));
    }

    [Fact]
    public void The_window_after_the_gap_is_inclusive_at_its_edge()
    {
        Assert.Single(Wp14b.Seq([Usb("AA001", 240, letter: "E:")], extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)));
    }

    [Fact]
    public void The_window_after_the_gap_comes_from_the_data()
    {
        var d = SequenceData.FromJson("""{"controlGapMedia":{"minObservedSteps":2,"mediaAfterGapMinutes":10,"controlWords":["audit"]}}""");
        Assert.Empty(Wp14b.Seq([Usb("AA001", 131, letter: "E:")], data: d, extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)));
        Assert.Single(Wp14b.Seq([Usb("AA001", 130, letter: "E:")], data: d, extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)));
    }

    [Fact]
    public void A_medium_seen_before_and_during_the_gap_counts_and_says_it_was_seen_before()
    {
        var f = Wp14b.One(Wp14b.Seq([Usb("AA001", 5, letter: "E:"), Usb("AA001", 40, letter: "E:")], extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)), Rule);
        Assert.Contains("observat și înainte de decalaj", f.Description);
    }

    [Fact]
    public void A_gap_without_a_medium_is_not_a_sequence_the_minimum_is_two_observed_steps()
    {
        Assert.Empty(Wp14b.Seq([Sec(4624, 0)], extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)));
    }

    [Fact]
    public void A_gap_whose_start_is_unknown_cannot_be_placed_on_the_timeline()
    {
        Assert.Empty(Wp14b.Seq([Usb("AA001", 30, letter: "E:")], extra: Wp14b.Gap("Audit dispozitive amovibile", -1, 120)));
    }

    [Fact]
    public void A_gap_in_a_control_that_is_not_about_media_audit_or_defender_gives_no_sequence()
    {
        Assert.Empty(Wp14b.Seq([Usb("AA001", 30, letter: "E:")], extra: Wp14b.Gap("Lungimea minimă a parolei", 10, 120)));
    }

    [Fact]
    public void Severity_is_High_on_a_classified_scope_and_Medium_otherwise()
    {
        var gap = Wp14b.Gap("Audit dispozitive amovibile", 10, 120);
        Assert.Equal(Severity.High, Wp14b.One(Wp14b.Seq([Usb("AA001", 30, letter: "E:")], extra: gap), Rule).Severity);
        Assert.Equal(Severity.Medium, Wp14b.One(Wp14b.Seq([Usb("AA001", 30, letter: "E:")], Wp14b.Scope(classified: false), extra: gap), Rule).Severity);
    }

    private static List<TimelineEvent> UnregisteredWithWrite(double writeMinute) =>
        [Usb("AA001", 30, letter: "E:"), Wp14b.MediaWrite(writeMinute, @"E:\secret.docx")];

    private static MediaRegister OtherMedium() => W14.Media(Row("ZZ999"));

    [Fact]
    public void Critical_only_with_write_evidence_on_an_unregistered_medium_during_the_gap_on_a_classified_scope()
    {
        var gap = Wp14b.Gap("Audit dispozitive amovibile", 10, 120);
        Assert.Equal(Severity.Critical, Wp14b.One(Wp14b.Seq(UnregisteredWithWrite(40), media: OtherMedium(), extra: gap), Rule).Severity);
        // write after the gap ended: not "during the gap"
        Assert.Equal(Severity.High, Wp14b.One(Wp14b.Seq(UnregisteredWithWrite(150), media: OtherMedium(), extra: gap), Rule).Severity);
        // no write evidence at all
        Assert.Equal(Severity.High, Wp14b.One(Wp14b.Seq([Usb("AA001", 30, letter: "E:")], media: OtherMedium(), extra: gap), Rule).Severity);
        // unclassified scope never reaches Critical
        Assert.Equal(Severity.Medium, Wp14b.One(Wp14b.Seq(UnregisteredWithWrite(40), Wp14b.Scope(classified: false), OtherMedium(), extra: gap), Rule).Severity);
    }

    [Fact]
    public void A_registered_authorized_medium_with_a_write_is_High_not_Critical()
    {
        var reg = W14.Media(Row("AA001"));
        Assert.Equal(Severity.High, Wp14b.One(Wp14b.Seq(UnregisteredWithWrite(40), media: reg, extra: Wp14b.Gap("Audit dispozitive amovibile", 10, 120)), Rule).Severity);
    }
}

public class Wp14bSmbStagingUsbTests
{
    private const string Rule = "SEQ-SMB-STAGING-USB";

    private static List<TimelineEvent> Chain(string stagedAs = @"C:\Staging\plan.xlsx", string writtenAs = @"E:\plan.xlsx", double staging = 10, double write = 30, string writer = "ion") =>
        [Wp14b.Share(0), Wp14b.Usn(staging, stagedAs), Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(write, writtenAs, writer)];

    [Fact]
    public void The_full_chain_with_a_file_name_link_is_High_and_names_the_account_from_the_evidence()
    {
        var f = Wp14b.One(Wp14b.Seq(Chain()), Rule);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(@"CORP\ion", f.User);
        Assert.Contains("legătură dovedită prin nume de fișier", f.Description);
        Assert.Contains("plan.xlsx", f.Description);
        Assert.Contains("legătură de nume de fișier dovedită", f.Title);
        Assert.Equal(3, f.Sequence!.Steps.Count(s => s.Observed));
        Assert.Equal(["acces la partajare de la distanță", "staging local"], f.Sequence.Steps.Take(2).Select(s => s.Name));
        Assert.Contains(f.RelatedFindingIds, id => id.StartsWith("F-")); // the MEDIA-* findings
    }

    [Fact]
    public void Without_a_shared_file_name_the_sequence_is_reported_as_temporal_correlation_only()
    {
        var f = Wp14b.One(Wp14b.Seq(Chain(writtenAs: @"E:\other.docx", stagedAs: @"C:\Staging\temp.bin")), Rule);
        Assert.Contains("corelare temporală, fără legătură dovedită între fișiere", f.Description);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Confidence.Low, f.Confidence);
    }

    [Fact]
    public void The_severity_is_one_level_lower_on_an_unclassified_scope()
    {
        Assert.Equal(Severity.Medium, Wp14b.One(Wp14b.Seq(Chain(), Wp14b.Scope(classified: false)), Rule).Severity);
        Assert.Equal(Severity.Low, Wp14b.One(Wp14b.Seq(Chain(writtenAs: @"E:\other.docx", stagedAs: @"C:\Staging\temp.bin"), Wp14b.Scope(classified: false)), Rule).Severity);
    }

    [Fact]
    public void The_staging_window_is_inclusive_and_comes_from_the_data()
    {
        var other = (string.Empty, @"E:\other.docx", @"C:\Staging\temp.bin");
        Assert.Single(Wp14b.Seq(Chain(other.Item3, other.Item2, staging: 120, write: 200)), f => f.RuleId == Rule);
        Assert.Empty(Wp14b.Seq(Chain(other.Item3, other.Item2, staging: 121, write: 200)));
        var tight = SequenceData.FromJson("""{"smbStagingUsb":{"minObservedSteps":3,"minObservedStepsWithFileLink":2,"smbToStagingMinutes":5,"stagingToMediaMinutes":240,"ignoredShares":["IPC$"]}}""");
        Assert.Empty(Wp14b.Seq(Chain(other.Item3, other.Item2, staging: 10, write: 30), data: tight));
    }

    [Fact]
    public void The_write_to_the_medium_must_follow_staging_within_its_window()
    {
        Assert.Empty(Wp14b.Seq(Chain(@"C:\Staging\temp.bin", @"E:\other.docx", staging: 10, write: 251)));
        Assert.Single(Wp14b.Seq(Chain(@"C:\Staging\temp.bin", @"E:\other.docx", staging: 10, write: 250)), f => f.RuleId == Rule);
    }

    [Fact]
    public void Without_staging_a_proven_file_name_link_still_gives_a_sequence_and_the_staging_step_is_not_observed()
    {
        var f = Wp14b.One(Wp14b.Seq([Wp14b.Share(0), Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(30, @"E:\plan.xlsx")]), Rule);
        var staging = f.Sequence!.Steps.Single(s => s.Name == "staging local");
        Assert.False(staging.Observed);
        Assert.Contains("pas neobservat", staging.Note);
        Assert.Contains("partajare → mediu", f.Description);
    }

    [Fact]
    public void Without_staging_and_without_a_file_name_link_two_steps_are_below_the_minimum()
    {
        Assert.Empty(Wp14b.Seq([Wp14b.Share(0), Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(30, @"E:\other.docx")]));
    }

    [Fact]
    public void A_missing_staging_source_is_said_to_be_collected_but_empty_never_absent()
    {
        var f = Wp14b.One(Wp14b.Seq([Wp14b.Share(0), Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(30, @"E:\plan.xlsx")]), Rule);
        var note = f.Sequence!.Steps.Single(s => s.Name == "staging local").Note;
        Assert.Contains("nu are înregistrări potrivite", note);
        Assert.DoesNotContain("absent", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_write_by_another_account_does_not_join_the_sequence()
    {
        Assert.Empty(Wp14b.Seq(Chain(writer: "mary")));
    }

    [Fact]
    public void A_write_whose_account_the_sources_do_not_give_joins_by_time_and_says_so()
    {
        var ev = new List<TimelineEvent> { Wp14b.Share(0), Wp14b.Usn(10, @"C:\Staging\plan.xlsx"), Usb("AA001", 20, letter: "E:"), Wp14b.Usn(30, @"E:\plan.xlsx") };
        var f = Wp14b.One(Wp14b.Seq(ev), Rule);
        Assert.Contains("cont necunoscut în sursa acestui pas", f.Description);
        Assert.Equal(@"CORP\ion", f.User);
    }

    [Fact]
    public void IPC_share_and_local_addresses_are_not_remote_share_access()
    {
        Assert.Empty(Wp14b.Seq([Wp14b.Share(0, share: @"\\*\IPC$"), Wp14b.Usn(10, @"C:\Staging\plan.xlsx"), Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(30, @"E:\plan.xlsx")]));
        Assert.Empty(Wp14b.Seq([Wp14b.Share(0, ip: "127.0.0.1"), Wp14b.Usn(10, @"C:\Staging\plan.xlsx"), Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(30, @"E:\plan.xlsx")]));
    }

    [Fact]
    public void Staging_on_the_removable_volume_or_in_system_paths_is_not_local_staging()
    {
        Assert.Empty(Wp14b.Seq(Chain(stagedAs: @"E:\temp.bin", writtenAs: @"E:\other.docx")));
        Assert.Empty(Wp14b.Seq(Chain(stagedAs: @"C:\Windows\Temp\plan.xlsx", writtenAs: @"E:\other.docx")));
    }

    [Fact]
    public void Access_to_the_medium_without_a_write_is_not_a_write_step()
    {
        Assert.Empty(Wp14b.Seq([Wp14b.Share(0), Wp14b.Usn(10, @"C:\Staging\plan.xlsx"), Usb("AA001", 20, letter: "E:"), Row("LNK", 30, path: @"E:\plan.xlsx", f: [("DriveType", "REMOVABLE")])]));
    }

    [Fact]
    public void Remote_access_alone_or_a_write_alone_is_not_a_sequence()
    {
        Assert.Empty(Wp14b.Seq([Wp14b.Share(0), Wp14b.Usn(10, @"C:\Staging\plan.xlsx")]));
        Assert.Empty(Wp14b.Seq([Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(30, @"E:\plan.xlsx")]));
    }

    [Fact]
    public void The_sequence_never_uses_the_signed_in_user_as_the_subject()
    {
        var f = Wp14b.One(Wp14b.Seq(Chain()), Rule);
        if (Environment.UserName.Length > 3 && !Environment.UserName.Contains("ion", StringComparison.OrdinalIgnoreCase))
            Assert.DoesNotContain(Environment.UserName, f.User + "|" + f.Title + "|" + f.Description, StringComparison.OrdinalIgnoreCase);
        Assert.All(f.Sequence!.Steps.Where(s => s.Observed && s.Account.Length > 0), s => Assert.Contains("ion", s.Account));
    }
}

public class Wp14bPortableUsbArchiveTests
{
    private const string Rule = "SEQ-PORTABLE-USB-ARCHIVE";
    private static readonly long Big = 200L * 1024 * 1024;

    private static List<TimelineEvent> Full(long archiveSize = 0, string mediaFile = @"E:\data.zip") =>
    [
        Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"C:\Users\ion\Downloads\7zG.exe", "7zG.exe"), Wp14b.Usn(20, @"C:\Users\ion\Documents\data.zip"),
        .. (archiveSize > 0 ? new[] { Wp14b.Lnk(21, @"C:\Users\ion\Documents\data.zip", archiveSize) } : []),
        Wp14b.MediaWrite(40, mediaFile),
    ];

    [Fact]
    public void Portable_software_a_large_archive_and_a_write_to_the_medium_give_High_on_a_classified_scope()
    {
        var f = Wp14b.One(Wp14b.Seq(Full(Big)), Rule);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(3, f.Sequence!.Steps.Count(s => s.Observed));
        Assert.Contains("200 MB", f.Description);
        Assert.Contains("software rulat", f.Title);
        Assert.Contains("data.zip", f.Description);
        Assert.Contains("arhiva apare și pe mediu prin nume de fișier", f.Description);
    }

    [Fact]
    public void The_severity_is_one_level_lower_on_an_unclassified_scope()
    {
        Assert.Equal(Severity.Medium, Wp14b.One(Wp14b.Seq(Full(Big), Wp14b.Scope(classified: false)), Rule).Severity);
    }

    [Fact]
    public void An_archive_below_the_size_threshold_does_not_count_and_the_step_says_so()
    {
        var ev = new List<TimelineEvent>
        {
            Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"C:\Users\ion\Downloads\7zG.exe", "7zG.exe"), Wp14b.Usn(20, @"C:\Users\ion\Documents\data.zip"),
            Wp14b.Lnk(21, @"C:\Users\ion\Documents\data.zip", 1000), Wp14b.MediaWrite(40, @"E:\notes.txt"),
        };
        var f = Wp14b.One(Wp14b.Seq(ev), Rule);
        var arc = f.Sequence!.Steps.Single(s => s.Name == "arhivă mare creată");
        Assert.False(arc.Observed);
        Assert.Contains("pas neobservat", arc.Note); Assert.Contains("sub pragul", arc.Note);
        Assert.Equal(Severity.Medium, f.Severity); // two observed steps, one level below the full sequence
    }

    [Fact]
    public void The_size_threshold_comes_from_the_data()
    {
        var d = SequenceData.FromJson("""{"portableUsbArchive":{"minObservedSteps":2,"execToArchiveMinutes":480,"archiveToMediaMinutes":240,"archiveMinBytes":500,"archiveExtensions":[".zip"],"archiverTools":["7zg.exe"],"userWritablePathParts":["\\users\\"]}}""");
        var ev = new List<TimelineEvent>
        {
            Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"C:\Users\ion\Downloads\7zG.exe", "7zG.exe"), Wp14b.Usn(20, @"C:\Users\ion\Documents\data.zip"),
            Wp14b.Lnk(21, @"C:\Users\ion\Documents\data.zip", 1000), Wp14b.MediaWrite(40, @"E:\notes.txt"),
        };
        Assert.True(Wp14b.One(Wp14b.Seq(ev, data: d), Rule).Sequence!.Steps.Single(s => s.Name == "arhivă creată").Observed);
    }

    [Fact]
    public void An_archive_of_unknown_size_counts_says_so_and_does_not_reach_the_top_severity()
    {
        var f = Wp14b.One(Wp14b.Seq(Full(0, @"E:\notes.txt")), Rule);
        Assert.Contains("dimensiune necunoscută", f.Description);
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void The_archive_to_medium_window_is_inclusive_and_the_write_after_it_is_not_joined()
    {
        var ok = Full(Big).Where(e => e.Source != "EventLog:Security").Append(Wp14b.MediaWrite(260, @"E:\data.zip")).ToList();   // archive at 20 + 240
        Assert.Single(Wp14b.Seq(ok), f => f.RuleId == Rule);
        var late = Full(Big).Where(e => e.Source != "EventLog:Security").Append(Wp14b.MediaWrite(261, @"E:\data.zip")).ToList();
        Assert.Contains(Wp14b.Seq(late), f => f.RuleId == Rule); // still reached through the program window (5 + 480 + 240), without the archive step
        Assert.DoesNotContain(Wp14b.Seq(late).Single(f => f.RuleId == Rule).Sequence!.Steps, s => s.Name == "arhivă creată" && s.Observed && s.WhenUtc == Wp14b.At(20));
    }

    [Fact]
    public void Software_run_from_the_removable_drive_qualifies_without_being_a_known_tool()
    {
        var ev = new List<TimelineEvent> { Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"E:\launcher.exe", "launcher.exe"), Wp14b.MediaWrite(30, @"E:\out.txt") };
        var f = Wp14b.One(Wp14b.Seq(ev), Rule);
        Assert.Contains("unitate amovibilă E:", f.Description);
        Assert.False(f.Sequence!.Steps.Single(s => s.Name == "arhivă mare creată").Observed);
    }

    [Fact]
    public void Software_in_a_system_path_or_an_unknown_tool_in_a_user_path_does_not_qualify()
    {
        Assert.Empty(Wp14b.Seq([Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"C:\Program Files\App\tool.exe", "tool.exe"), Wp14b.MediaWrite(30, @"E:\out.txt")]));
        Assert.Empty(Wp14b.Seq([Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"C:\Users\ion\Downloads\random.exe", "random.exe"), Wp14b.MediaWrite(30, @"E:\out.txt")]));
    }

    [Fact]
    public void Software_first_seen_before_the_case_period_does_not_qualify()
    {
        var scope = new CaseScope { Network = NetworkCategory.AirGappedNetwork, Classification = ClassificationLevel.Classified, PeriodFromUtc = Wp14b.At(60), PeriodToUtc = Wp14b.At(600) };
        Assert.Empty(Wp14b.Seq([Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"C:\Users\ion\Downloads\7zG.exe", "7zG.exe"), Wp14b.MediaWrite(40, @"E:\out.txt")], scope));
    }

    [Fact]
    public void Amcache_alone_is_presence_not_execution_and_the_step_says_so()
    {
        var ev = new List<TimelineEvent> { Usb("AA001", 0, letter: "E:"), Row("Amcache", 5, process: "portable7z.exe", path: @"C:\Users\ion\Downloads\portable7z.exe"), Wp14b.MediaWrite(30, @"E:\out.txt") };
        var f = Wp14b.One(Wp14b.Seq(ev), Rule);
        Assert.Contains("prezență, nu execuție", f.Description);
        Assert.Contains("software prezent", f.Title);
        Assert.DoesNotContain("software rulat", f.Title);
    }

    [Fact]
    public void Without_a_write_to_the_medium_there_is_no_sequence()
    {
        Assert.Empty(Wp14b.Seq([Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"C:\Users\ion\Downloads\7zG.exe", "7zG.exe"), Wp14b.Usn(20, @"C:\Users\ion\Documents\data.zip")]));
    }

    [Fact]
    public void An_archive_written_straight_to_the_medium_is_one_fact_not_a_sequence()
    {
        Assert.Empty(Wp14b.Seq([Usb("AA001", 0, letter: "E:"), Wp14b.Usn(20, @"E:\data.zip")]));
    }

    [Fact]
    public void A_source_that_was_not_collected_is_reported_as_unavailable_never_as_absent()
    {
        var ev = new List<TimelineEvent> { Usb("AA001", 0, letter: "E:"), Wp14b.Usn(20, @"C:\Users\ion\Documents\data.zip"), Wp14b.Usn(40, @"E:\data.zip") };
        var f = Wp14b.One(Wp14b.Seq(ev), Rule);
        var prog = f.Sequence!.Steps.Single(s => s.Name == "software portabil sau de pe mediu amovibil");
        Assert.False(prog.Observed);
        Assert.Contains("indisponibilă", prog.Note); Assert.Contains("Prefetch", prog.Note);
        Assert.DoesNotContain("absent", prog.Note, StringComparison.OrdinalIgnoreCase);
    }
}

public class Wp14bContractTests
{
    private static readonly string[] Rules = ["SEQ-CONTROL-GAP-MEDIA", "SEQ-SMB-STAGING-USB", "SEQ-PORTABLE-USB-ARCHIVE"];
    private static readonly string[] IntentWords = ["exfiltr", "intenți", "intenți", "furt", "sustras", "malițios", "scurgere de date", "a copiat", "a furat", "vinovat"];

    [Fact]
    public void Every_scenario_fires_its_rule()
    {
        var ids = Wp14b.AllPositive().Select(f => f.RuleId).ToHashSet();
        foreach (var r in Rules) Assert.Contains(r, ids);
    }

    [Fact]
    public void No_title_or_description_asserts_intent_or_exfiltration()
    {
        foreach (var f in Wp14b.AllPositive())
            foreach (var w in IntentWords)
            {
                Assert.DoesNotContain(w, f.Title, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(w, f.Description, StringComparison.OrdinalIgnoreCase);
            }
    }

    [Fact]
    public void No_sequence_is_called_conform_and_none_treats_a_missing_step_as_absent()
    {
        foreach (var f in Wp14b.AllPositive())
        {
            Assert.DoesNotContain("conform", f.Title + f.Description, StringComparison.OrdinalIgnoreCase);
            foreach (var s in f.Sequence!.Steps.Where(s => !s.Observed))
            {
                Assert.Contains("pas neobservat", s.Note);
                Assert.DoesNotContain("absent", s.Note, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Every_rule_has_a_complete_catalog_entry_produced_by_SequenceRules()
    {
        foreach (var id in Rules)
        {
            var c = RuleContracts.For(id);
            Assert.NotNull(c);
            Assert.EndsWith("SequenceRules", c!.Producer);
            Assert.NotEmpty(c.Limitations); Assert.NotEmpty(c.MissingEvidence); Assert.NotEmpty(c.NextSteps); Assert.True(c.Meaning.Length > 20);
            foreach (var w in IntentWords) Assert.DoesNotContain(w, c.Meaning + string.Join(" ", c.Limitations) + string.Join(" ", c.NextSteps), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Findings_carry_the_contract_fields_and_no_anti_overclaim_violation()
    {
        foreach (var f in FindingContract.Enrich(Wp14b.AllPositive()).Where(f => Rules.Contains(f.RuleId)))
        {
            Assert.True(f.HasSemanticType, f.RuleId);
            Assert.Equal(SemanticType.Correlation, f.SemanticType);
            Assert.NotEmpty(f.Limitations); Assert.NotEmpty(f.AlternativeExplanations); Assert.NotEmpty(f.SupportingEvidence); Assert.NotEmpty(f.RelatedFindingIds);
            Assert.Empty(AntiOverclaim.Violations(f));
            Assert.Contains(f.Limitations, l => l.Contains("NOT_ASSESSED"));
            Assert.Equal(SequenceRules.Category, f.Category);
            Assert.NotNull(f.Sequence);
        }
    }

    [Fact]
    public void The_WP4_sufficiency_check_gives_a_verdict_for_every_sequence_and_does_not_reject_it()
    {
        var gap = Wp14b.Gap("Audit dispozitive amovibile", 10, 120);
        var scenarios = new (List<TimelineEvent> Events, Finding[] Extra)[]
        {
            ([Usb("AA001", 30, letter: "E:")], [gap]),
            ([Wp14b.Share(0), Wp14b.Usn(10, @"C:\Staging\plan.xlsx"), Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(30, @"E:\plan.xlsx")], []),
            ([Usb("AA001", 0, letter: "E:"), Wp14b.Prefetch(5, @"C:\Users\ion\Downloads\7zG.exe", "7zG.exe"), Wp14b.Usn(20, @"C:\Users\ion\Documents\data.zip"),
              Wp14b.Lnk(21, @"C:\Users\ion\Documents\data.zip", 200L * 1024 * 1024), Wp14b.MediaWrite(40, @"E:\data.zip")], []),
        };
        foreach (var (events, extra) in scenarios)
        {
            var found = Wp14b.Seq(events, extra: extra);
            var f = FindingContract.Enrich(found).Single(x => Rules.Contains(x.RuleId));
            var refs = f.SupportingEvidence.Select(r =>
            {
                var e = events.FirstOrDefault(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator);
                if (e is null) return new ResolvedRef(r, null, [], "EventLog:Security", "EventLog:Security 4719", true);
                var row = new TimelineRow(e.Time.Utc, e.TimeSemantics, e.Source, e.EventId, e.Path, e.Process, e.Summary, e.EvidenceId, e.Locator, "");
                var kind = e.Source.StartsWith("EventLog:", StringComparison.Ordinal) && e.EventId.Length > 0 ? $"{e.Source} {e.EventId}" : e.Source;
                return new ResolvedRef(r, null, [row], e.Source, kind, true);
            }).ToList();
            var res = Checks.Sufficiency(f, refs);
            Assert.True(res.Outcome != CheckOutcome.Fail, $"{f.RuleId}: {res.Reason}");
            // The temporal check reads the time of each cited row against the finding's own window.
            foreach (var r in f.SupportingEvidence)
                if (events.FirstOrDefault(x => x.EvidenceId == r.EvidenceId && x.Locator == r.Locator)?.Time.Utc is { } t)
                    Assert.True(t >= f.FirstSeenUtc && t <= f.LastSeenUtc, $"{f.RuleId}: {r.Locator} outside {f.FirstSeenUtc:o} - {f.LastSeenUtc:o}");
        }
    }

    [Fact]
    public void Existing_findings_are_not_modified_and_the_sequence_adds_no_second_detection()
    {
        var (ev, existing) = Wp14b.World([Wp14b.Share(0), Wp14b.Usn(10, @"C:\Staging\plan.xlsx"), Usb("AA001", 20, letter: "E:"), Wp14b.MediaWrite(30, @"E:\plan.xlsx")]);
        var before = existing.Select(f => (f.FindingId, f.RuleId, f.Severity, f.Title)).ToList();
        var seq = SequenceRules.Run(ev, existing, () => "F-9100", new SequenceInput { Scope = Wp14b.Scope() });
        Assert.Equal(before, existing.Select(f => (f.FindingId, f.RuleId, f.Severity, f.Title)).ToList());
        Assert.All(seq, f => Assert.StartsWith("SEQ-", f.RuleId));
    }
}
