using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>The geometry of an NTFS volume read from its boot sector (the first sector).</summary>
public readonly record struct NtfsBootSector(int BytesPerSector, int SectorsPerCluster, long MftLcn, int RecordSize)
{
    public int ClusterSize => BytesPerSector * SectorsPerCluster;

    /// <summary>
    /// Layout: OEM id "NTFS    " at 3, bytes per sector (u16) at 0x0B, sectors per cluster (u8) at 0x0D (above 0x80 the value is 2^(256-x)), first
    /// cluster of the $MFT (u64) at 0x30, clusters per file record (i8) at 0x40 (negative: the record is 2^-x bytes).
    /// </summary>
    /// <exception cref="InvalidDataException">Not an NTFS boot sector or values outside what NTFS allows.</exception>
    public static NtfsBootSector Parse(ReadOnlySpan<byte> sector)
    {
        if (sector.Length < 0x44) throw new InvalidDataException("Sectorul de boot este prea scurt.");
        if (!sector.Slice(3, 8).SequenceEqual("NTFS    "u8)) throw new InvalidDataException("Volumul nu este NTFS (lipsește identificatorul „NTFS” din sectorul de boot).");
        int bps = BinaryPrimitives.ReadUInt16LittleEndian(sector[0x0B..]);
        if (bps is < 512 or > 4096 || (bps & (bps - 1)) != 0) throw new InvalidDataException($"Dimensiunea sectorului ({bps}) nu este validă pentru NTFS.");
        int spcRaw = sector[0x0D];
        int spc = spcRaw > 0x80 ? (256 - spcRaw <= 12 ? 1 << (256 - spcRaw) : 0) : spcRaw;
        if (spc <= 0 || (spc & (spc - 1)) != 0 || (long)spc * bps > 2 * 1024 * 1024) throw new InvalidDataException($"Numărul de sectoare pe cluster ({spcRaw}) nu este valid.");
        ulong lcn = BinaryPrimitives.ReadUInt64LittleEndian(sector[0x30..]);
        if (lcn == 0 || lcn > (ulong)(long.MaxValue / ((long)spc * bps))) throw new InvalidDataException("Poziția tabelei $MFT din sectorul de boot nu este validă.");
        sbyte cpr = (sbyte)sector[0x40];
        long record = cpr > 0 ? (long)cpr * spc * bps : cpr >= -31 ? 1L << -cpr : 0;
        if (record is < 512 or > 65536 || (record & (record - 1)) != 0) throw new InvalidDataException($"Dimensiunea înregistrării $MFT ({record}) nu este validă.");
        return new NtfsBootSector(bps, spc, (long)lcn, (int)record);
    }
}

/// <summary>NTFS run list of a non-resident attribute: where the data is on the volume. A null <c>Lcn</c> is a sparse run (reads as zeroes, nothing on disk).</summary>
public static class DataRuns
{
    public readonly record struct Run(long? Lcn, long Clusters);

    /// <summary>
    /// Header byte: low nibble = size of the length field, high nibble = size of the offset field. The offset is signed and relative to the previous run's
    /// start; an offset size of 0 is a sparse run. A header byte of 0 ends the list (the end of the span does too).
    /// </summary>
    /// <exception cref="InvalidDataException">A field longer than the list, a zero or negative length, or a position outside the volume.</exception>
    public static List<Run> Decode(ReadOnlySpan<byte> list)
    {
        var runs = new List<Run>();
        long lcn = 0;
        int i = 0;
        while (i < list.Length && list[i] != 0)
        {
            int lenSize = list[i] & 0x0F, offSize = list[i] >> 4;
            i++;
            if (lenSize is 0 or > 8 || offSize > 8 || i + lenSize + offSize > list.Length) throw new InvalidDataException("Lista de extensii (run list) este coruptă: câmp mai lung decât lista.");
            ulong len = 0;
            for (int k = 0; k < lenSize; k++) len |= (ulong)list[i + k] << (8 * k);
            i += lenSize;
            if (len == 0 || len > long.MaxValue) throw new InvalidDataException("Lista de extensii (run list) conține o extensie cu lungime nulă sau imposibilă.");
            if (offSize == 0) { runs.Add(new Run(null, (long)len)); continue; }
            long delta = (sbyte)list[i + offSize - 1] < 0 ? -1L : 0L;   // sign-extend from the top byte
            for (int k = 0; k < offSize; k++) delta = (delta & ~(0xFFL << (8 * k))) | ((long)list[i + k] << (8 * k));
            i += offSize;
            try { lcn = checked(lcn + delta); }
            catch (OverflowException) { throw new InvalidDataException("Lista de extensii (run list) depășește limitele volumului."); }
            if (lcn < 0) throw new InvalidDataException("Lista de extensii (run list) indică o poziție negativă pe volum.");
            runs.Add(new Run(lcn, (long)len));
        }
        return runs;
    }
}

