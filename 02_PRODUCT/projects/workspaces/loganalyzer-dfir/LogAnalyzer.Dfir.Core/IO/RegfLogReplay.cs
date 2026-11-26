using System.Buffers.Binary;
using System.Text;

namespace LogAnalyzer.Dfir.IO;

public enum HiveReplayOutcome
{
    /// <summary>The base block has a good checksum and equal sequence numbers: nothing to recover (log entries of a clean hive are ignored, spec "Dirty state of a hive").</summary>
    NotDirty,
    /// <summary>At least one log entry was applied and the replayed copy was written.</summary>
    Replayed,
    /// <summary>The hive is dirty but could not be recovered; no copy was written. <see cref="HiveLogReplayResult.Reason"/> says why.</summary>
    Failed,
}

/// <param name="Notes">One line per log file and per rejected or unused entry, for the notes of the derived evidence item.</param>
/// <param name="StoppedOnInvalidEntry">A log entry failed a check (signature, size, hashes, page structure): replay stopped at it (spec, notes 7 and 8).</param>
public sealed record HiveLogReplayResult(HiveReplayOutcome Outcome, string Reason, int EntriesApplied, uint? FirstSequence, uint? LastSequence,
    IReadOnlyList<string> Notes, bool StoppedOnInvalidEntry);

/// <summary>
/// Replays the transaction logs (.LOG1/.LOG2, new "HvLE" format, Windows 8.1+) of a dirty registry hive onto a COPY of it.
/// The source hive and the logs are only read; the recovered image is written to <c>destPath</c> and only when at least one entry was applied.
/// Implemented from the public "Windows registry file format specification" by Maxim Suhanov (github.com/msuhanov/regf, sections
/// "Format of transaction log files / New format", "Dirty state of a hive", "Multiple transaction log files / New format"); no code taken from any implementation.
/// Marvin32 (the hash of the log entries) follows the published algorithm; the seed bytes 82 EF 4D 88 7A 4E 55 C5 are read as the 64-bit value 0x82EF4D887A4E55C5
/// (low dword first) and the 64-bit hash is (p1 shl 32) or p0 — checked against hashes of real Windows logs.
/// Implemented: dirty test (checksum / sequence numbers), HvLE entry validation (signature, size, Hive bins data size, Hash-1, Hash-2, page references),
/// the expected-sequence chain across LOG1 and LOG2 (the log holding the longer valid run from the expected sequence is applied first, recovery resumes in the
/// other log at N+1), hive growth, update of both sequence numbers, the bit 0 of Flags and the checksum in the copy.
/// Not implemented: the old DIRT format (File type 1), recovery of a damaged base block from a log base block, verification of dirty hive bins (spec note 10).
/// </summary>
public static class RegfLogReplay
{
    private const int BaseBlock = 0x1000, Sector = 512, EntryHeader = 40, MaxHiveBytes = 1 << 29;
    private const ulong Seed = 0x82EF4D887A4E55C5UL;

    private sealed record Entry(int Offset, int Size, uint Seq, uint HiveBinsSize, uint Flags, int PageCount);
    private sealed class Log(string name)
    {
        public string Name { get; } = name;
        public byte[] Data = [];
        public List<Entry> Entries { get; } = [];
        public string Note = "";
        public bool Invalid;
        /// <summary>Sequence number read from the first rejected entry (signature HvLE, bad checks); null when the log has none or its bytes are not an entry.</summary>
        public uint? RejectedSeq;
    }

