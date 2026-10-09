using System.Security.Cryptography;

namespace LogAnalyzer.Dfir.IO;

/// <summary>Streaming SHA-256 (spec §10). Opens with shared read so live/locked-for-write files can still be hashed.</summary>
public static class Hashing
{
    public static string Sha256File(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    /// <summary>
    /// Streaming SHA-256 that never holds more than one buffer of the file in memory and can be cancelled between reads
    /// (large evidence files; the WP3b re-check on open).
    /// </summary>
    public static string Sha256File(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        int n;
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            h.AppendData(buffer, 0, n);
            ct.ThrowIfCancellationRequested();
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    public static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));
}
