using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogAnalyzer.Dfir.Case;

/// <summary>The anchor of a hash-chained log at one entry: chain id (from entry 1), seq, head hash and the short human code.</summary>
public sealed record AuditAnchorPoint(string ChainId, long Seq, string Hash, string Code)
{
    /// <summary>"Cod de ancorare: XXXXX-XXXXX-XXXXX-XXXXX (înregistrarea nr. N)", as printed on the report.</summary>
    public string Line => $"Cod de ancorare: {Code} (înregistrarea nr. {Seq})";
}

public enum AnchorContinuityStatus { NotGiven, Continuous, Rewritten, InvalidCode, Unreadable }

/// <summary>Result of checking a previous anchor code against the current chain (check A11).</summary>
public sealed record AnchorContinuity(AnchorContinuityStatus Status, string Detail, string GivenCode, long? MatchedSeq);

/// <summary>Files written by <see cref="AuditAnchor.Export"/> for burning on write-once media.</summary>
public sealed record AnchorExport(string Folder, string LogCopy, string AnchorFile, string LogSha256, AuditAnchorPoint Anchor);

/// <summary>
/// External anchoring of the application's sealed audit log (owner decision 2026-10-16). The local seal (HMAC with a machine key) stops someone
/// without administrator rights, but a local administrator can read that key, rewrite the log and reseal it. The anchor is the countermeasure: at
/// every control the head of the chain is turned into a short code printed on the signed paper report (proces-verbal) and the full log is copied to
/// write-once media; at the next control the code is typed back and the application checks that the current chain still contains that exact entry.
/// A rewrite of anything up to that entry changes its hash (every hash covers all earlier entries), so the code no longer matches.
/// Code: 19 base32 characters of SHA-256("LogAnalyzer-audit-anchor-v1|chainId|seq|hash") + 1 check character, as 4 groups of 5.
/// </summary>
public static class AuditAnchor
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // 32 symbols, no I/O/0/1 (read back from paper)

    /// <summary>Chain id: first 16 hex characters of the hash of entry 1 (a log started again from zero gets another id).</summary>
    public static string ChainIdOf(string firstEntryHash) => firstEntryHash.Length >= 16 ? firstEntryHash[..16].ToLowerInvariant() : firstEntryHash.ToLowerInvariant();

    public static string Code(string chainId, long seq, string hash)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"LogAnalyzer-audit-anchor-v1|{chainId}|{seq}|{hash.ToLowerInvariant()}"));
        var body = Base32(digest, 19);
        return Group(body + Check(body));
    }

    /// <summary>Normalises a typed code (spaces, dashes, lower case, O→0-like confusions are NOT guessed) and checks its check character.</summary>
    public static bool TryNormalize(string? input, out string code)
    {
        code = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var raw = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());
        if (raw.Length != 20 || raw.Any(c => Alphabet.IndexOf(c) < 0)) return false;
        if (Check(raw[..19]) != raw[19]) return false;
        code = Group(raw);
        return true;
    }

    /// <summary>The anchor of the head of the log now; null when the log is empty or has no chained entries.</summary>
    public static AuditAnchorPoint? Current(string logPath)
    {
        var entries = Entries(logPath);
        if (entries.Count == 0) return null;
        var chainId = ChainIdOf(entries[0].Hash);
        var (seq, hash) = entries[^1];
        return new AuditAnchorPoint(chainId, seq, hash, Code(chainId, seq, hash));
    }

    /// <summary>
    /// Check A11: does the current chain still contain the entry the <paramref name="previousCode"/> was computed from? The plain chain must also be
    /// intact from entry 1 to the head (a broken link anywhere is reported as rewritten). An empty code is NotGiven; a mistyped code is InvalidCode.
    /// </summary>
    public static AnchorContinuity Verify(string logPath, string? previousCode)
    {
        if (string.IsNullOrWhiteSpace(previousCode))
            return new(AnchorContinuityStatus.NotGiven, "Nu s-a introdus codul de ancorare de la controlul anterior, deci continuitatea jurnalului aplicației nu s-a putut verifica.", "", null);
        if (!TryNormalize(previousCode, out var code))
            return new(AnchorContinuityStatus.InvalidCode, $"Codul introdus („{previousCode.Trim()}”) nu este un cod de ancorare valid (lungime greșită sau o literă tastată greșit). Recitiți-l de pe procesul-verbal semnat.", previousCode.Trim(), null);
        List<(long Seq, string Hash)> entries;
        ChainVerification plain;
        try { entries = Entries(logPath); plain = new HashChain(logPath).VerifyPlainChain(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(AnchorContinuityStatus.Unreadable, "Jurnalul aplicației nu a putut fi citit: " + ex.Message, code, null); }
        if (plain.Status == ChainStatus.Broken)
            return new(AnchorContinuityStatus.Rewritten, $"Jurnalul aplicației are o verigă ruptă ({plain.Reason}), deci a fost modificat după scriere.", code, null);
        if (entries.Count > 0)
        {
            var chainId = ChainIdOf(entries[0].Hash);
            foreach (var (seq, hash) in entries)
                if (Code(chainId, seq, hash) == code)
                    return new(AnchorContinuityStatus.Continuous,
                        $"Jurnalul aplicației conține neschimbată înregistrarea nr. {seq} de la controlul anterior (cod {code}); după ea urmează {entries[^1].Seq - seq} înregistrări noi.", code, seq);
        }
        return new(AnchorContinuityStatus.Rewritten,
            $"Jurnalul aplicației a fost rescris după controlul anterior: nicio înregistrare din jurnalul actual ({entries.Count} înregistrări) nu corespunde codului {code} de pe procesul-verbal.", code, null);
    }

    /// <summary>
    /// Copies the log (and its seal file, if any) to <paramref name="destinationFolder"/> with an anchor file next to it, for burning on write-once media (CD-R).
    /// Nothing in the source is changed.
    /// </summary>
    public static AnchorExport Export(string logPath, string destinationFolder, string host, DateTimeOffset nowUtc)
    {
        var anchor = Current(logPath) ?? throw new InvalidOperationException("Jurnalul aplicației nu are încă înregistrări; nu există ce ancora.");
        var folder = Path.Combine(destinationFolder, $"LogAnalyzer_ancorare_{nowUtc:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, Path.GetFileName(logPath));
        File.Copy(logPath, copy, overwrite: false);
        var head = HashChain.HeadPath(logPath);
        if (File.Exists(head)) File.Copy(head, Path.Combine(folder, Path.GetFileName(head)), overwrite: false);
        string sha;
        using (var fs = File.OpenRead(copy)) sha = Convert.ToHexStringLower(SHA256.HashData(fs));
        var anchorFile = Path.Combine(folder, "ancorare.txt");
        File.WriteAllText(anchorFile, string.Join(Environment.NewLine,
            "LogAnalyzer — ancorarea jurnalului aplicației (se arde pe un CD-R și se păstrează la ofițerul de securitate)",
            $"Stația: {host}",
            $"Data (UTC): {nowUtc:yyyy-MM-dd HH:mm:ss}",
            anchor.Line,
            $"Identificatorul lanțului: {anchor.ChainId}",
            $"Hash-ul înregistrării nr. {anchor.Seq}: {anchor.Hash}",
            $"Fișierul jurnalului: {Path.GetFileName(copy)}, SHA-256 {sha}",
            "La controlul următor introduceți codul de ancorare de pe procesul-verbal semnat; aplicația verifică dacă jurnalul îl mai conține.",
            ""), new UTF8Encoding(false));
        return new AnchorExport(folder, copy, anchorFile, sha, anchor);
    }

    /// <summary>
    /// The code of the last "audit.anchor" event the station itself wrote: only a HINT for the next control ("cod propus de stație, nesigur"), because whoever
    /// rewrites the log can rewrite this too. Empty when there is none.
    /// </summary>
    public static string LastRecorded(string logPath)
    {
        string last = "";
        foreach (var o in Objects(logPath))
            if (Str(o, "event") == "audit.anchor" && System.Text.RegularExpressions.Regex.Match(Str(o, "detail"), @"anchor=([A-Z2-9-]{23})") is { Success: true } m) last = m.Groups[1].Value;
        return last;
    }

    /// <summary>
    /// The entries with one of <paramref name="events"/> written after entry <paramref name="sinceSeq"/> (when the previous anchor was found) or else after
    /// <paramref name="sinceUtc"/>: "înregistrarea nr. N, data, eveniment, detalii".
    /// </summary>
    public static List<string> EventsSince(string logPath, IReadOnlyCollection<string> events, long? sinceSeq, DateTimeOffset sinceUtc)
    {
        var list = new List<string>();
        foreach (var o in Objects(logPath))
        {
            var ev = Str(o, "event");
            if (!events.Contains(ev)) continue;
            long seq = o["seq"] is { } s ? s.GetValue<long>() : 0;
            DateTimeOffset? when = null;
            if (DateTimeOffset.TryParse(Str(o, "whenUtc"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var w)) when = w;
            if (sinceSeq is { } ss ? seq <= ss : when is { } t && t < sinceUtc) continue;
            list.Add($"înregistrarea nr. {seq}, {when:yyyy-MM-dd HH:mm} UTC, {ev}: {Str(o, "detail")} (de {Str(o, "who")})");
        }
        return list;
    }

    /// <summary>The detail texts of every entry, in order.</summary>
    public static List<string> AllDetails(string logPath) => Objects(logPath).Select(o => Str(o, "detail")).ToList();

    /// <summary>The detail texts of every entry with event <paramref name="eventName"/>, in order.</summary>
    public static List<string> EventDetails(string logPath, string eventName) =>
        Objects(logPath).Where(o => Str(o, "event") == eventName).Select(o => Str(o, "detail")).ToList();

    private static string Str(JsonObject o, string name) =>
        o.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";

    private static IEnumerable<JsonObject> Objects(string logPath)
    {
        if (!File.Exists(logPath)) yield break;
        List<string> lines;
        using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(fs, Encoding.UTF8))
        {
            lines = [];
            for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine()) lines.Add(line);
        }
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? o = null;
            try { o = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { }
            if (o is not null) yield return o;
        }
    }

    private static List<(long Seq, string Hash)> Entries(string logPath)
    {
        var list = new List<(long, string)>();
        if (!File.Exists(logPath)) return list;
        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var o = JsonNode.Parse(line)?.AsObject();
                if (o?["seq"] is { } s && o["hash"] is { } h) list.Add((s.GetValue<long>(), h.GetValue<string>()));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return list;
    }

    private static string Base32(byte[] data, int chars)
    {
        var sb = new StringBuilder(chars);
        int buffer = 0, bits = 0, i = 0;
        while (sb.Length < chars)
        {
            if (bits < 5) { buffer = (buffer << 8) | data[i++]; bits += 8; }
            bits -= 5;
            sb.Append(Alphabet[(buffer >> bits) & 31]);
        }
        return sb.ToString();
    }

    /// <summary>Check character from a hash of the 19 others: a typing mistake is caught with probability 31/32 and reported as a mistyped code, not as a rewrite.</summary>
    private static char Check(string body) => Alphabet[SHA256.HashData(Encoding.ASCII.GetBytes("check|" + body))[0] & 31];

    private static string Group(string raw) => string.Join("-", Enumerable.Range(0, 4).Select(g => raw.Substring(g * 5, 5)));
}
