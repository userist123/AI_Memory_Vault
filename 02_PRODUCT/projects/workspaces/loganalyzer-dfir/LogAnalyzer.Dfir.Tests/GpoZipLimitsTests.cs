using System.IO.Compression;
using System.Text;
using LogAnalyzer.Dfir.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security audit #13: a GPO backup zip is read with caps on the number of entries, the uncompressed size of each entry and of all entries,
/// and the compression ratio; the caps hold even when the sizes declared in the zip headers lie. Two entries with the same name (ignoring case
/// and slash direction) are refused explicitly, never resolved silently.
/// </summary>
public sealed class GpoZipLimitsTests
{
    private const string BackupXml = "<GroupPolicyBackupScheme><GroupPolicyObject><GroupPolicyCoreSettings><ID>{AB}</ID><DisplayName>T</DisplayName></GroupPolicyCoreSettings></GroupPolicyObject></GroupPolicyBackupScheme>";

    private static MemoryStream Zip(Action<ZipArchive> fill)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) fill(z);
        ms.Position = 0;
        return ms;
    }

    private static void Add(ZipArchive z, string name, byte[] data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var s = z.CreateEntry(name, level).Open();
        s.Write(data);
    }

    private static void AddZeros(ZipArchive z, string name, long bytes)
    {
        using var s = z.CreateEntry(name, CompressionLevel.Fastest).Open();
        var chunk = new byte[1 << 20];
        for (long left = bytes; left > 0; left -= chunk.Length) s.Write(chunk, 0, (int)Math.Min(chunk.Length, left));
    }

    [Fact]
    public void A_one_gigabyte_entry_is_refused_with_a_clear_message()
    {
        using var ms = Zip(z =>
        {
            Add(z, "{AB}/Backup.xml", Encoding.UTF8.GetBytes(BackupXml));
            AddZeros(z, "{AB}/DomainSysvol/GPO/Machine/registry.pol", 1L << 30);
        });
        var ex = Assert.Throws<InvalidDataException>(() => GpoBackup.FromZip(ms));
        Assert.Contains("registry.pol", ex.Message);
        Assert.Contains("prea mare", ex.Message);
    }

    [Fact]
    public void Too_many_entries_are_refused()
    {
        using var ms = Zip(z =>
        {
            Add(z, "{AB}/Backup.xml", Encoding.UTF8.GetBytes(BackupXml));
            for (int i = 0; i < 20; i++) Add(z, $"{{AB}}/DomainSysvol/GPO/Machine/f{i}.txt", "x"u8.ToArray());
        });
        var ex = Assert.Throws<InvalidDataException>(() => GpoBackup.FromZip(ms, new GpoZipLimits { MaxEntries = 10 }));
        Assert.Contains("prea multe", ex.Message);
    }

    [Fact]
    public void Total_uncompressed_size_is_capped()
    {
        using var ms = Zip(z =>
        {
            Add(z, "{AB}/Backup.xml", Encoding.UTF8.GetBytes(BackupXml));
            for (int i = 0; i < 4; i++) Add(z, $"{{AB}}/DomainSysvol/GPO/Machine/Preferences/Registry/r{i}.xml", RandomBytes(4000), CompressionLevel.NoCompression);
        });
        var ex = Assert.Throws<InvalidDataException>(() => GpoBackup.FromZip(ms, new GpoZipLimits { MaxEntryBytes = 10_000, MaxTotalBytes = 10_000 }));
        Assert.Contains("total", ex.Message);
    }

    [Fact]
    public void A_high_compression_ratio_is_refused()
    {
        using var ms = Zip(z =>
        {
            Add(z, "{AB}/Backup.xml", Encoding.UTF8.GetBytes(BackupXml));
            AddZeros(z, "{AB}/DomainSysvol/GPO/Machine/registry.pol", 4L << 20);
        });
        var ex = Assert.Throws<InvalidDataException>(() => GpoBackup.FromZip(ms));
        Assert.Contains("comprimare", ex.Message);
    }

    [Fact]
    public void A_lying_size_header_does_not_bypass_the_per_entry_cap()
    {
        // Entry declared small in the central directory; the stream holds more. Simulated by a limit below the real size and a reader that must count.
        using var ms = Zip(z =>
        {
            Add(z, "{AB}/Backup.xml", Encoding.UTF8.GetBytes(BackupXml));
            Add(z, "{AB}/DomainSysvol/GPO/Machine/registry.pol", RandomBytes(5000), CompressionLevel.NoCompression);
        });
        var bytes = ms.ToArray();
        PatchUncompressedSize(bytes, "registry.pol", 100);
        var ex = Assert.ThrowsAny<Exception>(() => GpoBackup.FromZip(new MemoryStream(bytes), new GpoZipLimits { MaxEntryBytes = 1000 }));
        Assert.True(ex is InvalidDataException, ex.GetType().Name + ": " + ex.Message);
    }

    [Theory]
    [InlineData("{AB}/DomainSysvol/GPO/Machine/registry.pol", "{AB}/DomainSysvol/GPO/Machine/REGISTRY.POL")]
    [InlineData("{AB}/DomainSysvol/GPO/Machine/registry.pol", @"{AB}\DomainSysvol\GPO\Machine\registry.pol")]
    public void Duplicate_entry_names_are_refused_explicitly(string a, string b)
    {
        using var ms = Zip(z =>
        {
            Add(z, "{AB}/Backup.xml", Encoding.UTF8.GetBytes(BackupXml));
            Add(z, a, "PReg\u0001\0\0\0"u8.ToArray());
            Add(z, b, "PReg\u0001\0\0\0"u8.ToArray());
        });
        var ex = Assert.Throws<InvalidDataException>(() => GpoBackup.FromZip(ms));
        Assert.Contains("de două ori", ex.Message);
    }

    [Fact]
    public void A_normal_backup_is_still_read()
    {
        using var ms = Zip(z =>
        {
            Add(z, "{AB}/Backup.xml", Encoding.UTF8.GetBytes(BackupXml));
            Add(z, "{AB}/DomainSysvol/GPO/Machine/registry.pol", "PReg\u0001\0\0\0"u8.ToArray());
        });
        var g = GpoBackup.FromZip(ms);
        Assert.Equal("{AB}", g.Guid);
        Assert.NotNull(g.MachinePol);
    }

    private static byte[] RandomBytes(int n) { var b = new byte[n]; new Random(n).NextBytes(b); return b; }

    /// <summary>Rewrites the uncompressed-size fields (local header and central directory) of the entry whose name ends with <paramref name="suffix"/>.</summary>
    private static void PatchUncompressedSize(byte[] zip, string suffix, uint size)
    {
        var name = Encoding.ASCII.GetBytes(suffix);
        for (int i = 0; i + 46 < zip.Length; i++)
        {
            uint sig = BitConverter.ToUInt32(zip, i);
            if (sig == 0x04034b50)
            {
                int nlen = BitConverter.ToUInt16(zip, i + 26);
                if (EndsWith(zip, i + 30, nlen, name)) BitConverter.TryWriteBytes(zip.AsSpan(i + 22), size);
            }
            else if (sig == 0x02014b50)
            {
                int nlen = BitConverter.ToUInt16(zip, i + 28);
                if (EndsWith(zip, i + 46, nlen, name)) BitConverter.TryWriteBytes(zip.AsSpan(i + 24), size);
            }
        }
    }

    private static bool EndsWith(byte[] buf, int start, int len, byte[] suffix) =>
        len >= suffix.Length && start + len <= buf.Length && buf.AsSpan(start + len - suffix.Length, suffix.Length).SequenceEqual(suffix);
}
