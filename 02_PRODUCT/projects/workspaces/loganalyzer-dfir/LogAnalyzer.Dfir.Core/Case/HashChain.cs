using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Case;

public enum ChainStatus { Valid, Broken, Legacy }

/// <summary>Result of verifying one hash-chained log (WP3a). <see cref="Legacy"/> is never reported as valid and never as an error.</summary>
public sealed record ChainVerification(string Log, ChainStatus Status, int Entries, int LegacyLines, long? BrokenAtSeq, string Reason, string Message);

public sealed record ChainReport(ChainVerification Custody, ChainVerification Audit);

/// <summary>The sealed log refuses to append because its sealing key cannot be read (the application was not started as administrator). A type, so no caller has to recognise the message.</summary>
public sealed class SealingKeyUnavailableException(string message) : UnauthorizedAccessException(message);

/// <summary>
/// Append-only JSONL log in which every line carries <c>seq</c>, <c>prevHash</c> and <c>hash</c>,
/// <c>hash = SHA-256(canonical JSON of the line without "hash")</c>; the first <c>prevHash</c> is 64 zeros.
/// Lines written before WP3 have no chain fields and are counted as legacy.
/// Limit: removing lines from the END of the log leaves a valid shorter chain; only an external anchor of the head hash detects that.
/// A log can be <b>sealed</b> (<see cref="RegisterHeadSeal"/>, used for the auth audit, security audit #4): every append then also writes
/// <c>&lt;log&gt;.head</c> = {seq, hash, mac = HMAC-SHA256(key, …)}, and <see cref="Verify"/> requires the chain to end exactly at that sealed head.
/// Someone without the key can still recompute the plain chain, but not the seal. An append that finds the file not ending at the sealed head
/// is still written (evidence is not dropped) but the seal is not moved over it, so the log stays broken until an administrator reseals it.
/// </summary>
/// <summary>
/// Anti-rollback witness of a sealed log, kept OUTSIDE the log folder (production: a file in %PROGRAMDATA%\LogAnalyzer\witness, Administrators/SYSTEM only; the registry is not allowed in the Standalone edition): the highest head (seq, hash, MAC) ever written. A head MAC has no
/// freshness, so replacing log + .head by an older valid copy verifies; the witness makes that visible. Residual: whoever can write the witness store
/// (an administrator) can delete or lower it.
/// </summary>
public interface IHeadWitness
{
    (long Seq, string Hash, string Mac)? Read();
    void Write(long seq, string hash, string mac);
}

public sealed class HashChain(string path)
{
    public const string LegacyMessage = "lanț neverificabil (caz creat înainte de WP3)";
    public static readonly string Genesis = new('0', 64);

    private readonly object _gate = new();

