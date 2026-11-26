using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Case;

/// <summary>One collector of a run, as shown to the operator (Romanian status, short manifest hash, first error).</summary>
public sealed record AcquisitionCollectorRow(string Name, string Version, string StatusText, int ItemCount, string ManifestShort, string FirstError);

/// <summary>One acquisition run of a case, with the verdict of the integrity re-check for it. Built by <see cref="AcquisitionRunOverview"/>; every text that came from a record is already cleaned.</summary>
public sealed record AcquisitionRunRow(
    string RunId, string ShortId, DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, string StateText, string Operator, string Station,
    string ApplicationVersion, string Profile, int CollectorCount, int ItemCount, long TotalBytes, string VerificationText, bool HasProblem,
    IReadOnlyList<AcquisitionCollectorRow> Collectors)
{
    /// <summary>Local time for the grid ("—" when the record could not be trusted).</summary>
    public string StartLocalText => StartUtc is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
    public string EndLocalText => EndUtc is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
    /// <summary>False for a record that is unregistered, modified, invalid or unreadable: only the reason is shown for it.</summary>
    public bool IsTrusted => StartUtc is not null;
}

/// <summary>
/// Reads every acquisition run record of a case for display, through <see cref="AcquisitionRunVerifier.Inspect"/>: the record shown is the one the checks were
/// made on (read once, hashed against the registered SHA-256). A record that is unregistered, modified or invalid shows only the reason.
/// Cost: the verifier hashes every listed file, so this is as expensive as the re-check; pass the maps of a finished re-check
/// (<see cref="AcquisitionRunVerifier.MapsFrom"/>) to avoid hashing them again.
/// </summary>
public static class AcquisitionRunOverview
{
    public const string NotRegisteredText = "fișă neînregistrată în lanțul de custodie — nu este de încredere";
    public const string ModifiedText = "fișă modificată după înregistrare — nu este de încredere";
    public const string NoFilesText = "fără fișiere colectate";
    public const int MaxTextLength = 120;