    public static HiveLogReplayResult Replay(string hivePath, IReadOnlyList<string> logPaths, string destPath)
    {
        if (string.Equals(Path.GetFullPath(hivePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Copia recuperată nu poate fi scrisă peste hive-ul original.", nameof(destPath));
        var notes = new List<string>();
        HiveLogReplayResult Fail(string reason, bool invalid = false) => new(HiveReplayOutcome.Failed, reason, 0, null, null, notes, invalid);

        byte[] hive;
        try { hive = ReadAll(hivePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return Fail($"hive-ul nu a putut fi citit: {ex.Message}"); }
        if (hive.Length < BaseBlock || Encoding.ASCII.GetString(hive, 0, 4) != "regf") return Fail("nu este un hive (lipsește semnătura regf)");

        uint seq1 = U32(hive, 4), seq2 = U32(hive, 8);
        bool checksumOk = Checksum(hive) == U32(hive, 0x1FC);
        if (checksumOk && seq1 == seq2)
            return new(HiveReplayOutcome.NotDirty, $"hive-ul nu este murdar (secvențe {seq1}/{seq2}, suma de control corectă)", 0, null, null, notes, false);
        if (!checksumOk)
            return Fail("blocul de bază al hive-ului are suma de control greșită; recuperarea blocului de bază din jurnal nu este implementată");

        var logs = new List<Log>();
        foreach (var p in logPaths)
        {
            var log = new Log(Path.GetFileName(p));
            logs.Add(log);
            try { log.Data = ReadAll(p); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { log.Invalid = true; log.Note = $"nu a putut fi citit: {ex.Message}"; notes.Add($"{log.Name}: {log.Note}"); continue; }
            Scan(log, notes);
        }
        if (logs.Count == 0) return Fail("hive-ul este murdar, dar nu există niciun jurnal de tranzacții în caz");

        // Expected chain: the entries after the last completed write (secondary sequence number) of the primary file, N+1, N+2, ...
        uint expected = unchecked(seq2 + 1);
        int applied = 0;
        uint? first = null, last = null;
        uint hiveBins = U32(hive, 0x28);
        uint flagBit = U32(hive, 0x90) & 1;
        // growth cap: a damaged or hostile entry must not make the copy huge (4x the original, at least 1 MiB, never over MaxHiveBytes)
        long growthLimit = Math.Min(MaxHiveBytes, Math.Max(4L * hive.Length, 1 << 20));
        var usedLogs = new List<string>();
        while (true)
        {
            Log? bestLog = null; int bestStart = -1, bestRun = 0;
            foreach (var l in logs.Where(l => !l.Invalid))
            {
                int s = l.Entries.FindIndex(e => e.Seq == expected);
                if (s < 0) continue;
                int run = 1;
                while (s + run < l.Entries.Count && l.Entries[s + run].Seq == unchecked(l.Entries[s + run - 1].Seq + 1)) run++;
                if (run > bestRun) { bestLog = l; bestStart = s; bestRun = run; }
            }
            if (bestLog is null) break;
            for (int i = bestStart; i < bestStart + bestRun; i++)
            {
                var e = bestLog.Entries[i];
                if (BaseBlock + (long)e.HiveBinsSize > growthLimit)
                    return Fail($"{bestLog.Name}: intrarea cu secvența {e.Seq} cere Hive bins data size {e.HiveBinsSize}, peste limita de creștere ({growthLimit} octeți = 4x hive-ul original, min. 1 MiB); nu se aplică nimic");
                ApplyEntry(ref hive, bestLog.Data, e);
                hiveBins = e.HiveBinsSize; flagBit = e.Flags & 1;
                applied++; first ??= e.Seq; last = e.Seq;
            }
            usedLogs.Add($"{bestLog.Name}: {bestRun} intrări aplicate (secvențele {bestLog.Entries[bestStart].Seq}-{bestLog.Entries[bestStart + bestRun - 1].Seq})");
            expected = unchecked(bestLog.Entries[bestStart + bestRun - 1].Seq + 1);
        }
        notes.InsertRange(0, usedLogs);
        // the chain is broken by an invalid entry only when the entry that follows the last applied one (the expected sequence) is the rejected one;
        // stale bytes after the last valid entry, or a rejected entry of another sequence, are only noted
        bool invalidStop = logs.Any(l => !l.Invalid && l.RejectedSeq == expected);
        if (applied == 0)
        {
            var seen = string.Join("; ", logs.Select(l => l.Invalid ? $"{l.Name}: {l.Note}" : $"{l.Name}: {l.Entries.Count} intrări valide" + (l.Entries.Count > 0 ? $" (secvențele {string.Join(",", l.Entries.Select(e => e.Seq).Distinct().Take(8))})" : "")));
            return Fail($"nicio intrare de jurnal nu poate fi aplicată (se aștepta secvența {expected}): {seen}", invalidStop);
        }
        foreach (var l in logs.Where(l => !l.Invalid))
        {
            var leftover = l.Entries.Where(e => unchecked(e.Seq - expected) < 0x80000000u).Select(e => e.Seq).Distinct().ToList();
            if (leftover.Count > 0)
                notes.Add($"{l.Name}: intrări neaplicate cu secvențele {string.Join(",", leftover.Take(8))} (se aștepta {expected}; salt în secvență, reaplicarea s-a oprit)");
        }

        // Base block of the copy: both sequence numbers = the last applied one, size and flag bit from that entry, checksum recomputed.
        BinaryPrimitives.WriteUInt32LittleEndian(hive.AsSpan(4), last!.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(hive.AsSpan(8), last.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(hive.AsSpan(0x28), hiveBins);
        BinaryPrimitives.WriteUInt32LittleEndian(hive.AsSpan(0x90), (U32(hive, 0x90) & ~1u) | flagBit);
        BinaryPrimitives.WriteUInt32LittleEndian(hive.AsSpan(0x1FC), Checksum(hive));

        try
        {
            using var fs = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.Write(hive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(destPath)) File.Delete(destPath); } catch (Exception) { /* a partial copy that cannot be removed is reported by the caller through the failure below */ }
            return Fail($"copia recuperată nu a putut fi scrisă: {ex.Message}");
        }
        return new(HiveReplayOutcome.Replayed, $"{applied} intrări aplicate, secvențele {first}-{last}", applied, first, last, notes, invalidStop);
    }

    private static void ApplyEntry(ref byte[] hive, byte[] log, Entry e)
    {
        long need = BaseBlock + (long)e.HiveBinsSize;
        if (need > hive.Length) Array.Resize(ref hive, checked((int)need));
        int refs = e.Offset + EntryHeader, data = refs + 8 * e.PageCount;
        for (int i = 0; i < e.PageCount; i++)
        {
            uint off = U32(log, refs + 8 * i), size = U32(log, refs + 8 * i + 4);
            Buffer.BlockCopy(log, data, hive, BaseBlock + (int)off, (int)size);
            data += (int)size;
        }
    }

    /// <summary>Collects the valid entries of a log, in file order, up to the first invalid one.</summary>
    private static void Scan(Log log, List<string> notes)
    {
        var d = log.Data;
        if (d.Length == 0) { log.Note = "gol"; notes.Add($"{log.Name}: gol"); return; }
        if (d.Length < Sector || Encoding.ASCII.GetString(d, 0, 4) != "regf") { Reject(log, notes, "antet invalid (lipsește semnătura regf)"); return; }
        uint type = U32(d, 0x1C);
        if (type == 1) { Reject(log, notes, "format vechi (DIRT, File type 1): nu este implementat"); return; }
        if (type != 6) { Reject(log, notes, $"File type {type} necunoscut (se aștepta 6 = jurnal în format nou)"); return; }
        if (d.Length >= 0x200 && Checksum(d) != U32(d, 0x1FC)) { Reject(log, notes, "suma de control a antetului este greșită"); return; }
        int off = Sector;
        while (off + 4 <= d.Length)
        {
            if (d.AsSpan(off, 4).IndexOfAnyExcept((byte)0) < 0) break;   // zero-filled tail: the end of the entries
            var why = Validate(d, off, out var e);
            if (why is not null)
            {
                if (off + 16 <= d.Length && Encoding.ASCII.GetString(d, off, 4) == "HvLE") log.RejectedSeq = U32(d, off + 12);
                notes.Add($"{log.Name}: intrarea de la 0x{off:X} respinsă: {why}; citirea jurnalului s-a oprit");
                return;
            }
            log.Entries.Add(e!);
            off += e!.Size;
        }
    }

    private static void Reject(Log log, List<string> notes, string why) { log.Invalid = true; log.Note = why; notes.Add($"{log.Name}: {why}"); }

    private static string? Validate(byte[] d, int off, out Entry? entry)
    {
        entry = null;
        if (off + EntryHeader > d.Length) return "intrare trunchiată (antetul depășește fișierul)";
        if (Encoding.ASCII.GetString(d, off, 4) != "HvLE") return "semnătura nu este HvLE";
        uint size = U32(d, off + 4);
        if (size < Sector || size % Sector != 0) return $"dimensiune invalidă ({size})";
        if (size > d.Length - off) return $"intrare trunchiată (dimensiunea {size} depășește fișierul)";
        uint hbds = U32(d, off + 16);
        if (hbds == 0 || hbds % 4096 != 0 || hbds > MaxHiveBytes) return $"Hive bins data size invalid ({hbds})";
        ulong h1 = BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(off + 24)), h2 = BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(off + 32));
        if (Marvin32(d.AsSpan(off + EntryHeader, (int)size - EntryHeader)) != h1) return "Hash-1 invalid";
        if (Marvin32(d.AsSpan(off, 32)) != h2) return "Hash-2 invalid";
        uint count = U32(d, off + 20);
        long refsEnd = EntryHeader + 8L * count;
        if (refsEnd > size) return $"referințele a {count} pagini depășesc intrarea";
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            uint po = U32(d, off + EntryHeader + 8 * i), ps = U32(d, off + EntryHeader + 8 * i + 4);
            if (ps == 0 || (long)po + ps > hbds) return $"pagina {i} (decalaj {po}, dimensiune {ps}) iese din hive bins data size {hbds}";
            total += ps;
        }
        if (refsEnd + total > size) return "paginile depășesc dimensiunea intrării";
        entry = new Entry(off, (int)size, U32(d, off + 12), hbds, U32(d, off + 8), (int)count);
        return null;
    }

    private static byte[] ReadAll(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > MaxHiveBytes) throw new InvalidDataException($"fișier prea mare ({fs.Length} octeți)");
        var b = new byte[fs.Length];
        fs.ReadExactly(b);
        return b;
    }

    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));

