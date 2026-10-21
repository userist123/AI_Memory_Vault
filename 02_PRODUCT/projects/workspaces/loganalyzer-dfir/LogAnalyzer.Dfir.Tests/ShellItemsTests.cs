using System.Buffers.Binary;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Tests.Synthetic;
using Xunit;
using static LogAnalyzer.Dfir.Tests.Synthetic.ShellItemBuilder;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Shell item decoder (E7): exact values on synthetic items and the hostile-input probes of the threat model.</summary>
public sealed class ShellItemsTests
{
    private static readonly DateTime Mod = new(2026, 9, 1, 10, 0, 2);
    private static readonly DateTime Cre = new(2026, 8, 30, 8, 15, 40);
    private static readonly DateTime Acc = new(2026, 9, 2, 23, 59, 58);

    [Fact]
    public void Root_folder_with_known_guid_has_a_romanian_name_and_an_unknown_guid_stays_a_guid()
    {
        var known = Assert.Single(ShellItems.ParseList(List(Root(ThisPc))).Items);
        Assert.Equal(ShellItemKind.RootFolder, known.Kind);
        Assert.Equal("Acest PC", known.DisplayName);
        Assert.Equal("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", known.Guid);

        var other = new Guid("11111111-2222-3333-4444-555555555555");
        var unknown = Assert.Single(ShellItems.ParseList(List(Root(other))).Items);
        Assert.Equal("{11111111-2222-3333-4444-555555555555}", unknown.DisplayName);

        var docs = Assert.Single(ShellItems.ParseList(List(Root(new Guid("450D8FBA-AD25-11D0-98A8-0800361B1103")))).Items);
        Assert.Equal("Documente", docs.DisplayName);
    }