public sealed record MftAcquisitionResult(bool Ok, long Bytes, string Sha256, int RecordSize, string Problem)
{
    public static MftAcquisitionResult Fail(string problem) => new(false, 0, "", 0, problem);
}

/// <summary>
/// Reads the $MFT of a mounted NTFS volume (comparison row E8). Read only: the volume is opened with FileAccess.Read and FileShare.ReadWrite and nothing is
/// ever written to it. The boot sector gives the position of record 0 (the $MFT describing itself); its non-resident $DATA run list says where the whole
/// table is; the runs are copied cluster-aligned to the destination while the SHA-256 is computed. Needs administrator rights (raw volume access).
/// Limit: a $MFT so fragmented that its own run list sits in an $ATTRIBUTE_LIST is refused, not followed.
/// </summary>
public static partial class MftAcquisition
{
    private const int ChunkBytes = 4 * 1024 * 1024;

    [GeneratedRegex("^[A-Za-z]:\\\\?$")]
    private static partial Regex VolumePattern();

    /// <summary>Finds the unnamed non-resident $DATA of record 0 (after the update sequence fixup) and returns its run list and real size.</summary>
    /// <exception cref="InvalidDataException">The record is not a consistent $MFT record 0.</exception>
    public static (List<DataRuns.Run> Runs, long RealSize) ReadDataAttribute(byte[] record0)
    {
        if (record0.Length < 0x30 || !record0.AsSpan(0, 4).SequenceEqual("FILE"u8)) throw new InvalidDataException("Înregistrarea 0 a $MFT nu începe cu semnătura „FILE”.");
        var b = record0.AsSpan();
        if (!MftParser.ApplyFixup(b)) throw new InvalidDataException("Înregistrarea 0 a $MFT nu trece verificarea secvenței de actualizare.");
        int attr = BinaryPrimitives.ReadUInt16LittleEndian(b[0x14..]);
        long used = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(b[0x18..]), (uint)b.Length);
        while (attr >= 0x18 && attr + 8 <= used)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(b[attr..]);
            if (type == 0xFFFFFFFF) break;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(b[(attr + 4)..]);
            if (len < 0x18 || attr + (long)len > used) throw new InvalidDataException("Lanțul de atribute din înregistrarea 0 este corupt.");
            if (type == 0x80 && b[attr + 8] != 0 && b[attr + 9] == 0)
            {
                if (len < 0x40) throw new InvalidDataException("Atributul $DATA al $MFT este prea scurt.");
                int runOffset = BinaryPrimitives.ReadUInt16LittleEndian(b[(attr + 0x20)..]);
                ulong real = BinaryPrimitives.ReadUInt64LittleEndian(b[(attr + 0x30)..]);
                if (runOffset < 0x40 || runOffset >= len || real == 0 || real > long.MaxValue) throw new InvalidDataException("Atributul $DATA al $MFT are valori nevalide.");
                return (DataRuns.Decode(b.Slice(attr + runOffset, len - runOffset)), (long)real);
            }
            attr += len;
        }
        throw new InvalidDataException("Înregistrarea 0 nu are un atribut $DATA nerezident (lista de extensii a $MFT ar putea fi într-o listă de atribute, care nu se urmărește).");
    }

    /// <summary>Copies the $MFT of <paramref name="volume"/> (for example "C:") to <paramref name="destinationPath"/>. Never throws except for cancellation.</summary>
    public static MftAcquisitionResult Acquire(string? volume, string destinationPath, CancellationToken ct)
    {
        if (volume is null || !VolumePattern().IsMatch(volume)) return MftAcquisitionResult.Fail($"Volum nevalid „{volume}” (se așteaptă o literă de unitate, de exemplu C:).");
        var device = @"\\.\" + volume[..2];
        try
        {
            using var handle = File.OpenHandle(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.None);
            var boot = new byte[4096];
            if (RandomAccess.Read(handle, boot, 0) < 0x44) return MftAcquisitionResult.Fail("Sectorul de boot al volumului nu a putut fi citit.");
            var geo = NtfsBootSector.Parse(boot);
            int cluster = geo.ClusterSize;
            int span = (int)Math.Max(cluster, (geo.RecordSize + geo.BytesPerSector - 1) / geo.BytesPerSector * geo.BytesPerSector);
            var rec = new byte[span];
            if (RandomAccess.Read(handle, rec, geo.MftLcn * cluster) < geo.RecordSize) return MftAcquisitionResult.Fail("Înregistrarea 0 a $MFT nu a putut fi citită.");
            var (runs, real) = ReadDataAttribute(rec.AsSpan(0, geo.RecordSize).ToArray());
            long clusters = 0;
            foreach (var r in runs) clusters = checked(clusters + r.Clusters);
            if (clusters > long.MaxValue / cluster || clusters * cluster < real)
                return MftAcquisitionResult.Fail("Lista de extensii din înregistrarea 0 nu acoperă tot $MFT (probabil este într-o listă de atribute, care nu se urmărește).");

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            try
            {
                using var dest = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                var buf = new byte[Math.Max(1, ChunkBytes / cluster) * cluster];
                foreach (var run in runs)
                {
                    long left = run.Clusters * cluster;
                    long pos = run.Lcn is { } l ? checked(l * cluster) : 0;
                    while (left > 0 && written < real)
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = (int)Math.Min(buf.Length, left);
                        if (run.Lcn is null) Array.Clear(buf, 0, n);
                        else
                        {
                            int got = 0;
                            while (got < n)
                            {
                                int r = RandomAccess.Read(handle, buf.AsSpan(got, n - got), pos + got);
                                if (r <= 0) throw new IOException("Citirea volumului s-a oprit înainte de sfârșitul $MFT.");
                                got += r;
                            }
                        }
                        int take = (int)Math.Min(n, real - written);   // truncate to the real size: the last cluster is padding
                        dest.Write(buf, 0, take);
                        sha.AppendData(buf, 0, take);
                        written += take; pos += n; left -= n;
                    }
                }
                dest.Flush(true);
            }
            catch { TryDelete(destinationPath); throw; }
            if (written != real) { TryDelete(destinationPath); return MftAcquisitionResult.Fail("Numărul de octeți copiați nu corespunde dimensiunii $MFT."); }
            return new MftAcquisitionResult(true, written, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(), geo.RecordSize, "");
        }
        catch (UnauthorizedAccessException) { return MftAcquisitionResult.Fail("$MFT necesită drepturi de administrator (accesul direct la volum a fost refuzat)."); }
        catch (InvalidDataException ex) { return MftAcquisitionResult.Fail(ex.Message); }
        catch (FileNotFoundException) { return MftAcquisitionResult.Fail($"Volumul {volume} nu există."); }
        catch (DirectoryNotFoundException) { return MftAcquisitionResult.Fail($"Volumul {volume} nu există."); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or OverflowException)
        { return MftAcquisitionResult.Fail($"$MFT nu a putut fi citit de pe {volume}: {ex.Message}"); }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
}
