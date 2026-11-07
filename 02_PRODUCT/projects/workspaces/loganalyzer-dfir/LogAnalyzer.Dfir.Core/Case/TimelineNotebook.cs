using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>A saved timeline filter (master plan 6.9). Every part is optional; an empty spec shows the whole timeline.</summary>
public sealed record TimelineViewSpec(string Text = "", IReadOnlyList<string>? Sources = null, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, bool OnlyBookmarked = false)
{
    public string Describe()
    {
        var parts = new List<string>();
        if (Text.Trim().Length > 0) parts.Add($"text „{Text.Trim()}”");
        if (Sources is { Count: > 0 }) parts.Add("surse " + string.Join(", ", Sources));
        if (FromUtc is { } f) parts.Add($"de la {f:yyyy-MM-dd HH:mm} UTC");
        if (ToUtc is { } t) parts.Add($"până la {t:yyyy-MM-dd HH:mm} UTC");
        if (OnlyBookmarked) parts.Add("doar marcate");
        return parts.Count == 0 ? "toată cronologia" : string.Join("; ", parts);
    }
}

public sealed record TimelineBookmark(string EventKey, string Label, string By, DateTimeOffset AtUtc);

/// <summary>An analyst's note on an event. A note is an opinion about evidence, never evidence: the event itself is not changed.</summary>
public sealed record TimelineAnnotation(string EventKey, string Text, string By, DateTimeOffset AtUtc);

public sealed record TimelineNoteEvent(DateTimeOffset TimestampUtc, string Actor, string Kind, string Subject, string Value)
{
    public long Seq { get; init; }
}

public sealed record ViewComparison(string A, string B, int OnlyA, int OnlyB, int Both)
{
    public string Summary => $"„{A}”: {LogAnalyzer.Dfir.Language.RoText.Count(OnlyA + Both, "eveniment", "evenimente")}, „{B}”: {OnlyB + Both}; comune {Both}, doar în „{A}” {OnlyA}, doar în „{B}” {OnlyB}";
}

public sealed class TimelineNotebookState
{
    public Dictionary<string, TimelineBookmark> Bookmarks { get; } = new(StringComparer.Ordinal);
    public List<TimelineAnnotation> Annotations { get; } = [];
    public SortedDictionary<string, TimelineViewSpec> Views { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ChainVerification? Chain { get; internal set; }
    public IEnumerable<TimelineAnnotation> NotesOf(string key) => Annotations.Where(a => a.EventKey == key);
}

/// <summary>
/// Master plan 6.9: bookmarks, annotations, saved views and the comparison of two views, kept per case in <c>Logs/timeline_notes.jsonl</c>
/// (hash-chained, append-only; every write mirrored to the case audit as <c>timeline.note.*</c>, a prefix of its own: review 2026-11-08 found that the existing <c>timeline.export_masked</c> action counted as a note and locked the notebook). Events are referred to by <see cref="EventKey"/>, built
/// from the evidence, the locator inside it, the source and the time, so a bookmark survives a reanalysis that renumbers or reorders rows.
/// </summary>
public sealed class TimelineNotebook
{
    public const string FileName = "timeline_notes.jsonl";
    private readonly CaseWorkspace _ws;
    private readonly HashChain _chain;
    private readonly object _gate = new();
    public string LogPath { get; }

    public TimelineNotebook(CaseWorkspace ws)
    {
        _ws = ws;
        LogPath = Path.Combine(ws.Root, "Logs", FileName);
        _chain = new HashChain(LogPath);
    }

