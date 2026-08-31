using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XAU_Kinetic.Desktop.Models;
using XAU_Kinetic.Desktop.Services;

namespace XAU_Kinetic.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ITradingEngineService _engineService;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartEngineCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopEngineCommand))]
        [NotifyCanExecuteChangedFor(nameof(TriggerEmergencyKillSwitchCommand))]
        private bool _isEngineRunning = false;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(TriggerEmergencyKillSwitchCommand))]
        private bool _isCircuitBroken = false;

        [ObservableProperty]
        private double _balance = 10000.00;

        [ObservableProperty]
        private double _equity = 10000.00;

        [ObservableProperty]
        private double _drawdownPercentage = 0.0;

        [ObservableProperty]
        private string _statusMessage = "Engine Stopped";

        [ObservableProperty]
        private string _circuitReason = "Normal Operation";

        public ObservableCollection<ActivePosition> ActivePositions { get; } = new();
        public ObservableCollection<AuditLogRecord> AuditLogs { get; } = new();

        public MainViewModel(ITradingEngineService engineService, IDialogService dialogService)
        {
            _engineService = engineService ?? throw new ArgumentNullException(nameof(engineService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _engineService.StatusChanged += OnEngineStatusChanged;
        }

        private bool CanStartEngine() => !IsEngineRunning && !IsCircuitBroken;
        private bool CanStopEngine() => IsEngineRunning;
        private bool CanTriggerKillSwitch() => !IsCircuitBroken;

        private void OnEngineStatusChanged(object? sender, EngineStatus status)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(() => UpdateStatusState(status));
            }
            else
            {
                UpdateStatusState(status);
            }
        }

        private void UpdateStatusState(EngineStatus status)
        {
            IsEngineRunning = status == EngineStatus.Running;
            IsCircuitBroken = status == EngineStatus.CircuitBroken;
            StatusMessage = status switch
            {
                EngineStatus.Running => "Engine Running (XAUUSD)",
                EngineStatus.Initializing => "Initializing MT5 Client...",
                EngineStatus.CircuitBroken => "CIRCUIT BREAKER TRIGGERED",
                EngineStatus.Error => "Engine Error / Disconnected",
                _ => "Engine Stopped"
            };
        }

        [RelayCommand(CanExecute = nameof(CanStartEngine))]
        private async Task StartEngineAsync()
        {
            try
            {
                StatusMessage = "Starting...";
                bool success = await _engineService.StartEngineAsync(configPath: "", mockMode: true);
                if (!success)
                {
                    StatusMessage = "Failed to start engine";
                    _dialogService.ShowError("Could not launch trading process.", "Start Engine Error");
                }
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Unexpected error: {ex.Message}", "Engine Failure");
            }
        }

        [RelayCommand(CanExecute = nameof(CanStopEngine))]
        private async Task StopEngineAsync()
        {
            try
            {
                await _engineService.StopEngineAsync();
                StatusMessage = "Engine Stopped";
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error stopping engine: {ex.Message}", "Stop Error");
            }
        }

        [RelayCommand(CanExecute = nameof(CanTriggerKillSwitch))]
        private async Task TriggerEmergencyKillSwitchAsync()
        {
            bool confirmed = _dialogService.ConfirmWarning(
                "ARE YOU SURE YOU WANT TO TRIGGER EMERGENCY KILL SWITCH?\nThis will stop trading loop and halt all execution.",
                "EMERGENCY KILL SWITCH"
            );

            if (confirmed)
            {
                await _engineService.TriggerEmergencyKillSwitchAsync();
                CircuitReason = "EMERGENCY HARDWARE KILL SWITCH ACTIVATED BY USER";
                StatusMessage = "KILL SWITCH ACTIVATED";
                await RefreshDataAsync();
            }
        }

        [RelayCommand]
        private async Task RefreshDataAsync()
        {
            try
            {
                var state = await _engineService.GetAccountStateAsync();
                Balance = state.Balance;
                Equity = state.Equity;

                var logs = await _engineService.GetAuditLogsAsync(50);
                var positions = await _engineService.GetActivePositionsAsync();

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    await dispatcher.InvokeAsync(() => UpdateCollections(logs, positions));
                }
                else
                {
                    UpdateCollections(logs, positions);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during RefreshDataAsync: {ex.Message}");
            }
        }

        private void UpdateCollections(System.Collections.Generic.IReadOnlyList<AuditLogRecord> logs, System.Collections.Generic.IReadOnlyList<ActivePosition> positions)
        {
            AuditLogs.Clear();
            foreach (var log in logs) AuditLogs.Add(log);

            ActivePositions.Clear();
            foreach (var pos in positions) ActivePositions.Add(pos);
        }

        [RelayCommand]
        private async Task VerifyAuditLedgerAsync()
        {
            var (isValid, message) = await _engineService.VerifyAuditChainIntegrityAsync();
            if (isValid)
            {
                _dialogService.ShowInformation($"Cryptographic Audit Chain Status: VERIFIED VALID\n\n{message}", "SHA-256 Ledger Integrity");
            }
            else
            {
                _dialogService.ShowError($"TAMPER DETECTED!\n\n{message}", "SECURITY ALERT");
            }
        }
    }
}
