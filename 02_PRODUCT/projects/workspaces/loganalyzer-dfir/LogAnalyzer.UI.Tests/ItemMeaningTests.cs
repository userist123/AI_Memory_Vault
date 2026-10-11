using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services;
using LogAnalyzer.Core.Services.Details;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// Owner 2026-10-11: the „Ce înseamnă” tab said „Nu există o explicație predefinită” for registry entries, risk factors and most other items,
    /// and showed the knowledge base's sample event (another computer, another person) as „Exemplu de abuz”.
    /// </summary>
    [Collection("DetailCorpus (process-wide state)")]
    public class ItemMeaningTests
    {
        private const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

        [Fact]
        public void A_risk_factor_says_what_it_is_why_how_much_and_the_technique_in_words()
        {
            var m = DetailSheetBuilder.Build(new ExplainableRiskFactor
            {
                Category = "Camuflare procese", Description = "Execuția proceselor critice de sistem din căi neobișnuite", WeightPoints = 25,
                EvidenceSource = "Process Tree & EVTX", MitreTechniqueId = "T1036.003",
            }).Meaning;
            Assert.Contains(m, l => l.StartsWith("Ce este: Factor de risc"));
            Assert.Contains(m, l => l.Contains("căi neobișnuite"));
            Assert.Contains(m, l => l.Contains("25 puncte din scorul de risc de 100"));
            Assert.Contains(m, l => l.Contains("T1036.003") && l.Contains("redenumirea sau copierea unui program de sistem") && l.Contains("nu dovedește un atac"));
        }

        [Fact]
        public void A_boot_configuration_registry_value_is_explained_and_decoded()
        {
            var m = DetailSheetBuilder.Build(new RegistryArtifact
            {
                KeyPath = @"HKEY_LOCAL_MACHINE\BCD00000000\Objects\{9dea862c-5cdd-4e70-acc1-f32b344d4795}\Elements\23000003", ValueName = "Element",
                ValueData = "dword:00000002", Category = "Configurație", SuspicionLevel = "Info",
            }).Meaning;
            Assert.Contains(m, l => l.Contains("întregului calculator"));
            Assert.Contains(m, l => l.Contains("Baza de date de pornire a Windows (BCD)"));
            Assert.Contains(m, l => l.Contains("număr 2"));
            Assert.Contains(m, l => l.Contains("INFORMATIV"));
        }

        [Fact]
        public void Registry_value_notations_are_decoded_and_binary_is_called_binary()
        {
            Assert.Equal("listă de texte: C:\\ · D:.", ItemMeaning.DecodeValue("hex(7):43,00,3a,00,5c,00,00,00,44,00,3a,00,00,00,00,00"));
            Assert.Contains("date binare (3 octeți)", ItemMeaning.DecodeValue("hex:01,02,03"));
            Assert.Null(ItemMeaning.DecodeValue("Windows Recovery Environment"));
        }

        [Fact]
        public void A_service_logon_of_SYSTEM_is_called_normal_and_the_sample_event_is_never_shown()
        {
            var utc = new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);
            var ev = new ParsedEvent
            {
                EventId = 4624, TimeCreated = utc, ProviderName = "Microsoft-Windows-Security-Auditing", MachineName = "WS-01",
                XmlData = $"<Event xmlns=\"{Ns}\"><System><EventID>4624</EventID></System><EventData><Data Name=\"LogonType\">5</Data>" +
                          "<Data Name=\"TargetUserName\">SYSTEM</Data><Data Name=\"TargetDomainName\">NT AUTHORITY</Data><Data Name=\"TargetUserSid\">S-1-5-18</Data></EventData></Event>",
                TacticalExample = "An account was successfully logged on.\nSubject:\n Security ID: SYSTEM\n Account Name: DESKTOP$",
                PotentialCriticality = "Low",
            };
            var m = DetailSheetBuilder.Build(ev).Meaning;
            Assert.Contains(m, l => l.Contains("operație normală a Windows") && l.Contains("NU activitatea unei persoane"));
            Assert.DoesNotContain(m, l => l.Contains("Exemplu de abuz") || l.Contains("Security ID:"));
            Assert.Contains(m, l => l.Contains("SCĂZUT"));
        }

        [Fact]
        public void A_person_at_the_keyboard_is_called_a_person()
        {
            var ev = new ParsedEvent
            {
                EventId = 4624, TimeCreated = DateTime.UtcNow, ProviderName = "Microsoft-Windows-Security-Auditing",
                XmlData = $"<Event xmlns=\"{Ns}\"><System><EventID>4624</EventID></System><EventData><Data Name=\"LogonType\">2</Data><Data Name=\"TargetUserName\">ion</Data></EventData></Event>",
            };
            Assert.Contains(DetailSheetBuilder.Build(ev).Meaning, l => l.Contains("o persoană a intrat pe stație la tastatură"));
        }

        [Fact]
        public void RunMRU_is_what_the_user_typed_not_a_startup_entry()
        {
            var mru = DetailSheetBuilder.Build(new RegistryArtifact { KeyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU", ValueName = "a", ValueData = "cmd" }).Meaning;
            Assert.DoesNotContain(mru, l => l.Contains("persistență"));
            Assert.Contains(mru, l => l.Contains("Ce a tastat utilizatorul"));
            var run = DetailSheetBuilder.Build(new RegistryArtifact { KeyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "x", ValueData = @"c:\x.exe" }).Meaning;
            Assert.Contains(run, l => l.Contains("pornire automată"));
        }

        private sealed class Unknown { public string Foo { get; set; } = "x"; }

        [Fact]
        public void An_unknown_item_still_says_where_to_look_instead_of_nothing()
        {
            var m = DetailSheetBuilder.Build(new Unknown()).Meaning;
            Assert.Single(m);
            Assert.Contains("Toate câmpurile", m[0]);
        }

        [Fact]
        public void Every_attack_technique_the_application_names_has_a_plain_description()
        {
            string root = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(root, "LogAnalyzer.slnx"))) root = Path.GetDirectoryName(root)!;
            var ids = new[] { "LogAnalyzer.Core", "LogAnalyzer.Infrastructure", "LogAnalyzer.App", "LogAnalyzer.Dfir.Core", "LogAnalyzer.Dfir.Windows", "LogAnalyzer.Connectors", "LogAnalyzer.Response" }
                .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\bT1\d{3}(?:\.\d{3})?\b").Select(m => m.Value))
                .Distinct().ToList();
            Assert.True(ids.Count > 100, $"only {ids.Count} technique ids found");
            var missing = ids.Where(id => ItemMeaning.Technique(id).Contains("nu are încă o descriere")).OrderBy(x => x).ToList();
            Assert.True(missing.Count == 0, "techniques without a description: " + string.Join(", ", missing));
        }
    }
}
