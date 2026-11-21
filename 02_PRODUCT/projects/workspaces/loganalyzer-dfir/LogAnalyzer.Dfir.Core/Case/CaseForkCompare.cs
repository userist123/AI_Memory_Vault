using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogAnalyzer.Dfir.Case;

/// <summary>How two copies of the same hash-chained log relate.</summary>
public enum ForkRelation
{
    /// <summary>Same chained entries (seq and hash) in both copies.</summary>
    Identical,
    /// <summary>Every entry of the first copy is in the second, which continues after it.</summary>
    FirstIsPrefix,
    /// <summary>Every entry of the second copy is in the first, which continues after it.</summary>
    SecondIsPrefix,
    /// <summary>The copies share entries 1..N-1 and differ from Seq N on: two histories grew from one point.</summary>
    Forked,
    /// <summary>The log is in only one of the two cases.</summary>
    OnlyInOne,
    /// <summary>No chained entry to compare (log absent in both, empty, or written before chaining).</summary>
    NotComparable,
}

/// <summary>Comparison of one chained log between two copies of a case.</summary>
/// <param name="Log">Path of the log inside the case (e.g. Logs/chain_of_custody.jsonl).</param>
/// <param name="ComparedFirst">Entries looked at in the first copy: only the verified part of its chain (up to the first break). Same for <paramref name="ComparedSecond"/>.</param>
/// <param name="ForkAtSeq">For <see cref="ForkRelation.Forked"/>: the first Seq whose entry differs.</param>
/// <param name="SameBytes">Both copies present and byte for byte equal. The chain covers only chained entries; lines written before chaining, line
/// endings and JSON formatting are outside it, so "identical chains" with different bytes is reported apart.</param>
public sealed record LogForkComparison(
    string Log, ForkRelation Relation, ChainVerification? First, ChainVerification? Second,
    int ComparedFirst, int ComparedSecond, long? ForkAtSeq, string FirstHashAtFork, string SecondHashAtFork, string Message, bool SameBytes = false);

/// <summary>The verdict for the two copies as a whole (all logs together).</summary>
public enum CaseForkVerdict
{
    /// <summary>Every chained log identical, byte for byte.</summary>
    Identical,
    /// <summary>Every chain identical, but some file differs outside its chain (lines written before chaining, line endings, formatting).</summary>
    IdenticalChainsDifferentFiles,
    /// <summary>The first copy lacks entries the second has, and has none the second lacks: it is an older copy.</summary>
    FirstOlder,
    /// <summary>The second copy lacks entries the first has, and has none the first lacks.</summary>
    SecondOlder,
    /// <summary>Each copy has entries the other lacks (in one log, or in different logs): both were worked on after a common point.</summary>
    Forked,
    /// <summary>Every compared log differs from its first entry: no common history (different cases, or one rebuilt).</summary>
    Unrelated,
    /// <summary>A chain is broken in at least one copy: tampering or damage comes before any comparison.</summary>
    Broken,
    /// <summary>No chained entry found anywhere.</summary>
    NothingComparable,
}

public sealed record CaseForkComparison(string First, string Second, string FirstCaseId, string SecondCaseId, IReadOnlyList<LogForkComparison> Logs)
{
    public bool SameCaseId => FirstCaseId.Length > 0 && string.Equals(FirstCaseId, SecondCaseId, StringComparison.Ordinal);
    public bool AnyBroken => Logs.Any(l => l.First?.Status == ChainStatus.Broken || l.Second?.Status == ChainStatus.Broken);

    /// <summary>Logs in which the first copy lacks entries the second has (it stops earlier, or the log exists only in the second).</summary>
    public IEnumerable<LogForkComparison> FirstLacks => Logs.Where(l => l.Relation == ForkRelation.FirstIsPrefix || (l.Relation == ForkRelation.OnlyInOne && l.First is null && l.ComparedSecond > 0));
    /// <summary>Logs in which the second copy lacks entries the first has.</summary>
    public IEnumerable<LogForkComparison> SecondLacks => Logs.Where(l => l.Relation == ForkRelation.SecondIsPrefix || (l.Relation == ForkRelation.OnlyInOne && l.Second is null && l.ComparedFirst > 0));

