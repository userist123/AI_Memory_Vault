using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Dfir.Detection;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>Master plan 6.7, „Executabile” tab: the evidence files that are Windows programs, read statically, with the signature check of this station.</summary>
    public partial class InvestigationViewModel
    {
        public ObservableCollection<PeTriageRow> PeRows { get; } = new();
        public ObservableCollection<string> PeDetail { get; } = new();
        [ObservableProperty] private PeTriageRow? _selectedPe;
        [ObservableProperty] private string _peLine = "Niciun executabil printre probe.";

        internal void FillExecutables()
        {
            PeRows.Clear(); PeDetail.Clear();
            if (_result is null) { PeLine = "Niciun executabil printre probe."; return; }
            foreach (var p in _result.PeTriage.OrderByDescending(p => p.Report.Indicators.Count)) PeRows.Add(p);
            PeLine = PeRows.Count == 0
                ? "Niciun executabil printre probe. Un program suspect adus de pe stația afectată se importă în caz ca probă; aici se citește fără să fie rulat."
                : $"{LogAnalyzer.Dfir.Language.RoText.Count(PeRows.Count, "executabil", "executabile")}, citite fără să fie rulate. Indicatorii sunt observații cu motivul lor, nu verdicte.";
            SelectedPe = PeRows.FirstOrDefault();
        }

        partial void OnSelectedPeChanged(PeTriageRow? value)
        {
            PeDetail.Clear();
            if (value is null) return;
            var r = value.Report;
            PeDetail.Add($"{value.OriginalName} ({value.EvidenceId}) · {r.Machine}{(r.IsDll ? " · bibliotecă (DLL)" : "")}{(r.Subsystem.Length > 0 ? " · " + r.Subsystem : "")} · {r.Size:N0} octeți");
            PeDetail.Add($"SHA-256 {r.Sha256} · MD5 {r.Md5}" + (r.ImpHash.Length > 0 ? $" · imphash (ordinale netraduse) {r.ImpHash}" : ""));
            PeDetail.Add("Semnătura (verificată pe această stație, fără rețea): " + (value.SignatureValid ? "validă" : value.SignatureStatus) +
                         (value.SignatureKind is "none" or "" ? "" : $" · {value.SignatureKind}") + (value.Signer.Length > 0 ? $" · {value.Signer}" : ""));
            PeDetail.Add("Momentul compilării (scris de linker, se poate falsifica): " + (r.LinkerTimestampUtc is { } ts ? ts.ToString("yyyy-MM-dd HH:mm") + " UTC" : "lipsă"));
            foreach (var i in r.Indicators) PeDetail.Add($"{i.Text} — {i.WhyItMatters}");
            PeDetail.Add("Secțiuni: " + string.Join("; ", r.Sections.Select(s => $"{s.Name} entropie {s.Entropy}{(s.Executable ? " X" : "")}{(s.Writable ? " W" : "")}")));
            foreach (var imp in r.Imports.Take(40)) PeDetail.Add($"Importă din {imp.Dll}: {string.Join(", ", imp.Functions.Take(25))}{(imp.Functions.Count > 25 ? $" … (+{imp.Functions.Count - 25})" : "")}");
            if (r.Imports.Count > 40) PeDetail.Add($"… încă {r.Imports.Count - 40} biblioteci importate");
        }
    }
}