    /// <summary>Text from a record made safe for a grid and a report line: control, line-separator and bidi characters become a space, whitespace collapses, long text is cut with "…".</summary>
    public static string Clean(string? s, int max = MaxTextLength)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(Math.Min(s.Length, max + 1));
        var lastSpace = true;
        // Review round 2: walk by code point (a lone surrogate becomes U+FFFD) and blank every Format character (bidi, zero-width, soft hyphen, tags).
        foreach (var rune in s.EnumerateRunes())
        {
            if (IsBlank(rune)) { if (!lastSpace) sb.Append(' '); lastSpace = true; }
            else { sb.Append(rune.ToString()); lastSpace = false; }
        }
        var t = sb.ToString().TrimEnd();
        if (t.Length <= max) return t;
        var cut = max - 1;
        if (char.IsHighSurrogate(t[cut - 1])) cut--;   // never split a surrogate pair
        return t[..cut] + "…";
    }

    private static bool IsBlank(System.Text.Rune r) =>
        System.Text.Rune.IsControl(r) || System.Text.Rune.IsWhiteSpace(r) ||
        System.Text.Rune.GetUnicodeCategory(r) is System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;

    public static IReadOnlyList<AcquisitionRunRow> Load(CaseWorkspace ws, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? knownActualHashes = null, IReadOnlyDictionary<string, string>? registeredOutputs = null)
    {
        var inspected = AcquisitionRunVerifier.Inspect(ws, ct, knownActualHashes, registeredOutputs);
        var when = $" (verificare din {DateTimeOffset.Now:dd.MM.yyyy HH:mm})";
        var rows = new List<AcquisitionRunRow>();
        if (inspected.FolderProblem is { } fp)
        {
            var text = "neverificată: " + Clean(fp, 240) + when;
            if (inspected.Runs.Count == 0) rows.Add(Invalid("", "", text));
            foreach (var run in inspected.Runs) rows.Add(Invalid(run.RunId, run.RunFile, text));
            return rows;
        }
        foreach (var run in inspected.Runs)
        {
            ct.ThrowIfCancellationRequested();
            AcquisitionRunRow row;
            try { row = run.Record is null ? Untrusted(run, when) : Build(run.RunId, run.Record, run.Findings, when); }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                // review round 2: one odd record (e.g. sizes that overflow) must never stop the case from opening
                row = Invalid(run.RunId, run.RunFile, "fișă invalidă: " + Clean(ex.Message) + when);
            }
            rows.Add(row);
        }
        return rows.OrderByDescending(r => r.StartUtc ?? DateTimeOffset.MinValue).ThenBy(r => r.RunId, StringComparer.Ordinal).ToList();
    }

    private static AcquisitionRunRow Untrusted(AcquisitionRunInspection run, string when)
    {
        string text;
        if (run.Findings.Any(f => f.Kind == AcquisitionFindingKind.Unregistered)) text = NotRegisteredText;
        else if (run.Findings.Any(f => f.Kind == AcquisitionFindingKind.RecordModified)) text = ModifiedText;
        else text = "fișă invalidă: " + string.Join("; ", run.Findings.Take(3).Select(f => Clean(f.Detail)));
        return Invalid(run.RunId, run.RunFile, text + when);
    }

    private static AcquisitionRunRow Build(string stem, AcquisitionRunRecord rec, IReadOnlyList<AcquisitionRunFinding> findings, string when)
    {
        var problems = findings.Where(f => f.IsProblem).ToList();
        var items = rec.Collectors.SelectMany(c => c.Items).ToList();
        var intact = findings.Count(f => f.Kind == AcquisitionFindingKind.ItemIntact);
        string verification;
        var hasProblem = true;
        if (problems.Count > 0)
            verification = $"PROBLEMĂ: {RoText.Count(problems.Count, "constatare", "constatări")}: " + string.Join("; ", problems.Take(3).Select(p => Clean(p.Detail))) + (problems.Count > 3 ? "; …" : "");
        else if (items.Count == 0) { verification = NoFilesText; hasProblem = false; }
        else if (intact != items.Count) verification = "neverificată: fișierele listate nu au putut fi toate verificate";
        else { verification = $"verificată: {RoText.Count(items.Count, "fișier", "fișiere")} cu aceeași amprentă"; hasProblem = false; }

        var collectors = rec.Collectors.Select(c => new AcquisitionCollectorRow(
            Clean(c.Name), Clean(c.Version), RoLabels.Source(c.Status), c.Items.Count,
            Clean(c.ManifestSha256.Length >= 12 ? c.ManifestSha256[..12] : "—"), Clean(c.Errors?.FirstOrDefault()))).ToList();
        return new AcquisitionRunRow(stem, stem[..8], rec.StartUtc, rec.EndUtc, StateText(rec.State), Or(rec.Operator), Or(rec.Station), Or(rec.ApplicationVersion), Or(rec.Profile),
            rec.Collectors.Count, items.Count, SaturatingTotal(items.Select(i => i.Size)), verification + when, hasProblem, collectors);
    }

    /// <summary>Sum of sizes that never throws: negative sizes count as 0 and the total stops at long.MaxValue.</summary>
    internal static long SaturatingTotal(IEnumerable<long> sizes)
    {
        long total = 0;
        foreach (var s in sizes)
            total = s <= 0 ? total : (total > long.MaxValue - s ? long.MaxValue : total + s);
        return total;
    }

    private static string Or(string? s) => Clean(s) is { Length: > 0 } c ? c : "—";

    public static string StateText(OperationState s) => s switch
    {
        OperationState.Completed => "încheiată", OperationState.Cancelled => "anulată", OperationState.Failed => "eșuată",
        OperationState.Partial => "parțială", OperationState.Blocked => "blocată", OperationState.Running => "în curs", _ => "nedefinită",
    };

    private static AcquisitionRunRow Invalid(string stem, string name, string text)
    {
        var id = Clean(stem.Length > 0 ? stem : name, 40);
        return new(id, id.Length >= 8 ? id[..8] : (id.Length > 0 ? id : "—"), null, null, "—", "—", "—", "—", "—", 0, 0, 0, text, true, []);
    }
}
