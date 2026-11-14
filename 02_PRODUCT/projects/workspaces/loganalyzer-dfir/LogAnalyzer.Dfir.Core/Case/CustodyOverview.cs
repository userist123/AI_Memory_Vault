using System.Text.Json;
using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.Dfir.Case;

/// <summary>One line of the case custody chain as shown to the operator. Every text that came from the log is already cleaned.</summary>
public sealed record CustodyRow(long? Seq, DateTimeOffset? TimestampUtc, string Who, string ActionText, string EvidenceId, string FromTo,
    string ShaShort, string Sha256, string Tool, string Details, bool Unreadable)
{
    public string SeqText => Seq is { } s ? s.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
    public string LocalText => TimestampUtc is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";
}

/// <summary>The custody chain of one case: the newest rows first, and the verdict of the chain check made on the same bytes as the rows.</summary>
public sealed record CustodyLedger(IReadOnlyList<CustodyRow> Rows, int TotalLines, string ChainLine, bool ChainProblem)
{
    public bool Truncated => TotalLines > Rows.Count;
}

/// <summary>
/// Reads <c>Logs/chain_of_custody.jsonl</c> of a case for display (tab 9). Nothing is written. The log is read once; the chain is verified on those bytes
/// (<see cref="HashChain.Verify(byte[])"/>) and the rows are parsed from the same bytes. A case created before WP3 reads as "neverificabil", never as valid;
/// lines without chain fields in front of the chain are verified by nothing, so they make the page show a problem and are marked "neverificat" (no hash shown).
/// A line that is not JSON is shown as unreadable, not skipped, so a damaged log cannot look shorter than it is. Only the newest <see cref="MaxRows"/> lines are kept.
/// </summary>
public static class CustodyOverview
{
    public const int MaxRows = 20_000;
    public const long MaxBytes = 256L * 1024 * 1024;
    public const string UnverifiedPrefix = "neverificat (linie fără lanț): ";

