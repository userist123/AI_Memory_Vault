using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Windows.Auth;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// The application's authentication state (owner decision 33): one <see cref="AuthService"/> over %PROGRAMDATA%\LogAnalyzer\auth, the Windows smart-card
    /// provider (contact card in the keyboard slot; PIN handled by Windows / SafeNet) and the signed-in session. The directory is fixed: there is no
    /// environment variable or switch that points the application at another auth store.
    /// </summary>
    public static class AuthApp
    {
#if CLASSIFIED_EDITION
        public const bool Classified = true;
#else
        public const bool Classified = false;
#endif
        private static AuthService? _service;

        public static AuthService Service => _service ?? throw new InvalidOperationException("Authentication not initialised.");
        public static bool IsReady => _service is not null;
        public static ICardProvider Cards { get; } = new WindowsSmartCardProvider();
        public static AuthSession? Session { get; private set; }
        public static event Action? SessionChanged;

        public static void Initialize() => Initialize(null);

        /// <summary>
        /// Test seam (internal, visible only to LogAnalyzer.App.Tests): <paramref name="storeDirectory"/> points the service at a temporary store so the
        /// startup test never writes the machine's %PROGRAMDATA%\LogAnalyzer\auth. Production calls only <see cref="Initialize()"/> (null: the fixed folder).
        /// </summary>
        internal static void Initialize(string? storeDirectory)
        {
            _service = new AuthService(storeDirectory, Classified);
            LogAnalyzer.Dfir.Policy.PolicyApprovals.Ledger = _service;   // review #2: policy approvals are recorded in and verified against the sealed auth audit
            OperatorIdentity.AuthenticationRequired = true;     // from now on registers / profile edits fail closed without an administrator session
            // every program the application starts is written to the sealed audit before it starts ("collector.exec"); a failed write raises the audit warning
            LogAnalyzer.Dfir.Auth.CollectorExec.Sink = Record;
        }

        public static void SetSession(AuthSession? s)
        {
            Session = s;
            OperatorIdentity.Current = s;
            SessionChanged?.Invoke();
        }

        /// <summary>
        /// Appends an operator operation to the sealed audit with the current session. Returns null when it was written, otherwise the reason (in Romanian).
        /// Nothing is reported here: the caller decides (an export is refused, see <see cref="ExportAudit"/>).
        /// </summary>
        public static DeviceAuditReading? ReadDeviceAudit()
        {
            if (_service is null) return null;
            var verdict = _service.VerifyAuditSnapshot(out var bytes);
            return LogAnalyzer.Dfir.Auth.OperationAudit.ReadDeviceMonitor(bytes, verdict.Status == LogAnalyzer.Dfir.Case.ChainStatus.Valid);
        }

        public static string? TryRecord(string ev, string detail) => TryRecordTyped(ev, detail)?.Reason;

        /// <summary>Like <see cref="TryRecord"/>, but says by type (not by the text) when the refusal is because the sealing key cannot be read.</summary>
        public static LogAnalyzer.Dfir.Devices.AuditRefusal? TryRecordTyped(string ev, string detail)
        {
            if (_service is null) return new("autentificarea aplicației nu este pornită");
            try { _service.AuditOperation(Session, ev, detail); return null; }
            catch (LogAnalyzer.Dfir.Case.SealingKeyUnavailableException ex) { return new(ex.Message, KeyUnavailable: true); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException) { return new(ex.Message); }
        }

        /// <summary>
        /// Appends an operator operation to the sealed audit. A failure is never silent: it raises the persistent warning of <see cref="AuditHealth"/>
        /// (banner in the main window and the status line) and returns false.
        /// </summary>
        public static bool Record(string ev, string detail)
        {
            if (TryRecord(ev, detail) is not { } error) return true;
            AuditHealth.Instance.Report(ev, error);
            return false;
        }

        public static void NotifyChanged() => SessionChanged?.Invoke();

        /// <summary>
        /// The operator confirms that the station clock was corrected: appends "clock.ack" to the chained audit (operator, reason, previous and
        /// current time) and refreshes the clock warning. Returns the message to show.
        /// </summary>
        public static string ConfirmClockCorrected(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "Scrieți motivul (cum a fost corectat ceasul) înainte de confirmare.";
            try
            {
                var who = Session is { } s ? $"{s.Role} ({s.Account})" : "operator";
                LogAnalyzer.Dfir.Case.ClockMemory.Acknowledge(Service.AuditPath, DateTimeOffset.UtcNow, who, reason, LogAnalyzer.Dfir.Windows.Audit.StationFactCollector.ReadInstallDate());
                return "Corectarea ceasului a fost confirmată și trecută în jurnalul de audit.";
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return "Confirmarea nu a putut fi scrisă în jurnalul de audit: " + ex.Message;
            }
        }
    }

    /// <summary>
    /// Locks the main window on inactivity (all sessions) and, for a card session only, when the card leaves the keyboard slot. A password session
    /// (primary administrator) locks on inactivity only. A disabled account or card ends the session at once.
    /// </summary>
    public sealed class SessionGuard
    {
        private readonly Window _main;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool _busy;

        public SessionGuard(Window main) { _main = main; }

        public void Start()
        {
            InputManager.Current.PreProcessInput += OnInput;
            _timer.Tick += async (_, _) => await TickAsync();
            _timer.Start();
        }

        private static void OnInput(object sender, PreProcessInputEventArgs e)
        {
            if (AuthApp.Session is { } s && e.StagingItem.Input is KeyboardEventArgs or MouseEventArgs) s.Touch(DateTimeOffset.UtcNow);
        }

        private async Task TickAsync()
        {
            if (_busy || AuthApp.Session is not { } s) return;
            _busy = true;
            try
            {
                var status = await Task.Run(() => AuthApp.Service.CheckSession(s, AuthApp.Cards));
                if (status == SessionStatus.Ended)
                {
                    _timer.Stop();
                    MessageBox.Show("Sesiunea s-a încheiat: contul sau cardul a fost dezactivat de administrator.", "LogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                }
                else if (status == SessionStatus.Locked) PromptUnlock(s);
            }
            finally { _busy = false; }
        }

        private void PromptUnlock(AuthSession s)
        {
            _timer.Stop();
            _main.Hide();            // nothing of the case stays on screen while the session is locked
            var w = new Views.SignInWindow(Views.SignInWindowMode.Unlock, s);
            var ok = w.ShowDialog() == true;
            if (ok) { _main.Show(); _timer.Start(); }
            else { AuthApp.Service.SignOut(s); Application.Current.Shutdown(); }
        }
    }
}