    [Fact]
    public void Volume_item_gives_the_drive_root()
    {
        var v = Assert.Single(ShellItems.ParseList(List(Volume(@"C:\"))).Items);
        Assert.Equal(ShellItemKind.Volume, v.Kind);
        Assert.Equal(@"C:\", v.DisplayName);
    }

    [Fact]
    public void Ascii_folder_and_file_entries_keep_the_dos_time_as_naive_local_with_2_second_resolution()
    {
        var list = ShellItems.ParseList(List(FileEntry(0x31, "Proiecte", 0, Mod), FileEntry(0x32, "plan.txt", 1234, Mod.AddSeconds(1))));
        Assert.Equal(2, list.Items.Count);
        Assert.True(list.Terminated);
        var dir = list.Items[0];
        Assert.Equal(ShellItemKind.Folder, dir.Kind);
        Assert.Equal("Proiecte", dir.DisplayName);
        Assert.Equal(Mod, dir.Modified!.Local);
        Assert.Equal("2026-09-01T10:00:02", dir.Modified.LocalIso);          // no zone suffix: it is not UTC
        Assert.Equal(DateTimeKind.Unspecified, dir.Modified.Local.Kind);
        Assert.Contains("without time zone", DosDateTime.Method);
        Assert.Equal(DosDateTime.Method, dir.Modified.ToTimestamp().ConversionMethod);
        Assert.Equal($"0x{DosDate(Mod):X4}:0x{DosTime(Mod):X4}", dir.Modified.Raw);   // raw words kept
        var file = list.Items[1];
        Assert.Equal(ShellItemKind.File, file.Kind);
        Assert.Equal(1234u, file.FileSize);
        Assert.Equal(Mod, file.Modified!.Local);                              // 10:00:03 is stored as 10:00:02 (2 s resolution)
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void Extension_block_gives_long_unicode_name_created_accessed_and_mft_reference(int version)
    {
        var ext = new Ext(version, "Proiecte ăâîșț", Cre, Acc, version >= 7 ? 123456 : 0, version >= 7 ? 7 : 0);
        var item = Assert.Single(ShellItems.ParseList(List(FileEntry(0x35, "PROIEC~1", 0, Mod, ext))).Items);
        Assert.Equal("Proiecte ăâîșț", item.DisplayName);
        Assert.Equal("PROIEC~1", item.ShortName);
        Assert.Equal(version, item.ExtensionVersion);
        Assert.Equal(Cre, item.Created!.Local);
        Assert.Equal(Acc, item.Accessed!.Local);
        Assert.Equal(Mod, item.Modified!.Local);
        if (version >= 7) { Assert.Equal(123456L, item.MftEntry); Assert.Equal(7, item.MftSequence); }
        else { Assert.Null(item.MftEntry); Assert.Null(item.MftSequence); }
    }

    [Fact]
    public void Unicode_primary_name_without_extension_block()
    {
        var item = Assert.Single(ShellItems.ParseList(List(FileEntry(0x36, "raport ț.docx", 10, Mod))).Items);
        Assert.Equal(ShellItemKind.File, item.Kind);
        Assert.Equal("raport ț.docx", item.DisplayName);
        Assert.Null(item.Created);
    }

    [Fact]
    public void Control_panel_uri_users_files_variable_and_network_items_are_decoded_best_effort()
    {
        var cp = new byte[30]; cp[2] = 0x71; BinaryPrimitives.WriteUInt16LittleEndian(cp, 30);
        new Guid("11111111-2222-3333-4444-555555555555").ToByteArray().CopyTo(cp, 14);
        var c = ShellItems.Decode(cp);
        Assert.Equal(ShellItemKind.ControlPanel, c.Kind);
        Assert.Contains("{11111111-2222-3333-4444-555555555555}", c.DisplayName);

        var uri = Wrap(0x61, [0x80, 0, 0, .. System.Text.Encoding.Unicode.GetBytes("https://exemplu.test/a"), 0, 0]);
        var u = ShellItems.Decode(uri);
        Assert.Equal(ShellItemKind.Uri, u.Kind);
        Assert.Contains("https://exemplu.test/a", u.DisplayName);

        var users = FileEntry(0x32, "x", 0, Mod, new Ext(7, "Descărcări", Cre, Acc, 5, 1));
        users[2] = 0x74;
        var uf = ShellItems.Decode(users);
        Assert.Equal(ShellItemKind.UsersFilesFolder, uf.Kind);
        Assert.Equal("Descărcări", uf.DisplayName);

        var variable = Wrap(0x00, [0, 0, .. System.Text.Encoding.Unicode.GetBytes("arhiva.zip"), 0, 0]);
        var v = ShellItems.Decode(variable);
        Assert.Equal(ShellItemKind.Variable, v.Kind);
        Assert.Contains("arhiva.zip", v.DisplayName);

        var net = Wrap(0x41, [0, 0, .. System.Text.Encoding.ASCII.GetBytes(@"\\server\partaj"), 0]);
        var n = ShellItems.Decode(net);
        Assert.Equal(ShellItemKind.Network, n.Kind);
        Assert.Contains(@"\\server\partaj", n.DisplayName);
    }

    [Fact]
    public void Unknown_type_is_reported_with_its_type_and_hex_never_dropped()
    {
        var raw = Wrap(0x99, [1, 2, 3, 4]);
        var list = ShellItems.ParseList(List(raw));
        var item = Assert.Single(list.Items);
        Assert.Equal(ShellItemKind.Unknown, item.Kind);
        Assert.Equal("element necunoscut (tip 0x99)", item.DisplayName);
        Assert.Equal(Convert.ToHexString(raw), item.Hex);
    }

    // ---- hostile input (threat model) ----

    [Fact]
    public void Size_zero_ends_the_list_and_sizes_one_and_two_are_reported_without_a_crash()
    {
        var empty = ShellItems.ParseList([0, 0]);
        Assert.Empty(empty.Items); Assert.True(empty.Terminated);

        foreach (ushort size in new ushort[] { 1, 2 })
        {
            var bytes = new byte[10]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, size);
            var l = ShellItems.ParseList(bytes);
            Assert.Empty(l.Items);
            Assert.False(l.Terminated);
            Assert.Contains(l.Issues, i => i.Contains("dimensiune invalidă"));
        }
    }

    [Fact]
    public void Item_larger_than_the_remaining_buffer_is_kept_flagged_truncated()
    {
        var good = FileEntry(0x31, "Proiecte", 0, Mod);
        var bad = Wrap(0x31, new byte[10]);
        BinaryPrimitives.WriteUInt16LittleEndian(bad, 0xFFF0);
        var l = ShellItems.ParseList(List(false, good, bad));
        Assert.Equal(2, l.Items.Count);
        Assert.Equal("Proiecte", l.Items[0].DisplayName);
        Assert.True(l.Items[1].Truncated);
        Assert.Contains(l.Issues, i => i.Contains("declară"));
        Assert.False(l.Terminated);
    }

    [Fact]
    public void List_without_terminator_is_reported()
    {
        var l = ShellItems.ParseList(List(false, FileEntry(0x31, "A", 0, Mod)));
        Assert.Single(l.Items);
        Assert.False(l.Terminated);
        Assert.Contains(l.Issues, i => i.Contains("terminator"));
        var cut = ShellItems.ParseList([0x14]);   // one byte: not even a size
        Assert.Empty(cut.Items);
        Assert.NotEmpty(cut.Issues);
    }

    [Fact]
    public void A_nested_list_inside_an_item_is_not_followed()
    {
        var inner = List(FileEntry(0x31, "Interior", 0, Mod));
        var outer = Wrap(0x99, inner);
        var l = ShellItems.ParseList(List(outer));
        var only = Assert.Single(l.Items);
        Assert.Equal(ShellItemKind.Unknown, only.Kind);
    }

    [Fact]
    public void Item_count_is_capped()
    {
        var items = Enumerable.Range(0, ShellItems.MaxItems + 100).Select(_ => Wrap(0x99, [0])).ToArray();
        var l = ShellItems.ParseList(List(items));
        Assert.Equal(ShellItems.MaxItems, l.Items.Count);
        Assert.Contains(l.Issues, i => i.Contains("mai mult de"));
    }

    [Fact]
    public void Extension_block_with_wrong_size_version_signature_or_offset_never_misleads()
    {
        var good = FileEntry(0x35, "SCURT~1", 0, Mod, new Ext(7, "Nume lung", Cre, Acc, 9, 2));
        int start = BinaryPrimitives.ReadUInt16LittleEndian(good.AsSpan(good.Length - 2));

        var oversize = (byte[])good.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(oversize.AsSpan(start), 0xFFFF);
        Assert.Equal("SCURT~1", Assert.Single(ShellItems.ParseList(List(oversize)).Items).DisplayName);   // block ignored: the short name

        foreach (ushort version in new ushort[] { 0, 1, 2, 99, 0xFFFF })
        {
            var badVersion = (byte[])good.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(badVersion.AsSpan(start + 2), version);
            var it = Assert.Single(ShellItems.ParseList(List(badVersion)).Items);
            Assert.Equal("SCURT~1", it.DisplayName);
            Assert.Null(it.Created);
        }

        var badSig = (byte[])good.Clone(); badSig[start + 4] ^= 0xFF;
        Assert.Equal("SCURT~1", Assert.Single(ShellItems.ParseList(List(badSig)).Items).DisplayName);

        var badOffset = (byte[])good.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(badOffset.AsSpan(badOffset.Length - 2), 0xFFF0);
        Assert.Equal("Nume lung", Assert.Single(ShellItems.ParseList(List(badOffset)).Items).DisplayName);   // signature scan finds the block

        var zeroOffset = (byte[])good.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(zeroOffset.AsSpan(zeroOffset.Length - 2), 0);
        Assert.Equal("Nume lung", Assert.Single(ShellItems.ParseList(List(zeroOffset)).Items).DisplayName);
    }

    [Fact]
    public void Strings_without_terminator_or_with_odd_length_stay_inside_the_item()
    {
        // ASCII name running to the end of the item, no NUL.
        var ascii = FileEntry(0x31, "FARATERMINATOR", 0, Mod);
        var cut = ascii.Take(ascii.Length - 2).ToArray();            // drops the NUL and the pad byte
        BinaryPrimitives.WriteUInt16LittleEndian(cut, (ushort)cut.Length);
        Assert.StartsWith("FARATERMINATO", Assert.Single(ShellItems.ParseList(List(cut)).Items).DisplayName);

        // UTF-16 name with an odd number of bytes and no terminator.
        var uni = FileEntry(0x35, "abcdef", 0, Mod);
        var odd = uni.Take(uni.Length - 3).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(odd, (ushort)odd.Length);
        var it = Assert.Single(ShellItems.ParseList(List(odd)).Items);
        Assert.StartsWith("abcde", it.DisplayName);
        Assert.True(it.DisplayName.Length <= 6);
    }

    [Fact]
    public void Invalid_dos_dates_are_unknown_not_an_exception()
    {
        var item = FileEntry(0x31, "A", 0, Mod);
        BinaryPrimitives.WriteUInt16LittleEndian(item.AsSpan(8), (ushort)((46 << 9) | (13 << 5) | 31));   // month 13
        Assert.Null(Assert.Single(ShellItems.ParseList(List(item)).Items).Modified);
        BinaryPrimitives.WriteUInt16LittleEndian(item.AsSpan(8), 0);
        Assert.Null(Assert.Single(ShellItems.ParseList(List(item)).Items).Modified);
    }

    [Fact]
    public void Every_type_byte_at_every_short_length_decodes_without_throwing()
    {
        for (int type = 0; type < 256; type++)
            for (int len = 3; len <= 40; len++)
            {
                var b = new byte[len];
                BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)len);
                b[2] = (byte)type;
                for (int i = 3; i < len; i++) b[i] = (byte)(i * 31 + type);
                var item = ShellItems.Decode(b);
                Assert.False(string.IsNullOrEmpty(item.DisplayName));
            }
    }

    [Fact]
    public void Random_buffers_never_throw_and_stay_bounded()
    {
        var rnd = new Random(20261021);
        for (int n = 0; n < 3000; n++)
        {
            var b = new byte[rnd.Next(0, 300)];
            rnd.NextBytes(b);
            var l = ShellItems.ParseList(b);
            Assert.True(l.Items.Count <= ShellItems.MaxItems);
            Assert.True(l.BytesConsumed <= b.Length);
        }
    }
}