    public string Path { get; } = path;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]?> Seals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Seals the head of the log at <paramref name="logPath"/> for this process. A null key means "sealed, but the key cannot be read": appends are refused.</summary>
    public static void RegisterHeadSeal(string logPath, byte[]? key, IHeadWitness? witness = null)
    {
        var full = System.IO.Path.GetFullPath(logPath);
        Seals[full] = key;
        if (witness is null) Witnesses.TryRemove(full, out _); else Witnesses[full] = witness;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IHeadWitness> Witnesses = new(StringComparer.OrdinalIgnoreCase);

    private IHeadWitness? WitnessOf() => Witnesses.TryGetValue(System.IO.Path.GetFullPath(Path), out var w) ? w : null;

    private static string WitnessMac(byte[] key, long seq, string hash) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"LogAnalyzer-chain-witness-v1|{seq}|{hash}")));

    private static bool WitnessMacOk(byte[] key, (long Seq, string Hash, string Mac) w) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(w.Mac ?? ""), Encoding.ASCII.GetBytes(WitnessMac(key, w.Seq, w.Hash ?? "")));

    /// <summary>Append path: moves the witness forward only (never down, never over a tampered one). A failure to read or write it never fails the append.</summary>
    private void AdvanceWitness(byte[] key, long seq, string hash, FileStream fs, long prevSeq, string prevHash)
    {
        var w = WitnessOf();
        if (w is null) return;
        try
        {
            var cur = w.Read();
            if (cur is { } c)
            {
                if (c.Seq >= seq || !WitnessMacOk(key, c)) return;
                // a fork that outgrew the witness must not take it over: only a witness that this very log still contains moves forward (else Verify keeps reporting "rescris")
                if (!(c.Seq == prevSeq && c.Hash == prevHash))
                {
                    fs.Position = 0;
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    var lines = Lines(ms.ToArray());
                    // only over a plain-valid chain (review 2026-11-18: a crafted line with the witness's seq/hash must not let a fork take the witness over)
                    if (VerifyPlain(lines, exists: true).Status != ChainStatus.Valid || !ContainsIn(lines, c.Seq, c.Hash)) return;
                }
            }
            w.Write(seq, hash, WitnessMac(key, seq, hash));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) { }   // residual: a lagging witness
    }

    public static string HeadPath(string logPath) => logPath + ".head";

    private bool IsSealed(out byte[]? key) => Seals.TryGetValue(System.IO.Path.GetFullPath(Path), out key);

    private const string KeyUnavailable = "cheia de sigilare a jurnalului nu poate fi citită (porniți aplicația dintr-un cont de administrator Windows); nu s-a scris nimic";

    private static string Mac(byte[] key, long seq, string hash) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"LogAnalyzer-chain-head-v1|{seq}|{hash}")));

    private sealed record SealedHead(long Seq, string Hash, string Mac);

    /// <summary>Retry budget for sharing/lock violations (about 15-20 s with the growing pause below). Only ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION are retried.</summary>
    internal static int SharingAttempts = 400;

    /// <summary>Attempts for an "access denied" replace of the head (another reader without FileShare.Delete): ~2 s, then the append is rolled back.</summary>
    internal const int UnauthorizedAttempts = 60;

    internal static bool IsSharingViolation(IOException e) => (e.HResult & 0xFFFF) is 32 or 33;

    private static void SharingPause(int attempt) => Thread.Sleep(Math.Min(5 + attempt, 50));

    /// <summary>Text of the <c>.head</c> file, or null when there is none. Opened with FileShare.ReadWrite | Delete so the writer's replace succeeds while it is read.</summary>
    private string? ReadHeadText()
    {
        var hp = HeadPath(Path);
        for (int attempt = 0; ; attempt++)
        {
            if (!File.Exists(hp)) return null;
            try
            {
                using var fs = new FileStream(hp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException e) when (IsSharingViolation(e) && attempt < SharingAttempts) { SharingPause(attempt); }
        }
    }

    private static SealedHead? ParseHead(string? text)
    {
        if (text is null) return null;
        try
        {
            var o = JsonNode.Parse(text)!.AsObject();
            return new SealedHead(o["seq"]!.GetValue<long>(), o["hash"]!.GetValue<string>(), o["mac"]!.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NullReferenceException) { return new SealedHead(-1, "", ""); }
    }

    private SealedHead? ReadSealedHead() => ParseHead(ReadHeadText());

    // ---- intent of a sealed append (planner #8, 2026-11-17) ----
    // A process killed after writing its line and before moving the sealed head left the log one entry ahead of the seal: Verify said "entries added
    // after the last sealed head without the sealing key" for ever (card sign-in refused) although the key holder wrote it (multi-process kill test).
    // Before the line, the writer now records, MACed with the seal key, exactly which entry it is about to add after which sealed head. Only that entry,
    // directly after that head, is accepted as "seal move pending" and sealed by the next append. A line appended without the key has no such intent.

    public static string IntentPath(string logPath) => logPath + ".head.pending";

    // the announced entry's hash is NOT stored in clear (review 2026-11-17: from a clear hash an attacker without the key could rebuild a rolled-back entry by
    // brute force and revive it); only its MAC is stored, which nobody can check or forge without the key
    private sealed record SealIntent(long PrevSeq, string PrevHash, long Seq, string Mac);

    private static string IntentMac(byte[] key, long prevSeq, string prevHash, long seq, string hash) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"LogAnalyzer-chain-intent-v1|{prevSeq}|{prevHash}|{seq}|{hash}")));

    private string? ReadIntentText()
    {
        var ip = IntentPath(Path);
        for (int attempt = 0; ; attempt++)
        {
            if (!File.Exists(ip)) return null;
            try
            {
                using var fs = new FileStream(ip, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException e) when (IsSharingViolation(e) && attempt < SharingAttempts) { SharingPause(attempt); }
        }
    }

    private static SealIntent? ParseIntent(string? text)
    {
        if (text is null) return null;
        try
        {
            var o = JsonNode.Parse(text)!.AsObject();
            return new SealIntent(o["prevSeq"]!.GetValue<long>(), o["prevHash"]!.GetValue<string>(), o["seq"]!.GetValue<long>(), o["mac"]!.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NullReferenceException) { return null; }
    }

    private void WriteIntent(byte[] key, long prevSeq, string prevHash, long seq, string hash)
    {
        var o = new JsonObject { ["prevSeq"] = prevSeq, ["prevHash"] = prevHash, ["seq"] = seq, ["mac"] = IntentMac(key, prevSeq, prevHash, seq, hash) };
        var bytes = Encoding.UTF8.GetBytes(o.ToJsonString(Json.Line));
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(IntentPath(Path), FileMode.Create, FileAccess.Write, FileShare.Read);
                fs.Write(bytes);
                fs.Flush(true);   // durable before the line it announces
                return;
            }
            catch (IOException e) when (IsSharingViolation(e) && attempt < SharingAttempts) { SharingPause(attempt); }   // a scanner holding the file only delays
        }
    }

    private void DeleteIntent() { try { File.Delete(IntentPath(Path)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    /// <summary>
    /// Whether the log's last entry (<paramref name="seq"/>, <paramref name="head"/>, linked by <paramref name="lastPrevHash"/>) is exactly the entry a key holder
    /// announced right after the valid sealed head <paramref name="h"/>: the seal move of that append is pending (a crash), not an entry added without the key.
    /// </summary>
    private static bool IntentCovers(byte[] key, SealIntent? i, SealedHead? h, long seq, string head, string? lastPrevHash)
    {
        if (i is null) return false;
        // the sealed predecessor: a valid sealed head, or (first sealed append, review 2026-11-17) no head at all and the empty chain
        var (prevSeq, prevHash) = h is null ? (0L, Genesis) : (h.Seq, h.Hash);
        if (h is not null && !SealMatches(key, h, h.Seq, h.Hash)) return false;
        return seq == prevSeq + 1 && i.PrevSeq == prevSeq && i.PrevHash == prevHash && i.Seq == seq && lastPrevHash == prevHash
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(i.Mac), Encoding.ASCII.GetBytes(IntentMac(key, prevSeq, prevHash, seq, head)));
    }

    private static string? PrevHashOf(string? line)
    {
        if (line is null) return null;
        try { return (JsonNode.Parse(line) as JsonObject)?["prevHash"]?.GetValue<string>(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>
    /// Test-only fault injection for multi-process crash tests. Off by default (only LogAnalyzer.Dfir.ChainWorker turns it on): when off, no environment variable is read
    /// and <see cref="Append"/> behaves exactly as without it. LA_CHAIN_FAULT=&lt;point&gt;[:&lt;n&gt;] kills the process (FailFast) at the n-th append of this process (default 1).
    /// Points: after-intent, after-line-flush, after-head-tmp, after-head-move.
    /// </summary>
    internal static bool FaultInjection;
    private static int _appendNo;

    private static void Fault(string point, int appendNo)
    {
        if (!FaultInjection) return;
        var spec = Environment.GetEnvironmentVariable("LA_CHAIN_FAULT");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split(':', 2);
        int n = parts.Length > 1 && int.TryParse(parts[1], out var v) ? v : 1;
        if (parts[0] == point && appendNo == n) Environment.FailFast("fault injection: " + point);
    }

    private void WriteSealedHead(byte[] key, long seq, string hash, int faultAppendNo = 0)
    {
        var o = new JsonObject { ["seq"] = seq, ["hash"] = hash, ["mac"] = Mac(key, seq, hash) };
        var tmp = HeadPath(Path) + ".tmp";
        using (var t = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read))   // as File.WriteAllText did: a reader of the tmp does not fail the append
        {
            t.Write(Encoding.UTF8.GetBytes(o.ToJsonString(Json.Line)));
            t.Flush(true);   // durable before it replaces the head: a power loss must not leave a torn .head ("sigiliu falsificat")
        }
        Fault("after-head-tmp", faultAppendNo);
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, HeadPath(Path), overwrite: true); Fault("after-head-move", faultAppendNo); return; }
            // Windows reports a replace of a file that another handle holds without FileShare.Delete as "access denied": retried only while the target is a
            // writable file, and only ~2 s (review 2026-11-17: a persistent deny would otherwise hold the log, and every other instance's sign-in, ~19 s)
            catch (UnauthorizedAccessException) when (attempt < UnauthorizedAttempts && File.Exists(HeadPath(Path))
                                                       && !File.GetAttributes(HeadPath(Path)).HasFlag(FileAttributes.ReadOnly)) { SharingPause(attempt); }
            catch (IOException e) when (IsSharingViolation(e) && attempt < SharingAttempts) { SharingPause(attempt); }
        }
    }

    private static bool SealMatches(byte[] key, SealedHead? h, long seq, string head) =>
        h is null ? seq == 0
                  : h.Seq == seq && h.Hash == head && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h.Mac), Encoding.ASCII.GetBytes(Mac(key, seq, head)));

    /// <summary>
    /// Seals the head as the log is now (key required). Used once when the key is first created (the existing chain is trusted on first use) and by an
    /// audited administrator decision after a break. Refuses when the plain chain itself is broken.
    /// </summary>
    public void Reseal()
    {
        lock (_gate)
        {
            if (!IsSealed(out var key)) throw new InvalidOperationException("jurnalul nu este sigilat");
            if (key is null) throw new SealingKeyUnavailableException(KeyUnavailable);
            if (VerifyPlain().Status == ChainStatus.Broken) throw new InvalidOperationException("lanțul simplu este rupt; nu se sigilează");
            var (seq, head) = Head();
            if (seq > 0)
            {
                WriteSealedHead(key, seq, head);
                WitnessOf()?.Write(seq, head, WitnessMac(key, seq, head));   // the audited recovery: may go DOWN (a failure is raised, the break stays visible)
            }
        }
    }

    /// <summary>
    /// Appends <paramref name="entry"/> (any serializable object) with chain fields. The head is re-read from the file under an
    /// exclusive lock on every append, so several workspaces (or processes) writing the same case continue one chain instead of forking it.
    /// </summary>
    public void Append(object entry)
    {
        lock (_gate)
        {
            bool sealedLog = IsSealed(out var key);
            if (sealedLog && key is null) throw new SealingKeyUnavailableException(KeyUnavailable);
            using var fs = OpenExclusive();
            var (seq, head) = ReadHead(fs);
            int appendNo = FaultInjection ? Interlocked.Increment(ref _appendNo) : 0;
            bool moveSeal = false;
            if (sealedLog)
            {
                var sealedHead = ReadSealedHead();
                moveSeal = SealMatches(key!, sealedHead, seq, head);
                // a previous append of a key holder was killed between its line and its head move: finish that head move (only for the announced entry)
                if (!moveSeal && IntentCovers(key!, ParseIntent(ReadIntentText()), sealedHead, seq, head, PrevHashOf(SharedAppend.LastNonEmptyLine(fs))))
                {
                    WriteSealedHead(key!, seq, head);
                    AdvanceWitness(key!, seq, head, fs, -1, "");
                    DeleteIntent();
                    moveSeal = true;
                }
            }
            var obj = JsonSerializer.SerializeToNode(entry, Json.Line)!.AsObject();
            obj["seq"] = seq + 1;
            obj["prevHash"] = head;
            obj["hash"] = HashOf(obj);
            var bytes = Encoding.UTF8.GetBytes(obj.ToJsonString(Json.Line) + "\n");
            if (moveSeal)
            {
                WriteIntent(key!, seq, head, seq + 1, obj["hash"]!.GetValue<string>());
                Fault("after-intent", appendNo);
            }
            long lengthBefore = fs.Seek(0, SeekOrigin.End);
            fs.Write(bytes);
            fs.Flush(true);
            Fault("after-line-flush", appendNo);
            if (moveSeal)
            {
                try { WriteSealedHead(key!, seq + 1, obj["hash"]!.GetValue<string>(), appendNo); }
                catch (Exception moveError)
                {
                    // all-or-nothing: never leave the log ahead of its seal because the head could not be moved
                    try
                    {
                        fs.SetLength(lengthBefore);
                        fs.Flush(true);
                    }
                    catch (Exception rollbackError) { throw new AggregateException(moveError, rollbackError); }   // keep the original cause
                    try { File.Delete(HeadPath(Path) + ".tmp"); } catch { }
                    DeleteIntent();
                    throw;
                }
                DeleteIntent();
                AdvanceWitness(key!, seq + 1, obj["hash"]!.GetValue<string>(), fs, seq, head);
            }
        }
    }

    /// <summary>Seq and hash of the last chained line (<c>(0, Genesis)</c> when empty or legacy). Read-only: a writer holding the file only delays it.</summary>
    public (long Seq, string Head) Head()
    {
        lock (_gate)
        {
            if (!File.Exists(Path)) return (0, Genesis);
            using var turn = Turnstile.Enter(Path);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return ReadHead(fs);
                }
                catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
            }
        }
    }

    /// <summary>Whether an entry with this <paramref name="seq"/> and <paramref name="hash"/> is on the chain (a copy of the head held outside the case).</summary>
    /// <summary>
    /// Reads a whole chained log that a writer may hold exclusively (<see cref="Append"/> opens it with FileShare.None) — retried with a growing pause,
    /// about 15 s in all (CI 2026-11-08: a busy writer kept a 2-second reader out on a slow runner). A file that disappears is not waited for.
    /// </summary>
    public static byte[] ReadAllShared(string path)
    {
        using var turn = Turnstile.Enter(path);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (IOException) when (attempt < 400 && File.Exists(path)) { Thread.Sleep(Math.Min(5 + attempt, 50)); }
        }
    }

    public bool Contains(long seq, string hash) => File.Exists(Path) && ContainsIn(SharedRead.Lines(Path), seq, hash);

    private static bool ContainsIn(IEnumerable<string> lines, long seq, string hash)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var o = (JsonNode.Parse(line) as JsonObject ?? throw new JsonException("linia nu este un obiect JSON"));
                if (o["seq"] is { } s && s.GetValue<long>() == seq) return o["hash"] is { } h && string.Equals(h.GetValue<string>(), hash, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return false;
    }

    private FileStream OpenExclusive()
    {
        using var turn = Turnstile.Enter(Path);   // a waiting reader holds the turnstile: no new writer gets in before it (CI 2026-11-18: readers starved)
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 400) { Thread.Sleep(Math.Min(5 + attempt, 50)); }   // another writer holds the file; wait up to ~15 s (CI 2026-11-08: 2 s was not enough on a busy runner)
        }
    }

    /// <summary>
    /// Fairness between the writers of one log and its readers (CI 2026-11-18: an append loop re-took the exclusive handle right after releasing it, so a polling
    /// reader of the same log never got in within its ~20 s budget). Whoever wants to OPEN the log (writer or reader) passes a named mutex keyed by the log path;
    /// the mutex is held only while opening (a writer) or reading (a reader), never while a writer holds the file, so it cannot deadlock against the file handle.
    /// Waiters on a mutex are served roughly in order, so a waiting reader is let in after the current writer instead of after an unbounded run of writers.
    /// Best effort: if the mutex cannot be created or is not obtained in time, the old retry loop runs unchanged (never a failure, only the old starvation risk).
    /// </summary>
    internal sealed class Turnstile : IDisposable
    {
        private Mutex? _m;
        private Turnstile(Mutex m) => _m = m;

        /// <summary>
        /// Short on purpose (review 2026-11-18): the mutex name follows from the log path, so any process of the session can create and hold it. A legitimate holder
        /// keeps it only for one open (milliseconds); after a timeout the turnstile is skipped for that log for <see cref="Backoff"/>, so a hostile holder costs at
        /// most ~2 s per minute per log (the old retry loop then runs as before), never a failure.
        /// </summary>
        internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);
        internal static readonly TimeSpan Backoff = TimeSpan.FromSeconds(60);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> SkipUntil = new(StringComparer.Ordinal);

        public static Turnstile? Enter(string logPath)
        {
            Mutex? m = null;
            try
            {
                var name = @"Local\LogAnalyzer-chain-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(logPath).ToLowerInvariant())))[..32];
                if (SkipUntil.TryGetValue(name, out var until) && Environment.TickCount64 < until) return null;
                m = new Mutex(false, name);
                bool owned;
                try { owned = m.WaitOne(Wait); }
                catch (AbandonedMutexException) { owned = true; }   // a holder died: the mutex is ours, it guards no data
                if (owned) return new Turnstile(m);
                SkipUntil[name] = Environment.TickCount64 + (long)Backoff.TotalMilliseconds;
                m.Dispose();
                return null;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException or ArgumentException)
            {
                m?.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            var m = Interlocked.Exchange(ref _m, null);
            if (m is null) return;
            try { m.ReleaseMutex(); } catch (ApplicationException) { }
            m.Dispose();
        }
    }

    /// <summary>Seq and hash of the last chained line; a legacy or unreadable last line starts a fresh chain (Verify() reports it).</summary>
    private static (long Seq, string Head) ReadHead(FileStream fs)
    {
        var last = LogAnalyzer.Dfir.IO.SharedAppend.LastNonEmptyLine(fs);
        if (last is null) return (0, Genesis);
        try
        {
            var o = (JsonNode.Parse(last) as JsonObject ?? throw new JsonException("linia nu este un obiect JSON"));
            if (o["seq"] is { } s && o["hash"] is { } h) return (s.GetValue<long>(), h.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        return (0, Genesis);
    }

    /// <summary>The plain chain check and, for a sealed log, that the chain ends exactly at the sealed head.</summary>
    public ChainVerification Verify() => ReadVerified().Verification;

    /// <summary>
    /// Reads the log bytes and (for a sealed log) the <c>.head</c> text under ONE read handle of the log (writers are excluded for that moment, so an append and its
    /// head move cannot fall between the two reads) and verifies exactly those bytes. A missing log is empty.
    /// </summary>
    public (byte[] Content, ChainVerification Verification) ReadVerified()
    {
        bool exists = false;
        byte[] content = [];
        string? headText = null, intentText = null;
        WitnessRead witnessRead = default;
        bool sealedLog = IsSealed(out _);
        using (var turn = Turnstile.Enter(Path))
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                content = ms.ToArray();
                exists = true;
                // the witness too is read while the log is held (review 2026-11-18: read after the handle, a concurrent append + witness move gave a false "copie mai veche")
                if (sealedLog) { headText = ReadHeadText(); intentText = ReadIntentText(); witnessRead = ReadWitness(); }
                break;
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            catch (IOException e) when (IsSharingViolation(e) && attempt < SharingAttempts) { SharingPause(attempt); }
        }
        if (!exists && sealedLog) { headText = ReadHeadText(); intentText = ReadIntentText(); witnessRead = ReadWitness(); }
        return (content, VerifyCore(content, exists, ParseHead(headText), ParseIntent(intentText), witnessRead));
    }

    /// <summary>
    /// The same check on <paramref name="content"/>, the bytes of the log read once by the caller (who then reads the records from the same bytes, so the answer
    /// and the records are about one and the same content). The sealed head is read from its own file AFTER the caller read the bytes, so it is NOT atomic with
    /// them: on a sealed log that other processes append to, use <see cref="ReadVerified"/>.
    /// </summary>
    public ChainVerification Verify(byte[] content) => IsSealed(out _)
        ? VerifyCore(content, content.Length > 0, ParseHead(ReadHeadText()), ParseIntent(ReadIntentText()), ReadWitness())
        : VerifyCore(content, content.Length > 0, null, null, default);

    private ChainVerification VerifyCore(byte[] content, bool exists, SealedHead? sealedHead, SealIntent? intent, WitnessRead witnessRead)
    {
        var lines = Lines(content);
        (long, string) HeadOf()
        {
            var last = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (last is null) return (0, Genesis);
            try
            {
                var o = (JsonNode.Parse(last) as JsonObject ?? throw new JsonException("linia nu este un obiect JSON"));
                if (o["seq"] is { } s && o["hash"] is { } h) return (s.GetValue<long>(), h.GetValue<string>());
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            return (0, Genesis);
        }
        return Verify(VerifyPlain(lines, exists), HeadOf, (seq, hash) => ContainsIn(lines, seq, hash), sealedHead, intent,
                      PrevHashOf(lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))), witnessRead);
    }

    /// <summary>The lines of a log held in memory, split as <see cref="Verify(byte[])"/> splits them: exactly as File.ReadLines splits the file on disk (the integrity re-check): a leading UTF-8 BOM is dropped and a line ends at \r\n, \r or \n.</summary>
    public static List<string> Lines(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        return System.Text.RegularExpressions.Regex.Split(text, "\r\n|\r|\n").ToList();
    }
    /// <summary>What reading the witness gave: not registered / absent (Value null, Error null), a value, or an error (present but unreadable).</summary>
    private readonly record struct WitnessRead(bool Registered, (long Seq, string Hash, string Mac)? Value, string? Error);

    private WitnessRead ReadWitness()
    {
        if (WitnessOf() is not { } w) return default;
        try { return new WitnessRead(true, w.Read(), null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) { return new WitnessRead(true, null, e.Message); }
    }

    private ChainVerification Verify(ChainVerification plain, Func<(long Seq, string Head)> headOf, Func<long, string, bool> contains, SealedHead? h, SealIntent? intent, string? lastPrevHash, WitnessRead witnessRead)
    {
        var r = VerifySealed(plain, headOf, contains, h, intent, lastPrevHash);
        if (r.Status != ChainStatus.Valid || !IsSealed(out var key) || key is null || !witnessRead.Registered) return r;
        var name = System.IO.Path.GetFileName(Path);
        var (seq, _) = headOf();
        // present but unreadable is a problem to see, never a silent "valid" (review 2026-11-18)
        if (witnessRead.Error is { } err) return Broken(name, r.Entries, r.LegacyLines, seq, $"martorul anti-revenire nu poate fi citit: {err}");
        if (witnessRead.Value is not { } wv) return r;   // absent: unchanged (residual: an administrator can delete it)
        if (!WitnessMacOk(key, wv)) return Broken(name, r.Entries, r.LegacyLines, seq, "martorul anti-revenire nu este valid (modificat în afara aplicației)");
        if (wv.Seq > seq) return Broken(name, r.Entries, r.LegacyLines, seq, $"jurnalul a fost înlocuit cu o copie mai veche: martorul anti-revenire indică Seq {wv.Seq}, jurnalul se oprește la Seq {seq}");
        if (!contains(wv.Seq, wv.Hash)) return Broken(name, r.Entries, r.LegacyLines, wv.Seq, $"jurnalul nu mai conține intrarea Seq {wv.Seq} confirmată de martorul anti-revenire: rescris");
        return r;
    }

    private ChainVerification VerifySealed(ChainVerification plain, Func<(long Seq, string Head)> headOf, Func<long, string, bool> contains, SealedHead? h, SealIntent? intent, string? lastPrevHash)
    {
        if (!IsSealed(out var key) || plain.Status == ChainStatus.Broken) return plain;
        var name = System.IO.Path.GetFileName(Path);
        if (key is null) return Broken(name, plain.Entries, plain.LegacyLines, 0, KeyUnavailable.Replace("; nu s-a scris nimic", ""));
        var (seq, head) = headOf();
        if (seq == 0 && h is null) return plain;
        if (h is null && IntentCovers(key, intent, null, seq, head, lastPrevHash)) return plain;   // first sealed append cut by a crash
        if (h is null) return Broken(name, plain.Entries, plain.LegacyLines, seq, "capul lanțului nu are sigiliu (fișierul .head lipsește)");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h.Mac), Encoding.ASCII.GetBytes(Mac(key, h.Seq, h.Hash))))
            return Broken(name, plain.Entries, plain.LegacyLines, h.Seq, "sigiliul capului lanțului nu este valid (lanț recalculat sau sigiliu falsificat)");
        if (h.Seq == seq && h.Hash == head) return plain;
        // the key holder's own last entry whose seal move was cut by a crash (announced in the MACed intent): valid, the next append moves the seal
        if (IntentCovers(key, intent, h, seq, head, lastPrevHash)) return plain;
        if (seq > h.Seq && contains(h.Seq, h.Hash))
            return Broken(name, plain.Entries, plain.LegacyLines, h.Seq + 1, $"intrări adăugate după ultimul cap sigilat (Seq {h.Seq}) fără cheia de sigilare");
        return Broken(name, plain.Entries, plain.LegacyLines, Math.Min(seq, h.Seq), $"lanțul nu mai ajunge la capul sigilat (Seq {h.Seq}): trunchiat sau rescris");
    }

    /// <summary>Only the plain chain (seq, prevHash, hash of every entry), without the seal: what an external anchor relies on.</summary>
    public ChainVerification VerifyPlainChain() => VerifyPlain();

    private ChainVerification VerifyPlain() =>
        File.Exists(Path) ? VerifyPlain(SharedRead.Lines(Path), exists: true) : VerifyPlain([], exists: false);

    private ChainVerification VerifyPlain(IEnumerable<string> lines, bool exists)
    {
        var name = System.IO.Path.GetFileName(Path);
        if (!exists) return new(name, ChainStatus.Valid, 0, 0, null, "", "gol");
        int legacy = 0, chained = 0, lineNo = 0;
        long expectSeq = 1; string expectPrev = Genesis;
        foreach (var line in lines)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject o;
            try { o = (JsonNode.Parse(line) as JsonObject ?? throw new JsonException("linia nu este un obiect JSON")); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            { return Broken(name, chained, legacy, expectSeq, $"linia {lineNo} nu este JSON valid"); }
            bool hasChain = o.ContainsKey("seq") || o.ContainsKey("hash") || o.ContainsKey("prevHash");
            if (!hasChain)
            {
                if (chained > 0) return Broken(name, chained, legacy, expectSeq, $"linia {lineNo} fără câmpuri de lanț după intrări înlănțuite");
                legacy++; continue;
            }
            long seq; string prev, hash;
            try { seq = o["seq"]!.GetValue<long>(); prev = o["prevHash"]!.GetValue<string>(); hash = o["hash"]!.GetValue<string>(); }
            catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or FormatException)
            { return Broken(name, chained, legacy, expectSeq, $"linia {lineNo}: câmpuri de lanț lipsă sau malformate"); }
            if (seq != expectSeq) return Broken(name, chained, legacy, expectSeq, $"secvență neașteptată: găsit {seq}, așteptat {expectSeq} (intrare ștearsă sau reordonată)");
            if (prev != expectPrev) return Broken(name, chained, legacy, seq, "prevHash nu corespunde intrării anterioare");
            o.Remove("hash");
            if (!string.Equals(HashOf(o), hash, StringComparison.Ordinal)) return Broken(name, chained, legacy, seq, "hash-ul intrării nu corespunde conținutului (intrare modificată)");
            chained++; expectSeq = seq + 1; expectPrev = hash;
        }
        if (chained == 0 && legacy > 0) return new(name, ChainStatus.Legacy, 0, legacy, null, "", LegacyMessage);
        var msg = legacy > 0 ? $"{LogAnalyzer.Dfir.Language.RoText.Count(chained, "intrare", "intrări")} verificate; {legacy} linii anterioare: {LegacyMessage}" : $"{LogAnalyzer.Dfir.Language.RoText.Count(chained, "intrare", "intrări")} verificate";
        return new(name, ChainStatus.Valid, chained, legacy, null, "", msg);
    }

    private static ChainVerification Broken(string name, int entries, int legacy, long seq, string reason) =>
        new(name, ChainStatus.Broken, entries, legacy, seq, reason, $"lanț rupt la Seq {seq}: {reason}");

    /// <summary>SHA-256 (lowercase hex) of the canonical form: compact JSON, object keys sorted ordinally, "hash" excluded.</summary>
    internal static string HashOf(JsonObject withoutHash)
    {
        using var doc = JsonDocument.Parse(withoutHash.ToJsonString(Json.Line));
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false, Encoder = Json.Line.Encoder })) Canonical(doc.RootElement, w);
        return Convert.ToHexStringLower(SHA256.HashData(ms.ToArray()));
    }

    private static void Canonical(JsonElement e, Utf8JsonWriter w)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { w.WritePropertyName(p.Name); Canonical(p.Value, w); }
                w.WriteEndObject(); break;
            case JsonValueKind.Array:
                w.WriteStartArray(); foreach (var i in e.EnumerateArray()) Canonical(i, w); w.WriteEndArray(); break;
            default: e.WriteTo(w); break;
        }
    }
}
