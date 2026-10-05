using System.Buffers.Binary;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Integrity;

/// <summary>Format of a source file recognised from its content, not from its name.</summary>
public static class EvidenceFingerprint
{
    public static string Detect(string path)
    {
        Span<byte> h = stackalloc byte[16];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(h, h.Length, throwOnEndOfStream: false);
        if (n == 0) return "empty";
        h = h[..n];
        if (StartsWith(h, 0, "ElfFile\0"u8)) return "evtx";
        if (StartsWith(h, 0, "MAM\u0004"u8)) return "prefetch_mam";
        if (StartsWith(h, 4, "SCCA"u8)) return "prefetch";
        if (StartsWith(h, 0, "regf"u8)) return "regf";
        if (n >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(h[4..]) == 0x89ABCDEF) return "ese";
        if (n >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(h) == 0x0A0D0D0A) return "pcapng";
        var t = h.TrimStart(" \t\r\n﻿"u8);
        if (t.Length > 0 && (t[0] == (byte)'{' || t[0] == (byte)'[') || StartsWith(h, 0, [0xEF, 0xBB, 0xBF, (byte)'{'])) return "json";
        return "unknown";
    }

    /// <summary>Formats an evidence item may legitimately have, from its declared type or name; null when not checked.</summary>
    public static IReadOnlyCollection<string>? Expected(EvidenceItem item)
    {
        var t = item.SourceType;
        var name = Path.GetFileName(item.StoredPath);
        if (t == "evtx" || t.StartsWith("EventLog:", StringComparison.Ordinal) || name.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase)) return ["evtx"];
        if (t == "prefetch" || name.EndsWith(".pf", StringComparison.OrdinalIgnoreCase)) return ["prefetch", "prefetch_mam"];
        if (t == "srum" || name.Equals("SRUDB.dat", StringComparison.OrdinalIgnoreCase)) return ["ese"];
        if (t is "system_hive" or "amcache") return ["regf"];
        if (t == "pcapng" || name.EndsWith(".pcapng", StringComparison.OrdinalIgnoreCase)) return ["pcapng"];
        if (t == "live_snapshot") return ["json"];
        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> h, int offset, ReadOnlySpan<byte> magic) =>
        h.Length >= offset + magic.Length && h.Slice(offset, magic.Length).SequenceEqual(magic);
}

public enum PreflightStatus { Ok, Missing, Unreadable, SizeMismatch, HashMismatch, FormatMismatch }

/// <summary>State of a stored source right before (or after) it is read. Only <see cref="PreflightStatus.Ok"/> may be parsed.</summary>
public sealed record PreflightResult(string EvidenceId, PreflightStatus Status, long Size, string Sha256, string ExpectedSha256,
                                     string Fingerprint, string Detail)
{
    public bool CanParse => Status == PreflightStatus.Ok;

    /// <summary>Spec name used in errors, gaps and the audit log.</summary>
    public string Code => Status switch
    {
        PreflightStatus.Ok => "OK",
        PreflightStatus.Missing => "EVIDENCE_MISSING",
        PreflightStatus.Unreadable => "EVIDENCE_UNREADABLE",
        PreflightStatus.SizeMismatch or PreflightStatus.HashMismatch => "EVIDENCE_MUTATED",
        PreflightStatus.FormatMismatch => "EVIDENCE_FORMAT_MISMATCH",
        _ => throw new ArgumentOutOfRangeException(),
    };
}

/// <summary>
/// Verifies a stored source against what was recorded at acquisition (size, SHA-256) and against its declared format,
/// before a parser touches it. A source that changed after acquisition is never parsed as if it were the original.
/// </summary>
public static class EvidencePreflight
{
    public static PreflightResult Check(EvidenceItem item, string fullPath)
    {
        if (!File.Exists(fullPath))
            return new(item.EvidenceId, PreflightStatus.Missing, 0, "", item.Sha256, "", $"Fișierul probei lipsește: {fullPath}");
        long size;
        string sha, fingerprint;
        try
        {
            size = new FileInfo(fullPath).Length;
            sha = Hashing.Sha256File(fullPath);
            fingerprint = EvidenceFingerprint.Detect(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(item.EvidenceId, PreflightStatus.Unreadable, 0, "", item.Sha256, "", $"{ex.GetType().Name}: {ex.Message}");
        }

        if (item.Sha256.Length > 0 && !sha.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(item.EvidenceId, size != item.Size ? PreflightStatus.SizeMismatch : PreflightStatus.HashMismatch, size, sha, item.Sha256, fingerprint,
                $"SHA-256 la achiziție {item.Sha256} ({item.Size} B), acum {sha} ({size} B): proba a fost modificată după achiziție.");
        var expected = EvidenceFingerprint.Expected(item);
        if (expected is not null && !expected.Contains(fingerprint))
            return new(item.EvidenceId, PreflightStatus.FormatMismatch, size, sha, item.Sha256, fingerprint,
                $"Conținutul are formatul „{fingerprint}”, dar proba este declarată {item.SourceType} (așteptat: {string.Join("/", expected)}).");
        return new(item.EvidenceId, PreflightStatus.Ok, size, sha, item.Sha256, fingerprint, "");
    }
}
