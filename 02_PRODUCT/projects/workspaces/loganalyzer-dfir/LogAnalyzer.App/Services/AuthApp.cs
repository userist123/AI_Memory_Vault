using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LogAnalyzer.Dfir.Auth;
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
            OperatorIdentity.AuthenticationRequired = true;     // from now on registers / profile edits fail closed without an administrator session
        }

        public static void SetSession(AuthSession? s)
        {
            Session = s;
            OperatorIdentity.Current = s;
            SessionChanged?.Invoke();
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
