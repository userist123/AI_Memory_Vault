using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.HashSets;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>One row of the list of fingerprint sets.</summary>
    public sealed record HashSetRow(string Id, string Name, string Category, int Entries, string Source, string ImportedUtc, string Algorithms = "", bool CoversEvidenceSamples = true)
    {
        public string Display => $"{Name} · {Category} · {Entries:N0} intrări · {Algorithms} · {Source} · {ImportedUtc}" +
            (CoversEvidenceSamples ? "" : " · acoperire: doar câmpuri de eveniment (nu probele din caz)");
    }

    /// <summary>
    /// „Seturi de amprente cunoscute” (R7a), on the „Profil de proceduri” page: it is administrator-entered data kept in the same application data,
    /// read by the investigation. Known-bad sets raise findings; known-good sets only label (never „curat”).
    /// A set that does not pass the integrity checks is not used and is listed in <see cref="Problems"/> with the reason.
    /// </summary>
    public partial class HashSetsViewModel : ObservableObject
    {
        private const string ReadyStatus = "Importați listele de amprente din fișiere (text sau CSV). Nu se folosește rețeaua.";
        private const string LoadingStatus = "Se încarcă seturile de amprente…";
        private readonly HashSetLibrary _library;
        private int _generation;
        public ObservableCollection<HashSetRow> Sets { get; } = new();
        /// <summary>Invalid lines of the last import.</summary>
        public ObservableCollection<string> Issues { get; } = new();
        /// <summary>Sets that were NOT used (and why), and a folder-rights problem, if any. Replaced at every load.</summary>
        public ObservableCollection<string> Problems { get; } = new();
        public static System.Collections.Generic.IReadOnlyList<string> Categories { get; } = new[] { HashSetCategory.KnownGood, HashSetCategory.KnownBad };
        public static string CoverageNote => HashSetMatcher.CoverageNote;

        [ObservableProperty] private string _selectedCategory = HashSetCategory.KnownBad;
        [ObservableProperty] private HashSetRow? _selectedSet;
        [ObservableProperty] private string _status = LoadingStatus;

        /// <summary>The load started by the constructor (the page does not wait for it: reading big sets must not block the UI thread).</summary>
        public Task LoadTask { get; }

        public HashSetsViewModel(HashSetLibrary? library = null)
        {
            _library = library ?? new HashSetLibrary();
            LoadTask = ReloadAsync();
        }

        public bool CanEdit => LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration;

        /// <summary>Synchronous reload (after an import or a removal, which already run in the foreground).</summary>
        public void Reload()
        {
            Interlocked.Increment(ref _generation);
            Apply(_library.Load(out var problems), problems);
        }

        /// <summary>Loads on a background thread and applies the result on the calling context; a newer load makes an older one drop its result.</summary>
        public async Task ReloadAsync()
        {
            var gen = Interlocked.Increment(ref _generation);
            List<KnownHashSet> sets; List<string> problems;
            try { (sets, problems) = await Task.Run(() => { var s = _library.Load(out var p); return (s, p); }); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { sets = []; problems = [$"Seturile de amprente nu au putut fi încărcate: {ex.Message}"]; }
            if (gen == Volatile.Read(ref _generation)) Apply(sets, problems);
        }

        private void Apply(List<KnownHashSet> loaded, List<string> problems)
        {
            Sets.Clear();
            foreach (var s in loaded)
                Sets.Add(new HashSetRow(s.Info.Id, s.Info.Name, s.Info.Category, s.Info.EntryCount, s.Info.SourceFileName, s.Info.ImportedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    string.Join(", ", s.Info.Algorithms), s.Info.CoversEvidenceSamples));
            Problems.Clear();
            foreach (var p in problems) Problems.Add(p);
            if (Status == LoadingStatus) Status = problems.Count > 0 ? $"{problems.Count} set(uri) nefolosite: vezi lista de probleme." : ReadyStatus;
        }

        [RelayCommand]
        private void Import()
        {
            var dlg = new OpenFileDialog { Filter = "Liste de amprente (*.txt;*.csv)|*.txt;*.csv|Toate fișierele|*.*", Title = $"Importați un set de amprente ({SelectedCategory})" };
            if (dlg.ShowDialog() == true) ImportFile(dlg.FileName);
        }

        public HashSetImportResult ImportFile(string path, string? name = null, HashSetImportOptions? options = null)
        {
            var r = _library.Import(path, name ?? "", SelectedCategory, options);
            Issues.Clear();
            foreach (var i in r.InvalidLines) Issues.Add(i.ToString());
            if (r.InvalidLineCount > r.InvalidLines.Count) Issues.Add($"… încă {r.InvalidLineCount - r.InvalidLines.Count} linii invalide nelistate.");
            if (!r.Ok) { Status = "Nu s-a importat nimic: " + r.Error; return r; }
            Reload();
            Status = $"Set „{r.Set!.Info.Name}” importat: {r.Set.Info.EntryCount:N0} intrări ({string.Join(", ", r.Set.Info.Algorithms)}), {r.DuplicateCount} duplicate eliminate, {r.InvalidLineCount} linii invalide" + (r.InvalidLineCount > 0 ? " (vezi lista, pe linii)." : ".")
                + (r.Set.Info.CoversEvidenceSamples ? "" : " Atenție: setul nu are SHA-256, deci nu se compară cu probele din caz, doar cu câmpurile de eveniment.");
            return r;
        }

        [RelayCommand]
        private void Remove()
        {
            if (SelectedSet is null) { Status = "Alegeți întâi setul din listă."; return; }
            var name = SelectedSet.Name;
            var err = _library.Remove(SelectedSet.Id);
            if (err.Length > 0) { Status = err; return; }
            Reload();
            Status = $"Setul „{name}” a fost eliminat; nu se mai folosește la potrivire. Cazurile deja analizate își păstrează rezultatele.";
        }
    }
}
