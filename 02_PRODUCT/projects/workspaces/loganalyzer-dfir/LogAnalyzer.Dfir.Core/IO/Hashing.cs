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

    public static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));
}
