using System.Globalization;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using Xunit;
using static LogAnalyzer.Dfir.Tests.NtfsTestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Regression tests for the independent review of MftParser (M1–M3, L1, L2). Each was written to fail on the code reviewed.</summary>
public sealed class MftParserReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_mftr_" + Guid.NewGuid().ToString("N"));
    public MftParserReviewTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly DateTimeOffset Acq = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    private static long Ft(int y, int m, int d) => new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    private static readonly long Recent = Ft(2026, 11, 10);
    private static string F(TimelineEvent e, string k) => e.Fields.TryGetValue(k, out var v) ? v : "";

    private string W(string sub, string name, byte[] data)
    {
        var d = Path.Combine(_dir, sub); Directory.CreateDirectory(d);
        var p = Path.Combine(d, name); File.WriteAllBytes(p, data); return p;
    }

    private static (ParseResult R, List<TimelineEvent> E) Run(string p, MftParser? parser = null)
    {
        var sink = new ListSink();
        var item = new EvidenceItem { EvidenceId = "E", CaseId = "C", Source = "t", SourceType = MftParser.SourceTypeName, StoredPath = p, AcquiredAtUtc = Acq };
        var r = (parser ?? new MftParser()).Parse(item, p, sink, default);
        return (r, sink.Events.ToList());
    }

    private static TimelineEvent Summary(List<TimelineEvent> ev) => Assert.Single(ev, e => F(e, "Category") == "summary");

    [Fact] // M1
    public void M1_deleted_cap_is_not_crowded_out_by_records_with_impossible_times()
    {
        var recs = new List<(int, byte[])>();
        for (int i = 0; i < MftParser.MaxDeletedEvents; i++)
            recs.Add((40 + i, FileRecord(1, Ref(5, 5), $"junk{i}.txt", long.MaxValue, long.MaxValue, flags: 0)));
        recs.Add((40 + MftParser.MaxDeletedEvents, FileRecord(1, Ref(5, 5), "real_deleted.txt", Ft(2026, 11, 13), Ft(2026, 11, 13), flags: 0)));
        var (r, ev) = Run(W("a", "a.mft", Mft([.. recs])));
        Assert.Contains(ev, e => F(e, "FileName") == "real_deleted.txt");
        Assert.Equal(MftParser.MaxDeletedEvents - 1, ev.Count(e => F(e, "FileName").StartsWith("junk")));
        var s = Summary(ev);
        Assert.Equal("1", F(s, "DeletedNotEmitted"));
        Assert.Equal("1", F(s, "DeletedImplausibleNotEmitted"));
        Assert.Equal((MftParser.MaxDeletedEvents - 1).ToString(CultureInfo.InvariantCulture), F(s, "DeletedImplausibleEmitted"));
        var gap = Assert.Single(r.Gaps, g => g.Reason.Contains("Plafoane"));
        Assert.Contains("neplauzibile", gap.Reason);
        // tie-break is stable by record number: the junk with the highest number was the one left out
        Assert.DoesNotContain(ev, e => F(e, "FileName") == $"junk{MftParser.MaxDeletedEvents - 1}.txt");
        Assert.Contains(ev, e => F(e, "FileName") == "junk0.txt");
    }

    [Fact] // M2
    public void M2_boot_sector_of_another_volume_is_not_believed_over_the_record_headers()
    {
        var mft = Mft((40, FileRecord(1, Ref(5, 5), "x.txt", Recent, Recent)), (41, FileRecord(1, Ref(5, 5), "gone.txt", Recent, Recent, flags: 0)));
        var p = W("b", "$MFT", mft);
        File.WriteAllBytes(Path.Combine(_dir, "b", "$Boot"), BootSector(clustersPerRecord: 0xF4));   // 2^12 = 4096
        var (r, ev) = Run(p);
        var s = Summary(ev);
        Assert.Equal("1024", F(s, "RecordSize")); Assert.Equal("42", F(s, "TotalRecords")); Assert.Equal("false", F(s, "TruncatedFile"));
        Assert.Contains(ev, e => F(e, "FileName") == "x.txt"); Assert.Contains(ev, e => F(e, "FileName") == "gone.txt");
        var gap = Assert.Single(r.Gaps, g => g.Reason.Contains("nu corespunde"));
        Assert.Contains("4096", gap.Reason); Assert.Contains("1024", gap.Reason);
        Assert.DoesNotContain(r.Gaps, g => g.Reason.Contains("termină în mijlocul"));
    }

    private static byte[] BrokenBefore(string name, bool withFn)
    {
        var broken = Resident(0x40, new byte[8], lengthOverride: 0);
        var si = Resident(0x10, Si(Recent, Recent, Recent, Recent));
        return withFn ? Record(1, InUse, 0, si, Resident(0x30, Fn(Ref(5, 5), name, Recent, Recent, Recent, Recent)), broken, Resident(0x80, new byte[2]))
                      : Record(1, InUse, 0, si, broken, Resident(0x30, Fn(Ref(5, 5), name, Recent, Recent, Recent, Recent)));
    }

    [Fact] // M3
    public void M3_unreadable_attributes_are_counted_and_the_readable_part_is_still_emitted()
    {
        var del = Record(1, 0, 0, Resident(0x10, Si(Recent, Recent, Recent, Recent)),
                         Resident(0x30, Fn(Ref(5, 5), "deleted_victim.txt", Recent, Recent, Recent, Recent)), Resident(0x40, new byte[8], lengthOverride: 0));
        var mft = Mft((40, BrokenBefore("lost_name.txt", false)), (41, BrokenBefore("kept_name.txt", true)), (42, del), (43, FileRecord(1, Ref(5, 5), "ok.txt", Recent, Recent)));
        var (r, ev) = Run(W("c", "c.mft", mft));
        Assert.True(r.MalformedRecords >= 3);
        Assert.Equal(EvidenceStatus.Partial, r.Status);
        var s = Summary(ev);
        Assert.Equal("3", F(s, "AttributeWalkProblems"));
        Assert.Contains(r.Gaps, g => g.Reason.Contains("atribute") && g.Reason.Contains('3'));
        Assert.Contains(ev, e => F(e, "FileName") == "ok.txt" && F(e, "AttributeWalk") == "");
        var kept = Assert.Single(ev, e => F(e, "FileName") == "kept_name.txt");   // $SI and $FN were read before the problem
        Assert.Equal("incomplete", F(kept, "AttributeWalk")); Assert.Equal("\\kept_name.txt", kept.Path);
        var lost = Assert.Single(ev, e => F(e, "RecordNumber") == "40");           // $SI only: reported without a name
        Assert.Equal("incomplete", F(lost, "AttributeWalk")); Assert.Equal("incomplete", F(lost, "Category")); Assert.Equal("false", F(lost, "PathComplete"));
        Assert.Equal("2026-11-10T00:00:00.0000000Z", F(lost, "SiCreated"));
        var d = Assert.Single(ev, e => F(e, "RecordNumber") == "42");
        Assert.Equal("incomplete", F(d, "AttributeWalk")); Assert.Equal("deleted", F(d, "Category"));
    }

    [Fact] // L1
    public void L1_zeroed_or_baad_record_0_does_not_lose_the_whole_mft()
    {
        var mft = Mft((40, FileRecord(1, Ref(5, 5), "x.txt", Recent, Recent)));
        var zero = (byte[])mft.Clone(); Array.Clear(zero, 0, 1024);
        var baad = (byte[])mft.Clone(); "BAAD"u8.CopyTo(baad);
        foreach (var (name, data) in new[] { ("zero.mft", zero), ("baad.mft", baad) })
        {
            var p = W("d", name, data);
            var (r, ev) = Run(p);
            Assert.NotEqual(EvidenceStatus.Failed, r.Status);
            Assert.Contains(ev, e => F(e, "FileName") == "x.txt");
            Assert.Equal("1024", F(Summary(ev), "RecordSize"));
            Assert.Equal("ntfs_mft", EvidenceFingerprint.Detect(p));
        }
        // the damaged-record-0 file is still Partial/accounted: BAAD counted
        Assert.Equal("1", F(Summary(Run(W("d", "baad2.mft", baad)).E), "BaadRecords"));
        // a file whose only header has a USA count that is not size/512+1 for a power-of-two size is not an $MFT
        var junk = new byte[2048]; "FILE"u8.CopyTo(junk); junk[4] = 0x30; junk[6] = 4;   // count 4 → 1536 bytes
        Assert.Equal("unknown", EvidenceFingerprint.Detect(W("d", "badcount.bin", junk)));
        var (rj, _) = Run(W("d", "badcount.mft", junk));
        Assert.Equal(EvidenceStatus.Failed, rj.Status);
    }

    [Fact] // L2
    public void L2_every_format_and_control_character_is_neutralised_in_the_summary_text()
    {
        foreach (var s in new[] { "a؜b", "a⁠b", "a­b", "a᠎b", "a͏b", "a​b", "a‮b", "a⁦b", "a﻿b", "a\u0000b", "a\u009Fb", "a\U000E0001b" })
        {
            var t = MftParser.SafeText(s);
            Assert.NotEqual(s, t);
            Assert.StartsWith("a", t); Assert.EndsWith("b", t);
            Assert.DoesNotContain(t, c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format || c == '͏');
        }
        for (int c = 0; c <= 0xFFFF; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF) continue;
            var cat = char.GetUnicodeCategory((char)c);
            if (cat is UnicodeCategory.Format or UnicodeCategory.Control || c == 0x034F)
                Assert.NotEqual("x" + (char)c + "y", MftParser.SafeText("x" + (char)c + "y"));
        }
        Assert.Equal("ordinary name.txt", MftParser.SafeText("ordinary name.txt"));
        Assert.Equal("țară_ăîâșț 日本語 😀", MftParser.SafeText("țară_ăîâșț 日本語 😀"));
    }
}
