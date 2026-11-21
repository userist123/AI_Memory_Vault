using System.Buffers.Binary;
using LogAnalyzer.Dfir.FileSystem.Ntfs;
using Xunit;
using static LogAnalyzer.Dfir.Tests.I30TestKit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review 2026-11-13 (S5, round 2, LOW): a damaged $INDEX_ROOT (no end marker, or used = 0) was read as complete with no problem, so it could hide
/// names silently. Every node ends with a last-entry marker; without it the root is reported as a problem.
/// </summary>
public sealed class IndexRootDamageTests
{
    private static readonly long T = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

    [Fact]
    public void An_intact_root_reads_its_entries_with_no_problem()
    {
        var v = IndexRootValue([Entry(0x0001_0000_0000_0040UL, "a.txt", T), Entry(0x0001_0000_0000_0041UL, "b.txt", T)]);
        var list = IndexRootNode.Read(v, out var problem);
        Assert.Null(problem);
        Assert.Equal(["a.txt", "b.txt"], list.Select(e => e.Key.Name));
    }

    [Fact]
    public void A_root_without_its_end_marker_is_a_problem_and_keeps_what_was_read()
    {
        var v = IndexRootValue([Entry(0x0001_0000_0000_0040UL, "a.txt", T)]);
        var withoutEnd = v.AsSpan(0, v.Length - 16).ToArray();   // drop the 16-byte end marker
        BinaryPrimitives.WriteUInt32LittleEndian(withoutEnd.AsSpan(20), (uint)(withoutEnd.Length - 16));   // used
        BinaryPrimitives.WriteUInt32LittleEndian(withoutEnd.AsSpan(24), (uint)(withoutEnd.Length - 16));   // allocated

        var list = IndexRootNode.Read(withoutEnd, out var problem);

        Assert.NotNull(problem);
        Assert.Contains("fără intrarea de sfârșit", problem, StringComparison.Ordinal);
        Assert.Equal("a.txt", Assert.Single(list).Key.Name);
    }

    [Fact]
    public void A_root_with_used_zero_is_a_problem_not_an_empty_directory()
    {
        var v = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(0), 0x30);
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(16), 16);   // first entry offset; used = allocated = 0 below the first entry
        var list = IndexRootNode.Read(v, out var problem);
        Assert.Empty(list);
        Assert.NotNull(problem);
    }

    [Fact]
    public void An_all_zero_root_is_a_problem()
    {
        var list = IndexRootNode.Read(new byte[32], out var problem);
        Assert.Empty(list);
        Assert.NotNull(problem);
    }
}