    /// <summary>XOR-32 of the first 508 bytes of the base block (spec, "Base block", Checksum).</summary>
    public static uint Checksum(byte[] baseBlock)
    {
        uint c = 0;
        for (int i = 0; i < 508; i += 4) c ^= BinaryPrimitives.ReadUInt32LittleEndian(baseBlock.AsSpan(i));
        // spec: a result of 0 is stored as 1 and 0xFFFFFFFF as 0xFFFFFFFE
        return c == 0 ? 1u : c == 0xFFFFFFFFu ? 0xFFFFFFFEu : c;
    }

    private static uint Rol(uint x, int n) => (x << n) | (x >> (32 - n));

    /// <summary>Marvin32 (N. Ferguson's block function: 20, 9, 27, 19 rotations) with the registry log seed; result = (p1 shl 32) or p0.</summary>
    public static ulong Marvin32(ReadOnlySpan<byte> data)
    {
        uint p0 = unchecked((uint)Seed), p1 = (uint)(Seed >> 32);
        static void Block(ref uint a, ref uint b)
        {
            b ^= a; a = Rol(a, 20); a += b; b = Rol(b, 9); b ^= a; a = Rol(a, 27); a += b; b = Rol(b, 19);
        }
        int n = data.Length / 4;
        for (int i = 0; i < n; i++) { p0 += BinaryPrimitives.ReadUInt32LittleEndian(data[(4 * i)..]); Block(ref p0, ref p1); }
        var rem = data[(4 * n)..];
        uint tail = 0x80u << (8 * rem.Length);
        for (int i = 0; i < rem.Length; i++) tail |= (uint)rem[i] << (8 * i);
        p0 += tail; Block(ref p0, ref p1); Block(ref p0, ref p1);
        return ((ulong)p1 << 32) | p0;
    }
}