    public CaseForkVerdict Verdict
    {
        get
        {
            if (AnyBroken) return CaseForkVerdict.Broken;
            var forked = Logs.Where(l => l.Relation == ForkRelation.Forked).ToList();
            var shared = Logs.Any(l => l.Relation is ForkRelation.Identical or ForkRelation.FirstIsPrefix or ForkRelation.SecondIsPrefix);
            if (forked.Count > 0) return !shared && forked.All(l => l.ForkAtSeq == 1) ? CaseForkVerdict.Unrelated : CaseForkVerdict.Forked;
            bool firstLacks = FirstLacks.Any(), secondLacks = SecondLacks.Any();
            // review 2026-11-18: one log older in each direction is a fork of the case, never "an older copy"
            if (firstLacks && secondLacks) return CaseForkVerdict.Forked;
            if (firstLacks) return CaseForkVerdict.FirstOlder;
            if (secondLacks) return CaseForkVerdict.SecondOlder;
            var identical = Logs.Where(l => l.Relation == ForkRelation.Identical).ToList();
            if (identical.Count == 0) return CaseForkVerdict.NothingComparable;
            return identical.All(l => l.SameBytes) ? CaseForkVerdict.Identical : CaseForkVerdict.IdenticalChainsDifferentFiles;
        }
    }
}

/// <summary>
/// Planner #8 item 5 (G3): compares two copies of one case (two folders, two archives, or one of each) log by log, read-only.
/// For every hash-chained log in Logs/ it says whether the copies are identical, one is a prefix of the other (one copy was taken earlier),
/// or they forked at Seq N (both were written to after a common point: two working copies of one case). It never merges and never writes.
/// Only the plain chain is checked (seq, prevHash, hash): a copy carries no sealing key. A broken chain is compared only up to its break, and
/// the break is reported. An archive is read as it is; whether the archive is authentic is <c>verify-archive</c>'s job.
/// </summary>
public static class CaseForkCompare
{
    // both copies of a log are held in memory (bytes, text and lines): keep one log well under a gigabyte of working set
    private const long MaxLogBytes = 64L * 1024 * 1024;
    private const int MaxLogs = 64;

