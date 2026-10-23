using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Windows.Policy;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// "Politici": opens an owner policy (native or imported from GPO/LGPO/INF/audit.csv), takes it through the lifecycle and
    /// applies an approved policy only after the operator confirms the exact plan. Every step runs as the Windows account
    /// of this process; approval must come from another account using the same policy store.
    /// </summary>
    /// <summary>One registered policy as the library list shows it (2026-10-23: an earlier import must stay visible after a restart).</summary>
    public sealed record PolicyLibraryItem(string Id, string Version, string Title, string StateText, string DateText, string? LibraryPath)
    {
        public bool CanOpen => LibraryPath is not null;
        public string Name => string.IsNullOrWhiteSpace(Title) ? Id : Title;
        public string Summary => $"{Name} · versiunea {Version} · {StateText} · {DateText}";

        public static string StateName(PolicyStatus s) => s switch
        {
            PolicyStatus.Draft => "CIORNĂ (DRAFT)",
            PolicyStatus.Validated => "VALIDATĂ (VALIDATED)",
            PolicyStatus.Tested => "TESTATĂ (TESTED)",
            PolicyStatus.Approved => "APROBATĂ (APPROVED)",
            PolicyStatus.Deployed => "APLICATĂ (DEPLOYED)",
            PolicyStatus.Verified => "VERIFICATĂ (VERIFIED)",
            PolicyStatus.Retired => "RETRASĂ (RETIRED)",
            _ => s.ToString().ToUpperInvariant(),
        };
    }

    public partial class PolicyViewModel : ObservableObject
    {
        /// <summary>The policies registered in the store, newest first; filled from the store at start and after every step.</summary>
        public ObservableCollection<PolicyLibraryItem> Library { get; } = new();
        public bool HasLibrary => Library.Count > 0;
        /// <summary>The „Ce este / de ce contează / primul pas” card: only when nothing is open and the library is empty.</summary>
        public bool NothingOpenAndLibraryEmpty => NothingOpen && !HasLibrary;
        [ObservableProperty] private string _libraryNote = "";

        private void RefreshLibrary()
        {
            Library.Clear();
            try
            {
                foreach (var e in _bench.LibraryEntries())
                    Library.Add(new PolicyLibraryItem(e.Id, e.Version, e.Title, PolicyLibraryItem.StateName(e.Status),
                        e.LastChangeUtc is { } at ? at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("ro-RO")) : "—", e.LibraryPath));
                LibraryNote = Library.Count == 0 ? "" : $"Biblioteca are {Library.Count} politici înregistrate. Alegeți una ca să o redeschideți.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { LibraryNote = "Biblioteca de politici nu a putut fi citită: " + ex.Message; }
            OnPropertyChanged(nameof(HasLibrary));
            OnPropertyChanged(nameof(NothingOpenAndLibraryEmpty));
        }

        [RelayCommand]
        private void OpenLibraryEntry(PolicyLibraryItem? item)
        {
            if (item is null) return;
            if (item.LibraryPath is null) { Status = $"Copia din bibliotecă a politicii {item.Id} nu mai există; deschideți din nou fișierul sau backup-ul."; return; }
            Open(item.LibraryPath);
        }

        private readonly LogAnalyzer.Dfir.Windows.Policy.IRegistryValueWriter? _registryWriter;
        private PolicyWorkbench _bench;
        private PolicyDocument? _policy;
        private PolicyPlan? _plan;

        public ObservableCollection<ControlPlan> PlanRows { get; } = new();
        public ObservableCollection<string> Unsupported { get; } = new();
        public ObservableCollection<PolicyTransition> History { get; } = new();
        public ObservableCollection<ControlResult> Results { get; } = new();
        public ObservableCollection<LogAnalyzer.Dfir.Compliance.ControlAssessment> Compliance { get; } = new();

        [ObservableProperty] private string _storeRoot = PolicyWorkbench.DefaultRoot;
        [ObservableProperty] private string _policyInfo = "Nicio politică deschisă.";
        [ObservableProperty] private string _approvalReason = "";
        [ObservableProperty] private string _planSummary = "";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _status = "Deschideți o politică (.lapolicy/.yaml/.json, Registry.pol, .inf, audit.csv, text LGPO sau backup GPO). Nimic nu se aplică fără aprobare și confirmarea planului.";

        /// <summary>No policy is open: the page then shows the "Ce este / de ce contează / primul pas" panel (row 17).</summary>
        public bool NothingOpen => _policy is null;
        public string Operator => _bench.Operator;

        // Owner 2026-10-11: one "next step" with its reason, instead of ten buttons the operator has to know the order of.
        [ObservableProperty] private PolicyNextStep _nextStep = PolicyNextStep.For(false, null, "", false, false, 0, false);
        partial void OnApprovalReasonChanged(string value) => UpdateNextStep();

        private void UpdateNextStep() =>
            NextStep = PolicyNextStep.For(_policy is not null, _policy is null ? null : _bench.Store.Get(_policy), _bench.Operator,
                                          !string.IsNullOrWhiteSpace(ApprovalReason), _plan is not null, _plan?.ToChange ?? 0, _registryWriter is not null);

        [RelayCommand]
        private async Task DoNextStep()
        {
            switch (NextStep.Kind)
            {
                case PolicyStepKind.Open: OpenFile(); break;
                case PolicyStepKind.Register: Register(); break;
                case PolicyStepKind.Validate: Validate(); break;
                case PolicyStepKind.DryRun: await DryRun(); break;
                case PolicyStepKind.Approve: if (NextStep.CanAct) Approve(); else Status = NextStep.Text; break;
                case PolicyStepKind.ComputePlan: await ComputePlan(); break;
                case PolicyStepKind.Apply: ApplyPlan(); break;
                case PolicyStepKind.Assess: await AssessAgainstPolicy(); break;
                default: Status = NextStep.Text; break;
            }
            UpdateNextStep();
        }

        // Plain-language explanation of the selected plan row (owner 2026-10-12: „ce face fiecare politică în parte"): what the setting does, why it
        // matters on an isolated station, the recommended value, what the opened policy requires, what the current value means and where it is set.
        [ObservableProperty] private ControlPlan? _selectedPlanRow;
        [ObservableProperty] private PolicySettingExplanation? _selectedSettingExplanation;
        public bool HasSelectedSetting => SelectedSettingExplanation is not null;
        partial void OnSelectedPlanRowChanged(ControlPlan? value) =>
            SelectedSettingExplanation = value is null ? null : PolicySettingCatalogue.Explain(value);
        partial void OnSelectedSettingExplanationChanged(PolicySettingExplanation? value) => OnPropertyChanged(nameof(HasSelectedSetting));

        [RelayCommand]
        private void CopyExplanation()
        {
            if (SelectedSettingExplanation is null) { Status = "Alegeți întâi o setare din plan."; return; }
            try
            {
                if (LogAnalyzer.UI.Services.ExportAudit.Clipboard(LogAnalyzer.UI.Services.ExportAudit.LiveCaseId, "policy_setting_explanation", SelectedSettingExplanation.ToPlainText()) is { } refused) { Status = refused; return; }
                Status = "Explicația a fost copiată; o puteți lipi în procesul-verbal.";
            }
            catch (System.Runtime.InteropServices.COMException) { Status = "Clipboard-ul este ocupat de alt program; încercați din nou."; }
        }

        /// <param name="registryWriter">Null in the classified edition: policies can be opened, validated and compared but not applied to the registry.</param>
        public PolicyViewModel(LogAnalyzer.Dfir.Windows.Policy.IRegistryValueWriter? registryWriter = null)
        {
            _registryWriter = registryWriter;
            _bench = NewBench(null);
            try { RefreshLibrary(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the list is a convenience */ }
        }

        private PolicyWorkbench NewBench(string? root) =>
            new(root, LogAnalyzer.Dfir.Windows.Policy.WindowsSettingProviders.All(_registryWriter));

        partial void OnStoreRootChanged(string value)
        {
            try { _bench = NewBench(value); Refresh(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { Status = "Depozitul nu poate fi citit: " + ex.Message; }
        }

        [RelayCommand]
        private void OpenFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Deschideți o politică",
                Filter = "Politici (*.lapolicy;*.yaml;*.yml;*.json;*.pol;*.inf;*.csv;*.txt;*.zip)|*.lapolicy;*.yaml;*.yml;*.json;*.pol;*.inf;*.csv;*.txt;*.zip|Toate fișierele|*.*",
            };
            if (dlg.ShowDialog() == true) Open(dlg.FileName);
        }

        [RelayCommand]
        private void OpenFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Alegeți folderul unui backup GPO" };
            if (dlg.ShowDialog() == true) Open(dlg.FolderName);
        }

        private void Open(string path) => Step(() =>
        {
            var opened = _bench.Open(path);
            _policy = opened.Policy;
            _plan = null;
            Unsupported.Clear();
            foreach (var u in opened.Unsupported) Unsupported.Add(u);
            PlanRows.Clear();
            Results.Clear();
            PlanSummary = "";
            return $"Deschisă: {opened.Policy.Controls.Count} controale, {opened.Unsupported.Count} elemente neacceptate (listate). Copie în bibliotecă: {opened.LibraryPath}";
        });

        [RelayCommand] private void Register() => WithPolicy(p => { _bench.Register(p); return "Înregistrată ca ciornă (DRAFT)."; });
        [RelayCommand] private void Validate() => WithPolicy(p => { _bench.Validate(p); return "Validare structurală trecută: stare VALIDATĂ (VALIDATED)."; });

        [RelayCommand]
        private async Task DryRun()
        {
            if (_policy is null) { Status = "Ca să faceți acest pas, deschideți întâi politica: butonul „Deschide politica…” de sus."; return; }
            var p = _policy;
            IsBusy = true;
            try
            {
                var plan = await Task.Run(() => _bench.Plan(p));
                ShowPlan(plan);
                Step(() => { _bench.Store.MarkTested(p, _bench.Operator, plan.Controls.Count - plan.Unreadable, plan.Unreadable); return "Rulare de probă: toate controalele au fost citite. Stare: TESTATĂ (TESTED).";});
            }
            finally { IsBusy = false; }
        }

        [RelayCommand] private void Approve() => WithPolicy(p => { _bench.Approve(p, ApprovalReason); return $"Aprobată de {_bench.Operator}: stare APROBATĂ (APPROVED)."; });

        [RelayCommand]
        private async Task ComputePlan()
        {
            if (_policy is null) { Status = "Ca să faceți acest pas, deschideți întâi politica: butonul „Deschide politica…” de sus."; return; }
            var p = _policy;
            IsBusy = true;
            try
            {
                var plan = await Task.Run(() => _bench.Plan(p));
                ShowPlan(plan);
                Status = $"Plan calculat (doar citire). SHA-256 plan: {plan.Sha256[..16]}…";
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void ApplyPlan()
        {
            if (_policy is null || _plan is null) { Status = "Ca să aplicați, calculați întâi planul: butonul „Calculează planul” arată exact ce se va schimba."; return; }
            if (_plan.ToChange == 0) { Status = "Planul nu are nicio modificare de aplicat."; return; }
            var text = $"Se vor modifica {_plan.ToChange} setări pe {_plan.Station} ca {_bench.Operator}.\n" +
                       $"Politica {_plan.PolicyId} {_plan.PolicyVersion}\nSHA-256 politică: {_plan.PolicySha256}\nSHA-256 plan: {_plan.Sha256}\n\n" +
                       string.Join("\n", _plan.Controls.Where(c => c.Action == PlannedAction.Set).Take(15).Select(c => $"• {c.Setting.Display}: {c.Reason}")) +
                       (_plan.ToChange > 15 ? $"\n… și încă {_plan.ToChange - 15}" : "") + "\n\nConfirmați aplicarea exact a acestui plan?";
            if (MessageBox.Show(text, "Confirmare aplicare politică", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            { Status = "Aplicare anulată de operator."; return; }
            var plan = _plan;
            WithPolicy(p =>
            {
                var exec = _bench.Apply(p, plan, plan.Sha256);
                ShowResults(exec);
                _plan = null;
                return $"Execuția {exec.ExecutionId}: {Word(exec.Status)}; politica {(exec.PolicyCompliantAfter ? "este" : "NU este")} integral conformă după re-citire.";
            });
        }

        [RelayCommand]
        private void RollbackLast() => WithPolicy(p =>
        {
            var last = _bench.Executions(p).FirstOrDefault(e => e.Kind == "apply") ?? throw new PolicyLifecycleException("Nu există nicio execuție pentru această politică.");
            if (MessageBox.Show($"Se restaurează valorile dinainte de execuția {last.ExecutionId}. Continuați?", "Rollback", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return "Rollback anulat.";
            var rb = _bench.Rollback(p, last.ExecutionId);
            ShowResults(rb);
            return $"Rollback {rb.ExecutionId}: {Word(rb.Status)}.";
        });

        [RelayCommand] private Task AssessAgainstPolicy() => Assess(null);

        [RelayCommand]
        private async Task AssessAgainstBenchmark()
        {
            var dlg = new OpenFileDialog { Title = "Maparea benchmark → controale de politică (YAML)", Filter = "Mapare (*.yaml;*.yml)|*.yaml;*.yml|Toate fișierele|*.*" };
            if (dlg.ShowDialog() == true) await Assess(dlg.FileName);
        }

        private async Task Assess(string? mapping)
        {
            if (_policy is null) { Status = "Ca să faceți acest pas, deschideți întâi politica: butonul „Deschide politica…” de sus."; return; }
            var p = _policy;
            IsBusy = true;
            try
            {
                var (a, assessmentPath, oscalPath) = await Task.Run(() => _bench.Assess(p, mapping));
                Compliance.Clear();
                foreach (var c in a.Controls.OrderBy(c => c.Result == LogAnalyzer.Dfir.Compliance.ComplianceResult.Satisfied).ThenBy(c => c.BenchmarkControl)) Compliance.Add(c);
                Status = $"{a.Statement} Probe: {assessmentPath}; OSCAL: {Path.GetFileName(oscalPath)}";
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or PolicyLifecycleException or YamlDotNet.Core.YamlException)
            {
                Status = "Oprit: " + ex.Message;
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void OpenEvidenceFolder()
        {
            Directory.CreateDirectory(_bench.EvidenceDir);
            Process.Start(new ProcessStartInfo("explorer.exe", _bench.EvidenceDir) { UseShellExecute = true });
        }

        private static string Word(ExecutionStatus s) => s switch
        {
            ExecutionStatus.Verified => "VERIFIED (re-citirea confirmă)",
            ExecutionStatus.NotVerified => "NOT_VERIFIED (scris, dar nu s-a putut confirma)",
            ExecutionStatus.Failed => "FAILED",
            _ => "nimic aplicat",
        };

        private void ShowPlan(PolicyPlan plan)
        {
            _plan = plan;
            PlanRows.Clear();
            foreach (var c in plan.Controls) PlanRows.Add(c);
            SelectedPlanRow = PlanRows.FirstOrDefault();
            UpdateNextStep();
            PlanSummary = $"Conforme {plan.Compliant} · de modificat {plan.ToChange} · manual {plan.Manual} · necitite {plan.Unreadable}";
        }

        private void ShowResults(ExecutionRecord e)
        {
            Results.Clear();
            foreach (var r in e.Results) Results.Add(r);
        }

        private void WithPolicy(Func<PolicyDocument, string> action)
        {
            if (_policy is null) { Status = "Ca să faceți acest pas, deschideți întâi politica: butonul „Deschide politica…” de sus."; return; }
            var p = _policy;
            Step(() => action(p));
        }

        private void Step(Func<string> action)
        {
            try { Status = action(); }
            catch (Exception ex) when (ex is PolicyLifecycleException or LogAnalyzer.Dfir.Auth.HostChangeNotAuthorizedException or FormatException or InvalidDataException or NotSupportedException or IOException
                                          or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
            {
                Status = "Oprit: " + ex.Message;
            }
            Refresh();
        }

        private void Refresh()
        {
            OnPropertyChanged(nameof(Operator));
            UpdateNextStep();
            History.Clear();
            OnPropertyChanged(nameof(NothingOpen));
            RefreshLibrary();
            if (_policy is null) { PolicyInfo = "Nicio politică deschisă."; return; }
            var rec = _bench.Store.Get(_policy);
            if (rec is not null) foreach (var h in rec.History) History.Add(h);
            PolicyInfo = $"{_policy.Id} {_policy.Version} — {_policy.Title} · autor {_policy.Author} · SHA-256 {_policy.Sha256[..16]}… · " +
                         $"stare {(rec is null ? "neînregistrată" : rec.Status.ToString().ToUpperInvariant())}" +
                         (rec?.RegisteredBy is { } by ? $" · înregistrată de {by}" : "");
        }
    }
}
