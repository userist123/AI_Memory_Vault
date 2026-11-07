using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// Master plan 6.9 on the „Cronologie” tab: source and interval filters, bookmarks, analyst notes, saved views and the comparison of two views.
    /// Notes and bookmarks go to <see cref="TimelineNotebook"/> (chained log of the case); the events themselves are never changed.
    /// </summary>
    public partial class InvestigationViewModel
    {
        public const string AllSources = "(toate sursele)";
        public ObservableCollection<string> TimelineSources { get; } = new() { AllSources };
        public ObservableCollection<string> SavedViewNames { get; } = new();
        public ObservableCollection<string> SelectedEventNotes { get; } = new();

        [ObservableProperty] private string _selectedTimelineSource = AllSources;
        [ObservableProperty] private string _timelineFromText = "";
        [ObservableProperty] private string _timelineToText = "";
        [ObservableProperty] private bool _timelineOnlyBookmarked;
        [ObservableProperty] private string _timelineNotesLine = "";
        [ObservableProperty] private string _newViewName = "";
        [ObservableProperty] private string? _selectedSavedView;
        [ObservableProperty] private string? _compareViewName;
        [ObservableProperty] private TimelineEvent? _selectedTimelineEvent;
        [ObservableProperty] private string _annotationText = "";
        [ObservableProperty] private string _bookmarkButtonText = "Marchează evenimentul";

        private TimelineNotebookState _notes = new();

        partial void OnSelectedTimelineSourceChanged(string value) => FillTimeline();
        partial void OnTimelineOnlyBookmarkedChanged(bool value) => FillTimeline();
        partial void OnSelectedTimelineEventChanged(TimelineEvent? value) => ShowEventNotes();

        private TimelineNotebook? Notebook => _result?.Case is { } ws ? new TimelineNotebook(ws) : null;

        /// <summary>Reads the notes of the open case and refreshes sources, saved views and bookmark marks.</summary>
        internal void RefreshTimelineNotes()
        {
            var keepSource = SelectedTimelineSource;
            TimelineSources.Clear(); TimelineSources.Add(AllSources);
            if (_result is not null) foreach (var s in _result.Timeline.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)) TimelineSources.Add(s);
            _suppressFill = true;   // the caller refills
            try { SelectedTimelineSource = TimelineSources.Contains(keepSource) ? keepSource : AllSources; }
            finally { _suppressFill = false; }
            try { _notes = Notebook?.Load() ?? new TimelineNotebookState(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { _notes = new TimelineNotebookState(); TimelineNotesLine = "Notițele cronologiei nu au putut fi citite: " + ex.Message; }
            Views.BookmarkMarkConverter.Marked = _notes.Bookmarks.Keys.ToHashSet(StringComparer.Ordinal);
            Views.BookmarkMarkConverter.Annotated = _notes.Annotations.Select(a => a.EventKey).ToHashSet(StringComparer.Ordinal);
            SavedViewNames.Clear();
            foreach (var n in _notes.Views.Keys) SavedViewNames.Add(n);
            if (_notes.Chain is { Status: ChainStatus.Broken } c) TimelineNotesLine = "NOTIȚELE CRONOLOGIEI SUNT COMPROMISE: " + c.Reason + "; nu se mai scrie nimic în ele.";
            else if (TimelineNotesLine.Length == 0 || TimelineNotesLine.StartsWith("Marcaje:", StringComparison.Ordinal))
                TimelineNotesLine = $"Marcaje: {_notes.Bookmarks.Count} · note: {_notes.Annotations.Count} · vederi salvate: {_notes.Views.Count}";
        }

        private const string DateHelp = "Data se scrie aaaa-ll-zz sau aaaa-ll-zz hh:mm (UTC), de exemplu 2026-11-07 10:30.";

        /// <summary>
        /// The spec the controls describe now. Strict (saving a view): null with a message when a date cannot be read. Lenient (showing the grid):
        /// an unreadable date drops only the interval and says so; source and „doar marcate” stay as shown (review 2026-11-08: the whole filter
        /// used to widen silently).
        /// </summary>
        private TimelineViewSpec? CurrentSpec(bool strict = true)
        {
            bool okFrom = TryUtc(TimelineFromText, false, out var from), okTo = TryUtc(TimelineToText, true, out var to);
            if (!okFrom || !okTo)
            {
                TimelineNotesLine = strict ? DateHelp : DateHelp + " Intervalul NU este aplicat până la corectare; sursa și „doar marcate” sunt aplicate.";
                if (strict) return null;
                from = to = null;
            }
            return new TimelineViewSpec(TimelineFilter.Trim(), SelectedTimelineSource is { } s && s != AllSources ? [s] : null, from, to, TimelineOnlyBookmarked);
        }

        /// <summary>UTC, bounds included. A date without a time means the start of that day for „de la” and its end for „până la”.</summary>
        private static bool TryUtc(string text, bool endOfDay, out DateTimeOffset? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            var styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
            if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, styles, out var day))
            { value = new DateTimeOffset(endOfDay ? day.AddDays(1).AddTicks(-1) : day, TimeSpan.Zero); return true; }
            if (DateTime.TryParseExact(text.Trim(), ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"], CultureInfo.InvariantCulture, styles, out var d))
            {
                // „până la 10:30” includes the whole minute 10:30
                value = new DateTimeOffset(endOfDay && d.Second == 0 && text.Trim().Length <= 16 ? d.AddMinutes(1).AddTicks(-1) : d, TimeSpan.Zero);
                return true;
            }
            return false;
        }

        private bool _suppressFill;

        [RelayCommand] private void ApplyTimelineFilter() => FillTimeline();

        [RelayCommand]
        private void SaveTimelineView()
        {
            if (Notebook is not { } nb) { TimelineNotesLine = "Deschideți întâi un caz."; return; }
            if (CurrentSpec() is not { } spec) return;
            var r = Guard(() => nb.SaveView(Me, NewViewName, spec));
            TimelineNotesLine = r.Ok ? $"Vederea „{NewViewName.Trim()}” a fost salvată: {spec.Describe()}." : "Refuzat: " + r.Reason;
            if (r.Ok) { var n = NewViewName.Trim(); NewViewName = ""; RefreshTimelineNotes(); SelectedSavedView = n; }
        }

        [RelayCommand]
        private void ApplySavedView()
        {
            if (SelectedSavedView is not { } name || !_notes.Views.TryGetValue(name, out var v)) { TimelineNotesLine = "Alegeți o vedere salvată."; return; }
            var missingSource = v.Sources is { Count: > 0 } src0 && !TimelineSources.Contains(src0[0]) ? src0[0] : null;
            _suppressFill = true;   // one refill at the end, not one per control
            try
            {
                TimelineFilter = v.Text;
                SelectedTimelineSource = v.Sources is { Count: > 0 } src && TimelineSources.Contains(src[0]) ? src[0] : AllSources;
                TimelineFromText = v.FromUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
                TimelineToText = v.ToUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
                TimelineOnlyBookmarked = v.OnlyBookmarked;
            }
            finally { _suppressFill = false; }
            FillTimeline();
            TimelineNotesLine = $"Vederea „{name}”: {v.Describe()} — {LogAnalyzer.Dfir.Language.RoText.Count(Timeline.Count, "eveniment", "evenimente")}." +
                                (missingSource is null ? "" : $" ATENȚIE: sursa „{missingSource}” nu mai există în cronologie; se arată toate sursele.");
        }

        [RelayCommand]
        private void DeleteSavedView()
        {
            if (Notebook is not { } nb || SelectedSavedView is not { } name) { TimelineNotesLine = "Alegeți o vedere salvată."; return; }
            var r = Guard(() => nb.DeleteView(Me, name));
            TimelineNotesLine = r.Ok ? $"Vederea „{name}” a fost ștearsă din listă (rămâne în jurnalul notițelor)." : "Refuzat: " + r.Reason;
            if (r.Ok) RefreshTimelineNotes();
        }

        [RelayCommand]
        private void CompareViews()
        {
            if (_result is null) return;
            if (SelectedSavedView is not { } a || CompareViewName is not { } b || !_notes.Views.TryGetValue(a, out var va) || !_notes.Views.TryGetValue(b, out var vb))
            { TimelineNotesLine = "Alegeți două vederi salvate de comparat."; return; }
            TimelineNotesLine = TimelineNotebook.Compare(a, va, b, vb, _result.Timeline, Views.BookmarkMarkConverter.Marked).Summary + ".";
        }

        [RelayCommand]
        private void ToggleBookmark()
        {
            if (Notebook is not { } nb || SelectedTimelineEvent is not { } e) { TimelineNotesLine = "Alegeți un eveniment din cronologie."; return; }
            var key = TimelineNotebook.EventKey(e);
            bool marked = _notes.Bookmarks.ContainsKey(key);
            var r = Guard(() => marked ? nb.Unbookmark(Me, key) : nb.Bookmark(Me, key, AnnotationText.Trim()));
            TimelineNotesLine = r.Ok ? (marked ? "Marcajul a fost scos." : "Evenimentul a fost marcat.") : "Refuzat: " + r.Reason;
            if (r.Ok) { RefreshTimelineNotes(); FillTimeline(); SelectedTimelineEvent = Timeline.FirstOrDefault(x => TimelineNotebook.EventKey(x) == key); }
        }

        [RelayCommand]
        private void AddAnnotation()
        {
            if (Notebook is not { } nb || SelectedTimelineEvent is not { } e) { TimelineNotesLine = "Alegeți un eveniment din cronologie."; return; }
            var r = Guard(() => nb.Annotate(Me, TimelineNotebook.EventKey(e), AnnotationText));
            TimelineNotesLine = r.Ok ? "Nota a fost adăugată (este o notă a analistului, nu o probă)." : "Refuzat: " + r.Reason;
            if (r.Ok)
            {
                var key = TimelineNotebook.EventKey(e);
                AnnotationText = ""; RefreshTimelineNotes(); FillTimeline();   // refill so the ✎ mark shows at once
                SelectedTimelineEvent = Timeline.FirstOrDefault(x => TimelineNotebook.EventKey(x) == key);
                ShowEventNotes();
            }
        }

        private void ShowEventNotes()
        {
            SelectedEventNotes.Clear();
            if (SelectedTimelineEvent is not { } e) { BookmarkButtonText = "Marchează evenimentul"; return; }
            var key = TimelineNotebook.EventKey(e);
            BookmarkButtonText = _notes.Bookmarks.ContainsKey(key) ? "Scoate marcajul" : "Marchează evenimentul";
            if (_notes.Bookmarks.TryGetValue(key, out var b)) SelectedEventNotes.Add($"★ marcat de {b.By}, {b.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm}" + (b.Label.Length > 0 ? ": " + b.Label : ""));
            foreach (var n in _notes.NotesOf(key)) SelectedEventNotes.Add($"{n.By}, {n.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm}: {n.Text}");
        }

        private static StateResult Guard(Func<StateResult> act)
        {
            try { return act(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException) { return StateResult.Refuse("jurnalul cazului nu a putut fi scris: " + ex.Message); }
        }
    }
}
