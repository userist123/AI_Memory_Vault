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
        if (StartsWith(h, 0, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])) return "cfb";
        if (StartsWith(h, 0, [0x4C, 0, 0, 0, 0x01, 0x14, 0x02, 0, 0, 0, 0, 0, 0xC0, 0, 0, 0])) return "lnk";
        if (n >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(h[4..]) == 0x89ABCDEF) return "ese";
        if (n >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(h) == 0x0A0D0D0A) return "pcapng";
        if (IsTaskXml(path)) return "task_xml";
        if (IsUsnExport(path)) return "usn_fsutil";
        if (IsSetupApiLog(path)) return "setupapi_dev";
        if (IsCustomDestinations(path, n, h)) return "custom_destinations";
        if (IsDefenderHistory(path)) return LogAnalyzer.Dfir.FileSystem.DefenderDetectionHistoryParser.FingerprintName;
        if (IsNotepadTab(path)) return LogAnalyzer.Dfir.FileSystem.NotepadTabStateParser.FingerprintName;
        if (LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.Detect(path) is { } remoteToolLog) return remoteToolLog;
        if (IsFirewallLog(path)) return "pfirewall";
        if (IsZeekTsv(path)) return LogAnalyzer.Dfir.Network.ZeekLogParser.TsvFingerprint;
        if (IsRecycleInfo(path)) return "recycle_info";
        if (StartsWith(h, 0, "INDX"u8)) return LogAnalyzer.Dfir.FileSystem.I30Parser.FingerprintName;
        if (IsMft(path, h)) return LogAnalyzer.Dfir.FileSystem.MftParser.FingerprintName;
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

    /// <summary>setupapi.dev.log: starts with "[Device Install Log]" (UTF-8, ANSI or UTF-16 LE with a BOM).</summary>
    private static bool IsSetupApiLog(string path)
    {
        var buf = new byte[96];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        var head = n >= 2 && buf[0] == 0xFF && buf[1] == 0xFE ? System.Text.Encoding.Unicode.GetString(buf, 2, (n - 2) & ~1) : System.Text.Encoding.UTF8.GetString(buf, 0, n);
        return head.TrimStart('﻿', ' ', '\r', '\n').StartsWith("[Device Install Log]", StringComparison.Ordinal);
    }

    /// <summary>A custom Jump List: version 2 in the first 4 bytes and the footer 0xBABFFBAB in the last 4.</summary>
    private static bool IsCustomDestinations(string path, int n, ReadOnlySpan<byte> head)
    {
        if (n < 12 || BinaryPrimitives.ReadUInt32LittleEndian(head) != 2) return false;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length < 12) return false;
        fs.Seek(-4, SeekOrigin.End);
        Span<byte> tail = stackalloc byte[4];
        return fs.ReadAtLeast(tail, 4, throwOnEndOfStream: false) == 4 && BinaryPrimitives.ReadUInt32LittleEndian(tail) == 0xBABFFBAB;
    }

    /// <summary>A Notepad tab file: a GUID name ending in .bin / .0.bin / .1.bin and content "NP" + sequence + a "saved" byte of 0 or 1 (a WindowState file has another value there); see <see cref="LogAnalyzer.Dfir.FileSystem.NotepadTabStateParser.LooksLikeTab"/>.</summary>
    private static bool IsNotepadTab(string path)
    {
        if (!LogAnalyzer.Dfir.FileSystem.NotepadTabStateParser.TryTabFileName(Path.GetFileName(path), out _, out _)) return false;
        var buf = new byte[32];
        int n;
        long length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            length = fs.Length;
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        }
        return LogAnalyzer.Dfir.FileSystem.NotepadTabStateParser.LooksLikeTab(buf.AsSpan(0, n), length);
    }

    /// <summary>A Defender DetectionHistory file: an 8-byte ThreatId record followed by a 16-byte GUID record; see <see cref="LogAnalyzer.Dfir.FileSystem.DefenderDetectionHistoryParser.LooksLikeHistory"/>.</summary>
    private static bool IsDefenderHistory(string path)
    {
        var buf = new byte[0x18];
        int n;
        long length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            length = fs.Length;
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        }
        return LogAnalyzer.Dfir.FileSystem.DefenderDetectionHistoryParser.LooksLikeHistory(buf.AsSpan(0, n), length);
    }

    /// <summary>Text written by <c>fsutil usn readjournal … csv</c>: it starts with the "USN Journal ID" line.</summary>
    private static bool IsUsnExport(string path)
    {
        var buf = new byte[64];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        // Redirected from Windows PowerShell 5.1 the export is UTF-16 LE with a BOM; from cmd it is ANSI/UTF-8.
        var head = n >= 2 && buf[0] == 0xFF && buf[1] == 0xFE ? System.Text.Encoding.Unicode.GetString(buf, 2, (n - 2) & ~1)
                                                             : System.Text.Encoding.UTF8.GetString(buf, 0, n);
        return head.TrimStart('\uFEFF').StartsWith("USN Journal ID", StringComparison.Ordinal);
    }

    /// <summary>Recycle Bin metadata ($I): version 1 or 2 and the fixed part present; see <see cref="LogAnalyzer.Dfir.FileSystem.RecycleBinInfoParser.LooksLikeInfo"/>.</summary>
    private static bool IsRecycleInfo(string path)
    {
        var buf = new byte[64];
        int n;
        long length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            length = fs.Length;
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        }
        return LogAnalyzer.Dfir.FileSystem.RecycleBinInfoParser.LooksLikeInfo(buf.AsSpan(0, n), length, Path.GetFileName(path));
    }

    /// <summary>Windows Firewall text log: "#Version:" then "#Software: Microsoft Windows Firewall".</summary>
    private static bool IsFirewallLog(string path)
    {
        var buf = new byte[256];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        return LogAnalyzer.Dfir.Network.FirewallLogParser.LooksLikeFirewallLog(System.Text.Encoding.UTF8.GetString(buf, 0, n));
    }

    /// <summary>Zeek TSV log: the first line is "#separator".</summary>
    private static bool IsZeekTsv(string path)
    {
        var buf = new byte[64];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        return LogAnalyzer.Dfir.Network.ZeekLogParser.LooksLikeZeekTsv(System.Text.Encoding.UTF8.GetString(buf, 0, n));
    }

    /// <summary>Formats an evidence item may legitimately have, from its declared type or name; null when not checked.</summary>
    public static IReadOnlyCollection<string>? Expected(EvidenceItem item)
    {
        var t = item.SourceType;
        var name = Path.GetFileName(item.StoredPath);
        if (t == "evtx" || t.StartsWith("EventLog:", StringComparison.Ordinal) || name.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase)) return ["evtx"];
        if (t == "prefetch" || name.EndsWith(".pf", StringComparison.OrdinalIgnoreCase)) return ["prefetch", "prefetch_mam"];
        if (t == "srum" || name.Equals("SRUDB.dat", StringComparison.OrdinalIgnoreCase)) return ["ese"];
        if (t is "system_hive" or "amcache" or "ntuser_hive" or "usrclass_hive" or "software_hive") return ["regf"];
        if (t == "pcapng" || name.EndsWith(".pcapng", StringComparison.OrdinalIgnoreCase)) return ["pcapng"];
        if (t is "live_snapshot" or "airgap_facts") return ["json"];
        if (t == "task_xml") return ["task_xml"];
        if (t == LogAnalyzer.Dfir.Network.FirewallLogParser.SourceTypeName) return ["pfirewall"];
        if (t == LogAnalyzer.Dfir.Network.ZeekLogParser.SourceTypeName) return [LogAnalyzer.Dfir.Network.ZeekLogParser.TsvFingerprint, "json"];
        if (t == "usn_journal") return ["usn_fsutil"];
        if (t == LogAnalyzer.Dfir.FileSystem.I30Parser.SourceTypeName) return [LogAnalyzer.Dfir.FileSystem.I30Parser.FingerprintName];
        if (t == LogAnalyzer.Dfir.FileSystem.MftParser.SourceTypeName) return [LogAnalyzer.Dfir.FileSystem.MftParser.FingerprintName];
        if (t == LogAnalyzer.Dfir.FileSystem.NotepadTabStateParser.SourceTypeName) return [LogAnalyzer.Dfir.FileSystem.NotepadTabStateParser.FingerprintName];
        if (LogAnalyzer.Dfir.RemoteTools.RemoteToolLogFormats.IsKnown(t)) return [t];   // the source type is the fingerprint name
        if (t == LogAnalyzer.Dfir.FileSystem.DefenderDetectionHistoryParser.SourceTypeName) return [LogAnalyzer.Dfir.FileSystem.DefenderDetectionHistoryParser.FingerprintName];
        if (t == LogAnalyzer.Dfir.FileSystem.RecycleBinInfoParser.SourceTypeName) return ["recycle_info"];
        if (t is "chromium_history" or "firefox_places" or "windows_search_index" or "windows_search_gather") return ["sqlite"];   // WindowsSearchIndexParser / WindowsSearchGatherParser .SourceTypeName
        if (t == "jumplist_auto" || name.EndsWith(".automaticDestinations-ms", StringComparison.OrdinalIgnoreCase)) return ["cfb"];
        if (t == "lnk" || name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return ["lnk"];
        return null;
    }

    /// <summary>
    /// An exported $MFT: a valid FILE header (same rule as NtfsFileRecord.DetectRecordSize) among the first
    /// 64 positions of 1,024 bytes. Record 0 may be zeroed or BAAD. Only files that start with FILE, BAAD or zeros are read that far.
    /// </summary>
    private static bool IsMft(string path, ReadOnlySpan<byte> h)
    {
        if (h.Length < 4 || !(StartsWith(h, 0, "FILE"u8) || StartsWith(h, 0, "BAAD"u8) || StartsWith(h, 0, [0, 0, 0, 0]))) return false;
        var buf = new byte[LogAnalyzer.Dfir.FileSystem.Ntfs.NtfsFileRecord.DetectPositions * 1024 + 8];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        return LogAnalyzer.Dfir.FileSystem.Ntfs.NtfsFileRecord.DetectRecordSize(buf.AsSpan(0, n), out _) > 0;
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

    /// <summary>
    /// The file is gone or its content changed after acquisition: results attributed to it no longer belong to the acquired source.
    /// A format mismatch with an intact hash is not this: the file is the acquired one, it was only not interpreted.
    /// </summary>
    public bool IsIntegrityFailure => Status is PreflightStatus.Missing or PreflightStatus.SizeMismatch or PreflightStatus.HashMismatch;

    /// <summary>Reader-facing notice for a format mismatch; empty for every other state.</summary>
    public string FormatNotice => Status != PreflightStatus.FormatMismatch ? ""
        : ExpectedSha256.Length > 0
            ? "fișier gol sau format neașteptat; amprenta este intactă; elementul de probă nu a fost interpretat"
            : "fișier gol sau format neașteptat; nu există amprentă de la achiziție; elementul de probă nu a fost interpretat";

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
                (fingerprint == "empty" ? "Fișierul este gol" : $"Conținutul are formatul „{fingerprint}”") + $", dar elementul de probă este declarat {item.SourceType} (așteptat: {string.Join("/", expected)}).", fileTime);
        if (item.Sha256.Length == 0)
            return new(item.EvidenceId, PreflightStatus.Unverified, size, sha, "", fingerprint,
                "Proba nu are un SHA-256 de la achiziție; integritatea nu poate fi verificată.", fileTime);
        return new(item.EvidenceId, PreflightStatus.Ok, size, sha, item.Sha256, fingerprint, "", fileTime);
    }
}
