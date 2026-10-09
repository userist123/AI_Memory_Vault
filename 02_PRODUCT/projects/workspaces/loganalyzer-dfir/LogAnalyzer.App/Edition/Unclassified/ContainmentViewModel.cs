using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Containment;
using LogAnalyzer.Dfir.Windows.Native;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// "Izolare procese": find suspicious third-party programs, cut only their network access, scan them, keep the
    /// incident in the live case and export it as PDF. Works in both modes (it never opens a connection).
    /// </summary>
    public partial class ContainmentViewModel : ObservableObject
    {
        private CaseWorkspace? _case;
        private ProcessContainmentService? _service;
        private System.Windows.Threading.DispatcherTimer? _autoTimer;
        private readonly TrustedProgramStore _trusted = new();

        public ObservableCollection<SuspectProcess> Suspects { get; } = new();
        public ObservableCollection<ProcessIncident> Incidents { get; } = new();

        [ObservableProperty] private SuspectProcess? _selectedSuspect;
        [ObservableProperty] private ProcessIncident? _selectedIncident;
        [ObservableProperty] private string _incidentDetail = "Selectați un incident pentru detalii.";
        [ObservableProperty] private string _status = "Apăsați „Caută procese suspecte”.";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _suspendOnContain;
        [ObservableProperty] private bool _autoContainEnabled;
        [ObservableProperty] private string _auditStatus = "";
        [ObservableProperty] private string _manualProgramPath = "";

        public bool IsAdministrator { get; } = CheckAdmin();
        public bool HasAdminWarning => !IsAdministrator;
        public string AdminWarning => IsAdministrator ? "" :
            "Aplicația nu rulează ca administrator: regulile de firewall, Prefetch și unele chei de autostart nu sunt accesibile. Reporniți LogAnalyzer cu „Run as administrator”.";
        public string CaseRoot => _case?.Root ?? "";

        partial void OnSelectedIncidentChanged(ProcessIncident? value) =>
            IncidentDetail = value is null ? "Selectați un incident pentru detalii." : IncidentDetailFormatter.Format(value);

        partial void OnAutoContainEnabledChanged(bool value)
        {
            if (value)
            {
                _autoTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
                _autoTimer.Tick -= AutoTick;
                _autoTimer.Tick += AutoTick;
                _autoTimer.Start();
                Status = "Izolare automată activă: la fiecare minut, procesele cu încredere mare (nesemnate, din locații în care orice utilizator poate scrie, cu conexiuni în Internet) sunt izolate de rețea. Nu sunt suspendate.";
                AutoTick(this, EventArgs.Empty);
            }
            else
            {
                _autoTimer?.Stop();
                Status = "Izolare automată oprită.";
            }
        }

        private void EnsureCase()
        {
            if (_service is not null) return;
            _case = LogAnalyzer.UI.Services.LiveCase.Get();
            _service = new ProcessContainmentService(new WindowsFirewallController(), new ProcessScanner(), _case,
                ProcessSuspension.Suspend, ProcessSuspension.Resume);
            OnPropertyChanged(nameof(CaseRoot));
        }

        [RelayCommand]
        private void Load()
        {
            EnsureCase();
            Incidents.Clear();
            foreach (var i in _service!.LoadAll()) Incidents.Add(i);
            var (on, detail) = BlockedConnectionLog.IsFailureAuditEnabled();
            AuditStatus = on
                ? "Auditul conexiunilor blocate este activ: încercările programelor izolate sunt înregistrate (evenimentul 5157)."
                : $"Auditul conexiunilor blocate NU este activ ({detail}). Fără el, Windows nu înregistrează unde încearcă să se conecteze un program izolat.";
        }

        [RelayCommand]
        private async Task ScanSuspects()
        {
            IsBusy = true;
            Status = "Se analizează procesele care rulează…";
            try
            {
                var found = await Task.Run(() => SuspiciousProcessDetector.Scan(_trusted));
                Suspects.Clear();
                foreach (var s in found) Suspects.Add(s);
                Status = found.Count == 0
                    ? "Niciun proces suspect găsit."
                    : $"{found.Count} procese de verificat; {found.Count(s => s.EligibleForAutoContainment)} cu încredere mare (neaprobate de operator).";
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task ContainSelected()
        {
            if (SelectedSuspect is not { } s) { Status = "Selectați un proces din listă."; return; }
            var question = $"Izolați programul?\n\n{s.Path}\nPID {s.Pid}\n\nMotive: {string.Join("; ", s.Reasons)}\n\n" +
                           "Se blochează accesul la rețea DOAR pentru acest program (restul PC-ului rămâne conectat)" +
                           (SuspendOnContain ? " și procesul este suspendat." : ".") +
                           "\nApoi programul este scanat și incidentul este salvat în caz. Acțiunea se poate anula.";
            if (MessageBox.Show(question, "Izolare program", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await ContainAsync(s.Path, s.Pid, $"operator: {string.Join("; ", s.Reasons)}", SuspendOnContain);
        }

        [RelayCommand]
        private async Task ContainManual()
        {
            var path = ManualProgramPath.Trim().Trim('"');
            if (!File.Exists(path))
            {
                var dlg = new OpenFileDialog { Filter = "Programe (*.exe;*.dll;*.scr)|*.exe;*.dll;*.scr|Toate fișierele|*.*", Title = "Alegeți programul de izolat" };
                if (dlg.ShowDialog() != true) return;
                path = dlg.FileName;
            }
            if (MessageBox.Show($"Izolați programul?\n\n{path}\n\nSe blochează accesul la rețea doar pentru el, apoi este scanat.",
                    "Izolare program", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int? pid = Process.GetProcesses().FirstOrDefault(p => SafePath(p).Equals(path, StringComparison.OrdinalIgnoreCase))?.Id;
            await ContainAsync(path, pid, "operator: program ales manual", SuspendOnContain);
        }

        private async Task ContainAsync(string path, int? pid, string trigger, bool suspend)
        {
            EnsureCase();
            IsBusy = true;
            Status = $"Se izolează și se scanează {Path.GetFileName(path)}…";
            try
            {
                var inc = await Task.Run(() => _service!.Contain(path, pid, trigger, new ContainmentOptions(suspend, CopySampleIntoCase: true)));
                Incidents.Insert(0, inc);
                SelectedIncident = inc;
                Status = inc.State == ContainmentState.Failed
                    ? $"{inc.IncidentId}: izolarea a EȘUAT (vezi jurnalul incidentului; de obicei lipsesc drepturile de administrator). Scanarea s-a făcut."
                    : $"{inc.IncidentId}: {Path.GetFileName(path)} izolat de rețea și scanat ({inc.Findings.Count} constatări).";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Status = "Eroare: " + ex.Message;
            }
            finally { IsBusy = false; }
            // Owner decision 28: isolation may start on the provisional scope; the scope dialog opens right after it.
            if (!LogAnalyzer.UI.Services.LiveCase.ScopeConfirmed)
                Status += LogAnalyzer.UI.Services.LiveCase.RequestScopeConfirmation()
                    ? " Scopul cazului a fost confirmat."
                    : " Scopul cazului rămâne provizoriu, neconfirmat (rapoartele îl menționează).";
        }

        private async void AutoTick(object? sender, EventArgs e)
        {
            if (IsBusy) return;
            EnsureCase();
            var contained = Incidents.Where(i => i.State == ContainmentState.Contained).Select(i => i.ProgramPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var suspects = await Task.Run(() => SuspiciousProcessDetector.Scan(_trusted));
            foreach (var s in suspects.Where(s => s.EligibleForAutoContainment && !contained.Contains(s.Path)))
            {
                contained.Add(s.Path);
                await ContainAsync(s.Path, s.Pid, $"automat: {string.Join("; ", s.Reasons)}", suspend: false);
            }
        }

        [RelayCommand]
        private async Task TrustSelected()
        {
            if (SelectedSuspect is not { } s) { Status = "Selectați un proces din listă."; return; }
            if (MessageBox.Show($"Marcați programul ca fiind de încredere?\n\n{s.Path}\n\nNu va mai fi izolat automat. Încrederea se leagă de SHA-256-ul fișierului curent: " +
                                "dacă fișierul se schimbă (actualizare sau înlocuire), trebuie confirmat din nou.",
                    "Program de încredere", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                var t = _trusted.Trust(s.Path, "aprobat de operator din ecranul de izolare", $"{Environment.UserDomainName}\\{Environment.UserName}");
                EnsureCase();
                _case!.Audit("trusted.add", $"{t.Sha256} {t.Path}");
                Status = $"{System.IO.Path.GetFileName(s.Path)} marcat ca de încredere (SHA-256 {t.Sha256[..16]}…).";
                await ScanSuspects();
            }
            catch (IOException ex) { Status = "Eroare: " + ex.Message; }
        }

        [RelayCommand]
        private async Task RefreshBlocked()
        {
            if (SelectedIncident is not { } inc || _service is null) return;
            IsBusy = true;
            try
            {
                await Task.Run(() => _service.RefreshBlockedAttempts(inc));
                IncidentDetail = IncidentDetailFormatter.Format(inc);
                Status = $"{inc.IncidentId}: {inc.BlockedAttempts.Count} încercări de conexiune blocate.";
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void Release()
        {
            if (SelectedIncident is not { } inc || _service is null) return;
            if (MessageBox.Show($"Ridicați izolarea pentru {Path.GetFileName(inc.ProgramPath)}?\nProgramul va avea din nou acces la rețea.",
                    "Ridicare izolare", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _service.Release(inc, "ridicată de operator");
            IncidentDetail = IncidentDetailFormatter.Format(inc);
            Status = $"{inc.IncidentId}: {(inc.State == ContainmentState.Released ? "izolare ridicată" : "ridicarea a eșuat parțial (vezi jurnalul)")}.";
            var idx = Incidents.IndexOf(inc);
            if (idx >= 0) { Incidents[idx] = inc; SelectedIncident = inc; }
        }

        [RelayCommand]
        private void ExportPdf()
        {
            if (SelectedIncident is not { } inc || _case is null) { Status = "Selectați un incident."; return; }
            var dlg = new SaveFileDialog
            {
                FileName = $"{inc.IncidentId}_{Path.GetFileNameWithoutExtension(inc.ProgramPath)}.pdf",
                Filter = "PDF (*.pdf)|*.pdf",
                InitialDirectory = _service!.IncidentDir(inc),
            };
            if (dlg.ShowDialog() != true) return;
            IncidentPdfReport.Write(inc, dlg.FileName, _case.Info.Name, Environment.MachineName);
            _case.Audit("report.pdf", $"{inc.IncidentId} {dlg.FileName}");
            Status = $"Raport salvat: {dlg.FileName}";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }

        [RelayCommand]
        private void OpenIncidentFolder()
        {
            if (SelectedIncident is not { } inc || _service is null) return;
            var dir = _service.IncidentDir(inc);
            if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }

        [RelayCommand]
        private void EnableBlockedAudit()
        {
            if (MessageBox.Show("Activați auditul Windows pentru conexiunile blocate (subcategoria „Filtering Platform Connection”, eșecuri)?\n\n" +
                                "Este o modificare a politicii de audit a stației. Fără ea nu se poate vedea unde încearcă să se conecteze un program izolat.",
                    "Activare audit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var (ok, detail) = LogAnalyzer.Response.Containment.AuditPolicyChange.EnableBlockedConnectionFailureAudit();
            _case?.Audit("auditpol.enable", $"Filtering Platform Connection failure: {(ok ? "OK" : "FAILED")} {detail}");
            Load();
            if (!ok) Status = "Activarea a eșuat: " + detail;
        }

        private static string SafePath(Process p)
        {
            try { return p.MainModule?.FileName ?? ""; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return ""; }
        }

        private static bool CheckAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Plain-text detail of an incident for the side panel (the PDF carries the same content).</summary>
    public static class IncidentDetailFormatter
    {
        public static string Format(ProcessIncident i)
        {
            string L(DateTimeOffset? t) => t is { } v ? TimeZoneInfo.ConvertTime(v, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm:ss") : "—";
            var sb = new StringBuilder();
            var s = i.Scan;
            sb.AppendLine($"{i.IncidentId} — {Path.GetFileName(i.ProgramPath)} — {i.State}");
            sb.AppendLine($"Declanșator: {i.Trigger}");
            sb.AppendLine($"Izolat la: {L(i.CreatedUtc)}   Suspendat: {(i.ProcessSuspended ? "da" : "nu")}");
            sb.AppendLine($"Cale: {i.ProgramPath}");
            if (s is not null)
            {
                sb.AppendLine($"PID: {s.Process.Pid?.ToString() ?? "—"}   Părinte: {s.Process.ParentPid?.ToString() ?? "—"} {s.Process.ParentName}");
                if (s.Process.CommandLine.Length > 0) sb.AppendLine($"Linie de comandă: {s.Process.CommandLine}");
                if (s.File is { } f)
                {
                    sb.AppendLine($"SHA-256: {f.Sha256}");
                    sb.AppendLine($"Semnătură: {f.Signature.Status} ({f.Signature.Kind}) {f.Signature.Signer}");
                    sb.AppendLine($"Producător: {f.CompanyName}   Produs: {f.ProductName}   Nume original: {f.OriginalFileName}");
                    sb.AppendLine($"Creat: {L(f.CreatedUtc)}   Modificat: {L(f.ModifiedUtc)}   Dimensiune: {f.Size:N0} B");
                }
                if (s.Defender is { } d) sb.AppendLine($"Defender: {(d.ThreatFound == true ? "AMENINȚARE" : d.ThreatFound == false ? "curat" : "nedeterminat")}");
                sb.AppendLine();
                sb.AppendLine("MOTIVE DE SUSPICIUNE");
                foreach (var r in s.SuspicionReasons) sb.AppendLine("  • " + r);
            }
            sb.AppendLine();
            sb.AppendLine("UNDE A MERS / A ÎNCERCAT SĂ MEARGĂ");
            var intents = i.AllNetworkIntents.ToList();
            if (intents.Count == 0) sb.AppendLine("  (nimic observat încă — folosiți „Reîmprospătează încercările blocate”)");
            foreach (var n in intents.OrderBy(n => n.Time.Utc).Take(100))
                sb.AppendLine($"  {L(n.Time.Utc)}  {n.Kind,-26} {n.Destination}:{n.Port}  {n.Protocol}  [{n.Source}]");
            if (s is { Iocs.Count: > 0 })
            {
                sb.AppendLine("  Din conținutul fișierului (intenții): " +
                              string.Join(", ", s.Iocs.Where(x => x.Type is "url" or "domain" or "ipv4").Select(x => x.Value).Take(30)));
            }
            if (s is { Persistence.Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("PORNIRE AUTOMATĂ");
                foreach (var p in s.Persistence) sb.AppendLine($"  {p.Mechanism}: {p.Location}  {p.Value}");
            }
            sb.AppendLine();
            sb.AppendLine("CONSTATĂRI");
            foreach (var f in i.Findings) sb.AppendLine($"  [{f.Severity.ToSpec()}/{f.Classification.ToSpec()}] {f.Title}: {f.Description}");
            sb.AppendLine();
            sb.AppendLine("JURNAL ACȚIUNI");
            foreach (var a in i.Actions) sb.AppendLine($"  {L(a.TimeUtc)}  {a.Action}  {(a.Success ? "OK" : "EȘUAT")}  {a.Target}  {a.Detail}");
            if (s is { Gaps.Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("GOLURI DE PROBĂ");
                foreach (var g in s.Gaps) sb.AppendLine($"  {g.Artifact}: {g.Status.ToSpec()} — {g.Reason}");
            }
            return sb.ToString();
        }
    }
}
