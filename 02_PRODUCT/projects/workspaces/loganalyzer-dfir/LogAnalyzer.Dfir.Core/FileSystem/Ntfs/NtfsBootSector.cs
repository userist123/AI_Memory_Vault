using System.Numerics;

namespace LogAnalyzer.Dfir.FileSystem.Ntfs;

/// <summary>NTFS boot sector (format card §1). Record size is read from the volume, never assumed.</summary>
public sealed record NtfsBootSector(int BytesPerSector, int SectorsPerCluster, ulong TotalSectors, ulong MftLcn, ulong MftMirrLcn,
                                    int MftRecordSize, int IndexRecordSize, ulong VolumeSerial)
{
    public long ClusterSize => (long)BytesPerSector * SectorsPerCluster;
    public ulong TotalClusters => TotalSectors / (ulong)SectorsPerCluster;

    /// <summary>Null when the sector is not a valid NTFS boot sector; <paramref name="error"/> then says why.</summary>
    public static NtfsBootSector? TryParse(ReadOnlySpan<byte> s, out string? error)
    {
        error = null;
        if (s.Length < 512) { error = "sector de boot mai scurt de 512 octeți"; return null; }
        if (!s.Slice(3, 8).SequenceEqual("NTFS    "u8)) { error = "OEM ID diferit de „NTFS    ”"; return null; }
        if (s[510] != 0x55 || s[511] != 0xAA) { error = "lipsește semnătura 0x55AA"; return null; }
        int bps = Le.U16(s, 0x0B);
        if (bps is < 256 or > 4096 || !BitOperations.IsPow2(bps)) { error = $"bytes/sector nevalid ({bps})"; return null; }
        int spcRaw = s[0x0D], spc;
        if (spcRaw == 0) { error = "sectors/cluster = 0"; return null; }
        if (spcRaw <= 128) { if (!BitOperations.IsPow2(spcRaw)) { error = $"sectors/cluster nu e putere a lui 2 ({spcRaw})"; return null; } spc = spcRaw; }
        else if (spcRaw >= 244) spc = 1 << (256 - spcRaw);
        else { error = $"sectors/cluster nevalid ({spcRaw})"; return null; }
        long clusterSize = (long)bps * spc;
        if (clusterSize > 2 * 1024 * 1024) { error = "mărimea clusterului depășește 2 MB"; return null; }
        ulong total = Le.U64(s, 0x28), mft = Le.U64(s, 0x30), mirr = Le.U64(s, 0x38);
        if (total == 0) { error = "total sectors = 0"; return null; }
        ulong totalClusters = total / (ulong)spc;
        if (mft >= totalClusters) { error = "LCN-ul $MFT este în afara volumului"; return null; }
        if (!TryClustersToBytes((sbyte)s[0x40], clusterSize, out int recSize, out error, "înregistrare MFT")) return null;
        if (recSize < 256 || recSize > 65536 || recSize < bps || !BitOperations.IsPow2(recSize))
        { error = $"mărimea înregistrării MFT ({recSize}) în afara 256..65536, nu e putere a lui 2 sau e sub bytes/sector"; return null; }
        if (!TryClustersToBytes((sbyte)s[0x44], clusterSize, out int idxSize, out error, "înregistrare index")) return null;
        if (idxSize < 256 || idxSize > 1024 * 1024) { error = $"mărimea înregistrării de index ({idxSize}) nevalidă"; return null; }
        return new NtfsBootSector(bps, spc, total, mft, mirr, recSize, idxSize, Le.U64(s, 0x48));
    }

    /// <summary>Positive n = clusters, negative n = 2^|n| bytes. |n| greater than 31 (e.g. 0x80 = −128, where a shift would wrap) is rejected before shifting.</summary>
    private static bool TryClustersToBytes(sbyte n, long clusterSize, out int bytes, out string? error, string what)
    {
        bytes = 0; error = null;
        if (n == 0) { error = $"clustere pe {what} = 0"; return false; }
        if (n > 0) { long b = n * clusterSize; if (b > int.MaxValue) { error = $"{what} prea mare"; return false; } bytes = (int)b; return true; }
        int shift = -(int)n;   // widened first: −(−128) must not wrap
        if (shift > 31) { error = $"{what}: exponent {shift} nevalid (maxim 31)"; return false; }
        bytes = 1 << shift;
        return true;
    }
}
