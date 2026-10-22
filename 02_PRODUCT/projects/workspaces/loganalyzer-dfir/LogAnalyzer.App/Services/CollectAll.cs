using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.Dfir.Windows.Investigation;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// The process-wide „Colectează tot” command. It never raises CanExecuteChanged (always executable; a press while running is answered in the status),
    /// so a static command shared by buttons of several pages never touches a button from another thread.
    /// </summary>
    public sealed class CollectAllCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter)
        {
            try { await CollectAllState.RunAsync(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                CollectAllState.Instance.Status = "Colectarea completă s-a oprit: " + ex.Message;
            }
        }
    }

    /// <summary>What „Colectează tot” runs for a station role (G3): the collection profile of the investigation and whether the station control runs too.</summary>
    public sealed record CollectAllPlan(int ProfileIndex, bool RunStationControl, string Description);

    /// <summary>
    /// G3: one „Colectează tot” for the whole application. The last analysis result is held here once and reused by every page (Acasă, Investigație,
    /// Centru medii: „Ce a urmat după conectare” and the connection history); a page with nothing to show offers the same button
    /// („Nu există încă date — Colectează acum”), bound to <see cref="Command"/>.
    /// </summary>
    public sealed partial class CollectAllState : ObservableObject
    {
        public const string EmptyText = "Nu există încă date — Colectează acum";

        /// <summary>Banner title of an empty page (G3; 2026-10-22 walkthrough: the title and the subtitle must not repeat each other).</summary>
        public const string EmptyTitle = "Nu există încă date";

        /// <summary>Banner subtitle of an empty page: says what „Colectează acum” does and that the collection only reads.</summary>
        public const string EmptySubtitle = "„Colectează acum” citește jurnalele, Prefetch, SRUM și starea stației (doar citire), apoi rulează analiza; rezultatele se salvează în caz.";

        public static CollectAllState Instance { get; } = new();

        /// <summary>Set by the main window: navigates to the investigation page and runs the complete collection and analysis for the role.</summary>
        public static Func<Task>? Handler { get; set; }

        /// <summary>The single command every „Colectează tot” / „Colectează acum” button binds to ({x:Static}).</summary>
        public static CollectAllCommand Command { get; } = new();

        /// <summary>Runs the handler once at a time; a second press while it runs only says so.</summary>
        public static async Task RunAsync()
        {
            if (Handler is null) { Instance.Status = "Colectarea nu este disponibilă în această fereastră."; return; }
            if (Instance.IsRunning) { Instance.Status = "Colectarea completă rulează deja; rezultatul apare pe toate paginile când se termină."; return; }
            await Handler();
        }

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasData), nameof(IsEmpty), nameof(IsEmptyForHome))] private InvestigationResult? _current;
        [ObservableProperty] private string _status = EmptySubtitle;
        [ObservableProperty] private bool _isRunning;

        public bool HasData => Current is not null;
        public bool IsEmpty => Current is null;

        /// <summary>Empty for Home and the control page: no analysis result and no station control yet. Investigation and Media keep <see cref="IsEmpty"/> (a control gives them no data).</summary>
        public bool IsEmptyForHome => Current is null && !ControlDone;

        /// <summary>
        /// True when earlier station controls are saved on this station: the same list the control page shows („N controale anterioare”, from the report folders). Set when that
        /// list is read. Disk state, not session state: <see cref="Clear"/> does not reset it. Kept apart from <see cref="IsEmptyForHome"/> (which is session state).
        /// </summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(OfferHiddenOnHome))] private bool _hasEarlierControls;

        /// <summary>Home and the control page hide „Nu există încă date / Colectează acum” when a control finished in this session or earlier controls exist on disk (2026-10-22 beginner walkthrough, finding 6).</summary>
        public bool OfferHiddenOnHome => ControlDone || HasEarlierControls;

        /// <summary>True once a station control finished in this session: the empty-page banner then no longer applies (2026-10-22 PC walkthrough).</summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEmptyForHome), nameof(OfferHiddenOnHome))] private bool _controlDone;

        /// <summary>One line about the last result of this session (control or analysis), for the status bar; empty before any.</summary>
        [ObservableProperty] private string _lastActivity = "";

        /// <summary>Raised when a control or an analysis result is recorded, with the line for the status bar.</summary>
        public event Action<string>? ActivityRecorded;

        /// <summary>A station control finished: the empty banner goes away and the status bar gets the summary.</summary>
        public void RecordControl(string summary)
        {
            ControlDone = true;
            LastActivity = summary;
            ActivityRecorded?.Invoke(summary);
        }

        /// <summary>Raised after a new result is published (pages refresh what they show from it).</summary>
        public event Action<InvestigationResult>? Published;

        /// <param name="opened">True when the result comes from a case opened from disk: the status bar then says „Caz deschis”, not „Investigație finalizată”.</param>
        public void Publish(InvestigationResult r, bool opened = false)
        {
            Current = r;
            Status = $"Date colectate și analizate: {r.Timeline.Count.ToString("N0", RoRo)} evenimente, {r.Findings.Count} constatări (caz {r.Case.Info.CaseId}). Toate paginile folosesc acest rezultat.";
            LastActivity = opened
                ? $"Caz deschis: {(string.IsNullOrWhiteSpace(r.Case.Info.Name) ? r.Case.Info.CaseId : r.Case.Info.Name)} ({r.Timeline.Count.ToString("N0", RoRo)} evenimente, {r.Findings.Count} constatări)."
                : $"Investigație finalizată: {r.Timeline.Count.ToString("N0", RoRo)} evenimente, {r.Findings.Count} constatări (caz {r.Case.Info.CaseId}).";
            ActivityRecorded?.Invoke(LastActivity);
            Published?.Invoke(r);
        }

        private static readonly System.Globalization.CultureInfo RoRo = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

        public void Clear() { Current = null; ControlDone = false; LastActivity = ""; Status = EmptySubtitle; }

        /// <summary>The plan for a role: the incident-response station collects everything (complete profile); the control station collects the standard set and runs the station control.</summary>
        public static CollectAllPlan Plan(StationRole role) => role switch
        {
            StationRole.Csirt => new(2, false, "Colectare completă de pe această stație (jurnale, inclusiv cele arhivate și Windows.old, Prefetch, SRUM, artefacte de execuție, stare live), apoi analiza completă."),
            _ => new(1, true, "Colectare standard de pe această stație (jurnale, inclusiv cele arhivate și Windows.old, Prefetch, SRUM, stare live), analiza completă și controlul stației."),
        };
    }
}
