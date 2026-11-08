using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// Master plan 6.12, „Pachete de informații despre amenințări” on the Politici page: put a package brought on approved media in waiting
    /// (verified on the copy), approve it with a reason (an administrator; activates it), discard it, or go back to an earlier approved version.
    /// The page shows the verification lines and the chained history; nothing in a package is used before approval.
    /// </summary>
    public partial class ThreatIntelPackagesViewModel : ObservableObject
    {
        private readonly Func<ThreatIntelStore> _store;
        private readonly Func<ICmsDetachedVerifier> _verifier;
        private readonly Func<QualifiedSignatureTrust> _trust;
        private readonly Func<IReadOnlyList<string>> _trusted;
        private readonly Func<bool> _mayAdminister;

        public ObservableCollection<StagedPackage> StagedRows { get; } = new();
        public ObservableCollection<string> CheckLines { get; } = new();
        public ObservableCollection<string> HistoryLines { get; } = new();

        [ObservableProperty] private string _activeLine = "";
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private string _sourceFolder = "";
        [ObservableProperty] private string _reason = "";
        [ObservableProperty] private string _rollbackVersion = "";
        [ObservableProperty] private StagedPackage? _selectedStaged;

        public ThreatIntelPackagesViewModel() : this(() => ThreatIntelFlows.Store(), () => ThreatIntelFlows.Verifier, ThreatIntelFlows.Trust, ThreatIntelFlows.TrustedPublishers,
            () => OperatorIdentity.MayEditAdministration) { }

        public ThreatIntelPackagesViewModel(Func<ThreatIntelStore> store, Func<ICmsDetachedVerifier> verifier, Func<QualifiedSignatureTrust> trust, Func<IReadOnlyList<string>> trusted, Func<bool> mayAdminister)
        {
            _store = store; _verifier = verifier; _trust = trust; _trusted = trusted; _mayAdminister = mayAdminister;
        }

        private static string Me => OperatorIdentity.Who;

        [RelayCommand]
        public void Refresh()
        {
            try
            {
                var s = _store();
                var active = s.LoadActive(_verifier(), _trust(), _trusted());
                ActiveLine = (ThreatIntelFlows.StoreProblem.Length > 0 ? "ATENȚIE: " + ThreatIntelFlows.StoreProblem + ". " : "") + (active.RulesRoot is not null ? $"Activ: „{active.PackageId}” versiunea {active.Version} (verificat acum: semnătură, semnatar, amprentele fișierelor)." + (active.Problem.Length > 0 ? " " + active.Problem + "." : "")
                                                          : "Niciun pachet folosit: " + active.Problem + ".");
                StagedRows.Clear();
                foreach (var p in s.Staged()) StagedRows.Add(p);
                HistoryLines.Clear();
                var chain = s.VerifyHistory();
                if (chain.Status == LogAnalyzer.Dfir.Case.ChainStatus.Broken) HistoryLines.Add("ISTORICUL ESTE COMPROMIS: " + chain.Reason);
                foreach (var e in s.HistoryEvents().AsEnumerable().Reverse().Take(100))
                    HistoryLines.Add($"{e.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {e.Actor}  {Word(e.Kind)}  {e.PackageId} v{e.Version}  {e.Detail}");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                ActiveLine = "Depozitul pachetelor nu a putut fi citit: " + ex.Message;
            }
        }

        private static string Word(string kind) => kind switch
        {
            "staged" => "pus în așteptare", "approved" => "APROBAT și activat", "refused" => "refuzat la verificare", "discarded" => "renunțat",
            "rolled_back" => "REVENIRE la versiunea", "approval_refused" => "aprobare refuzată", _ => kind,
        };

        private void Show(ThreatIntelCheck c)
        {
            CheckLines.Clear();
            foreach (var l in c.Lines) CheckLines.Add((l.Passed switch { true => "✓ ", false => "✗ ", _ => "⚠ " }) + l.Text);
        }

        [RelayCommand]
        private void Stage()
        {
            if (string.IsNullOrWhiteSpace(SourceFolder) || !System.IO.Directory.Exists(SourceFolder.Trim())) { Status = "Alegeți folderul pachetului (cu ti_package.json și ti_package.json.p7s)."; return; }
            try
            {
                var (check, staged) = _store().Stage(SourceFolder.Trim(), Me, _verifier(), _trust(), _trusted());
                Show(check);
                Status = staged is null ? "Refuzat: " + check.Headline + ". Pachetul nu a fost pus în așteptare."
                                        : $"Pus în așteptare ({staged.Id}): {check.Headline}. Un administrator îl aprobă cu un motiv; până atunci nu se folosește.";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Status = "Pachetul nu a putut fi citit: " + ex.Message;
            }
            Refresh();
        }

        [RelayCommand]
        private void Approve()
        {
            if (SelectedStaged is not { } p) { Status = "Alegeți un pachet din așteptare."; return; }
            if (!_mayAdminister()) { Status = "Refuzat: doar un administrator aprobă un pachet de informații despre amenințări."; return; }
            try
            {
                var (r, check) = _store().Approve(p.Id, Me, Reason, _verifier(), _trust(), _trusted());
                if (check is not null) Show(check);
                Status = r.Ok ? $"Aprobat și activat: „{p.PackageId}” versiunea {p.Version}. Analizele următoare îl folosesc." : "Refuzat: " + r.Reason;
                if (r.Ok) Reason = "";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Status = "Aprobarea nu s-a putut face: " + ex.Message; }
            Refresh();
        }

        [RelayCommand]
        private void Discard()
        {
            if (SelectedStaged is not { } p) { Status = "Alegeți un pachet din așteptare."; return; }
            try { var r = _store().Discard(p.Id, Me, Reason); Status = r.Ok ? "Pachetul a fost scos din așteptare." : "Refuzat: " + r.Reason; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Status = "Nu s-a putut renunța: " + ex.Message; }
            Refresh();
        }

        [RelayCommand]
        private void Rollback()
        {
            if (!_mayAdminister()) { Status = "Refuzat: doar un administrator revine la o versiune anterioară."; return; }
            var parts = RollbackVersion.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1].TrimStart('v', 'V'), out var v)) { Status = "Scrieți pachetul și versiunea, de exemplu: CERT-RO v3"; return; }   // ids are letters, digits, . _ - (no spaces)
            try { var r = _store().Rollback(parts[0], v, Me, Reason); Status = r.Ok ? $"Revenire făcută: „{parts[0]}” versiunea {v} este activă." : "Refuzat: " + r.Reason; if (r.Ok) Reason = ""; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Status = "Revenirea nu s-a putut face: " + ex.Message; }
            Refresh();
        }
    }
}
