using System.Globalization;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>Open = the measure is still due; Closed = the check came out CONFORM at a later control (the only way an action closes).</summary>
public enum CorrectiveState { Deschisa, Inchisa }

/// <summary>
/// One non-conformity of the "document de constatare" with its measure, the person responsible and the deadline (matrix HG 585 row 38). Measure, responsible and
/// deadline are what the inspector writes; the state is decided by the application from the checks, never typed.
/// </summary>
public sealed class CorrectiveAction
{
    public string CheckId { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>When the non-conformity was first recorded (the collection time of that control).</summary>
    public DateTimeOffset OpenedUtc { get; set; }
    public string Measure { get; set; } = "";
    public string Responsible { get; set; } = "";
    /// <summary>The deadline as typed (yyyy-MM-dd or dd.MM.yyyy); see <see cref="CorrectiveActions.TryDeadline"/>.</summary>
    public string Deadline { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CorrectiveState State { get; set; } = CorrectiveState.Deschisa;
    public DateTimeOffset? ClosedUtc { get; set; }
    /// <summary>Why the action is closed, or why it stays open although it was carried over (e.g. the check is NEDETERMINAT now).</summary>
    public string Note { get; set; } = "";
    /// <summary>The previous control it was carried from; null = recorded at this control.</summary>
    public DateTimeOffset? CarriedFromUtc { get; set; }
}

/// <summary>
/// The register of non-conformities across controls (matrix HG 585 row 38; flow step 5): every NECONFORM check of this control has an action; an action of the
/// previous control is closed only when its check is CONFORM now; with any other result it stays open and says why. Pure functions, no I/O.
/// </summary>
public static class CorrectiveActions
{
    public const string SectionTitle = "3d. Neconformități: măsuri, responsabili și termene";

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy"];

    public static bool TryDeadline(string? text, out DateOnly date) =>
        DateOnly.TryParseExact((text ?? "").Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>
    /// The actions of this control: the previous control's open actions (carried with their measure, responsible and deadline) and one new action for every
    /// NECONFORM check that had none. Previous closed actions are history (they are on the previous report) and are not repeated.
    /// </summary>
    public static List<CorrectiveAction> Plan(IReadOnlyList<ControlCheck> current, DateTimeOffset collectedUtc, PreviousControl? previous)
    {
        var now = current.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var list = new List<CorrectiveAction>();
        var carried = new HashSet<string>(StringComparer.Ordinal);
        // An action is closed only at a LATER control, so a check that was NECONFORM in the previous control must have had an open action there. A saved
        // "closed" state (or a missing action) for such a check contradicts the same report: it is carried as open, never trusted to hide the non-conformity.
        var prevNeconform = previous?.Actions is null ? new HashSet<string>(StringComparer.Ordinal)
            : previous.Checks.Where(c => c.Status == ControlStatus.Neconform).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var previousActions = (previous?.Actions ?? []).ToList();
        var reopened = new HashSet<CorrectiveAction>(ReferenceEqualityComparer.Instance);
        foreach (var id in prevNeconform.Where(id => !previousActions.Any(a => a.CheckId == id && a.State == CorrectiveState.Deschisa)))
        {
            var known = previousActions.FirstOrDefault(a => a.CheckId == id);
            var title = known?.Title ?? previous!.Checks.First(c => c.Id == id).Title;
            var guard = new CorrectiveAction
            {
                CheckId = id, Title = title, OpenedUtc = known?.OpenedUtc ?? previous!.CollectedUtc, Measure = known?.Measure ?? "", Responsible = known?.Responsible ?? "",
                Deadline = known?.Deadline ?? "", Note = "Starea salvată nu corespunde verificării NECONFORME din același control; măsura este tratată ca deschisă.",
            };
            previousActions.Insert(0, guard); reopened.Add(guard);
        }
        foreach (var old in previousActions)
        {
            if (old.State != CorrectiveState.Deschisa || !carried.Add(old.CheckId)) continue;
            var a = new CorrectiveAction
            {
                CheckId = old.CheckId, Title = old.Title, OpenedUtc = old.OpenedUtc, Measure = old.Measure, Responsible = old.Responsible, Deadline = old.Deadline,
                CarriedFromUtc = previous!.CollectedUtc,
            };
            if (now.TryGetValue(old.CheckId, out var c) && c.Status == ControlStatus.Conform)
            {
                a.State = CorrectiveState.Inchisa; a.ClosedUtc = collectedUtc;
                a.Note = $"Închisă: verificarea {c.Id} este CONFORMĂ la acest control.";
            }
            else
                a.Note = c is null ? $"Rămâne deschisă: verificarea {old.CheckId} nu a fost evaluată la acest control, deci remedierea nu se poate constata."
                     : c.Status == ControlStatus.Neconform ? $"Rămâne deschisă: verificarea {c.Id} este tot NECONFORMĂ."
                     : $"Rămâne deschisă: verificarea {c.Id} este {ControlReportPdf.StatusText(c.Status)} la acest control, nu CONFORMĂ, deci remedierea nu se poate constata.";
            if (reopened.Contains(old)) a.Note = old.Note + " " + a.Note;
            list.Add(a);
        }
        foreach (var c in current.Where(c => c.Status == ControlStatus.Neconform && !carried.Contains(c.Id)).GroupBy(c => c.Id).Select(g => g.First()))
            list.Add(new CorrectiveAction { CheckId = c.Id, Title = c.Title, OpenedUtc = collectedUtc });
        return list;
    }

    /// <summary>
    /// What is missing before the proces-verbal (flow step 5): every OPEN action needs a measure, a responsible person and a valid deadline.
    /// One line per incomplete action, in the inspector's words; an empty list = complete.
    /// </summary>
    public static IReadOnlyList<string> Missing(IEnumerable<CorrectiveAction> actions)
    {
        var l = new List<string>();
        foreach (var a in actions.Where(a => a.State == CorrectiveState.Deschisa))
        {
            var what = new List<string>();
            if (string.IsNullOrWhiteSpace(a.Measure)) what.Add("măsura");
            if (string.IsNullOrWhiteSpace(a.Responsible)) what.Add("responsabilul");
            if (!TryDeadline(a.Deadline, out _)) what.Add(string.IsNullOrWhiteSpace(a.Deadline) ? "termenul" : "un termen valid (zz.ll.aaaa)");
            if (what.Count > 0) l.Add($"{a.CheckId} {a.Title}: lipsește {string.Join(", ", what)}.");
        }
        return l;
    }

    public static bool IsOverdue(CorrectiveAction a, DateOnly today) =>
        a.State == CorrectiveState.Deschisa && TryDeadline(a.Deadline, out var d) && d < today;

    /// <summary>The lines of section 3d: open actions first (overdue in red), then the ones closed at this control.</summary>
    public static List<(string Text, bool Head, string Color)> SectionLines(IReadOnlyList<CorrectiveAction>? actions, DateOnly today)
    {
        var l = new List<(string, bool, string)>();
        if (actions is null) { l.Add(("Registrul neconformităților nu a fost completat pentru acest raport.", false, "#b45309")); return l; }
        if (actions.Count == 0) { l.Add(("Nicio neconformitate deschisă: nicio verificare NECONFORMĂ la acest control și nicio măsură rămasă deschisă de la controlul anterior.", false, "#475569")); return l; }
        var open = actions.Where(a => a.State == CorrectiveState.Deschisa).ToList();
        var closed = actions.Where(a => a.State == CorrectiveState.Inchisa).ToList();
        int overdue = open.Count(a => IsOverdue(a, today));
        l.Add(($"Deschise: {open.Count}" + (overdue > 0 ? $", dintre care cu termen depășit: {overdue}" : "") + $". Închise la acest control: {closed.Count}. " +
               "O măsură se închide doar când verificarea ei iese CONFORMĂ la un control.", true, "#0f172a"));
        foreach (var a in open.OrderByDescending(a => IsOverdue(a, today)).ThenBy(a => a.CheckId, StringComparer.Ordinal))
        {
            bool late = IsOverdue(a, today);
            string V(string s, string missing) => string.IsNullOrWhiteSpace(s) ? missing : s.Trim();
            l.Add(($"{a.CheckId} {a.Title}" + (late ? " — TERMEN DEPĂȘIT" : ""), true, late ? "#b91c1c" : "#0f172a"));
            l.Add(($"Măsura: {V(a.Measure, "NESTABILITĂ")} · Responsabil: {V(a.Responsible, "NESTABILIT")} · Termen: {(TryDeadline(a.Deadline, out var d) ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "NESTABILIT")}" +
                   $" · Constatată la {a.OpenedUtc.ToLocalTime():dd.MM.yyyy}" + (a.CarriedFromUtc is { } cf ? $" (preluată de la controlul din {cf.ToLocalTime():dd.MM.yyyy})" : ""),
                   false, late || string.IsNullOrWhiteSpace(a.Measure) || string.IsNullOrWhiteSpace(a.Responsible) || !TryDeadline(a.Deadline, out _) ? "#b91c1c" : "#1e293b"));
            if (a.Note.Length > 0) l.Add((a.Note, false, "#475569"));
        }
        foreach (var a in closed.OrderBy(a => a.CheckId, StringComparer.Ordinal))
            l.Add(($"{a.CheckId} {a.Title} — ÎNCHISĂ. Măsura: {(string.IsNullOrWhiteSpace(a.Measure) ? "—" : a.Measure.Trim())}. {a.Note}", false, "#15803d"));
        return l;
    }
}
