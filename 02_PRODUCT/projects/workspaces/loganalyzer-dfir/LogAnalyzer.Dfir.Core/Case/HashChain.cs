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

/// <summary>
/// Append-only JSONL log in which every line carries <c>seq</c>, <c>prevHash</c> and <c>hash</c>,
/// <c>hash = SHA-256(canonical JSON of the line without "hash")</c>; the first <c>prevHash</c> is 64 zeros.
/// Lines written before WP3 have no chain fields and are counted as legacy.
/// Limit: removing lines from the END of the log leaves a valid shorter chain; only an external anchor of the head hash detects that.
/// </summary>
public sealed class HashChain(string path)
{
    public const string LegacyMessage = "lanț neverificabil (caz creat înainte de WP3)";
    public static readonly string Genesis = new('0', 64);

    private readonly object _gate = new();

    public string Path { get; } = path;

    /// <summary>
    /// Appends <paramref name="entry"/> (any serializable object) with chain fields. The head is re-read from the file under an
    /// exclusive lock on every append, so several workspaces (or processes) writing the same case continue one chain instead of forking it.
    /// </summary>
    public void Append(object entry)
    {
        lock (_gate)
        {
            using var fs = OpenExclusive();
            var (seq, head) = ReadHead(fs);
            var obj = JsonSerializer.SerializeToNode(entry, Json.Line)!.AsObject();
            obj["seq"] = seq + 1;
            obj["prevHash"] = head;
            obj["hash"] = HashOf(obj);
            var bytes = Encoding.UTF8.GetBytes(obj.ToJsonString(Json.Line) + "\n");
            fs.Seek(0, SeekOrigin.End);
            fs.Write(bytes);
            fs.Flush(true);
        }
    }

    /// <summary>Seq and hash of the last chained line (<c>(0, Genesis)</c> when empty or legacy). Read-only: a writer holding the file only delays it.</summary>
    public (long Seq, string Head) Head()
    {
        lock (_gate)
        {
            if (!File.Exists(Path)) return (0, Genesis);
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
    public bool Contains(long seq, string hash)
    {
        if (!File.Exists(Path)) return false;
        foreach (var line in File.ReadLines(Path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var o = JsonNode.Parse(line)!.AsObject();
                if (o["seq"] is { } s && s.GetValue<long>() == seq) return o["hash"] is { } h && string.Equals(h.GetValue<string>(), hash, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return false;
    }

    private FileStream OpenExclusive()
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(20); }   // another writer holds the file; wait up to ~2 s
        }
    }

    /// <summary>Seq and hash of the last chained line; a legacy or unreadable last line starts a fresh chain (Verify() reports it).</summary>
    private static (long Seq, string Head) ReadHead(FileStream fs)
    {
        var last = LastNonEmptyLine(fs);
        if (last is null) return (0, Genesis);
        try
        {
            var o = JsonNode.Parse(last)!.AsObject();
            if (o["seq"] is { } s && o["hash"] is { } h) return (s.GetValue<long>(), h.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        return (0, Genesis);
    }

    private static string? LastNonEmptyLine(FileStream fs)
    {
        long end = fs.Length;
        if (end == 0) return null;
        var buffer = new List<byte>();
        var chunk = new byte[4096];
        long pos = end;
        while (pos > 0)
        {
            int n = (int)Math.Min(chunk.Length, pos);
            pos -= n;
            fs.Seek(pos, SeekOrigin.Begin);
            fs.ReadExactly(chunk, 0, n);
            buffer.InsertRange(0, chunk.Take(n));
            var text = Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r', '\n', ' ', '\t');
            int nl = text.LastIndexOf('\n');
            if (nl >= 0) return text[(nl + 1)..].TrimEnd('\r');
            if (pos == 0) return text.Length > 0 ? text : null;
        }
        return null;
    }

    public ChainVerification Verify()
    {
        var name = System.IO.Path.GetFileName(Path);
        if (!File.Exists(Path)) return new(name, ChainStatus.Valid, 0, 0, null, "", "gol");
        int legacy = 0, chained = 0, lineNo = 0;
        long expectSeq = 1; string expectPrev = Genesis;
        foreach (var line in File.ReadLines(Path))
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject o;
            try { o = JsonNode.Parse(line)!.AsObject(); }
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
        var msg = legacy > 0 ? $"{chained} intrări verificate; {legacy} linii anterioare: {LegacyMessage}" : $"{chained} intrări verificate";
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
