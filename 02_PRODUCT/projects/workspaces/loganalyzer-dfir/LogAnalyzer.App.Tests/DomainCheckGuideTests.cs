using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Services.Details;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Containment;
using LogAnalyzer.Dfir.Windows.Domain;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Senior review 2026-10-11 E2/E3: Kerberoasting and mail bursts were shown with no „ce verificăm / de ce contează”. Every check the domain and mailbox
/// evaluators can emit has a written guide, and the detail window (double-click on any row) shows it. (The station-check sync test lives in
/// LogAnalyzer.Dfir.Tests, whose compiled assembly Defender quarantines on the development machine; this one covers the domain and mail ids.)
/// </summary>
public sealed class DomainCheckGuideTests
{
    private static string Source(params string[] relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LogAnalyzer.slnx"))) return File.ReadAllText(Path.Combine([d.FullName, .. relative]));
        throw new DirectoryNotFoundException("LogAnalyzer.slnx");
    }

    internal static List<string> EmittedIds()
    {
        var domain = Regex.Matches(Source("LogAnalyzer.Dfir.Windows", "Domain", "DomainEvaluator.cs"), @"Add\(""(DM\d\d)""").Select(m => m.Groups[1].Value);
        // DM1x is built from the index of the group in the catalog: Add($"DM1{Array.FindIndex(PrivilegedGroupCatalog.Ids, ...)}", ...)
        var groups = Enumerable.Range(0, PrivilegedGroupCatalog.Ids.Length).Select(i => $"DM1{i}");
        var mail = Regex.Matches(Source("LogAnalyzer.Dfir.Windows", "Domain", "MailInvestigation.cs"), @"C\(""(EM\d\d)""").Select(m => m.Groups[1].Value);
        return domain.Concat(groups).Concat(mail).Distinct().OrderBy(x => x).ToList();
    }

    [Fact]
    public void Every_domain_and_mailbox_check_has_a_written_guide()
    {
        var ids = EmittedIds();
        Assert.True(ids.Count(i => i.StartsWith("DM", StringComparison.Ordinal)) >= 14, "domain ids no longer parsed: " + string.Join(", ", ids));
        Assert.True(ids.Count(i => i.StartsWith("EM", StringComparison.Ordinal)) == 5, "mail ids no longer parsed: " + string.Join(", ", ids));
        var missing = ids.Where(id => !CheckGuides.Ids.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "checks without a guide: " + string.Join(", ", missing));
        foreach (var id in ids)
        {
            var g = CheckGuides.For(id);
            Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 10, $"{id}: empty section"));
        }
    }

    [Fact]
    public void The_guides_of_the_privileged_groups_name_the_group()
    {
        for (var i = 0; i < PrivilegedGroupCatalog.Ids.Length; i++)
            Assert.Contains(PrivilegedGroupCatalog.Ids[i].Name, CheckGuides.For($"DM1{i}").What);
    }

    [Fact]
    public void Double_click_on_a_domain_check_shows_its_guide_not_the_generic_lines()
    {
        global::LogAnalyzer.UI.AppComposition.RegisterMeaningExplainers();
        var check = new ControlCheck("DM01", "Domeniu", "Conturi de utilizator cu SPN (vulnerabile la Kerberoasting)", ControlStatus.DeVerificat,
                                     "2 conturi: oricine din domeniu poate cere un tichet.", ["svc_backup: MSSQLSvc/srv01"], "Parole de cel puțin 25 de caractere.");
        var m = DetailSheetBuilder.Build(check).Meaning;
        Assert.Contains(m, l => l.StartsWith("Ce este: Verificare de control"));
        Assert.Contains(m, l => l.StartsWith("Ce verificăm: Ce conturi de persoane"));
        Assert.Contains(m, l => l.StartsWith("De ce contează: ") && l.Contains("Kerberoasting"));
        Assert.Contains(m, l => l.StartsWith("Rezultat: DE VERIFICAT"));
        Assert.Contains(m, l => l == "Ce faceți: Parole de cel puțin 25 de caractere.");
        Assert.DoesNotContain(m, l => l.Contains("nu are încă o explicație"));
    }

    [Fact]
    public void Containment_mail_user_and_live_media_rows_say_what_they_are()
    {
        var rows = new object[]
        {
            new SuspectProcess(42, "x.exe", @"C:\Users\Public\x.exe", ["rulează din Public", "conexiune spre exterior"], [], HighConfidence: true),
            new ProcessIncident { IncidentId = "I1", CaseId = "C1", ProgramPath = @"C:\Users\Public\x.exe", Trigger = "program suspect găsit la scanare" },
            new MailMessage(DateTimeOffset.UnixEpoch, "a@exemplu.ro", "b@exemplu.ro", "Factură", "Delivered", "10.0.0.1", "trace", "id1"),
            new UserActivity { User = "ion", InteractiveLogons = 3, FailedLogons = 1 },
            new LiveDeviceEntry(DateTimeOffset.UnixEpoch, "Conectat", "Stick X", "123", "NU este în registru", true),
        };
        foreach (var row in rows)
        {
            var m = DetailSheetBuilder.Build(row).Meaning;
            Assert.True(m.Count > 0 && m[0].StartsWith("Ce este: ", StringComparison.Ordinal), $"{row.GetType().Name}: {string.Join(" | ", m)}");
        }
        Assert.Contains(DetailSheetBuilder.Build(rows[0]).Meaning, l => l == "De ce este suspect: rulează din Public; conexiune spre exterior");
        Assert.Contains(DetailSheetBuilder.Build(rows[1]).Meaning, l => l.StartsWith("Ce l-a declanșat: program suspect"));
        Assert.Contains(DetailSheetBuilder.Build(rows[3]).Meaning, l => l.Contains("3 intrări la tastatură") && l.Contains("1 eșuate"));
        Assert.Contains(DetailSheetBuilder.Build(rows[4]).Meaning, l => l == "Concluzie: NU este în registru");
    }
}
