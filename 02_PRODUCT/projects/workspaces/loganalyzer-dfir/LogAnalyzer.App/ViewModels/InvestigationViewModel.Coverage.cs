using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Dfir.Coverage;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>One cell of the coverage heatmap: a source on a day. Level 0 = no events, 1..4 = growing counts (log scale).</summary>
    public sealed record CoverageCell(string Tooltip, int Level, bool IsGap);

    public sealed record CoverageHeatRow(string Source, string Totals, IReadOnlyList<CoverageCell> Cells);

    /// <summary>Master plan 6.4, „Acoperire în timp” tab: which source wrote on which day of the case, and the silent stretches as candidate gaps.</summary>
    public partial class InvestigationViewModel
    {
        public ObservableCollection<CoverageHeatRow> CoverageRows { get; } = new();
        public ObservableCollection<string> CoverageGapLines { get; } = new();
        [ObservableProperty] private string _coverageLine = "";
        [ObservableProperty] private string _coverageAxis = "";

        internal void FillCoverageTimeline()
        {
            CoverageRows.Clear(); CoverageGapLines.Clear();
            if (_result is null || _result.Timeline.Count == 0) { CoverageLine = "Cronologia cazului este goală."; CoverageAxis = ""; return; }
            var s = _result.Case.Info.Scope;
            var c = CoverageTimeline.Build(_result.Timeline, s.PeriodFromUtc, s.PeriodToUtc);
            foreach (var r in c.Rows.Take(40))
            {
                var gapDays = r.Gaps.SelectMany(g => Enumerable.Range(g.Start, g.Days)).ToHashSet();
                var cells = r.PerDay.Select((n, i) => new CoverageCell($"{r.Source} · {c.Day(i):yyyy-MM-dd} · {n:N0} evenimente" + (gapDays.Contains(i) ? " · lacună posibilă" : ""),
                    n == 0 ? 0 : n < 10 ? 1 : n < 100 ? 2 : n < 1000 ? 3 : 4, gapDays.Contains(i))).ToList();
                CoverageRows.Add(new CoverageHeatRow(r.Source, $"{r.Total:N0}", cells));
            }
            foreach (var l in c.GapLines()) CoverageGapLines.Add(l);
            CoverageAxis = c.Days == 0 ? "" : $"de la {c.FirstDay:yyyy-MM-dd} (stânga) până la {c.Day(c.Days - 1):yyyy-MM-dd} (dreapta), o celulă pe zi, UTC";
            CoverageLine = $"{LogAnalyzer.Dfir.Language.RoText.Count(c.Rows.Count, "sursă", "surse")} în {LogAnalyzer.Dfir.Language.RoText.Count(c.Days, "zi", "zile")}" +
                           (c.Rows.Count > 40 ? " (primele 40 de surse după volum)" : "") + (c.Truncated ? $"; perioada depășește {CoverageTimeline.MaxDays} de zile, se arată ultimele" : "") +
                           (c.WithoutTime > 0 ? $"; {c.WithoutTime:N0} evenimente fără oră UTC nu apar pe hartă" : "") +
                           $"; {LogAnalyzer.Dfir.Language.RoText.Count(CoverageGapLines.Count, "lacună posibilă", "lacune posibile")} (nu dovezi de ștergere).";
        }
    }
}
