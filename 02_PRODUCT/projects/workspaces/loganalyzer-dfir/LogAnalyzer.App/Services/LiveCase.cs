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
        public static void Configure(CaseScope scope) { lock (Gate) _configured = scope; }

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

        /// <summary>Verdict line of the re-check run when the LIVE case was opened (WP3b); null before the case is opened or when it was just created.</summary>
        public static string? IntegrityLine { get { lock (Gate) return _case?.LastRecheck?.Summary; } }

        public static CaseWorkspace Get()
        {
            lock (Gate)
            {
                if (_case is not null) return _case;
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "Cases", $"LIVE-{Environment.MachineName}");
                _case = File.Exists(Path.Combine(root, "case.json"))
                    ? CaseWorkspace.Open(root)
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
                return _case;
            }
        }
    }
}
