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
    public partial class AiAnalysisViewModel : ObservableObject
    {
        private readonly InvestigationViewModel _investigation;

        public ObservableCollection<LogAnalyzer.Dfir.AI.AiStatement> AiStatements { get; } = new();
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
                foreach (var s in ai.Accepted) AiStatements.Add(s);
                AiRejected.Clear();
                foreach (var s in ai.Rejected) AiRejected.Add(s);
                AiStatus = $"{ai.Accepted.Count} afirmații acceptate (UNPROVEN, cu citări), {ai.Rejected.Count} respinse de verificări. Model {ai.Model} (digest {ai.ModelDigest[..Math.Min(12, ai.ModelDigest.Length)]}). Salvat în Analysis/ai_reasoning.json.";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
            {
                AiStatus = "Oprit: " + ex.Message;
            }
        }
    }
}
