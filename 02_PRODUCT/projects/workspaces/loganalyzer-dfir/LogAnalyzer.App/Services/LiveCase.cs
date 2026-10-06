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
                    });
                return _case;
            }
        }
    }
}
