using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// The first-run guide (owner decision 2026-10-22, proposal 5 of the beginner walkthrough): three short screens shown once after the first sign-in on the station —
    /// what the application does, the usual path, where help is. It only explains; it changes nothing and never blocks the window behind it.
    /// </summary>
    public sealed partial class FirstRunGuideViewModel : ObservableObject
    {
        public sealed record Screen(string Title, IReadOnlyList<string> Lines);

        /// <summary>The three screens.</summary>
        public static IReadOnlyList<Screen> Screens { get; } =
        [
            new("Ce face aplicația",
            [
                "LogAnalyzer verifică această stație și vă ajută să înțelegeți ce s-a întâmplat pe ea.",
                "Aplicația doar citește: nu modifică nimic pe stație, nu instalează și nu șterge nimic.",
                "Tot ce găsește se salvează într-un caz, cu o amprentă care arată dacă ceva a fost schimbat după aceea.",
            ]),
            new("Drumul obișnuit",
            [
                "1. Faceți controlul stației (butonul mare de pe pagina Acasă).",
                "2. Deschideți raportul PDF și, dacă este nevoie, semnați-l ca ofițer de securitate.",
                "3. Treceți rezultatul în procesul-verbal, împreună cu codul de ancorare al acestui control.",
                "Păstrați acest cod de ancorare: data viitoare îl scrieți la întrebarea despre procesul-verbal anterior, iar controlul verifică dacă jurnalul stației a rămas același.",
            ]),
            new("Unde găsiți ajutor",
            [
                "Butonul „Ce înseamnă?” din bara de sus (sau tasta F1) explică fiecare termen tehnic pe înțelesul tuturor.",
                "Comutatorul Simplu / Expert din bara de sus: Simplu arată doar drumul principal; Expert adaugă numele tehnice și acțiunile avansate. Datele sunt aceleași.",
                "Puteți redeschide acest ghid oricând din fereastra „Ce înseamnă?” sau de pe pagina Acasă.",
            ]),
        ];

        private readonly LanguagePreferences _prefs;

        public FirstRunGuideViewModel(LanguagePreferences? prefs = null) => _prefs = prefs ?? new LanguagePreferences();

        [ObservableProperty] private int _index;
        private bool _closed;

        public string Title => Screens[Index].Title;
        public IReadOnlyList<string> Lines => Screens[Index].Lines;
        public string Progress => $"Ecranul {Index + 1} din {Screens.Count}";
        public bool CanGoBack => Index > 0;
        public bool IsLast => Index == Screens.Count - 1;
        public string NextText => IsLast ? "Am înțeles" : "Înainte";
        public bool Closed => _closed;

        /// <summary>Raised when the window should close (the last screen, „Nu mai arăta”, Esc).</summary>
        public event Action? CloseRequested;

        partial void OnIndexChanged(int value)
        {
            foreach (var n in new[] { nameof(Title), nameof(Lines), nameof(Progress), nameof(CanGoBack), nameof(IsLast), nameof(NextText) }) OnPropertyChanged(n);
        }

        [RelayCommand]
        public void Next()
        {
            if (IsLast) { Close(); return; }
            Index++;
        }

        [RelayCommand]
        public void Back()
        {
            if (Index > 0) Index--;
        }

        /// <summary>Closes the guide (Esc, the window's X): it is remembered as seen, so it does not open by itself again; it stays reachable from „Ce înseamnă?” and Home.</summary>
        [RelayCommand]
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _prefs.MarkFirstRunGuideSeen();
            OnPropertyChanged(nameof(Closed));
            CloseRequested?.Invoke();
        }

        /// <summary>„Nu mai arăta”: closes the guide and keeps it from opening by itself (the same store as the first showing).</summary>
        [RelayCommand]
        public void DontShowAgain() => Close();
    }

    /// <summary>Shows the guide once after the first sign-in, and on request from Home and „Ce înseamnă?”.</summary>
    public static class FirstRunGuideLauncher
    {
        /// <summary>Opens the guide window (modeless); replaceable by tests.</summary>
        public static Action Show { get; set; } = LogAnalyzer.UI.Views.FirstRunGuideWindow.ShowModeless;

        /// <summary>
        /// Shows the guide when this Windows user has not seen it. It is marked as seen before it opens, so a crash or a failure to show never makes it nag at every start;
        /// a failure to show it never stops the application (the guide stays reachable from „Ce înseamnă?”). Returns true when it was shown.
        /// </summary>
        public static bool ShowIfFirstTime(LanguagePreferences? prefs = null)
        {
            prefs ??= new LanguagePreferences();
            if (prefs.FirstRunGuideSeen()) return false;
            prefs.MarkFirstRunGuideSeen();
            try { Show(); return true; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
        }
    }
}
