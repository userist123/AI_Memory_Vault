using LogAnalyzer.Dfir.Windows.Audit;
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// The glossary window's list and search box as a view model: the box is bound with UpdateSourceTrigger=PropertyChanged, so the list filters while typing
    /// (a real-PC UIA test found that typing into the old code-behind box did not filter).
    /// </summary>
    public sealed partial class GlossarySearch : ObservableObject
    {
        private readonly IReadOnlyList<GlossaryTerm> _all = TermCatalog.All.ToList();

        public GlossarySearch(string? initial = null) { _query = initial ?? ""; Results = Filter(_query); }

        [ObservableProperty] private string _query;
        [ObservableProperty] private IReadOnlyList<GlossaryTerm> _results;
        [ObservableProperty] private string _summary = "";

        partial void OnQueryChanged(string value) => Results = Filter(value);

        partial void OnResultsChanged(IReadOnlyList<GlossaryTerm> value) =>
            Summary = value.Count == 0 ? "Niciun termen nu se potrivește căutării." : string.IsNullOrWhiteSpace(Query) ? $"{value.Count} termeni." : $"{value.Count} termeni potriviți.";

        private IReadOnlyList<GlossaryTerm> Filter(string text)
        {
            var q = (text ?? "").Trim();
            if (q.Length == 0) return _all.OrderBy(t => t.Romanian).ToList();
            return _all.Where(t => t.Spellings.Any(s => string.Equals(s, q, StringComparison.OrdinalIgnoreCase))).Concat(
                   _all.Where(t => t.Romanian.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Technical.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || t.Spellings.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase))
                                   || t.Explanation.Contains(q, StringComparison.OrdinalIgnoreCase) || t.English.Contains(q, StringComparison.OrdinalIgnoreCase))
                      .OrderBy(t => t.Romanian)).Distinct().ToList();
        }
    }
}
