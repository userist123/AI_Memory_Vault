using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// "Analiză AI (locală)": the optional explanation layer over a finished investigation. Part of the unclassified edition only;
    /// findings, timeline and graph never depend on it.
    /// </summary>
    /// <summary>One validated AI statement as the grid shows it. Only SUSTINUT has no warning; NESUSTINUT (listed with the rejected) is shown struck with its reason.</summary>
    public sealed class AiClaimRow
    {
        public string Section { get; init; } = "";
        public string Text { get; init; } = "";
        public string State { get; init; } = "";
        public string Warning { get; init; } = "";
        public int Citations { get; init; }
        public bool IsStruck { get; init; }

        public static AiClaimRow From(LogAnalyzer.Dfir.AI.AiStatement s)
        {
            var v = s.Validation;   // null = not validated: shown as NEVERIFICABIL, never as a supported statement
            var state = v?.State ?? LogAnalyzer.Dfir.AI.ClaimState.Neverificabil;
            return new AiClaimRow
            {
                Section = s.Section, Text = s.Text, State = LogAnalyzer.Dfir.AI.ClaimStates.ToSpec(state) + (v is null ? " (fără validare)" : ""), Citations = s.Citations.Count,
                Warning = state == LogAnalyzer.Dfir.AI.ClaimState.Sustinut ? "" : (v?.Reason is { Length: > 0 } r ? r : "afirmația nu a trecut validarea la nivel de caz"),
                IsStruck = state == LogAnalyzer.Dfir.AI.ClaimState.Nesustinut,
            };
        }
    }

    public partial class AiAnalysisViewModel : ObservableObject
    {
        private readonly InvestigationViewModel _investigation;

        /// <summary>R22.5: what the screen shows is the case-level validation of each statement, never the raw model statement.</summary>
        public ObservableCollection<AiClaimRow> AiStatements { get; } = new();
        public ObservableCollection<LogAnalyzer.Dfir.AI.RejectedStatement> AiRejected { get; } = new();

        // Local model (Ollama on this machine, loopback only).
        [ObservableProperty] private string _aiEndpoint = "http://127.0.0.1:11434";
        [ObservableProperty] private string _aiModel = "qwen3:30b-a3b";
        [ObservableProperty] private string _aiStatus = "Analiza AI folosește doar un model local (Ollama, adresă loopback) și doar rezultatele verificate ale cazului. Fiecare afirmație trebuie să citeze probe; rezultatul rămâne UNPROVEN până îl verificați.";

        public AiAnalysisViewModel(InvestigationViewModel investigation) => _investigation = investigation;

        [RelayCommand]
        private async Task AnalyzeWithLocalAi()
        {
            var result = _investigation.CurrentResult;
            if (result is null) { AiStatus = "Rulați întâi investigația."; return; }
            AiStatus = $"Modelul {AiModel} analizează catalogul cazului (poate dura câteva minute)…";
            try
            {
                var ai = await LogAnalyzer.Dfir.Windows.Investigation.AiCaseAnalysis.RunAsync(result, AiEndpoint.Trim(), AiModel.Trim());
                AiStatements.Clear();
                foreach (var s in ai.Accepted) AiStatements.Add(AiClaimRow.From(s));
                AiRejected.Clear();
                foreach (var s in ai.Rejected) AiRejected.Add(s);
                AiStatus = $"{ai.Validation?.SummaryRomanian ?? "Validarea afirmațiilor AI lipsește."} Model {ai.Model} (digest {ai.ModelDigest[..Math.Min(12, ai.ModelDigest.Length)]}). Salvat în Analysis/ai_reasoning.json și Analysis/ai_validation.json.";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
            {
                AiStatus = "Oprit: " + ex.Message;
            }
        }
    }
}
