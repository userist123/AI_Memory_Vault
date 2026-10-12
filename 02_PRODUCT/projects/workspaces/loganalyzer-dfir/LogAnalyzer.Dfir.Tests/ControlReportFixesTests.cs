using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Native;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Defects seen in a real control report (2026-10-12): UAC without a prompt counted as compliant, Romanian count grammar, the page-1
/// conclusion box, the A10 reason without classified folders, the N08 text for a stopped WinRM with listeners and the WQL group query.
/// </summary>
public class ControlReportFixesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static StationFacts Station(bool admin = true) => new()
    {
        Host = "ST-TEST", PeriodStartUtc = Start, PeriodEndUtc = Start.AddDays(30), CollectedUtc = Start.AddDays(30), IsAdministrator = admin, StationShouldBeIsolated = true,
    };

    private static ControlCheck Check(ControlReport r, string id) => Assert.Single(r.Checks, c => c.Id == id);

    // ---- 1. UAC ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1", "2", "1", ControlStatus.Conform)]
    [InlineData("1", "1", "1", ControlStatus.Conform)]
    [InlineData("1", "5", "1", ControlStatus.Conform)]
    [InlineData("1", "0", "1", ControlStatus.Neconform)]
    [InlineData("1", "0", "0", ControlStatus.Neconform)]
    [InlineData("0", "5", "1", ControlStatus.Neconform)]
    [InlineData("1", "5", "0", ControlStatus.DeVerificat)]
    [InlineData("1", "", "1", ControlStatus.DeVerificat)]
    [InlineData("1", "5", "", ControlStatus.DeVerificat)]
    [InlineData("1", "7", "1", ControlStatus.DeVerificat)]
    [InlineData("", "", "", ControlStatus.Nedeterminat)]
    public void P04_is_conform_only_with_UAC_on_a_prompt_and_the_secure_desktop(string lua, string consent, string secure, ControlStatus expected)
    {
        var f = Station();
        f.Settings["UAC.EnableLUA"] = lua;
        f.Settings["UAC.ConsentPromptBehaviorAdmin"] = consent;
        f.Settings["UAC.PromptOnSecureDesktop"] = secure;
        var c = Check(ControlEvaluator.Evaluate(f), "P04");
        Assert.Equal(expected, c.Status);
        if (lua == "1" && consent == "0")
            Assert.Contains("UAC nu cere confirmare la ridicarea drepturilor (ConsentPromptBehaviorAdmin=0): orice program pornit de un administrator capătă drepturi depline fără întrebare", c.Detail);
        if (expected == ControlStatus.DeVerificat) Assert.False(string.IsNullOrWhiteSpace(c.Detail));
        if (lua == "1" && secure == "0" && consent != "0") Assert.Contains("PromptOnSecureDesktop", c.Detail);
        Assert.Contains(c.Evidence, e => e.Contains("PromptOnSecureDesktop"));
    }

    [Fact]
    public void P04_guide_and_code_explanations_name_the_three_values()
    {
        var g = CheckGuides.For("P04");
        Assert.Contains("PromptOnSecureDesktop", g.Source);
        Assert.Contains("ConsentPromptBehaviorAdmin", g.Source);
        Assert.Contains("ConsentPromptBehaviorAdmin = 0", g.Meaning);
        var c = new ControlCheck("P04", "Politici", "UAC (Control cont utilizator) activ", ControlStatus.Neconform, "UAC nu cere confirmare.", [], "");
        var codes = CheckGuides.Explain(c).Codes.Select(x => x.Code + " " + x.When).ToList();
        Assert.Contains(codes, x => x.Contains("ConsentPromptBehaviorAdmin") && x.Contains("0 = "));
        Assert.Contains(codes, x => x.Contains("PromptOnSecureDesktop"));
    }

    // ---- 2. count grammar ------------------------------------------------------------------------------------------

    private static readonly HashSet<string> Plurals = new(StringComparer.Ordinal)
    {
        "conturi", "zile", "dispozitive", "conectări", "camere", "microfoane", "ieșiri", "excluderi", "ștergeri", "schimbări", "operații", "serii",
        "eșecuri", "sesiuni", "blocuri", "imprimante", "unități", "adaptoare", "profiluri", "componente", "minute", "caractere", "încercări",
        "reguli", "foldere", "discuri", "actualizări", "verificări", "probleme", "evenimente", "membri", "calculatoare", "parole",
    };

    private static readonly Regex NumberNoun = new(@"(?<![\d.,:\-])(?<n>\d+) (?<de>de )?(?<w>[\p{Ll}]+)", RegexOptions.Compiled);

    private static IEnumerable<string> GrammarErrors(string text)
    {
        foreach (Match m in NumberNoun.Matches(text))
        {
            long n = long.Parse(m.Groups["n"].Value);
            var w = m.Groups["w"].Value;
            bool de = m.Groups["de"].Success;
            if (!Plurals.Contains(w)) continue;
            if (n == 1) yield return $"„{m.Value}” (singular cerut)";
            else if (RoText.NeedsDe(n) && !de) yield return $"„{m.Value}” (lipsește „de”)";
        }
    }

    private static StationFacts Many(int n)
    {
        var f = Station();
        f.PasswordPolicy = new PasswordPolicy(n, TimeSpan.FromDays(n), TimeSpan.Zero, 0, n, TimeSpan.FromMinutes(n), TimeSpan.FromMinutes(30));
        for (int i = 0; i < n; i++)
        {
            f.Accounts.Add(new LocalAccount($"adm{i}", $"S-1-5-21-1-2-3-{2000 + i}", false, true, true, false, null, true, false, ""));
            f.UsbDevices.Add(new UsbDeviceFact($"Disk&Ven_X&Prod_{i}", $"SN{i}", $"Stick {i}", "USBSTOR"));
            f.DefenderExclusions.Add($@"C:\excl{i}");
            f.Logs.Add(new LogFact("Microsoft-Windows-Partition/Diagnostic", 1006, Start.AddDays(1).AddMinutes(i), 5000 + i, "",
                new Dictionary<string, string> { ["Capacity"] = "32000000000", ["BusType"] = "7", ["SerialNumber"] = $"SN{i}" }));
            f.Logs.Add(new LogFact("Security", 1102, Start.AddDays(2).AddMinutes(i), 6000 + i, "", new Dictionary<string, string> { ["SubjectUserName"] = "ion" }));
            f.Logs.Add(new LogFact("Security", 4616, Start.AddDays(3).AddMinutes(i), 7000 + i, "",
                new Dictionary<string, string> { ["SubjectUserName"] = "ion", ["ProcessName"] = @"C:\x.exe" }));
            f.Logs.Add(new LogFact("Application", 11707, Start.AddDays(4).AddMinutes(i), 8000 + i, "", new Dictionary<string, string> { ["Data0"] = "app" }));
        }
        foreach (var ch in StationFactCollector.Queries.Keys)
            f.Coverage.Add(new ChannelCoverage(ch, Start.AddDays(-60), 100, true, ""));
        f.Settings["Defender.SignaturesUtc"] = f.CollectedUtc.AddDays(-n).ToString("o");
        f.Settings["Hardware.Cameras"] = n.ToString(); f.Settings["Hardware.Microphones"] = n.ToString(); f.Settings["Hardware.Speakers"] = n.ToString();
        f.Settings["Print.PrinterCount"] = n.ToString(); f.Settings["Print.LogEnabled"] = "1";
        f.Settings["Optical.WriterCount"] = n.ToString(); f.Settings["Optical.DriveCount"] = n.ToString();
        f.Settings["Wifi.AdapterCount"] = n.ToString(); f.Settings["Wifi.UpCount"] = n.ToString(); f.Settings["Wlan.ProfileCount"] = n.ToString();
        f.Settings["Bluetooth.ComponentCount"] = n.ToString();
        f.Settings["Net.AdapterCount"] = n.ToString();
        f.Settings["ScreenLock.InactivityTimeoutSecs"] = (n * 60).ToString();
        f.Settings["LegalNotice.Caption"] = "Clasificat"; f.Settings["LegalNotice.Text"] = new string('x', n);
        f.FolderAudits = Enumerable.Range(0, n).Select(i => new FolderAuditFact($@"C:\Clasificat{i}", true, n, "Everyone: Read (Success)", "")).ToList();
        f.AuditPolicy = new() { ["File System"] = AuditSetting.Success | AuditSetting.Failure };
        f.InstalledUpdates = Enumerable.Range(0, n).Select(i => new InstalledUpdateFact($"KB{5000 + i}", Start.AddDays(2).UtcDateTime, "Update")).ToList();
        return f;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(21)]
    public void Control_texts_use_singular_for_one_and_de_after_twenty(int n)
    {
        var r = ControlEvaluator.Evaluate(Many(n));
        var screen = ControlResultScreen.Build(r, []);
        var texts = r.Checks.SelectMany(c => c.Evidence.Prepend(c.Detail).Prepend(c.Title))
            .Concat([screen.Headline, screen.Problem, screen.Seriousness, screen.Found]).Concat(screen.NextSteps).ToList();
        var errors = texts.SelectMany(t => GrammarErrors(t).Select(e => e + " în: " + t)).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void Owner_examples_are_written_in_words()
    {
        var r = ControlEvaluator.Evaluate(Many(1));
        Assert.Contains("Un cont activ cu drepturi", Check(r, "C02").Detail);
        Assert.Contains("Un dispozitiv în istoricul", Check(r, "D01").Detail);
        Assert.Contains("o conectare", Check(r, "D01").Detail);
        Assert.Contains("o cameră", Check(r, "P31").Detail);
        Assert.Contains("fără istoric", Check(r, "P03").Detail);
        Assert.DoesNotContain("istoric 0", Check(r, "P03").Detail);

        var f = Many(2);
        f.PasswordPolicy = f.PasswordPolicy! with { MaxAge = TimeSpan.FromDays(42) };
        f.Settings["Defender.SignaturesUtc"] = f.CollectedUtc.AddHours(-3).ToString("o");
        var r2 = ControlEvaluator.Evaluate(f);
        Assert.Contains("42 de zile", Check(r2, "P03").Detail);
        Assert.Contains("mai puțin de o zi", Check(r2, "P09").Detail);
        Assert.DoesNotContain("0 zile", Check(r2, "P09").Detail);
    }

    [Theory]
    [InlineData(0.2, "mai puțin de o zi")]
    [InlineData(1, "o zi")]
    [InlineData(2, "2 zile")]
    [InlineData(42, "42 de zile")]
    [InlineData(101, "101 zile")]
    public void Days_reads_naturally(double days, string expected) => Assert.Equal(expected, RoText.Days(days));

    [Theory]
    [InlineData(1, "un minut")]
    [InlineData(15, "15 minute")]
    [InlineData(30, "30 de minute")]
    [InlineData(1.5, "1,5 minute")]
    public void Minutes_reads_naturally(double minutes, string expected) => Assert.Equal(expected, RoText.Minutes(minutes));

    // ---- 3. page-1 conclusion box ----------------------------------------------------------------------------------

    [Fact]
    public void Control_report_page_one_has_the_short_conclusion_with_the_non_compliant_checks()
    {
        var r = new ControlReport { Facts = Station(admin: true) };
        r.Checks.Add(new("D02", "Dispozitive", "Scriere pe suporturi necriptate", ControlStatus.Neconform, "Se poate scrie pe stick-uri necriptate.", [], "Refuzați scrierea pe suporturi necriptate."));
        r.Checks.Add(new("P06", "Politici", "Conexiuni Remote Desktop", ControlStatus.Neconform, "Remote Desktop este ACTIVAT.", [], "Dezactivați Remote Desktop."));
        r.Checks.Add(new("P11", "Politici", "Criptarea discului de sistem (BitLocker)", ControlStatus.Neconform, "Discul nu este criptat.", [], "Criptați discul cu BitLocker."));
        r.Checks.Add(new("C01", "Conturi", "Contul Guest este dezactivat", ControlStatus.Neconform, "Contul Guest este ACTIV.", [], "Dezactivați contul Guest."));
        r.Checks.Add(new("A10", "Audit", "Auditarea accesului la folderele clasificate (SACL)", ControlStatus.Nedeterminat, "Profilul nu definește foldere.", [], ""));
        r.Checks.Add(new("C02", "Conturi", "Administratori", ControlStatus.Conform, "ok", [], ""));

        var box = ControlConclusion.Build(r);
        Assert.Contains("4", box.Verdict);
        Assert.Contains(box.Serious, s => s.Contains("Dispozitive") && s.Contains("Scriere pe suporturi necriptate"));
        Assert.Contains(box.Serious, s => s.Contains("Politici") && s.Contains("Remote Desktop") && s.Contains("BitLocker"));
        Assert.Contains(box.Serious, s => s.Contains("Conturi") && s.Contains("Guest"));
        Assert.True(box.Serious.Count <= 5);
        Assert.Equal(3, box.NextSteps.Count);
        Assert.Equal("Refuzați scrierea pe suporturi necriptate.", box.NextSteps[0]);       // media / transfer first
        Assert.Equal("Dezactivați Remote Desktop.", box.NextSteps[1]);                      // then remote access
        Assert.Equal("Criptați discul cu BitLocker.", box.NextSteps[2]);                    // then encryption
        Assert.Contains(box.NotChecked, s => s.Contains("A10") && s.Contains("Profilul nu definește foldere"));
        Assert.Contains(box.NotChecked, s => s.Contains("cu drepturi de administrator"));
        Assert.False(string.IsNullOrWhiteSpace(box.Meaning));

        var printed = new List<string>();
        ControlReportPdf.TextObserver.Value = t => printed.Add(t);
        try
        {
            var pdf = Path.Combine(Path.GetTempPath(), $"ctl_{Guid.NewGuid():N}.pdf");
            ControlReportPdf.Write(r, pdf, "inspector");
            File.Delete(pdf);
        }
        finally { ControlReportPdf.TextObserver.Value = null; }
        var text = string.Join("\n", printed);
        foreach (var part in new[] { "Concluzie pe scurt", "Ce înseamnă pentru dumneavoastră", "Pași următori", "Ce nu s-a putut verifica", "Scriere pe suporturi necriptate", "Contul Guest" })
            Assert.Contains(part, text);
        Assert.True(text.IndexOf("Concluzie pe scurt", StringComparison.Ordinal) < text.IndexOf("1. Verificări", StringComparison.Ordinal));
    }

    [Fact]
    public void Conclusion_without_non_compliant_checks_says_so_and_names_missing_admin_rights()
    {
        var r = new ControlReport { Facts = Station(admin: false) };
        r.Checks.Add(new("C02", "Conturi", "Administratori", ControlStatus.Conform, "ok", [], ""));
        var box = ControlConclusion.Build(r);
        Assert.Empty(box.Serious);
        Assert.Contains("nicio verificare neconformă", box.Verdict, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(box.NotChecked, s => s.Contains("FĂRĂ drepturi de administrator"));
    }

    // ---- 4. A10 ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A10_without_classified_folders_in_the_profile_asks_to_fill_in_the_table_not_for_admin_rights()
    {
        var f = Station(admin: true);
        f.FolderAudits = [];
        var c = Check(ControlEvaluator.Evaluate(f), "A10");
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains("Profilul de proceduri nu definește foldere clasificate; completați tabelul «Foldere clasificate»", c.Detail);
        Assert.DoesNotContain("administrator", c.Detail);
    }

    [Fact]
    public void A10_with_folders_that_cannot_be_read_keeps_the_admin_message()
    {
        var f = Station(admin: false);
        f.AuditPolicy = new() { ["File System"] = AuditSetting.Success };
        f.FolderAudits = [new FolderAuditFact(@"C:\Clasificat", true, null, "", "regulile de audit nu au putut fi citite (necesită administrator)")];
        var c = Check(ControlEvaluator.Evaluate(f), "A10");
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains("drepturi de administrator", c.Detail);
    }

    // ---- 5. N08 ------------------------------------------------------------------------------------------------------

    [Fact]
    public void N08_stopped_WinRM_with_listeners_is_to_verify_with_a_clear_text()
    {
        var f = Station();
        f.Settings["WinRM.ServiceStart"] = "3"; f.Settings["WinRM.Listeners"] = "Listener_1084132640"; f.Settings["WinRM.State"] = "Stopped";
        var c = Check(ControlEvaluator.Evaluate(f), "N08");
        Assert.Equal(ControlStatus.DeVerificat, c.Status);
        Assert.Contains("Serviciul WinRM este oprit acum, dar are configurate puncte de ascultare (Listener_1084132640); dacă pornește, stația acceptă administrare de la distanță", c.Detail);
        Assert.DoesNotContain("nu rulează acum și", c.Detail);
    }

    [Theory]
    [InlineData("Running", "Listener_1", ControlStatus.Neconform)]
    [InlineData("Running", "(niciuna)", ControlStatus.DeVerificat)]
    [InlineData("Stopped", "(niciuna)", ControlStatus.Conform)]
    public void N08_is_non_compliant_only_when_the_service_runs_with_listeners(string state, string listeners, ControlStatus expected)
    {
        var f = Station();
        f.Settings["WinRM.ServiceStart"] = "3"; f.Settings["WinRM.Listeners"] = listeners; f.Settings["WinRM.State"] = state;
        Assert.Equal(expected, Check(ControlEvaluator.Evaluate(f), "N08").Status);
    }

    // ---- 6. WQL (security audit finding #16) -------------------------------------------------------------------------

    [Fact]
    public void Group_member_query_escapes_quotes_and_backslashes_in_the_group_name()
    {
        Assert.Equal("SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent = \"Win32_Group.Domain='PC',Name='Administrators'\"",
            StationFactCollector.GroupUserQuery("PC", "Administrators"));
        var q = StationFactCollector.GroupUserQuery("PC", "Admin's \\ \"grup\"");
        // object-path level: ' → \' and \ → \\; WQL string level: \ → \\ and " → \"
        Assert.Equal("SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent = \"Win32_Group.Domain='PC',Name='Admin\\\\'s \\\\\\\\ \\\"grup\\\"'\"", q);
        // the literal still ends where it should: one unescaped double quote at each end of the value
        var inner = q[(q.IndexOf('"') + 1)..^1];
        Assert.DoesNotMatch(@"(?<!\\)""", inner);
    }
}
