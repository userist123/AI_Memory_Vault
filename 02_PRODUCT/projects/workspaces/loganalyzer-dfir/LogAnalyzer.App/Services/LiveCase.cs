using System;
using System.IO;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.UI.Services
{
    /// <summary>The case of this station (containment incidents, control reports): %LOCALAPPDATA%\LogAnalyzer\Cases\LIVE-&lt;host&gt;.</summary>
    public static class LiveCase
    {
        private static CaseWorkspace? _case;
        private static readonly object Gate = new();

        private static CaseScope? _configured;

        /// <summary>Scope entered by the operator in the case dialog; used if the LIVE case still has to be created.</summary>
        public static void Configure(CaseScope scope)
        {
            lock (Gate)
            {
                _configured = scope;
                // A scope the operator just entered also confirms an existing LIVE case that still has the provisional one.
                if (_case is { ScopeNote: not null } ws && scope.IsConfirmed) ws.ConfirmScope(scope, Environment.UserName);
            }
        }

        /// <summary>
        /// Shows the scope dialog: (current scope, true when it opens right after an emergency containment) -> the scope the operator confirmed, or null.
        /// Set by the application at start-up; null in tests and headless use, in which case nothing can be confirmed here.
        /// </summary>
        public static Func<CaseScope?, bool, CaseScope?>? ScopePrompt { get; set; }

        /// <summary>Shown by the screens when the operator closes the scope dialog without confirming.</summary>
        public const string ScopeRequiredMessage = "Scopul cazului nu este confirmat: completați scopul (de ce, perioadă, sisteme, aprobator) înainte de prima utilizare LIVE.";

        private static string RootPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "Cases", $"LIVE-{Environment.MachineName}");

        /// <summary>True when the LIVE case has a complete scope that the operator entered (not the placeholder).</summary>
        public static bool ScopeConfirmed
        {
            get { lock (Gate) return _case is not null ? _case.Info.Scope.IsConfirmed : _configured is { IsConfirmed: true }; }
        }

        /// <summary>
        /// Owner decision 28: the LIVE case for any use except emergency containment. Before the first LIVE use the scope dialog must be completed:
        /// returns null (and nothing is created or changed) when the operator does not confirm a scope. A confirmed scope goes to the audit chain
        /// as <c>case.scope_confirmed</c>.
        /// </summary>
        public static CaseWorkspace? GetConfirmed()
        {
            if (!ScopeConfirmed)
            {
                CaseScope? current;
                lock (Gate) current = _case?.Info.Scope ?? ReadScopeOnDisk();
                var scope = ScopePrompt?.Invoke(current, false);
                if (scope is null || !scope.IsConfirmed) return null;
                Configure(scope);   // confirms an existing provisional case, or is used when the case is created below
            }
            var ws = Get();
            return ws.Info.Scope.IsConfirmed ? ws : null;
        }

        /// <summary>
        /// Emergency containment may start on the provisional scope; this is called right after it: opens the scope dialog unless the scope is
        /// already confirmed. If the operator closes it, reports and exports keep carrying "scop provizoriu, neconfirmat".
        /// </summary>
        public static bool RequestScopeConfirmation()
        {
            if (ScopeConfirmed) return true;
            if (_prompting) return false;   // a modal dialog pumps messages: a timer-driven containment must not open a second one
            _prompting = true;
            try
            {
                CaseScope? current;
                lock (Gate) current = _case?.Info.Scope;
                var scope = ScopePrompt?.Invoke(current, true);
                if (scope is null || !scope.IsConfirmed) return false;
                Configure(scope);
                return ScopeConfirmed;
            }
            finally { _prompting = false; }
        }

        private static bool _prompting;

        private static CaseScope? ReadScopeOnDisk()
        {
            try
            {
                var file = Path.Combine(RootPath(), "case.json");
                return File.Exists(file) ? LogAnalyzer.Dfir.IO.Json.Read<CaseInfo>(file).Scope : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
        }

        /// <summary>
        /// Scope for a LIVE case created before the operator entered one. These values are NOT an authorization: the approver is
        /// marked unknown, the system category is the most restrictive one, the period starts now and is open for one year, and the note says it must be confirmed.
        /// OWNER DECISION PENDING (see todo-claude-wp3.md, Blockers).
        /// </summary>
        private static CaseScope ProvisionalScope()
        {
            var now = DateTimeOffset.UtcNow;
            return new CaseScope
            {
                Purpose = "Caz LIVE al stației (control și izolare), scop implicit de confirmat",
                PeriodFromUtc = now, PeriodToUtc = now.AddYears(1),
                SystemsInScope = [Environment.MachineName],
                Approver = "necunoscut (de confirmat)",
                LegalBasis = LegalBasis.Control,
                // Unknown to the application; the most restrictive handling category is the safe placeholder.
                Network = NetworkCategory.AirGappedNetwork,
                Classification = ClassificationLevel.Classified,
                Notes = "scop provizoriu: categoria sistemului, aprobatorul și perioada trebuie confirmate de operator",
                Provisional = true,
            };
        }

        private static Task? _recheck;

        /// <summary>
        /// Verdict line of the re-check started when the LIVE case was opened (WP3b); null before the case is opened or when it was just created.
        /// The re-check runs in the background (a large case would otherwise freeze the UI on the first <see cref="Get"/>), so until it ends
        /// this says it is still running, and a failed re-check is reported, never shown as a clean result.
        /// </summary>
        public static string? IntegrityLine
        {
            get
            {
                lock (Gate)
                {
                    if (_case is null) return null;
                    if (_recheck is { IsCompleted: false }) return "Verificare de integritate în curs…";
                    if (_recheck is { IsFaulted: true } t) return "Verificarea de integritate a eșuat: " + (t.Exception?.GetBaseException().Message ?? "eroare necunoscută");
                    return _case.LastRecheck?.Summary;
                }
            }
        }

        public static CaseWorkspace Get()
        {
            lock (Gate)
            {
                if (_case is not null) return _case;
                var root = RootPath();
                bool existing = File.Exists(Path.Combine(root, "case.json"));
                _case = existing
                    ? CaseWorkspace.Open(root, recheck: false)
                    : CaseWorkspace.Create(root, new CaseInfo
                    {
                        CaseId = $"LIVE-{Environment.MachineName}",
                        Name = $"Stația {Environment.MachineName}",
                        Host = Environment.MachineName,
                        User = Environment.UserName,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        Investigator = Environment.UserName,
                        Os = Environment.OSVersion.VersionString,
                        Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                        Timezone = TimeZoneInfo.Local.Id,
                        CollectionMode = "live",
                        Scope = _configured ?? ProvisionalScope(),
                    });
                if (existing)
                {
                    if (_case.ScopeNote is not null && _configured is { IsConfirmed: true }) _case.ConfirmScope(_configured, Environment.UserName);
                    var opened = _case;
                    _recheck = Task.Run(() => opened.Recheck());
                }
                return _case;
            }
        }
    }
}