    public static CaseForkComparison CompareCases(string first, string second)
    {
        using var a = CaseSource.Open(first);
        using var b = CaseSource.Open(second);
        var names = a.ChainedLogNames().Union(b.ChainedLogNames(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count > MaxLogs) throw new InvalidDataException($"{names.Count} jurnale înlănțuite în Logs/, peste limita de {MaxLogs} a comparației");
        var logs = names.Select(n => CompareLog(n, a.Read(n), b.Read(n))).ToList();
        return new CaseForkComparison(first, second, a.CaseId(), b.CaseId(), logs);
    }

    /// <summary>Compares two copies of one log (null = absent in that copy).</summary>
    public static LogForkComparison CompareLog(string log, byte[]? first, byte[]? second)
    {
        var name = Path.GetFileName(log);
        var va = first is null ? null : HashChain.VerifyPlainChain(name, first);
        var vb = second is null ? null : HashChain.VerifyPlainChain(name, second);
        if (first is null && second is null)
            return new(log, ForkRelation.NotComparable, null, null, 0, 0, null, "", "", "jurnalul lipsește din ambele copii");
        if (first is null || second is null)
        {
            var present = first is null ? "a doua" : "prima";
            var count = (first is null ? vb : va)!.Entries;
            return new(log, ForkRelation.OnlyInOne, va, vb, first is null ? 0 : Verified(first, va!).Count, second is null ? 0 : Verified(second, vb!).Count, null, "", "",
                       $"jurnalul există doar în {present} copie ({LogAnalyzer.Dfir.Language.RoText.Count(count, "intrare", "intrări")} înlănțuite)");
        }
        var ea = Verified(first, va!);
        var eb = Verified(second, vb!);
        var note = BrokenNote(va!, "prima") + BrokenNote(vb!, "a doua");
        if (ea.Count == 0 && eb.Count == 0)
            return new(log, ForkRelation.NotComparable, va, vb, 0, 0, null, "", "", "nicio intrare înlănțuită de comparat" + note);
        var common = Math.Min(ea.Count, eb.Count);
        for (var i = 0; i < common; i++)
        {
            if (string.Equals(ea[i].Hash, eb[i].Hash, StringComparison.Ordinal)) continue;
            var seq = ea[i].Seq;
            return new(log, ForkRelation.Forked, va, vb, ea.Count, eb.Count, seq, ea[i].Hash, eb[i].Hash,
                       (i == 0 ? $"copiile diferă de la prima intrare (Seq {seq}): istorii fără punct comun"
                               : $"copiile au în comun Seq 1-{seq - 1} și se despart la Seq {seq}: ambele au fost scrise după acel punct")
                       + $"; prima are {ea.Count}, a doua {eb.Count} intrări" + note);
        }
        if (ea.Count == eb.Count)
        {
            var sameBytes = first.AsSpan().SequenceEqual(second);
            return new(log, ForkRelation.Identical, va, vb, ea.Count, eb.Count, null, "", "",
                       (sameBytes ? $"identice, octet cu octet ({LogAnalyzer.Dfir.Language.RoText.Count(ea.Count, "intrare", "intrări")})"
                                  : $"lanțul este identic ({LogAnalyzer.Dfir.Language.RoText.Count(ea.Count, "intrare", "intrări")}), dar fișierele diferă în afara lui: linii scrise înainte de înlănțuire, terminații de rând sau formatare")
                       + note, sameBytes);
        }
        return ea.Count < eb.Count
            ? new(log, ForkRelation.FirstIsPrefix, va, vb, ea.Count, eb.Count, null, "", "",
                  $"prima copie se oprește la Seq {ea.Count}; a doua o conține și continuă până la Seq {eb[^1].Seq} (prima este o copie mai veche)" + note)
            : new(log, ForkRelation.SecondIsPrefix, va, vb, ea.Count, eb.Count, null, "", "",
                  $"a doua copie se oprește la Seq {eb.Count}; prima o conține și continuă până la Seq {ea[^1].Seq} (a doua este o copie mai veche)" + note);
    }

    private static string BrokenNote(ChainVerification v, string which) => v.Status == ChainStatus.Broken
        ? $"; ATENȚIE: lanțul din {which} copie este rupt la Seq {v.BrokenAtSeq} ({v.Reason}), comparat doar până acolo"
        : "";

    /// <summary>The chained entries (seq, hash) of the verified part: the first <see cref="ChainVerification.Entries"/> chained lines, in order.</summary>
    private static List<(long Seq, string Hash)> Verified(byte[] content, ChainVerification v)
    {
        var list = new List<(long, string)>();
        if (v.Entries <= 0) return list;
        foreach (var line in HashChain.Lines(content))
        {
            if (list.Count >= v.Entries) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? o;
            bool chained;
            try { o = JsonNode.Parse(line) as JsonObject; chained = o is not null && o.ContainsKey("seq"); }
            catch (Exception ex) when (ex is JsonException or ArgumentException) { break; }
            if (!chained) continue;   // legacy lines before the chain
            try { list.Add((o!["seq"]!.GetValue<long>(), o["hash"]!.GetValue<string>())); }
            catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or FormatException or ArgumentException) { break; }
        }
        return list;
    }

    /// <summary>A case folder or a case archive (.zip, entries under case/), read-only.</summary>
    private abstract class CaseSource : IDisposable
    {
        public virtual void Dispose() { }

