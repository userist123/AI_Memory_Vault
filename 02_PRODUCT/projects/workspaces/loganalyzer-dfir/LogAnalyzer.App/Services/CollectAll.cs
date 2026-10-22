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

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasData), nameof(IsEmpty))] private InvestigationResult? _current;
        [ObservableProperty] private string _status = EmptySubtitle;
        [ObservableProperty] private bool _isRunning;

        public bool HasData => Current is not null;
        public bool IsEmpty => Current is null;

        /// <summary>Raised after a new result is published (pages refresh what they show from it).</summary>
        public event Action<InvestigationResult>? Published;

        public void Publish(InvestigationResult r)
        {
            Current = r;
            Status = $"Date colectate și analizate: {r.Timeline.Count:N0} evenimente, {r.Findings.Count} constatări (caz {r.Case.Info.CaseId}). Toate paginile folosesc acest rezultat.";
            Published?.Invoke(r);
        }

        public void Clear() { Current = null; Status = EmptySubtitle; }

        /// <summary>The plan for a role: the incident-response station collects everything (complete profile); the control station collects the standard set and runs the station control.</summary>
        public static CollectAllPlan Plan(StationRole role) => role switch
        {
            StationRole.Csirt => new(2, false, "Colectare completă de pe această stație (jurnale, inclusiv cele arhivate și Windows.old, Prefetch, SRUM, artefacte de execuție, stare live), apoi analiza completă."),
            _ => new(1, true, "Colectare standard de pe această stație (jurnale, inclusiv cele arhivate și Windows.old, Prefetch, SRUM, stare live), analiza completă și controlul stației."),
        };
    }
}