    /// <summary>
    /// Stable identity of a timeline event: evidence id, locator, source, event id, UTC time (or the raw text when there is none) and, when the
    /// parser gave no locator, the summary. Explainable: the inputs are listed here, nothing else goes in.
    /// </summary>
    public static string EventKey(TimelineEvent e)
    {
        var time = e.Time.Utc?.ToString("O") ?? "raw:" + e.Time.Raw;
        var basis = string.Join("\u001f", e.EvidenceId, e.Locator, e.Source, e.EventId, time, e.Locator.Length == 0 ? e.Summary : "");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(basis)))[..20];
    }

    /// <summary>The filter of the timeline page and of saved views, in one place. Text matches summary, path, process, user, IP, DNS or the exact event id.</summary>
    public static IEnumerable<TimelineEvent> Apply(IEnumerable<TimelineEvent> events, TimelineViewSpec spec, IReadOnlySet<string>? bookmarked = null)
    {
        var q = events;
        var t = (spec.Text ?? "").Trim();
        if (t.Length > 0)
            q = q.Where(e => e.Summary.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Path.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                             e.Process.Contains(t, StringComparison.OrdinalIgnoreCase) || e.User.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                             e.EventId == t || e.RemoteIp.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Dns.Contains(t, StringComparison.OrdinalIgnoreCase));
        if (spec.Sources is { Count: > 0 } src)
        {
            var set = new HashSet<string>(src, StringComparer.OrdinalIgnoreCase);
            q = q.Where(e => set.Contains(e.Source));
        }
        // an event with no UTC time cannot be placed in an interval: an interval filter leaves it out rather than guess
        if (spec.FromUtc is { } from) q = q.Where(e => e.Time.Utc is { } u && u >= from);
        if (spec.ToUtc is { } to) q = q.Where(e => e.Time.Utc is { } u && u <= to);
        if (spec.OnlyBookmarked) q = q.Where(e => bookmarked is not null && bookmarked.Contains(EventKey(e)));
        return q;
    }

    public static ViewComparison Compare(string nameA, TimelineViewSpec a, string nameB, TimelineViewSpec b, IReadOnlyList<TimelineEvent> events, IReadOnlySet<string>? bookmarked = null)
    {
        var ka = Apply(events, a, bookmarked).Select(EventKey).ToHashSet(StringComparer.Ordinal);
        var kb = Apply(events, b, bookmarked).Select(EventKey).ToHashSet(StringComparer.Ordinal);
        int both = ka.Count(kb.Contains);
        return new ViewComparison(nameA, nameB, ka.Count - both, kb.Count - both, both);
    }

    public TimelineNotebookState Load()
    {
        var s = new TimelineNotebookState();
        int audited = AuditedCount();
        if (!File.Exists(LogPath))
        {
            if (audited > 0) s.Chain = new ChainVerification(FileName, ChainStatus.Broken, 0, 0, 1, "jurnal lipsă", $"notițele cronologiei lipsesc, dar auditul are {audited} intrări");
            return s;
        }
        var bytes = ReadShared(LogPath);
        s.Chain = _chain.Verify(bytes);
        int n = 0;
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            TimelineNoteEvent? e;
            try { e = JsonSerializer.Deserialize<TimelineNoteEvent>(line, Json.Options); }
            catch (JsonException) { continue; }
            if (e is null) continue;
            n++;
            switch (e.Kind)
            {
                case "bookmark.add": s.Bookmarks[e.Subject] = new TimelineBookmark(e.Subject, e.Value, e.Actor, e.TimestampUtc); break;
                case "bookmark.remove": s.Bookmarks.Remove(e.Subject); break;
                case "annotation": s.Annotations.Add(new TimelineAnnotation(e.Subject, e.Value, e.Actor, e.TimestampUtc)); break;
                case "view.save":
                    try { if (JsonSerializer.Deserialize<TimelineViewSpec>(e.Value, Json.Options) is { } v) s.Views[e.Subject] = v; } catch (JsonException) { }
                    break;
                case "view.delete": s.Views.Remove(e.Subject); break;
            }
        }
        if (s.Chain.Status != ChainStatus.Broken && n < audited)
            s.Chain = s.Chain with { Status = ChainStatus.Broken, Reason = "jurnal trunchiat", Message = $"notițele au {n} intrări, auditul cazului {audited}" };
        return s;
    }

    private int AuditedCount()
    {
        if (!File.Exists(_ws.AuditChainPath)) return 0;
        int n = 0;
        foreach (var line in Encoding.UTF8.GetString(ReadShared(_ws.AuditChainPath)).Split('\n'))
            if (line.Contains("\"action\":\"timeline.note.", StringComparison.Ordinal) && !line.Contains("\"action\":\"timeline.note.refused\"", StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>A chained log may be held exclusively by a writer for a moment: retried like <see cref="HashChain.Head"/>.</summary>
    private static byte[] ReadShared(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (IOException) when (attempt < 100 && File.Exists(path)) { Thread.Sleep(20); }
        }
    }

    private StateResult Write(string actor, string kind, string subject, string value, Func<TimelineNotebookState, StateResult> check)
    {
        StateResult r;
        if (_ws.IsReadOnly) r = StateResult.Refuse("caz doar pentru citire: " + _ws.ReadOnlyReason);
        else if (string.IsNullOrWhiteSpace(actor)) r = StateResult.Refuse("acțiune fără cont de operator");
        else
        {
            lock (_gate)
            {
                var s = Load();
                r = s.Chain is { Status: ChainStatus.Broken } c ? StateResult.Refuse("notițele cronologiei sunt compromise (" + c.Reason + "); nu se mai scrie nimic în ele") : check(s);
                if (r.Ok)
                {
                    var v = value.ReplaceLineEndings(" ").Trim();
                    _chain.Append(new TimelineNoteEvent(DateTimeOffset.UtcNow, actor.Trim(), kind, subject, v));
                    _ws.Audit("timeline.note." + kind, $"{subject} {(kind == "view.save" ? "" : v)}".Trim());
                    return r;
                }
            }
        }
        _ws.Audit("timeline.note.refused", $"{kind} {subject} by={actor}: {r.Reason}");
        return r;
    }

    private static StateResult KeyOk(string key) =>
        System.Text.RegularExpressions.Regex.IsMatch(key ?? "", "^[0-9a-f]{20}$") ? StateResult.Success : StateResult.Refuse("eveniment necunoscut");

    public StateResult Bookmark(string actor, string eventKey, string label = "") =>
        Write(actor, "bookmark.add", eventKey, label, s => KeyOk(eventKey) is { Ok: false } k ? k
            : s.Bookmarks.ContainsKey(eventKey) ? StateResult.Refuse("evenimentul este deja marcat") : StateResult.Success);

    public StateResult Unbookmark(string actor, string eventKey) =>
        Write(actor, "bookmark.remove", eventKey, "", s => s.Bookmarks.ContainsKey(eventKey) ? StateResult.Success : StateResult.Refuse("evenimentul nu este marcat"));

    public StateResult Annotate(string actor, string eventKey, string text) =>
        Write(actor, "annotation", eventKey, text, s => KeyOk(eventKey) is { Ok: false } k ? k
            : string.IsNullOrWhiteSpace(text) ? StateResult.Refuse("nota este goală") : text.Length > 2000 ? StateResult.Refuse("nota depășește 2000 de caractere") : StateResult.Success);

    public StateResult SaveView(string actor, string name, TimelineViewSpec spec)
    {
        name = (name ?? "").Trim();
        return Write(actor, "view.save", name, JsonSerializer.Serialize(spec, Json.Line), s =>
            name.Length is 0 or > 80 ? StateResult.Refuse("numele vederii lipsește sau are peste 80 de caractere")
            : spec.FromUtc is { } f && spec.ToUtc is { } t && f > t ? StateResult.Refuse("intervalul este inversat (de la > până la)")
            : StateResult.Success);
    }

    public StateResult DeleteView(string actor, string name) =>
        Write(actor, "view.delete", (name ?? "").Trim(), "", s => s.Views.ContainsKey((name ?? "").Trim()) ? StateResult.Success : StateResult.Refuse("vedere necunoscută"));
}
