using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Domain;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>"Investigație domeniu și e-mail": live AD inventory and checks, one-user investigation, mailbox analysis.</summary>
    public partial class DomainInvestigationViewModel : ObservableObject
    {
        private DomainSnapshot? _snapshot;
        private UserInvestigationResult? _user;
        private MailInvestigationResult? _mail;

        public ObservableCollection<ControlCheck> DomainChecks { get; } = new();
        public ObservableCollection<ActionEntry> UserTimeline { get; } = new();
        public ObservableCollection<ControlCheck> MailChecks { get; } = new();
        public ObservableCollection<MailMessage> MailMessages { get; } = new();

        [ObservableProperty] private string _domainServer = "";
        [ObservableProperty] private string _domainStatus;
        [ObservableProperty] private string _userName = "";
        [ObservableProperty] private string _domainControllers = "";
        [ObservableProperty] private int _days = 30;
        [ObservableProperty] private string _userSummary = "";
        [ObservableProperty] private string _mailbox = "";
        [ObservableProperty] private string _internalDomains = "";
        [ObservableProperty] private string _mailSummary = "";
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private bool _isBusy;

        public bool NetworkAllowed => !AppModeContext.IsAirGapped;
        public string ModeNote => NetworkAllowed
            ? "Modul Network: interogările către controlerele de domeniu sunt permise (citire, cu contul Windows curent)."
            : "Modul AirGapped: interogările de rețea (LDAP, jurnale de pe controlere) sunt BLOCATE. Pe o rețea izolată cu domeniu, porniți aplicația cu --mode=network. Analiza e-mailului din fișiere exportate funcționează.";

        public DomainInvestigationViewModel()
        {
            _domainStatus = DirectoryCollector.IsDomainJoined(out var d) ? $"Stația face parte din domeniul {d}." : "Stația NU face parte dintr-un domeniu; indicați un controler de domeniu dacă e cazul.";
            if (DirectoryCollector.IsDomainJoined(out var dom)) InternalDomains = dom;
        }

        [RelayCommand]
        private async Task InventoryDomain()
        {
            IsBusy = true;
            Status = "Se citește Active Directory…";
            try
            {
                var server = string.IsNullOrWhiteSpace(DomainServer) ? null : DomainServer.Trim();
                _snapshot = await Task.Run(() => DirectoryCollector.Collect(server));
                var checks = DomainEvaluator.Evaluate(_snapshot);
                DomainChecks.Clear();
                foreach (var c in checks.OrderBy(c => c.Status == ControlStatus.Neconform ? 0 : c.Status == ControlStatus.DeVerificat ? 1 : 2)) DomainChecks.Add(c);
                DomainControllers = string.Join(", ", _snapshot.Computers.Where(c => c.IsDomainController).Select(c => c.SamAccountName.TrimEnd('$')));
                DomainStatus = $"{_snapshot.DomainName}: {_snapshot.Users.Count} utilizatori, {_snapshot.Computers.Count} calculatoare, server {_snapshot.Server}.";
                Save("domain_inventory", new { _snapshot, checks });
                Status = $"Inventar complet: {checks.Count(c => c.Status == ControlStatus.Neconform)} NECONFORM, {checks.Count(c => c.Status == ControlStatus.DeVerificat)} DE VERIFICAT.";
            }
            catch (NetworkBlockedException ex) { Status = ex.Message + " " + ModeNote; }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                Status = "Active Directory nu a putut fi citit: " + ex.Message;
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task InvestigateUser()
        {
            if (string.IsNullOrWhiteSpace(UserName)) { Status = "Introduceți numele de utilizator (sAMAccountName)."; return; }
            IsBusy = true;
            Status = $"Se investighează {UserName}…";
            try
            {
                var dcs = DomainControllers.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var since = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, Days));
                _user = await Task.Run(() => UserInvestigation.Investigate(UserName.Trim(), _snapshot, dcs, since));
                UserTimeline.Clear();
                foreach (var t in _user.Timeline.OrderByDescending(t => t.TimeUtc)) UserTimeline.Add(t);
                UserSummary = (_user.Account is null ? "Contul nu este în inventarul încărcat (rulați întâi inventarul domeniului).\n" : "") +
                              string.Join("\n", _user.Observations) +
                              (_user.Gaps.Count > 0 ? "\nGoluri: " + string.Join("; ", _user.Gaps.Select(g => $"{g.Artifact}: {g.Reason}")) : "");
                Save($"user_{UserName.Trim()}", _user);
                Status = $"{_user.Timeline.Count} evenimente pentru {UserName}.";
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void GenerateOnlineScript() => SaveScript(online: true);

        [RelayCommand]
        private void GenerateOnPremScript() => SaveScript(online: false);

        private void SaveScript(bool online)
        {
            if (!Mailbox.Contains('@')) { Status = "Introduceți adresa cutiei poștale."; return; }
            var outDir = Path.Combine(LogAnalyzer.UI.Services.LiveCase.Get().Root, "Mail", Mailbox.Replace('@', '_'));
            var start = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, Days));
            var script = online ? MailInvestigation.ExchangeOnlineScript(Mailbox.Trim(), start, DateTimeOffset.UtcNow, outDir)
                                : MailInvestigation.ExchangeOnPremScript(Mailbox.Trim(), start, DateTimeOffset.UtcNow, outDir);
            var dlg = new SaveFileDialog { FileName = online ? "Investigatie_ExchangeOnline.ps1" : "Investigatie_ExchangeLocal.ps1", Filter = "PowerShell (*.ps1)|*.ps1" };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName, script, new System.Text.UTF8Encoding(true));
            Status = $"Script salvat: {dlg.FileName}. Rulați-l ca administrator de e-mail; rezultatele ajung în {outDir}. Apoi apăsați „Importă exportul”.";
        }

        [RelayCommand]
        private async Task ImportMail()
        {
            var dlg = new OpenFolderDialog { Title = "Folderul cu message_trace.csv / inbox_rules.csv / mailbox_forwarding.csv" };
            if (dlg.ShowDialog() != true) return;
            var domains = InternalDomains.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (domains.Length == 0 && Mailbox.Contains('@')) domains = new[] { Mailbox.Split('@')[1] };
            IsBusy = true;
            try
            {
                _mail = await Task.Run(() => MailInvestigation.ImportAndAnalyze(dlg.FolderName, Mailbox.Trim(), domains));
                MailChecks.Clear();
                foreach (var c in _mail.Checks) MailChecks.Add(c);
                MailMessages.Clear();
                foreach (var m in _mail.Messages.OrderByDescending(m => m.TimeUtc).Take(5000)) MailMessages.Add(m);
                MailSummary = $"{_mail.Messages.Count} mesaje, {_mail.Rules.Count} reguli, {_mail.Forwarding.Count} setări de redirecționare.";
                Save($"mail_{Mailbox.Replace('@', '_')}", _mail);
                Status = "Export importat și analizat.";
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void ExportPdf()
        {
            var dlg = new SaveFileDialog { FileName = $"Investigatie_{DateTime.Now:yyyyMMdd_HHmm}.pdf", Filter = "PDF (*.pdf)|*.pdf" };
            if (dlg.ShowDialog() != true) return;
            var checks = DomainChecks.Concat(MailChecks).ToList();
            var notes = new[] { DomainStatus, UserSummary, MailSummary }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            ChecksReportPdf.Write(dlg.FileName, "Investigație domeniu și e-mail", ModeNote, checks, notes,
                new ChecksReportPdf.Table($"Cronologia utilizatorului {UserName}", new[] { "Ora (UTC)", "Ce", "Detalii", "Probă" },
                    UserTimeline.Select(t => new[] { t.TimeUtc.ToString("yyyy-MM-dd HH:mm:ss"), t.Action, t.Detail, t.Source }).ToList()),
                new ChecksReportPdf.Table($"Mesaje {Mailbox}", new[] { "Ora (UTC)", "Expeditor", "Destinatari", "Subiect", "IP", "Status" },
                    MailMessages.Take(2000).Select(m => new[] { m.TimeUtc.ToString("yyyy-MM-dd HH:mm:ss"), m.Sender, m.Recipients, m.Subject, m.ClientIp, m.Status }).ToList()));
            Status = "Raport salvat: " + dlg.FileName;
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }

        /// <summary>Results are kept in the station's case as evidence (JSON with SHA-256 and custody).</summary>
        private static void Save(string name, object data)
        {
            var ws = LogAnalyzer.UI.Services.LiveCase.Get();
            var dir = Path.Combine(ws.Root, "Investigations");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var path = Path.Combine(dir, $"{safe}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            ws.RegisterStored(path, "live:" + Environment.MachineName, "investigation", name, LogAnalyzer.Dfir.Model.TemporalType.CurrentSnapshot,
                "DomainInvestigationViewModel", LogAnalyzer.Dfir.Model.DfirInfo.ApplicationVersion);
        }
    }
}