    public static CustodyLedger Load(CaseWorkspace ws, CancellationToken ct = default)
    {
        byte[] content = [];
        bool exists = File.Exists(ws.CustodyJsonlPath);
        if (exists)
        {
            using var fs = new FileStream(ws.CustodyJsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > MaxBytes) throw new IOException($"jurnalul de custodie are {fs.Length / (1024 * 1024)} MB, peste limita de afișare de {MaxBytes / (1024 * 1024)} MB");
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            content = ms.ToArray();
        }
        ct.ThrowIfCancellationRequested();
        var chain = new HashChain(ws.CustodyJsonlPath).Verify(content);
        if (chain.Status == ChainStatus.Valid && chain.Entries == 0 && chain.LegacyLines == 0 && CsvRows(ws.CustodyCsvPath) is > 0 and var csvRows)
            chain = chain with { Status = ChainStatus.Legacy, LegacyLines = csvRows, Message = HashChain.LegacyMessage };   // same reading as the integrity re-check

        var kept = new Queue<CustodyRow>();
        int total = 0;
        foreach (var line in HashChain.Lines(content))   // review round 2: the same lines the chain was verified on (BOM, lone '\r')
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            total++;
            kept.Enqueue(Row(line, total));
            if (kept.Count > MaxRows) kept.Dequeue();
        }
        var rows = kept.Reverse().ToList();
        return new CustodyLedger(rows, total, ChainText(chain, total, rows.Count),
            chain.Status != ChainStatus.Valid || chain.LegacyLines > 0 || rows.Any(r => r.Unreadable));
    }

    private static int CsvRows(string path)
    {
        if (!File.Exists(path)) return 0;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs);
        int n = 0;
        while (r.ReadLine() is { } l) if (!string.IsNullOrWhiteSpace(l)) n++;
        return Math.Max(0, n - 1);   // minus the header
    }
    internal static string ChainText(ChainVerification c, int total, int shown)
    {
        var head = c.Status switch
        {
            ChainStatus.Valid when total == 0 && c.LegacyLines == 0 => "Lanțul de custodie al cazului este gol",
            ChainStatus.Valid when c.LegacyLines > 0 => $"PROBLEMĂ: {RoText.Count(c.LegacyLines, "linie", "linii")} fără lanț (neverificabile) înaintea celor {RoText.Count(c.Entries, "intrare verificată", "intrări verificate")}",
            ChainStatus.Valid => $"Lanț de custodie verificat: {RoText.Count(c.Entries, "intrare", "intrări")}, fiecare legată de cea dinainte prin amprentă SHA-256",
            ChainStatus.Legacy => "Lanț de custodie neverificabil: " + AcquisitionRunOverview.Clean(c.Message, 240),
            _ => "PROBLEMĂ: lanțul de custodie este rupt" + (c.BrokenAtSeq is { } s ? $" la intrarea {s}" : "") + ": " + AcquisitionRunOverview.Clean(c.Message, 240),
        };
        return shown < total ? head + $" · se afișează cele mai noi {shown.ToString("N0", RoCulture)} din {total.ToString("N0", RoCulture)} linii" : head;
    }

    private static readonly System.Globalization.CultureInfo RoCulture = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

    private static CustodyRow Row(string line, int lineNo)
    {
        JsonObject o;
        try { o = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("nu este un obiect"); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return new CustodyRow(null, null, "—", "linie ilizibilă", "", "", "", "", "", $"linia {lineNo} din jurnal nu poate fi citită", true);
        }
        string S(string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        long? seq = o["seq"] is JsonValue sv && sv.TryGetValue<long>(out var n) ? n : null;
        DateTimeOffset? when = DateTimeOffset.TryParse(S("timestampUtc"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        string from = Clean(S("from")), to = Clean(S("to"));
        var fromTo = (from.Length, to.Length) switch { (0, 0) => "", (0, _) => to, (_, 0) => from, _ => from + " → " + to };
        var sha = Hex(S("sha256"));
        var tool = Clean(string.Join(" ", new[] { S("tool"), S("toolVersion") }.Where(x => x.Length > 0)));
        var details = Clean(string.Join(" · ", new[] { S("transformation"), Context(S("collectorAccount"), S("collectorMachine"), S("mediaId")) }.Where(x => x.Length > 0)));
        if (!(o.ContainsKey("seq") && o.ContainsKey("hash") && o.ContainsKey("prevHash")))
        {
            // review round 1: a line outside the chain is verified by nothing; no hash is shown for it and it says so
            sha = "";
            details = Clean(UnverifiedPrefix + details);
        }
        return new CustodyRow(seq, when, Or(Clean(S("who"))), ActionText(S("action")), Clean(S("evidenceId"), 40), fromTo,
            sha.Length >= 12 ? sha[..12] : "—", sha, tool, details, false);
    }

    private static string Context(string account, string machine, string media)
    {
        var parts = new List<string>();
        if (account is { Length: > 0 } && account != "necunoscut") parts.Add("cont " + account);
        if (machine is { Length: > 0 } && machine != "necunoscut") parts.Add("stația " + machine);
        if (media is { Length: > 0 } && media != "necunoscut") parts.Add("mediu " + media);
        return string.Join(", ", parts);
    }

    /// <summary>Only a 64-character hexadecimal value is shown as a hash; anything else in that field is not a hash.</summary>
    private static string Hex(string s) => s.Length == 64 && s.All(Uri.IsHexDigit) ? s : "";

    private static string Clean(string s, int max = AcquisitionRunOverview.MaxTextLength) => AcquisitionRunOverview.Clean(s, max);
    private static string Or(string s) => s.Length > 0 ? s : "—";

    /// <summary>Romanian label of a custody action; an unknown action is shown as written (cleaned), never dropped.</summary>
    public static string ActionText(string action) => action switch
    {
        "acquired" => "colectată",
        "imported" => "importată",
        "remote-collected" => "colectată de la distanță",
        "authorized" => "autorizare înregistrată",
        "parsed" => "prelucrată",
        "output.written" => "rezultat înregistrat",
        "state.transition" => "stare schimbată",
        "state.disposed" => "scoasă din uz",
        "warning.collector_admin" => "avertisment: colector administrator",
        "archive.imported_unverified" => "arhivă importată (neverificată)",
        _ => Clean(action, 40) is { Length: > 0 } c ? c : "—",
    };
}
