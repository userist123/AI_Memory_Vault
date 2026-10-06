using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IEventParser _eventParser;
        private readonly IAnalysisEngine _analysisEngine;
        private readonly IRegistryParser _registryParser;
        private readonly IIocExtractionService _iocExtractor;
        private readonly ILogIntegrityService _integrityService;
        private readonly IIncidentReportGenerator _reportGenerator;
        private readonly KnowledgeBaseService _kbService;

        public ObservableCollection<ParsedEvent> Events { get; set; } = new();
        public ObservableCollection<DetectedIssue> DetectedIssues { get; set; } = new();
        public ObservableCollection<RegistryArtifact> RegistryArtifacts { get; set; } = new();
        public ObservableCollection<TimelineItem> TimelineItems { get; set; } = new();
        public ObservableCollection<IocItem> CurrentIocs { get; set; } = new();
        public ObservableCollection<LogIntegrityResult> IntegritateFisiere { get; set; } = new();

        [ObservableProperty] private ICollectionView? _eventsView;
        [ObservableProperty] private ICollectionView? _artifactsView;
        [ObservableProperty] private ICollectionView? _issuesView;
        [ObservableProperty] private ICollectionView? _timelineView;

        [ObservableProperty] private string _searchEventsText = string.Empty;
        [ObservableProperty] private string _searchArtifactsText = string.Empty;
        [ObservableProperty] private string _searchAlertsText = string.Empty;

        [ObservableProperty] private ParsedEvent? _selectedEvent;
        [ObservableProperty] private RegistryArtifact? _selectedArtifact;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusMessage = "Sistem pregatit pentru investigatie offline...";
        [ObservableProperty] private bool _hideVerifiedAlerts;
        [ObservableProperty] private string _integrityStatusMessage = "Integritate: neevaluata (incarca un folder).";

        public ObservableCollection<DfirProfile> Profiles { get; } = new()
        {
            new DfirProfile { Name = "1. Toate Evenimentele si Registrii (Implicit)" },
            new DfirProfile { Name = "2. Logon si Autentificare", TargetEventIds = new() { 4624, 4625, 4634, 4648, 4768, 4769, 4776 } },
            new DfirProfile { Name = "3. Modificari Conturi/Privilegii", TargetEventIds = new() { 4720, 4722, 4724, 4728, 4732, 4672 } },
            new DfirProfile { Name = "4. Evaziune si Stergere Loguri", TargetEventIds = new() { 1102, 104, 7045, 4697 } }
        };

        [ObservableProperty] private DfirProfile? _selectedProfile;

        partial void OnSearchEventsTextChanged(string value) { EventsView?.Refresh(); TimelineView?.Refresh(); }
        partial void OnSearchArtifactsTextChanged(string value) => ArtifactsView?.Refresh();
        partial void OnSearchAlertsTextChanged(string value) => IssuesView?.Refresh();
        partial void OnHideVerifiedAlertsChanged(bool value) => IssuesView?.Refresh();
        partial void OnSelectedProfileChanged(DfirProfile? value) { EventsView?.Refresh(); TimelineView?.Refresh(); }

        partial void OnSelectedEventChanged(ParsedEvent? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.Message)) return;

            Task.Run(() =>
            {
                var intrari = new List<(string, string)> { ($"EVTX_{value.EventRecordId}", value.Message!) };
                var iocuri = _iocExtractor.Extrage(intrari);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentIocs.Clear();
                    foreach (var i in iocuri.Values.OrderByDescending(x => x.Occurrences))
                        CurrentIocs.Add(i);
                });
            });
        }

        private bool _isPopupActive = false;

        private DetectedIssue? _selectedIssue;
        public DetectedIssue? SelectedIssue
        {
            get => _selectedIssue;
            set
            {
                if (_isPopupActive) return;
                SetProperty(ref _selectedIssue, value);

                if (value != null)
                {
                    _isPopupActive = true;
                    var alertWindow = new LogAnalyzer.UI.Views.AlertDetailWindow(value) { Owner = Application.Current.MainWindow };
                    alertWindow.ShowDialog();

                    _selectedIssue = null;
                    OnPropertyChanged(nameof(SelectedIssue));
                    IssuesView?.Refresh();
                    Application.Current.Dispatcher.InvokeAsync(() => _isPopupActive = false);
                }
            }
        }

        public MainViewModel(
            IEventParser eventParser,
            IAnalysisEngine analysisEngine,
            IRegistryParser registryParser,
            IIocExtractionService iocExtractor,
            ILogIntegrityService integrityService,
            IIncidentReportGenerator reportGenerator)
        {
            _eventParser = eventParser;
            _analysisEngine = analysisEngine;
            _registryParser = registryParser;
            _iocExtractor = iocExtractor;
            _integrityService = integrityService;
            _reportGenerator = reportGenerator;
            SelectedProfile = Profiles.First();

            _kbService = new KnowledgeBaseService();
            string categoriesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Categories");
            _kbService.LoadCategories(categoriesPath);

            EventsView = CollectionViewSource.GetDefaultView(Events); EventsView.Filter = FilterEvents;
            ArtifactsView = CollectionViewSource.GetDefaultView(RegistryArtifacts); ArtifactsView.Filter = FilterArtifacts;
            IssuesView = CollectionViewSource.GetDefaultView(DetectedIssues); IssuesView.Filter = FilterIssues;
            TimelineView = CollectionViewSource.GetDefaultView(TimelineItems); TimelineView.Filter = FilterTimeline;
        }

        [RelayCommand]
        private void PivotIoc(string value)
        {
            SearchEventsText = value;
            StatusMessage = $"Pivotare activa: urmarire IOC [{value}] in loguri.";
        }

        [RelayCommand]
        private async Task LoadLogsAsync()
        {
            var dialog = new OpenFolderDialog { Title = "Selecteaza folderul cu loguri si artefacte", Multiselect = false };

            if (dialog.ShowDialog() == true)
            {
                string folderPath = dialog.FolderName;
                IsLoading = true;
                StatusMessage = $"Procesare date din {folderPath}...";

                try
                {
                    await Task.Run(() =>
                    {
                        var allEvents = new List<ParsedEvent>();
                        var allArtifacts = new List<RegistryArtifact>();
                        var integritatePeFisier = new List<LogIntegrityResult>();

                        try
                        {
                            var evtxFiles = Directory.GetFiles(folderPath, "*.evtx");
                            foreach (var file in evtxFiles)
                            {
                                if (allEvents.Count >= 15000) break;
                                var evenimenteFisier = new List<ParsedEvent>();
                                try
                                {
                                    foreach (var ev in _eventParser.ParseEvtxFile(file))
                                    {
                                        if (ev.Message != null && ev.Message.Length > 2500)
                                            ev.Message = ev.Message.Substring(0, 2500) + "\n\n... [TEXT TRUNCHIAT]";
                                        evenimenteFisier.Add(ev);
                                        allEvents.Add(ev);
                                        if (allEvents.Count >= 15000) break;
                                    }
                                }
                                catch { }

                                if (evenimenteFisier.Count > 0)
                                {
                                    try
                                    {
                                        var rezultatIntegritate = _integrityService.Analizeaza(Path.GetFileName(file), evenimenteFisier);
                                        integritatePeFisier.Add(rezultatIntegritate);
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        var sortedEvents = allEvents.OrderByDescending(e => e.TimeCreated).ToList();
                        var reguliActive = _kbService.Reguli;
                        var issues = _analysisEngine.AnalyzeEvents(sortedEvents, reguliActive).ToList();

                        try
                        {
                            string ntuserPath = Path.Combine(folderPath, "NTUSER.DAT");
                            if (File.Exists(ntuserPath))
                                try { allArtifacts.AddRange(_registryParser.ParseNtUserDat(ntuserPath).Take(5000)); } catch { }

                            var regFiles = Directory.GetFiles(folderPath, "*.reg");
                            foreach (var regFile in regFiles)
                                try { allArtifacts.AddRange(_registryParser.ParseRegFile(regFile).Take(5000)); } catch { }

                            foreach (var art in allArtifacts)
                            {
                                if (art.ValueData != null && art.ValueData.Length > 1000)
                                    art.ValueData = art.ValueData.Substring(0, 1000) + " ...[TRUNCHIAT]";
                            }
                        }
                        catch { }

                        var timeline = new List<TimelineItem>();

                        timeline.AddRange(sortedEvents.Select(e =>
                        {
                            var kbInfo = _kbService.GetDetails(e.EventId);
                            string category = kbInfo != null ? kbInfo.Category : $"Event ID: {e.EventId}";
                            string mitre = kbInfo != null ? kbInfo.MitreTTP : "";

                            return new TimelineItem
                            {
                                Timestamp = e.TimeCreated,
                                Source = "EVTX",
                                Category = category,
                                MitreTags = mitre,
                                UserOrHost = e.MachineName ?? "N/A",
                                Description = e.Message?.Substring(0, Math.Min(100, e.Message.Length)) + "..."
                            };
                        }));

                        timeline.AddRange(allArtifacts.Select(r => new TimelineItem
                        {
                            Timestamp = DateTime.Now,
                            Source = "Registru",
                            Category = r.Category,
                            UserOrHost = r.KeyPath?.Split('\\').LastOrDefault() ?? "NTUSER",
                            Description = $"{r.ValueName} = {r.ValueData}"
                        }));

                        timeline.AddRange(issues.Select(i => new TimelineItem
                        {
                            Timestamp = i.CreatedAt,
                            Source = "Alerta",
                            Category = i.Title,
                            Severity = i.Severity,
                            UserOrHost = "SOC Engine",
                            MitreTags = i.MitreTechniqueId,
                            Description = i.Explanation
                        }));

                        timeline.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

                        var iocIntrari = sortedEvents
                            .Where(e => !string.IsNullOrEmpty(e.Message))
                            .Select(e => ($"EVTX_{e.EventRecordId}", e.Message!));
                        var iocuriGasite = _iocExtractor.Extrage(iocIntrari);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Events = new ObservableCollection<ParsedEvent>(sortedEvents);
                            RegistryArtifacts = new ObservableCollection<RegistryArtifact>(allArtifacts);
                            DetectedIssues = new ObservableCollection<DetectedIssue>(issues);
                            TimelineItems = new ObservableCollection<TimelineItem>(timeline);
                            IntegritateFisiere = new ObservableCollection<LogIntegrityResult>(integritatePeFisier);

                            CurrentIocs.Clear();
                            foreach (var i in iocuriGasite.Values.OrderByDescending(x => x.Occurrences).Take(200))
                                CurrentIocs.Add(i);

                            EventsView = CollectionViewSource.GetDefaultView(Events); EventsView.Filter = FilterEvents;
                            ArtifactsView = CollectionViewSource.GetDefaultView(RegistryArtifacts); ArtifactsView.Filter = FilterArtifacts;
                            IssuesView = CollectionViewSource.GetDefaultView(DetectedIssues); IssuesView.Filter = FilterIssues;
                            TimelineView = CollectionViewSource.GetDefaultView(TimelineItems); TimelineView.Filter = FilterTimeline;

                            var puternicSuspect = integritatePeFisier.Count(x => x.Status == StatusIntegritate.PuternicSuspect);
                            var alterat = integritatePeFisier.Count(x => x.Status == StatusIntegritate.PosibilAlterat);
                            IntegrityStatusMessage = integritatePeFisier.Count == 0
                                ? "Integritate: niciun fisier EVTX analizat."
                                : $"Integritate: {integritatePeFisier.Count - alterat - puternicSuspect} normale, {alterat} posibil alterate, {puternicSuspect} puternic suspecte.";

                            StatusMessage = $"Incarcare completa: {Events.Count} loguri, {RegistryArtifacts.Count} chei reg, {DetectedIssues.Count} alerte, {CurrentIocs.Count} IOC-uri.";
                        });
                    });
                }
                catch (Exception ex) { Application.Current.Dispatcher.Invoke(() => StatusMessage = "Eroare: " + ex.Message); }
                finally { IsLoading = false; GC.Collect(); }
            }
        }

        [RelayCommand]
        private void ExportToCsv()
        {
            if (EventsView == null || EventsView.IsEmpty) return;
            var dialog = new SaveFileDialog { Filter = "Raport CSV (*.csv)|*.csv", FileName = "Raport_Incident.csv" };
            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Data,Severitate,EventID,Sursa,Mesaj");
                foreach (ParsedEvent ev in EventsView)
                    sb.AppendLine($"{ev.TimeCreated},{ev.Level},{ev.EventId},{ev.ProviderName},\"{ev.Message?.Replace("\r", " ").Replace("\n", " ") ?? ""}\"");
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                StatusMessage = "Export CSV complet.";
            }
        }

        [RelayCommand]
        private void ExportRaportIncident()
        {
            var dialog = new SaveFileDialog { Filter = "Raport Markdown (*.md)|*.md", FileName = "Raport_Incident.md" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var raport = new IncidentReport
                {
                    Rezumat = new IncidentSummary
                    {
                        TitluIncident = "Investigatie Endpoint",
                        Rezumat = $"Investigatie generata din LogAnalyzer.MVP la {DateTime.Now:dd.MM.yyyy HH:mm}. " +
                                   $"{Events.Count} evenimente EVTX, {RegistryArtifacts.Count} artefacte registry, {DetectedIssues.Count} alerte."
                    },
                    EvenimenteCheie = TimelineItems.Take(200).ToList(),
                    AlerteCheie = DetectedIssues.ToList(),
                    IocuriRelevante = CurrentIocs.ToList(),
                    IntegritateFisiere = IntegritateFisiere.ToList(),
                    NotiteAnalist = string.Join("\n\n", DetectedIssues
                        .Where(a => !string.IsNullOrWhiteSpace(a.AnalystNotes))
                        .Select(a => $"[{a.Title}] {a.AnalystNotes}"))
                };

                var markdown = _reportGenerator.GenereazaMarkdown(raport);
                File.WriteAllText(dialog.FileName, markdown, Encoding.UTF8);
                StatusMessage = "Raport de incident (Markdown) exportat cu succes.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Eroare la exportul raportului: " + ex.Message;
            }
        }

        private bool FilterEvents(object obj)
        {
            if (obj is not ParsedEvent ev) return false;
            if (SelectedProfile != null && SelectedProfile.TargetEventIds.Any() && !SelectedProfile.TargetEventIds.Contains(ev.EventId)) return false;
            if (string.IsNullOrWhiteSpace(SearchEventsText)) return true;
            string q = SearchEventsText.ToLower();
            return ev.EventId.ToString().Contains(q) || (ev.Message != null && ev.Message.ToLower().Contains(q));
        }

        private bool FilterTimeline(object obj)
        {
            if (obj is not TimelineItem item) return false;
            if (SelectedProfile != null && SelectedProfile.TargetEventIds.Any())
            {
                if (item.Source == "EVTX" && !SelectedProfile.TargetEventIds.Any(id => item.Category.Contains(id.ToString()))) return false;
            }
            if (string.IsNullOrWhiteSpace(SearchEventsText)) return true;
            string q = SearchEventsText.ToLower();
            return item.Category.ToLower().Contains(q) || item.Description.ToLower().Contains(q);
        }

        private bool FilterArtifacts(object obj)
        {
            if (obj is not RegistryArtifact art) return false;
            if (string.IsNullOrWhiteSpace(SearchArtifactsText)) return true;
            string q = SearchArtifactsText.ToLower();
            return (art.KeyPath != null && art.KeyPath.ToLower().Contains(q)) ||
                   (art.ValueName != null && art.ValueName.ToLower().Contains(q)) ||
                   (art.ValueData != null && art.ValueData.ToLower().Contains(q)) ||
                   (art.Category != null && art.Category.ToLower().Contains(q));
        }

        private bool FilterIssues(object obj) { if (obj is not DetectedIssue i) return false; return !(HideVerifiedAlerts && i.IsVerified); }
    }
}