        public static CaseSource Open(string path)
        {
            if (Directory.Exists(path)) return new FolderSource(Path.GetFullPath(path));
            if (File.Exists(path)) return new ZipSource(Path.GetFullPath(path));
            throw new FileNotFoundException($"Nu există folderul sau arhiva cazului: {path}", path);
        }

        public abstract IEnumerable<string> ChainedLogNames();
        public abstract byte[]? Read(string log);
        protected abstract byte[]? ReadCaseJson();

        public string CaseId()
        {
            try
            {
                var bytes = ReadCaseJson();
                if (bytes is null) return "";
                var node = JsonNode.Parse(bytes) as JsonObject;
                return node?["CaseId"]?.GetValue<string>() ?? node?["caseId"]?.GetValue<string>() ?? "";
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException) { return ""; }
        }

        /// <summary>Only Logs/*.jsonl that carry a chain (a line with seq/hash) count; plain logs are not chains.</summary>
        protected static bool IsChained(byte[] content) =>
            HashChain.Lines(content).Any(l => l.Contains("\"seq\"", StringComparison.Ordinal) && l.Contains("\"hash\"", StringComparison.Ordinal));

        protected static void CheckSize(long length, string log)
        {
            if (length > MaxLogBytes) throw new InvalidDataException($"{log}: {length} octeți, peste limita de {MaxLogBytes / (1024 * 1024)} MB a comparației");
        }
    }

    private sealed class FolderSource(string root) : CaseSource
    {
        public override IEnumerable<string> ChainedLogNames()
        {
            var logs = Path.Combine(root, "Logs");
            if (!Directory.Exists(logs)) return [];
            return Directory.EnumerateFiles(logs, "*.jsonl", SearchOption.TopDirectoryOnly)
                .Where(f => Read("Logs/" + Path.GetFileName(f)) is { } c && IsChained(c))
                .Select(f => "Logs/" + Path.GetFileName(f)).ToList();
        }

        public override byte[]? Read(string log)
        {
            var path = Path.Combine(root, log.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return null;
            CheckSize(new FileInfo(path).Length, log);
            return HashChain.ReadAllShared(path);
        }

        protected override byte[]? ReadCaseJson()
        {
            var p = Path.Combine(root, "case.json");
            return File.Exists(p) ? HashChain.ReadAllShared(p) : null;
        }
    }

    private sealed class ZipSource(string zipPath) : CaseSource
    {
        private Dictionary<string, ZipArchiveEntry>? _index;
        private ZipArchive? _zip;

        private Dictionary<string, ZipArchiveEntry> Index()
        {
            if (_index is not null) return _index;
            _zip = ZipFile.OpenRead(zipPath);
            _index = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _zip.Entries)
            {
                var name = e.FullName.Replace('\\', '/');
                if (!name.StartsWith(CaseArchive.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                // two entries for one name (case or slash variants): which one is "the" log is undecidable; refuse instead of picking one
                if (!_index.TryAdd(name[CaseArchive.Prefix.Length..], e))
                    throw new InvalidDataException($"arhiva {Path.GetFileName(zipPath)} conține de două ori intrarea {name}; verificați-o cu verify-archive");
            }
            return _index;
        }

        public override IEnumerable<string> ChainedLogNames() =>
            Index().Keys.Where(k => k.StartsWith("Logs/", StringComparison.OrdinalIgnoreCase) && k.IndexOf('/', 5) < 0
                                    && k.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                        .Where(k => Read(k) is { } c && IsChained(c)).ToList();

        public override byte[]? Read(string log)
        {
            if (!Index().TryGetValue(log, out var e)) return null;
            CheckSize(e.Length, log);
            using var s = e.Open();
            using var ms = new MemoryStream();
            var buf = new byte[81920]; int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                // the declared size can lie: count what is actually decompressed
                if (ms.Length + n > MaxLogBytes) throw new InvalidDataException($"{log}: peste limita de {MaxLogBytes / (1024 * 1024)} MB a comparației");
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }

        public override void Dispose() => _zip?.Dispose();

        protected override byte[]? ReadCaseJson() => Read("case.json");
    }
}
