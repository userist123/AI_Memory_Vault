using System;
using System.IO;
using System.Globalization;

namespace LogAnalyzer.Core.Services
{
    /// <summary>
    /// Planner #9 item 6. What is enforced, exactly:
    /// (1) <see cref="Require"/>: the operations that collect, write evidence, save a register, run a control, export / sign a proces-verbal or contain call it with the capability of
    /// their page (catalogue: <see cref="LicenseFeatures"/>) before they do anything; the navigation gate (MainViewModel.OpenTab) stays as the first line.
    /// (2) <see cref="RequireWritable"/>: a write on a page that has no capability (profile, policies, accounts, case notes, signing on Home) is refused only while the licence is expired (read-only).
    /// Both read the licence verified at start (<see cref="LicenseService.VerifiedClaims"/>, the same source as navigation) and fail closed: no verified licence, a capability the licence does not
    /// name, a name outside the catalogue, or an expiry day that has passed (checked against max(clock, latest time the station has recorded) at every call, so also while the application runs
    /// and after a clock set back) all refuse. An expired-but-otherwise-valid licence is the read-only state (<see cref="IsReadOnly"/>): reading is never gated here, every write is refused.
    /// What is NOT enforced: operations the catalogue does not tie to a capability and that this gate was not added to (see the task report); a process in which <see cref="Configure"/> was never
    /// called has no licence regime and allows (the application calls it at start; <see cref="IsConfigured"/> is asserted there).
    /// </summary>
    public static class LicenseGate
    {
        public const string NotLicensedMessage = "Această funcție nu este inclusă în licența acestei stații.";
        public const string NoLicenseMessage = "Nu există o licență verificată în această sesiune; operațiunea nu a fost executată.";

        private static readonly object Sync = new();
        private static LicenseService? _service;
        private static Func<DateTime> _utcNow = () => DateTime.UtcNow;
        private static DateTime _seenFloor = DateTime.MinValue;

        /// <summary>Called with (capability, reason) for every refusal so the application records it in its sealed audit; set by the application after the audit is open.</summary>
        public static Action<string, string>? Refusal { get; set; }

        /// <summary>Binds the gate to the licence service of this process (null removes the binding). <paramref name="utcNow"/> is for tests.</summary>
        public static void Configure(LicenseService? service, Func<DateTime>? utcNow = null)
        {
            lock (Sync) { _service = service; _utcNow = utcNow ?? (() => DateTime.UtcNow); _seenFloor = DateTime.MinValue; }
        }

        /// <summary>
        /// The latest time this station has recorded (the application's clock memory, ClockMemory.LatestRecorded). The expiry comparison uses max(clock, this), so setting the
        /// clock back before the expiry day, after the station has seen a later time, does not bring an expired licence back. Only raises, never lowers.
        /// </summary>
        public static void RaiseClockFloor(DateTime seenUtc)
        {
            lock (Sync) { if (seenUtc > _seenFloor) _seenFloor = seenUtc; }
        }

        private static DateTime Now() { lock (Sync) { var n = _utcNow(); return n > _seenFloor ? n : _seenFloor; } }

        /// <summary>True when the licence regime applies in this process.</summary>
        public static bool IsConfigured { get { lock (Sync) return _service is not null; } }

        private static string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static bool PastExpiry(LicenseClaims c, DateTime now) => now >= c.ExpiresUtcDate.Date.AddDays(1);

        /// <summary>The claims that make the station read-only (licence expired, signature / product / hardware / module right); null when it is not read-only.</summary>
        private static LicenseClaims? ReadOnlyClaims()
        {
            LicenseService? s; DateTime now;
            lock (Sync) { s = _service; }
            now = Now();
            if (s is null) return null;
            if (s.VerifiedClaims is { } v) return PastExpiry(v, now) ? v : null;   // verified at start, expired since (the application kept running)
            return s.ExpiredClaims;
        }

        public static bool IsReadOnly => ReadOnlyClaims() is not null;

        /// <summary>„Licență expirată — mod doar citire (data expirării …)”; null when the licence is not expired.</summary>
        public static string? ReadOnlyBanner => ReadOnlyClaims() is { } c ? $"Licență expirată — mod doar citire (data expirării {Day(c.ExpiresUtcDate)})" + (ClockBehind() ? " — ceasul stației este în urmă față de ultima oră înregistrată; expirarea se măsoară față de aceasta" : "") : null;

        private static bool ClockBehind() { lock (Sync) { return _seenFloor > DateTime.MinValue && _utcNow() < _seenFloor - LicenseService.ClockBehindTolerance; } }   // review L2: tolerance

        /// <summary>
        /// True when the verified licence grants <paramref name="capability"/> now. Otherwise false, with <paramref name="message"/> (Romanian) for the status line, and the
        /// refusal is passed to <see cref="Refusal"/>.
        /// </summary>
        public static bool Require(string capability, out string message)
        {
            LicenseService? s; DateTime now;
            lock (Sync) { s = _service; }
            now = Now();
            if (s is null) { message = ""; return true; }   // no licence regime in this process (see the class summary)

            string reason;
            if (!LicenseFeatures.IsKnown(capability)) { reason = "unknown_capability"; message = NotLicensedMessage; }
            else if (s.VerifiedClaims is { } v)
            {
                if (PastExpiry(v, now)) { reason = "expired"; message = ExpiredMessage(v); }
                else if (v.Has(capability)) { message = ""; return true; }
                else { reason = "not_in_licence"; message = NotLicensedMessage; }
            }
            else if (s.ExpiredClaims is { } e) { reason = "expired"; message = ExpiredMessage(e); }
            else { reason = "no_verified_licence"; message = NoLicenseMessage; }

            try { Refusal?.Invoke(capability ?? "", reason); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { /* the refusal stands even when it cannot be recorded */ }
            return false;
        }

        /// <summary>
        /// For a write on a page without a licence capability: refuses (false, Romanian message) while the licence is expired or nothing was verified; allows otherwise.
        /// It grants no right by itself: it only makes read-only mean "no writes" without inventing capabilities.
        /// </summary>
        public static bool RequireWritable(out string message)
        {
            LicenseService? s;
            lock (Sync) { s = _service; }
            if (s is null) { message = ""; return true; }
            var ro = ReadOnlyClaims();
            if (ro is null && s.VerifiedClaims is not null) { message = ""; return true; }
            message = ro is not null ? ExpiredMessage(ro) : NoLicenseMessage;
            try { Refusal?.Invoke("write", ro is not null ? "expired" : "no_verified_licence"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            return false;
        }

        private static string ExpiredMessage(LicenseClaims c) =>
            $"Licență expirată — mod doar citire (data expirării {Day(c.ExpiresUtcDate)}). Operațiunea nu a fost executată; cazurile, rapoartele și registrele se pot citi. Cereți o licență nouă pentru Hardware ID-ul stației.";
    }
}
