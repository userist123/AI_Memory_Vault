using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Detection;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// Master plan 6.8, „Laborator de reguli” tab: write or paste a Sigma, YARA or IOC rule, test it on the open case exactly as the engine would
    /// load it, keep a version as the reference and compare a new version with it, then add a rule that loads to the case's own rules.
    /// </summary>
    public partial class InvestigationViewModel
    {
        public IReadOnlyList<string> RuleKinds { get; } = ["Sigma", "YARA", "IOC (listă CSV)"];
        public ObservableCollection<string> RuleLabLines { get; } = new();
        [ObservableProperty] private int _ruleKindIndex;
        [ObservableProperty] private string _ruleName = "";
        [ObservableProperty] private string _ruleText = "";
        [ObservableProperty] private string _ruleLabSummary = "Scrieți sau lipiți o regulă și apăsați „Testează pe acest caz”.";
        [ObservableProperty] private bool _ruleLabProblem;
        private RuleLabRun? _ruleRun, _ruleBaseline;
        private string _ruleRunText = "";

        private RuleLabKind RuleKind => RuleKindIndex switch { 1 => RuleLabKind.Yara, 2 => RuleLabKind.Ioc, _ => RuleLabKind.Sigma };

        [RelayCommand]
        private void TestRule()
        {
            if (_result is null) { RuleLabSummary = "Deschideți întâi un caz: regula se testează pe cronologia și probele lui."; return; }
            var ws = _result.Case;
            IReadOnlyList<LogAnalyzer.Dfir.Model.EvidenceItem> files = [];
            try { files = ws.LoadEvidence().Where(e => ws.TryFullPath(e.StoredPath, out var p) && File.Exists(p) && new FileInfo(p).Length <= 64L * 1024 * 1024).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            _ruleRun = RuleLab.Test(RuleKind, RuleName, RuleText, _result.Timeline, files, e => File.ReadAllBytes(ws.FullPath(e.StoredPath)));
            _ruleRunText = RuleText;
            RuleLabProblem = !_ruleRun.Valid;
            RuleLabSummary = (RuleLabProblem ? "Refuzat: " : "Test: ") + _ruleRun.Summary + ".";
            RuleLabLines.Clear();
            foreach (var g in _ruleRun.Matches.GroupBy(m => m.EvidenceId).OrderByDescending(g => g.Count()))
                RuleLabLines.Add($"{g.Key}: {LogAnalyzer.Dfir.Language.RoText.Count(g.Count(), "potrivire", "potriviri")}");
            foreach (var m in _ruleRun.Matches.Take(300)) RuleLabLines.Add($"{m.TimeUtc:yyyy-MM-dd HH:mm:ss}  {m.EvidenceId}  {m.Locator}  {m.Match}");
            if (_ruleRun.Matches.Count > 300) RuleLabLines.Add($"… încă {_ruleRun.Matches.Count - 300} potriviri");
        }

        [RelayCommand]
        private void SaveRuleBaseline()
        {
            if (_ruleRun is not { Valid: true } || _ruleRunText != RuleText) { RuleLabSummary = "Testați întâi regula așa cum este acum; referința este ultima versiune testată fără erori."; return; }
            _ruleBaseline = _ruleRun;
            RuleLabSummary = $"Versiunea testată este acum referința ({_ruleBaseline.Summary}). Modificați regula, testați și comparați.";
        }

        [RelayCommand]
        private void CompareRuleWithBaseline()
        {
            if (_ruleBaseline is null) { RuleLabSummary = "Nu există o referință: testați o versiune și apăsați „Păstrează ca referință”."; return; }
            if (_ruleRun is not { Valid: true } || _ruleRunText != RuleText) { RuleLabSummary = "Testați întâi versiunea nouă."; return; }
            var d = RuleLab.Diff(_ruleBaseline, _ruleRun);
            RuleLabSummary = "Comparație: " + d.Summary + ".";
            RuleLabLines.Clear();
            foreach (var m in d.Added.Take(200)) RuleLabLines.Add($"+ nouă: {m.EvidenceId} {m.Locator} {m.Match}");
            foreach (var m in d.Removed.Take(200)) RuleLabLines.Add($"− pierdută: {m.EvidenceId} {m.Locator} {m.Match}");
        }

        [RelayCommand]
        private void AddRuleToCase()
        {
            if (_result is null) { RuleLabSummary = "Deschideți întâi un caz."; return; }
            if (_ruleRun is null || _ruleRunText != RuleText) { RuleLabSummary = "Testați întâi regula așa cum este acum; se adaugă doar o regulă testată."; return; }
            try
            {
                var r = RuleLab.AddToCase(_result.Case, RuleKind, RuleName, RuleText, _ruleRun);
                RuleLabSummary = r.Ok ? $"Regula a fost adăugată în regulile cazului ({RuleLab.Folder(RuleKind)}/{RuleLab.SafeName(RuleName, RuleKind)}); analiza următoare a cazului o folosește." : "Refuzat: " + r.Reason;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { RuleLabSummary = "Regula nu a putut fi scrisă: " + ex.Message; }
        }
    }
}
