using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.HashSets;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>One row of the list of fingerprint sets.</summary>
    public sealed record HashSetRow(string Id, string Name, string Category, int Entries, string Source, string ImportedUtc)
    {
        public string Display => $"{Name} · {Category} · {Entries:N0} intrări · {Source} · {ImportedUtc}";
    }

    /// <summary>
    /// „Seturi de amprente cunoscute” (R7a), on the „Profil de proceduri” page: it is administrator-entered data kept in the same application data,
    /// read by the investigation. Known-bad sets raise findings; known-good sets only label (never „curat”).
    /// </summary>
    public partial class HashSetsViewModel : ObservableObject
    {
        private readonly HashSetLibrary _library;
        public ObservableCollection<HashSetRow> Sets { get; } = new();
        public ObservableCollection<string> Issues { get; } = new();
        public static System.Collections.Generic.IReadOnlyList<string> Categories { get; } = new[] { HashSetCategory.KnownGood, HashSetCategory.KnownBad };

        [ObservableProperty] private string _selectedCategory = HashSetCategory.KnownBad;
        [ObservableProperty] private HashSetRow? _selectedSet;
        [ObservableProperty] private string _status = "Importați listele de amprente din fișiere (text sau CSV). Nu se folosește rețeaua.";

        public HashSetsViewModel(HashSetLibrary? library = null)
        {
            _library = library ?? new HashSetLibrary();
            Reload();
        }

        public bool CanEdit => LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration;

        public void Reload()
        {
            Sets.Clear();
            var loaded = _library.Load(out var problems);
            foreach (var s in loaded)
                Sets.Add(new HashSetRow(s.Info.Id, s.Info.Name, s.Info.Category, s.Info.EntryCount, s.Info.SourceFileName, s.Info.ImportedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
            if (problems.Count > 0) { Issues.Clear(); foreach (var p in problems) Issues.Add(p); }
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
            Status = $"Set „{r.Set!.Info.Name}” importat: {r.Set.Info.EntryCount:N0} intrări ({string.Join(", ", r.Set.Info.Algorithms)}), {r.DuplicateCount} duplicate eliminate, {r.InvalidLineCount} linii invalide" + (r.InvalidLineCount > 0 ? " (vezi lista, pe linii)." : ".");
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
