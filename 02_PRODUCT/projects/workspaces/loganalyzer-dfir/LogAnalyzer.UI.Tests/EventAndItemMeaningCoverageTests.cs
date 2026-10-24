using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Services.Details;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Senior review 2026-10-11 E1/E2: the events behind DCSync, LSASS and USB findings showed „nu are încă o explicație”, and the anti-forensics
    /// and media rows had no „Ce înseamnă”. Every event ID a production engine compares against must have a plain-language meaning.
    /// </summary>
    [Collection("DetailCorpus (process-wide state)")]
    public class EventAndItemMeaningCoverageTests
    {
        // Numbers the engines compare against that are NOT Windows event IDs, so no Windows meaning is written for them.
        private static readonly HashSet<int> NotWindowsEventIds =
        [
            20107, // synthetic id given by TriageCsvParser to a "running process" row of a triage CSV (AnomalyDetectionEngine reads it)
            20109, // synthetic id given by TriageCsvParser to another triage CSV row kind (AnomalyDetectionEngine reads it)
        ];

        private static string Root()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return d.FullName;
            throw new DirectoryNotFoundException("LogAnalyzer.slnx");
        }

        // `EventId == 4624`, `EventId != "1"`, `EventId >= 257`, `SysmonEventId = 8`, `EventId is "5137" or "5141"`.
        private static readonly Regex Comparison = new(@"EventId\s*(?:==|!=|>=|<=|=(?!=))\s*""?\d+""?|EventId\s+is\s+(?:not\s+)?""?\d+""?(?:\s+or\s+""?\d+""?)*", RegexOptions.Compiled);

        internal static Dictionary<int, SortedSet<string>> EngineEventIds()
        {
            var root = Root();
            var files = Directory.EnumerateFiles(Path.Combine(root, "LogAnalyzer.Core", "Services"), "*Engine.cs")
                .Concat(Directory.EnumerateFiles(Path.Combine(root, "LogAnalyzer.Dfir.Core", "Analysis"), "*.cs"));
            var ids = new Dictionary<int, SortedSet<string>>();
            foreach (var f in files)
                foreach (Match m in Comparison.Matches(File.ReadAllText(f)))
                    foreach (Match n in Regex.Matches(m.Value, @"\d+"))
                    {
                        var id = int.Parse(n.Value);
                        if (!ids.TryGetValue(id, out var where)) ids[id] = where = new SortedSet<string>();
                        where.Add(Path.GetFileName(f));
                    }
            return ids;
        }

        [Fact]
        public void Every_event_id_a_production_engine_analyses_has_a_plain_meaning()
        {
            var ids = EngineEventIds();
            Assert.True(ids.Count > 50, $"only {ids.Count} event ids parsed: the scan pattern no longer matches the engines");
            foreach (var id in new[] { 7, 8, 10, 15, 4656, 4662, 4663, 4742, 4756, 4800, 4801, 5136, 5137, 5141, 20001, 20003, 50053, 50074, 50125, 53003 })
                Assert.True(ids.ContainsKey(id), $"{id} is no longer found by the scan");
            var missing = ids.Where(kv => !NotWindowsEventIds.Contains(kv.Key) && !EventMeaning.HasMeaning(kv.Key))
                             .Select(kv => $"{kv.Key} ({string.Join(", ", kv.Value)})").ToList();
            Assert.True(missing.Count == 0, "event ids analysed but not explained: " + string.Join("; ", missing));
        }

        [Theory]
        [InlineData(10, "Microsoft-Windows-Sysmon", "lsass.exe")]
        [InlineData(8, "Microsoft-Windows-Sysmon", "CreateRemoteThread")]
        [InlineData(4662, "Microsoft-Windows-Security-Auditing", "DCSync")]
        [InlineData(4800, "Microsoft-Windows-Security-Auditing", "blocat")]
        [InlineData(5379, "Microsoft-Windows-Security-Auditing", "Credential Manager")]
        [InlineData(20001, "Microsoft-Windows-UserPnp", "stick USB")]
        [InlineData(41, "Microsoft-Windows-Kernel-Power", "fără o oprire corectă")]
        [InlineData(1, "Microsoft-Windows-Kernel-General", "Ora sistemului")]
        [InlineData(91, "Microsoft-Windows-WinRM", "de la distanță")]
        [InlineData(53003, "AzureAD-SignIn", "acces condiționat")]
        public void The_meaning_is_chosen_by_provider(int id, string provider, string expected)
        {
            var into = new List<string>();
            EventMeaning.Explain(id, provider, new Dictionary<string, string>(), into);
            Assert.Contains(into, l => l.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Sysmon_1_and_Kernel_General_1_keep_their_own_meanings()
        {
            var sysmon = new List<string>(); var kernel = new List<string>();
            EventMeaning.Explain(1, "Microsoft-Windows-Sysmon", new Dictionary<string, string>(), sysmon);
            EventMeaning.Explain(1, "Microsoft-Windows-Kernel-General", new Dictionary<string, string>(), kernel);
            Assert.Contains(sysmon, l => l.Contains("proces creat"));
            Assert.Contains(kernel, l => l.Contains("Ora sistemului"));
        }

        [Fact]
        public void An_anti_forensics_check_says_what_it_is_why_the_technique_and_the_result()
        {
            var m = DetailSheetBuilder.Build(new AntiForensicCheck("AF01", "Jurnal EVTX golit", "T1070.001", AntiForensicResult.Detected, "Security 1102 la 10:02.", [])).Meaning;
            Assert.Contains(m, l => l.StartsWith("Ce este: Verificare de ștergere a urmelor"));
            Assert.Contains(m, l => l.StartsWith("De ce: Security 1102"));
            Assert.Contains(m, l => l.Contains("T1070.001") && l.Contains("golirea jurnalelor"));
            Assert.Contains(m, l => l.StartsWith("Rezultat: DETECTAT"));
            Assert.DoesNotContain(m, l => l.Contains("nu are încă o explicație"));
        }

        [Fact]
        public void A_device_compared_with_the_register_says_the_verdict_and_what_to_do()
        {
            var a = new DeviceAssessment(new DeviceRecord { Kind = DeviceKind.UsbStorage, InstanceId = @"USBSTOR\DISK&VEN_X\123" }, RegisterMatch.NotRegistered, "",
                                         "Suport neînregistrat", "Opriți folosirea lui și cereți înregistrarea.");
            var m = DetailSheetBuilder.Build(a).Meaning;
            Assert.Contains(m, l => l.StartsWith("Ce este: Dispozitiv comparat cu registrul"));
            Assert.Contains(m, l => l == "Concluzie: Suport neînregistrat");
            Assert.Contains(m, l => l == "Față de registru: NU este în registru");
            Assert.Contains(m, l => l == "Ce faceți: Opriți folosirea lui și cereți înregistrarea.");
        }
    }
}
