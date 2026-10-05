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
        if (StartsWith(h, 0, "SQLite format 3\0"u8)) return "sqlite";
        if (n >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(h[4..]) == 0x89ABCDEF) return "ese";
        if (n >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(h) == 0x0A0D0D0A) return "pcapng";
        if (IsTaskXml(path)) return "task_xml";
        var t = h.TrimStart(" \t\r\n﻿"u8);
        if (t.Length > 0 && (t[0] == (byte)'{' || t[0] == (byte)'[') || StartsWith(h, 0, [0xEF, 0xBB, 0xBF, (byte)'{'])) return "json";
        return "unknown";
    }

    /// <summary>Scheduled task definition (System32\Tasks): XML, usually UTF-16 LE, root Task element in the task schema namespace.</summary>
    private static bool IsTaskXml(string path)
    {
        var buf = new byte[2048];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        string head = n >= 2 && buf[0] == 0xFF && buf[1] == 0xFE ? System.Text.Encoding.Unicode.GetString(buf, 2, (n - 2) & ~1)
                    : System.Text.Encoding.UTF8.GetString(buf, 0, n);
        return head.Contains("<Task", StringComparison.Ordinal) && head.Contains("schemas.microsoft.com/windows/2004/02/mit/task", StringComparison.Ordinal);
    }

    /// <summary>Formats an evidence item may legitimately have, from its declared type or name; null when not checked.</summary>
    public static IReadOnlyCollection<string>? Expected(EvidenceItem item)
    {
        var t = item.SourceType;
        var name = Path.GetFileName(item.StoredPath);
        if (t == "evtx" || t.StartsWith("EventLog:", StringComparison.Ordinal) || name.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase)) return ["evtx"];
        if (t == "prefetch" || name.EndsWith(".pf", StringComparison.OrdinalIgnoreCase)) return ["prefetch", "prefetch_mam"];
        if (t == "srum" || name.Equals("SRUDB.dat", StringComparison.OrdinalIgnoreCase)) return ["ese"];
        if (t is "system_hive" or "amcache" or "ntuser_hive" or "software_hive") return ["regf"];
        if (t == "pcapng" || name.EndsWith(".pcapng", StringComparison.OrdinalIgnoreCase)) return ["pcapng"];
        if (t == "live_snapshot") return ["json"];
        if (t == "task_xml") return ["task_xml"];
        if (t == "chromium_history") return ["sqlite"];
        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> h, int offset, ReadOnlySpan<byte> magic) =>
        h.Length >= offset + magic.Length && h.Slice(offset, magic.Length).SequenceEqual(magic);
}

public enum PreflightStatus { Ok, Unverified, Missing, Unreadable, Locked, SizeMismatch, HashMismatch, FormatMismatch }

/// <summary>State of a stored source right before (or after) it is read. Only Ok and Unverified may be parsed.</summary>
public sealed record PreflightResult(string EvidenceId, PreflightStatus Status, long Size, string Sha256, string ExpectedSha256,
                                     string Fingerprint, string Detail, DateTime? FileTimeUtc = null)
{
    /// <summary>Unverified = readable and of the declared format, but there is no acquisition hash to compare with.</summary>
    public bool CanParse => Status is PreflightStatus.Ok or PreflightStatus.Unverified;

    /// <summary>Code used in errors, gaps and the audit log.</summary>
    public string Code => Status switch
    {
        PreflightStatus.Ok => "OK",
        PreflightStatus.Unverified => "EVIDENCE_UNVERIFIED",
        PreflightStatus.Missing => "EVIDENCE_MISSING",
        PreflightStatus.Unreadable => "EVIDENCE_UNREADABLE",
        PreflightStatus.Locked => "EVIDENCE_LOCKED",
        PreflightStatus.SizeMismatch or PreflightStatus.HashMismatch => "EVIDENCE_MUTATED",
        PreflightStatus.FormatMismatch => "EVIDENCE_FORMAT_MISMATCH",
        _ => throw new ArgumentOutOfRangeException(),
    };

    /// <summary>Source status names from the master spec §4.</summary>
    public string SpecStatus => Status switch
    {
        PreflightStatus.Ok => "AVAILABLE",
        PreflightStatus.Unverified or PreflightStatus.FormatMismatch => "UNVERIFIED",
        PreflightStatus.Missing => "NO_EVIDENCE",
        PreflightStatus.Unreadable or PreflightStatus.Locked => "READ_ERROR",
        PreflightStatus.SizeMismatch or PreflightStatus.HashMismatch => "MUTATED",
        _ => throw new ArgumentOutOfRangeException(),
    };
}

/// <summary>
/// Verifies a stored source against what was recorded at acquisition (size, SHA-256) and against its declared format,
/// before a parser touches it. A source that changed after acquisition is never parsed as if it were the original.
/// </summary>
public static class EvidencePreflight
{
    private const int SharingViolation = unchecked((int)0x80070020), LockViolation = unchecked((int)0x80070021);

    /// <param name="acceptedFormats">Content formats the reader accepts (a parser's descriptor); null = those implied by the declared type.</param>
    public static PreflightResult Check(EvidenceItem item, string fullPath, IReadOnlyCollection<string>? acceptedFormats = null)
    {
        if (!File.Exists(fullPath))
            return new(item.EvidenceId, PreflightStatus.Missing, 0, "", item.Sha256, "", $"Fișierul probei lipsește: {fullPath}");
        long size;
        string sha, fingerprint;
        DateTime fileTime;
        try
        {
            var fi = new FileInfo(fullPath);
            size = fi.Length;
            fileTime = fi.LastWriteTimeUtc;
            sha = Hashing.Sha256File(fullPath);
            fingerprint = EvidenceFingerprint.Detect(fullPath);
        }
        catch (IOException ex) when (ex.HResult is SharingViolation or LockViolation)
        {
            return new(item.EvidenceId, PreflightStatus.Locked, 0, "", item.Sha256, "", $"Fișierul este blocat de alt proces: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(item.EvidenceId, PreflightStatus.Unreadable, 0, "", item.Sha256, "", $"{ex.GetType().Name}: {ex.Message}");
        }

        if (item.Sha256.Length > 0 && !sha.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(item.EvidenceId, size != item.Size ? PreflightStatus.SizeMismatch : PreflightStatus.HashMismatch, size, sha, item.Sha256, fingerprint,
                $"SHA-256 la achiziție {item.Sha256} ({item.Size} B), acum {sha} ({size} B): proba a fost modificată după achiziție.", fileTime);
        var expected = acceptedFormats ?? EvidenceFingerprint.Expected(item);
        if (expected is not null && !expected.Contains(fingerprint))
            return new(item.EvidenceId, PreflightStatus.FormatMismatch, size, sha, item.Sha256, fingerprint,
                $"Conținutul are formatul „{fingerprint}”, dar proba este declarată {item.SourceType} (așteptat: {string.Join("/", expected)}).", fileTime);
        if (item.Sha256.Length == 0)
            return new(item.EvidenceId, PreflightStatus.Unverified, size, sha, "", fingerprint,
                "Proba nu are un SHA-256 de la achiziție; integritatea nu poate fi verificată.", fileTime);
        return new(item.EvidenceId, PreflightStatus.Ok, size, sha, item.Sha256, fingerprint, "", fileTime);
    }
}
