using System.Buffers.Binary;
using System.Security.Cryptography;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Tests.Synthetic;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Planner #13 C1: replay of registry transaction logs (HvLE) onto a copy of a dirty hive. The inputs are synthetic hives from <see cref="RegfBuilder"/>; the Marvin32
/// check uses bytes and hashes read from a real Windows log (Default user NTUSER.DAT.LOG1), so the builder cannot agree with the code only by being circular.
/// </summary>
public sealed class HiveLogReplayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_hvle_{Guid.NewGuid():N}");
    private static readonly DateTime T = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private const string Key = @"Root\InventoryApplicationFile\whoami.exe|7380676a876ce9b1";

    public HiveLogReplayTests() => Directory.CreateDirectory(_root);
    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    /// <summary>State k of the Amcache-like hive: k = 0 the key without values (what a dirty copy shows), k >= 1 the values Name, LowerCaseLongPath, FileId, then V2..Vk.</summary>
    private static byte[] Hive(int k)
    {
        var root = new RegfBuilder.Key("ROOT", T);
        var key = root.Sub(Key);
        if (k >= 1)
        {
            key.Value("Name", RegfBuilder.String, "WHOAMI.EXE");
            key.Value("LowerCaseLongPath", RegfBuilder.String, @"c:\windows\system32\whoami.exe");
            key.Value("FileId", RegfBuilder.String, "0000" + new string('a', 40));
        }
        for (int i = 2; i <= k; i++) key.Value("V" + i, RegfBuilder.String, "v" + i);
        return RegfBuilder.Build(root, "Amcache.hve");
    }

    private string Write(string name, byte[] data) { var p = Path.Combine(_root, name); File.WriteAllBytes(p, data); return p; }
    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static byte[]? Val(string hive, string name)
    {
        using var fs = new FileStream(hive, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new RawRegistry(fs).ReadValue(Key, name);
    }

    /// <summary>Dirty primary file in state 0 (primary 6, secondary 5: the entry 6 is the first one not yet in the file).</summary>
    private (string Hive, byte[] Bytes) DirtyHive() { var b = RegfLogBuilder.WithSequences(Hive(0), 6, 5); return (Write("Amcache.hve", b), b); }

    [Fact]
    public void Marvin32_matches_the_hashes_of_a_real_windows_log_entry()
    {
        // first 32 bytes of the first HvLE entry of the Default user's NTUSER.DAT.LOG1 (Windows 11): its Hash-2 is stored at offset 32
        var header = Convert.FromHexString("48764c45002e0000000000002600000000000300020000005a6cf5cf8dddbbae");
        Assert.Equal(0x3a9ee9caad1d6073UL, RegfLogReplay.Marvin32(header));
    }

    [Fact]
    public void Valid_entries_are_applied_to_a_copy_and_the_original_is_not_changed()
    {
        var (hive, bytes) = DirtyHive();
        var before = Sha(hive);
        var log1 = Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1)), RegfLogBuilder.Entry(7, Hive(2))));
        var dest = Path.Combine(_root, "copy.hve");

        var r = RegfLogReplay.Replay(hive, [log1], dest);

        Assert.Equal(HiveReplayOutcome.Replayed, r.Outcome);
        Assert.Equal(2, r.EntriesApplied);
        Assert.Equal(7u, r.LastSequence);
        Assert.Equal(before, Sha(hive));                                   // evidence untouched
        Assert.Null(Val(hive, "Name"));                                    // ... and still dirty: valueless key
        Assert.Equal("v2", System.Text.Encoding.Unicode.GetString(Val(dest, "V2")!).TrimEnd('\0'));
        Assert.NotNull(Val(dest, "LowerCaseLongPath"));
        var h = File.ReadAllBytes(dest);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4)));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8)));
        Assert.Equal(RegfLogReplay.Checksum(h), BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(0x1FC)));
    }

    [Fact]
    public void Corrupted_hash1_rejects_the_entry_and_writes_no_copy()
    {
        var (hive, bytes) = DirtyHive();
        var e = RegfLogBuilder.Entry(6, Hive(1));
        e[40 + 8 + 5] ^= 0xFF;                                              // a page byte: Hash-1 no longer matches
        var log1 = Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 6, e));
        var dest = Path.Combine(_root, "copy.hve");

        var r = RegfLogReplay.Replay(hive, [log1], dest);

        Assert.Equal(HiveReplayOutcome.Failed, r.Outcome);
        Assert.Contains("Hash-1 invalid", string.Join(" ", r.Notes) + r.Reason);
        Assert.True(r.StoppedOnInvalidEntry);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void Corrupted_hash2_is_rejected_too()
    {
        var (hive, bytes) = DirtyHive();
        var e = RegfLogBuilder.Entry(6, Hive(1));
        e[8] ^= 1;                                                          // Flags are in the first 32 bytes: Hash-1 (data) still matches, Hash-2 does not
        var log1 = Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 6, e));
        var r = RegfLogReplay.Replay(hive, [log1], Path.Combine(_root, "copy.hve"));
        Assert.Equal(HiveReplayOutcome.Failed, r.Outcome);
        Assert.Contains("Hash-2 invalid", string.Join(" ", r.Notes));
    }

    [Fact]
    public void An_entry_out_of_sequence_stops_the_replay_after_the_previous_one()
    {
        var (hive, bytes) = DirtyHive();
        var log1 = Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1)), RegfLogBuilder.Entry(7, Hive(2)), RegfLogBuilder.Entry(9, Hive(4))));
        var dest = Path.Combine(_root, "copy.hve");

        var r = RegfLogReplay.Replay(hive, [log1], dest);

        Assert.Equal(HiveReplayOutcome.Replayed, r.Outcome);
        Assert.Equal(2, r.EntriesApplied);
        Assert.Equal(7u, r.LastSequence);
        Assert.NotNull(Val(dest, "V2"));
        Assert.Null(Val(dest, "V4"));                                       // entry 9 skipped sequence 8: not applied
        Assert.Contains(r.Notes, n => n.Contains("intrări neaplicate") && n.Contains('9'));
    }

    [Fact]
    public void A_first_entry_with_an_unexpected_sequence_fails_with_the_expected_number()
    {
        var (hive, bytes) = DirtyHive();
        var log1 = Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 7, RegfLogBuilder.Entry(7, Hive(1))));
        var r = RegfLogReplay.Replay(hive, [log1], Path.Combine(_root, "copy.hve"));
        Assert.Equal(HiveReplayOutcome.Failed, r.Outcome);
        Assert.Contains("se aștepta secvența 6", r.Reason);
    }

    [Fact]
    public void The_log_that_holds_the_expected_sequence_is_used_and_a_stale_log_is_ignored()
    {
        var (hive, bytes) = DirtyHive();
        var stale = Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 5, RegfLogBuilder.Entry(5, Hive(1))));       // older than the primary file
        var current = Write("Amcache.hve.LOG2", RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1)), RegfLogBuilder.Entry(7, Hive(3))));
        var r = RegfLogReplay.Replay(hive, [stale, current], Path.Combine(_root, "copy.hve"));
        Assert.Equal(HiveReplayOutcome.Replayed, r.Outcome);
        Assert.Equal((6u, 7u), (r.FirstSequence!.Value, r.LastSequence!.Value));
        Assert.Contains(r.Notes, n => n.StartsWith("Amcache.hve.LOG2: 2 intrări aplicate"));
    }

    [Fact]
    public void When_both_logs_hold_the_next_sequence_the_longer_valid_run_wins_and_replay_resumes_in_the_other_log()
    {
        var (hive, bytes) = DirtyHive();
        var log1 = Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1))));
        var log2 = Write("Amcache.hve.LOG2", RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1)), RegfLogBuilder.Entry(7, Hive(2)), RegfLogBuilder.Entry(8, Hive(3))));
        var dest = Path.Combine(_root, "copy.hve");
        var r = RegfLogReplay.Replay(hive, [log1, log2], dest);
        Assert.Equal(8u, r.LastSequence);
        Assert.Equal(3, r.EntriesApplied);
        Assert.NotNull(Val(dest, "V3"));

        // dual logging: LOG2 holds the earlier entries, LOG1 continues at N+1
        var logA = Write("A.LOG1", RegfLogBuilder.Log(bytes, 8, RegfLogBuilder.Entry(8, Hive(3)), RegfLogBuilder.Entry(9, Hive(4))));
        var logB = Write("A.LOG2", RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1)), RegfLogBuilder.Entry(7, Hive(2))));
        var dest2 = Path.Combine(_root, "copy2.hve");
        var r2 = RegfLogReplay.Replay(hive, [logA, logB], dest2);
        Assert.Equal(4, r2.EntriesApplied);
        Assert.Equal(9u, r2.LastSequence);
        Assert.NotNull(Val(dest2, "V4"));
    }

    [Fact]
    public void A_clean_hive_is_not_replayed_and_the_original_path_is_refused_as_destination()
    {
        var clean = Write("clean.hve", Hive(0));
        var log = Write("clean.hve.LOG1", RegfLogBuilder.Log(Hive(0), 1, RegfLogBuilder.Entry(2, Hive(1))));
        var dest = Path.Combine(_root, "copy.hve");
        Assert.Equal(HiveReplayOutcome.NotDirty, RegfLogReplay.Replay(clean, [log], dest).Outcome);
        Assert.False(File.Exists(dest));
        Assert.Throws<ArgumentException>(() => RegfLogReplay.Replay(clean, [log], clean));
    }

    [Fact]
    public void An_old_format_log_is_reported_not_silently_used()
    {
        var (hive, bytes) = DirtyHive();
        var old = RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1)));
        BinaryPrimitives.WriteUInt32LittleEndian(old.AsSpan(0x1C), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(old.AsSpan(0x1FC), RegfLogReplay.Checksum(old));
        var r = RegfLogReplay.Replay(hive, [Write("Amcache.hve.LOG1", old)], Path.Combine(_root, "copy.hve"));
        Assert.Equal(HiveReplayOutcome.Failed, r.Outcome);
        Assert.Contains("DIRT", r.Reason);
    }

    // ---- pipeline: derived evidence, parser reads the copy, gaps ----

    private CaseWorkspace ImportCase(byte[] hive, byte[] log1, byte[]? log2 = null)
    {
        var stage = Path.Combine(_root, "stage" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(stage);
        var files = new List<string>
        {
            Path.Combine(stage, "Amcache.hve"), Path.Combine(stage, "Amcache.hve.LOG1"),
        };
        File.WriteAllBytes(files[0], hive); File.WriteAllBytes(files[1], log1);
        if (log2 is not null) { files.Add(Path.Combine(stage, "Amcache.hve.LOG2")); File.WriteAllBytes(files[2], log2); }
        var ws = InvestigationPipeline.NewCase(Path.Combine(_root, "cases"), "hvle", TestScopes.Valid());
        InvestigationPipeline.Import(ws, files);
        return ws;
    }

    [Fact]
    public void Pipeline_parses_the_replayed_copy_registered_as_derived_evidence_and_the_gap_is_gone()
    {
        var dirty = RegfLogBuilder.WithSequences(Hive(0), 6, 5);
        var ws = ImportCase(dirty, RegfLogBuilder.Log(dirty, 6, RegfLogBuilder.Entry(6, Hive(1))));
        var original = ws.LoadEvidence().Single(e => e.OriginalName == "Amcache.hve");
        var originalFull = Path.Combine(ws.Root, original.StoredPath);
        var shaBefore = Sha(originalFull);

        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        var derived = Assert.Single(ws.LoadEvidence(), e => e.ParentEvidenceId == original.EvidenceId);
        Assert.Equal("derived", derived.AcquisitionMethod);
        Assert.Equal(original.SourceType, derived.SourceType);
        Assert.Equal(Sha(Path.Combine(ws.Root, derived.StoredPath)), derived.Sha256, ignoreCase: true);
        Assert.NotEqual(original.Sha256, derived.Sha256);
        Assert.Contains("Amcache.hve.LOG1", derived.Notes);
        Assert.Contains("1 intrări aplicate", derived.Notes);
        Assert.Equal(shaBefore, Sha(originalFull));
        Assert.Equal(original.Sha256, Sha(originalFull), ignoreCase: true);

        var ev = Assert.Single(r.Timeline, e => e.Source == "Amcache");
        Assert.Equal(derived.EvidenceId, ev.EvidenceId);
        Assert.Equal(@"c:\windows\system32\whoami.exe", ev.Path);
        Assert.DoesNotContain(r.Gaps, g => g.Artifact.StartsWith("Amcache.hve") || g.Artifact.StartsWith("Amcache Inventory", StringComparison.Ordinal) || g.Artifact == "Amcache transaction logs");
        Assert.Contains(r.Parsing, p => p.EvidenceId == original.EvidenceId && p.Status == EvidenceStatus.SkippedByDesign && p.Error.Contains(derived.EvidenceId));
    }

    [Fact]
    public void Pipeline_with_an_invalid_log_parses_the_original_and_records_the_exact_reason()
    {
        var dirty = RegfLogBuilder.WithSequences(Hive(0), 6, 5);
        var e = RegfLogBuilder.Entry(6, Hive(1));
        e[60] ^= 0x55;
        var ws = ImportCase(dirty, RegfLogBuilder.Log(dirty, 6, e));
        var original = ws.LoadEvidence().Single(x => x.OriginalName == "Amcache.hve");

        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        Assert.DoesNotContain(ws.LoadEvidence(), x => x.ParentEvidenceId is not null);
        var gap = Assert.Single(r.Gaps, g => g.Artifact == "Amcache.hve" && g.Reason.Contains("Hash-1 invalid"));
        Assert.Contains("hive-ul original este parsat ca atare", gap.Reason);
        var row = Assert.Single(r.Timeline, x => x.Source == "Amcache");
        Assert.Equal(original.EvidenceId, row.EvidenceId);                 // the original, valueless as before
        Assert.Contains("intrare fără valori", row.Summary);
    }

    [Fact]
    public void A_rerun_reuses_the_derived_copy_instead_of_deriving_again()
    {
        var dirty = RegfLogBuilder.WithSequences(Hive(0), 6, 5);
        var ws = ImportCase(dirty, RegfLogBuilder.Log(dirty, 6, RegfLogBuilder.Entry(6, Hive(1))));
        new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        Assert.Single(ws.LoadEvidence(), e => e.Collector == HiveLogReplay.Collector);
    }

    // ---- review fixes ----

    private static InvestigationResult NewResult(CaseWorkspace ws) => new() { Case = ws };

    [Fact]
    public void A_parented_remote_style_hive_with_its_logs_is_replayed()
    {
        var dirty = RegfLogBuilder.WithSequences(Hive(0), 6, 5);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_root, "cases"), "remote", TestScopes.Valid());
        var dir = ws.RawDir("remote");
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "{}");
        var manifest = ws.RegisterStored(Path.Combine(dir, "manifest.json"), "manifest.json", "remote", "remote_manifest", TemporalType.Historical, "t", "1");
        EvidenceItem Reg(string n, byte[] b)
        {
            File.WriteAllBytes(Path.Combine(dir, n), b);
            return ws.RegisterStored(Path.Combine(dir, n), @"pkg\files\Amcache\" + n, "remote", n.EndsWith("hve") ? "amcache" : "amcache_log", TemporalType.Historical, "t", "1", "imported", parentEvidenceId: manifest.EvidenceId);
        }
        var hive = Reg("Amcache.hve", dirty);
        Reg("Amcache.hve.LOG1", RegfLogBuilder.Log(dirty, 6, RegfLogBuilder.Entry(6, Hive(1))));

        var res = NewResult(ws);
        var map = HiveLogReplay.Apply(ws, res);

        var derived = Assert.Single(map).Value;
        Assert.Equal(hive.EvidenceId, derived.ParentEvidenceId);
        Assert.Equal("derived", derived.AcquisitionMethod);
        Assert.Empty(res.Gaps);
    }

    [Fact]
    public void A_rerun_after_the_original_changed_reports_the_mutation_and_does_not_supersede_it()
    {
        var dirty = RegfLogBuilder.WithSequences(Hive(0), 6, 5);
        var ws = ImportCase(dirty, RegfLogBuilder.Log(dirty, 6, RegfLogBuilder.Entry(6, Hive(1))));
        new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        var original = ws.LoadEvidence().Single(e => e.OriginalName == "Amcache.hve" && e.ParentEvidenceId is null);
        var full = Path.Combine(ws.Root, original.StoredPath);
        File.SetAttributes(full, FileAttributes.Normal);
        using (var fs = new FileStream(full, FileMode.Append)) fs.WriteByte(1);

        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        Assert.Contains(r.Gaps, g => g.Reason.Contains("EVIDENCE_MUTATED") && g.Status == EvidenceStatus.Failed);
        Assert.DoesNotContain(r.Parsing, p => p.EvidenceId == original.EvidenceId && p.Status == EvidenceStatus.SkippedByDesign && p.Error.Contains("înlocuit"));
        Assert.Contains(r.Parsing, p => p.EvidenceId == original.EvidenceId && p.Status == EvidenceStatus.Failed);
        // main-session review: the derived copy of a changed original is not read either (no finding may cite it)
        var copy = ws.LoadEvidence().Single(e => e.Collector == HiveLogReplay.Collector && e.ParentEvidenceId == original.EvidenceId);
        Assert.Contains(r.Parsing, p => p.EvidenceId == copy.EvidenceId && p.Status == EvidenceStatus.SkippedByDesign && p.Error.Contains("copie derivată nefolosită"));
        Assert.DoesNotContain(r.Timeline, t => t.EvidenceId == copy.EvidenceId);
    }

    [Fact]
    public void Two_imports_of_the_same_staging_folder_are_never_mixed()
    {
        var dirty = RegfLogBuilder.WithSequences(Hive(0), 6, 5);
        var stage = Path.Combine(_root, "stage_twice");
        Directory.CreateDirectory(stage);
        var files = new[] { Path.Combine(stage, "Amcache.hve"), Path.Combine(stage, "Amcache.hve.LOG1") };
        File.WriteAllBytes(files[0], dirty);
        File.WriteAllBytes(files[1], RegfLogBuilder.Log(dirty, 6, RegfLogBuilder.Entry(6, Hive(1))));
        var ws = InvestigationPipeline.NewCase(Path.Combine(_root, "cases"), "twice", TestScopes.Valid());
        InvestigationPipeline.Import(ws, files);
        InvestigationPipeline.Import(ws, files);

        var res = NewResult(ws);
        var map = HiveLogReplay.Apply(ws, res);

        Assert.Empty(map);
        Assert.Equal(2, res.Gaps.Count(g => g.Reason.Contains("asociere ambiguă")));
        Assert.DoesNotContain(ws.LoadEvidence(), e => e.ParentEvidenceId is not null);
    }

    [Fact]
    public void Stale_bytes_after_the_last_valid_entry_or_a_rejected_entry_of_an_unused_log_are_not_a_gap_but_a_broken_chain_is()
    {
        var (hive, bytes) = DirtyHive();
        var stale = RegfLogBuilder.Entry(20, Hive(4)); stale[60] ^= 1;                                   // invalid, sequence 20: not the next expected one
        var log1 = Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1)), RegfLogBuilder.Entry(7, Hive(2)), stale));
        var unusedBad = RegfLogBuilder.Entry(3, Hive(4)); unusedBad[60] ^= 1;
        var log2 = Write("Amcache.hve.LOG2", RegfLogBuilder.Log(bytes, 3, unusedBad));
        var r = RegfLogReplay.Replay(hive, [log1, log2], Path.Combine(_root, "c1.hve"));
        Assert.Equal(HiveReplayOutcome.Replayed, r.Outcome);
        Assert.False(r.StoppedOnInvalidEntry);
        Assert.Contains(r.Notes, n => n.Contains("respinsă"));                                            // still told, as information

        var broken = RegfLogBuilder.Entry(8, Hive(3)); broken[60] ^= 1;                                  // the NEXT expected sequence is the rejected one
        var log3 = Write("B.LOG1", RegfLogBuilder.Log(bytes, 6, RegfLogBuilder.Entry(6, Hive(1)), RegfLogBuilder.Entry(7, Hive(2)), broken, RegfLogBuilder.Entry(9, Hive(4))));
        var r2 = RegfLogReplay.Replay(hive, [log3], Path.Combine(_root, "c2.hve"));
        Assert.Equal(HiveReplayOutcome.Replayed, r2.Outcome);
        Assert.Equal(7u, r2.LastSequence);
        Assert.True(r2.StoppedOnInvalidEntry);
    }

    [Fact]
    public void Checksum_special_values_follow_the_spec()
    {
        Assert.Equal(1u, RegfLogReplay.Checksum(new byte[512]));                                         // XOR 0 -> 1
        var b = new byte[512];
        BinaryPrimitives.WriteUInt32LittleEndian(b, 0xFFFFFFFF);
        Assert.Equal(0xFFFFFFFEu, RegfLogReplay.Checksum(b));                                            // XOR -1 -> -2
    }

    [Fact]
    public void An_entry_that_asks_for_an_absurd_hive_size_is_refused_and_nothing_is_written()
    {
        var (hive, bytes) = DirtyHive();
        var e = RegfLogBuilder.Entry(6, Hive(1));
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(16), 8u << 20);                               // 8 MiB of hive bins for a 8 KiB hive
        RegfLogBuilder.Seal(e);
        var dest = Path.Combine(_root, "copy.hve");
        var r = RegfLogReplay.Replay(hive, [Write("Amcache.hve.LOG1", RegfLogBuilder.Log(bytes, 6, e))], dest);
        Assert.Equal(HiveReplayOutcome.Failed, r.Outcome);
        Assert.Contains("limita de creștere", r.Reason);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void When_the_copy_cannot_be_registered_it_is_deleted_and_a_gap_names_the_failure()
    {
        var dirty = RegfLogBuilder.WithSequences(Hive(0), 6, 5);
        var ws = ImportCase(dirty, RegfLogBuilder.Log(dirty, 6, RegfLogBuilder.Entry(6, Hive(1))));
        ws.MarkReadOnly("test");                                                                          // RegisterStored throws after the copy is written
        var res = NewResult(ws);
        var map = HiveLogReplay.Apply(ws, res);
        Assert.Empty(map);
        Assert.Empty(Directory.GetFiles(ws.Root, "*.replayed*", SearchOption.AllDirectories));
        Assert.Contains(res.Gaps, g => g.Reason.Contains("InvalidOperationException"));
        HiveLogReplay.Apply(ws, NewResult(ws));
        Assert.Empty(Directory.GetFiles(ws.Root, "*.replayed*", SearchOption.AllDirectories));
    }
}
