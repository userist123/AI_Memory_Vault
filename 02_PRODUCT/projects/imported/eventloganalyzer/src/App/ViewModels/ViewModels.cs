using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EventLogAnalyzer.Core.Detection;
using EventLogAnalyzer.Core.KnowledgeBase;
using EventLogAnalyzer.Core.Models;
using EventLogAnalyzer.Core.Parsers;
using EventLogAnalyzer.Core.Remediation;

namespace EventLogAnalyzer.App.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>Minimal ICommand implementation - MVP avoids pulling in a full MVVM toolkit.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

/// <summary>
/// Owns the loaded artifact set for the currently selected folder and
/// exposes KPI counts for the Dashboard's summary cards.
/// </summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly EvtxParser _evtxParser = new();
    private readonly NtUserHiveParser _ntUserParser = new();
    private readonly HklmHiveParser _hklmParser = new();
    private readonly EventKnowledgeBase _knowledgeBase;
    private readonly DetectionEngine _detectionEngine;

    public ObservableCollection<EventRecordModel> AllEvents { get; } = new();
    public ObservableCollection<Issue> Issues { get; } = new();
    public ObservableCollection<UserActivityItem> UserActivity { get; } = new();
    public ObservableCollection<ConfigFinding> ConfigFindings { get; } = new();

    private string? _rootFolder;
    public string? RootFolder
    {
        get => _rootFolder;
        set => SetField(ref _rootFolder, value);
    }

    private int _totalEvents, _errorCount, _warningCount, _suspiciousCount;
    public int TotalEvents { get => _totalEvents; private set => SetField(ref _totalEvents, value); }
    public int ErrorCount { get => _errorCount; private set => SetField(ref _errorCount, value); }
    public int WarningCount { get => _warningCount; private set => SetField(ref _warningCount, value); }
    public int SuspiciousCount { get => _suspiciousCount; private set => SetField(ref _suspiciousCount, value); }

    public ICommand SelectFolderCommand { get; }

    public DashboardViewModel(EventKnowledgeBase knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
        _detectionEngine = new DetectionEngine(new IIssueDetector[]
        {
            new RepeatedServiceCrashDetector(),
            new DiskErrorDetector(),
            new FailedLogonDetector()
        });

        SelectFolderCommand = new RelayCommand(_ => { /* opens FolderBrowserDialog in code-behind, then calls LoadFolder */ });
    }

    public void LoadFolder(string folderPath)
    {
        RootFolder = folderPath;
        AllEvents.Clear();
        Issues.Clear();
        UserActivity.Clear();
        ConfigFindings.Clear();

        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (_evtxParser.CanParse(file))
            {
                foreach (var raw in _evtxParser.Parse(file))
                    AllEvents.Add(_knowledgeBase.Explain(raw));
            }
            else if (_ntUserParser.CanParse(file))
            {
                foreach (var item in _ntUserParser.Parse(file))
                    UserActivity.Add(item);
            }
            else if (_hklmParser.CanParse(file))
            {
                foreach (var finding in _hklmParser.Parse(file))
                    ConfigFindings.Add(finding);
            }
        }

        foreach (var issue in _detectionEngine.Run(AllEvents.ToList()))
            Issues.Add(issue);

        RecomputeKpis();
    }

    private void RecomputeKpis()
    {
        TotalEvents = AllEvents.Count;
        ErrorCount = AllEvents.Count(e => e.Level == Severity.Error);
        WarningCount = AllEvents.Count(e => e.Level == Severity.Warning);
        SuspiciousCount = Issues.Count(i => i.Severity == Severity.Suspicious);
    }
}

/// <summary>
/// Backs the Event Explorer grid: filtering by time range, severity,
/// source, and free-text search over the already-loaded event set.
/// </summary>
public sealed class EventExplorerViewModel : ViewModelBase
{
    private readonly IReadOnlyList<EventRecordModel> _allEvents;
    public ObservableCollection<EventRecordModel> FilteredEvents { get; } = new();

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value)) ApplyFilters(); }
    }

    private Severity? _severityFilter;
    public Severity? SeverityFilter
    {
        get => _severityFilter;
        set { if (SetField(ref _severityFilter, value)) ApplyFilters(); }
    }

    private EventRecordModel? _selectedEvent;
    public EventRecordModel? SelectedEvent
    {
        get => _selectedEvent;
        set => SetField(ref _selectedEvent, value);
    }

    private readonly RemediationScriptBuilder _scriptBuilder = new();
    private string? _generatedScript;
    public string? GeneratedScript
    {
        get => _generatedScript;
        private set => SetField(ref _generatedScript, value);
    }

    public ICommand GenerateFixScriptCommand { get; }

    public EventExplorerViewModel(IReadOnlyList<EventRecordModel> allEvents)
    {
        _allEvents = allEvents;
        GenerateFixScriptCommand = new RelayCommand(param =>
        {
            if (param is Issue issue)
                GeneratedScript = _scriptBuilder.Build(issue);
        });
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        FilteredEvents.Clear();

        var query = _allEvents.AsEnumerable();

        if (SeverityFilter is { } sev)
            query = query.Where(e => e.Level == sev);

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(e =>
                e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (e.HumanTitle?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Provider.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var e in query.OrderByDescending(e => e.TimeCreated))
            FilteredEvents.Add(e);
    }
}
