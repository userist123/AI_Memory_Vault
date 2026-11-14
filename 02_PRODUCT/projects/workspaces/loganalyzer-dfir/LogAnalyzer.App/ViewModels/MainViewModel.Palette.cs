using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Core.Services.Edition;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// „Salt rapid” (Ctrl+K): the pages THIS module and licence offer (the role's main pages and the „Avansat” menu), filtered by what is typed;
    /// Enter opens the first match through <see cref="OpenTab"/> (licence and module checks). Lab VM 2026-11-06: the palette was a fixed list of
    /// seven old pages with hard-coded tab numbers, shared its text with the event search and could not reach Active Directory.
    /// </summary>
    public partial class MainViewModel
    {
        /// <summary>The text typed in the palette (its own, not the event search).</summary>
        [ObservableProperty] private string _paletteQuery = string.Empty;

        /// <summary>At most this many pages are listed.</summary>
        public const int PaletteMaxMatches = 12;

        /// <summary>The pages that match the typed text, in menu order.</summary>
        public IReadOnlyList<NavigationEntry> PaletteMatches => PaletteFilter(PalettePages(), PaletteQuery);

        partial void OnPaletteQueryChanged(string value) => OnPropertyChanged(nameof(PaletteMatches));

        partial void OnIsCommandPaletteVisibleChanged(bool value)
        {
            if (value) { PaletteQuery = string.Empty; OnPropertyChanged(nameof(PaletteMatches)); }
        }

        /// <summary>Every page the sidebar can open: the role's enabled main pages, then the „Avansat” pages, one entry per tab.</summary>
        private IEnumerable<NavigationEntry> PalettePages() =>
            RoleNavigation.Where(n => n.Enabled)
                .Concat(AdvancedMenu.SelectMany(s => s.Pages).Select(p => new NavigationEntry(p.Title, p.Tab, true, "")))
                .GroupBy(n => n.Tab).Select(g => g.First());

        /// <summary>Case- and diacritic-insensitive: „active”, „investigatie”, „Investigație” all match; every word typed must appear in the title.</summary>
        public static IReadOnlyList<NavigationEntry> PaletteFilter(IEnumerable<NavigationEntry> pages, string query)
        {
            var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return pages.Where(p => words.All(w => Fold(p.Title).Contains(w, StringComparison.Ordinal))).Take(PaletteMaxMatches).ToList();
        }

        private static string Fold(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in (s ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        /// <summary>Opens a page from the palette and closes it; an „Avansat” page switches the menu to Expert so the sidebar shows where it is.</summary>
        [RelayCommand]
        private void PaletteOpen(int tab)
        {
            IsCommandPaletteVisible = false;
            // the level changes only for a page that will open (review round 2: a refused page left Expert switched on and saved)
            if (CanOpenTab(tab) && !RoleNavigation.Any(n => n.Tab == tab)) { IsExpertLevel = true; AdvancedPagesOpen = true; }
            OpenTab(tab);
        }

        /// <summary>Enter in the palette: the first match, when there is one.</summary>
        [RelayCommand]
        private void PaletteOpenFirst()
        {
            if (PaletteMatches.FirstOrDefault() is { } first) PaletteOpen(first.Tab);
        }
    }
}
