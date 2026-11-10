using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Memory;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Memory;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>Master plan 6.6, „Memorie (Volatility)” tab: the approved Volatility 3 run on a memory image of the case, then a re-analysis that reads its outputs.</summary>
    public partial class InvestigationViewModel
    {
        public ObservableCollection<EvidenceItem> MemoryImages { get; } = new();
        public ObservableCollection<string> MemoryRunLines { get; } = new();
        [ObservableProperty] private EvidenceItem? _selectedMemoryImage;
        [ObservableProperty] private string _memoryToolLine = "";

        internal void FillMemoryImages()
        {
            MemoryImages.Clear();
            if (_result is null) { MemoryToolLine = ""; return; }
            foreach (var e in _result.Case.LoadEvidence().Where(VolatilityRunner.LooksLikeMemoryImage)) MemoryImages.Add(e);
            SelectedMemoryImage = MemoryImages.FirstOrDefault();
            MemoryToolLine = "Se verifică unealta Volatility aprobată …";
            bool noImage = MemoryImages.Count == 0;
            _ = CheckVolatilityToolAsync(noImage);
        }

        /// <summary>The tool check hashes the whole tool and symbol folders: off the UI thread.</summary>
        private async Task CheckVolatilityToolAsync(bool noImage)
        {
            var (line, _) = await Task.Run(() =>
            {
                var profile = VolatilityToolProfile.Load(null, out var problem);
                System.Collections.Generic.List<string> refused;
                try { refused = VolatilityRunner.CheckTool(profile, problem); }
                catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.InvalidOperationException)
                { refused = ["unealta nu poate fi verificată: " + ex.Message]; }
                return (refused.Count == 0
                    ? $"Volatility aprobat: {profile!.Executable} (folderul uneltei și simbolurile corespund manifestelor aprobate și sunt protejate). Pluginuri: {string.Join(", ", VolatilityRunner.AllowedPlugins)}. Rulează fără rețea."
                    : "Volatility nu poate rula: " + string.Join(" ", refused), refused.Count);
            });
            MemoryToolLine = line + (noImage ? " Cazul nu are o imagine de memorie (.raw, .mem, .vmem, .dmp, .lime, .img, .bin); adăugați-o ca probă." : "");
        }

        [RelayCommand]
        private async Task RunVolatility()
        {
            if (IsBusy) { MemoryRunLines.Add("O analiză rulează deja; așteptați să se încheie."); return; }
            if (_result is null || SelectedMemoryImage is not { } image) { MemoryRunLines.Add("Alegeți imaginea de memorie din caz."); return; }
            var ws = _result.Case;
            var profile = VolatilityToolProfile.Load(null, out var problem);
            IsBusy = true;
            MemoryRunLines.Clear();
            MemoryRunLines.Add($"Rulează Volatility pe {image.EvidenceId} ({image.OriginalName}) …");
            _cts = new System.Threading.CancellationTokenSource();
            var ct = _cts.Token;
            VolatilityRunResult r;
            try { r = await Task.Run(() => VolatilityRunner.Run(ws, image.EvidenceId, VolatilityRunner.AllowedPlugins, profile, problem, ct)); }
            catch (System.OperationCanceledException) { MemoryRunLines.Add("Oprit de operator; ieșirile neterminate nu au fost păstrate."); IsBusy = false; return; }
            catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.InvalidOperationException or System.ComponentModel.Win32Exception)
            { MemoryRunLines.Add("Eroare: " + ex.Message); IsBusy = false; return; }
            IsBusy = false;
            foreach (var x in r.Refused) MemoryRunLines.Add("Refuzat: " + x);
            if (r.Refused.Count > 0) MemoryToolLine = "Volatility nu poate rula acum (vezi motivele de mai jos); unealta sau profilul s-au schimbat după aprobare sau nu sunt protejate.";
            foreach (var p in r.Plugins)
                MemoryRunLines.Add(p.OutputEvidenceId.Length > 0
                    ? $"{p.Plugin}: ieșire păstrată ca proba {p.OutputEvidenceId} (derivată din {image.EvidenceId}), {p.Duration.TotalSeconds:0} s"
                    : $"Problemă: {p.Plugin}: {p.Problem}");
            if (r.Plugins.Any(p => p.OutputEvidenceId.Length > 0))
            {
                MemoryRunLines.Add("Reanalizez cazul ca ieșirile să intre în cronologie și în regulile de memorie …");
                await Reanalyze();
                MemoryRunLines.Add("Gata: rezultatele memoriei sunt în „Constatări” și „Cronologie”.");
            }
        }
    }
}
