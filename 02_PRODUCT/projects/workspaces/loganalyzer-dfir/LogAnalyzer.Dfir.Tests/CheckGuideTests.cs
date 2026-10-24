using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner 2026-10-11: „detaliile codurilor pe toate domeniile" in plain words for controllers without IT training. Every control check has a
/// guide (what, why, how, source, meaning), and the codes in its evidence are decoded; unknown codes are never given an invented meaning.
/// </summary>
public class CheckGuideTests
{
    private static string Source(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, relative);
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void Every_check_the_evaluator_can_emit_has_a_written_guide()
    {
        var ids = Regex.Matches(Source(Path.Combine("LogAnalyzer.Dfir.Windows", "Audit", "ControlEvaluator.cs")) + Source(Path.Combine("LogAnalyzer.Dfir.Windows", "Audit", "ControlEvaluator.Classified.cs")), @"Add\(r, ""([A-Z]\d\d)""")
                       .Select(m => m.Groups[1].Value).Concat(["A03", "A04"]).Distinct().OrderBy(x => x).ToList();
        Assert.True(ids.Count >= 30, $"only {ids.Count} ids parsed");
        var missing = ids.Where(id => !CheckGuides.Ids.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "checks without a guide: " + string.Join(", ", missing));
        foreach (var id in ids)
        {
            var g = CheckGuides.For(id);
            Assert.All(new[] { g.What, g.Why, g.How, g.Source, g.Meaning }, t => Assert.True(t.Length > 10, $"{id}: empty section"));
        }
    }

    [Fact]
    public void An_unknown_check_says_so_instead_of_showing_nothing()
    {
        var g = CheckGuides.For("Z99");
        Assert.Contains("nu are încă o descriere", g.What);
    }

    [Fact]
    public void Evidence_codes_are_decoded_event_audit_subcategory_and_change_code()
    {
        var c = new ControlCheck("A06", "Audit", "Politica de audit a fost modificată", ControlStatus.DeVerificat, "1 modificări.",
            ["Security 4719 RecordID 812 (2026-09-03 10:00:00 UTC) de ST\\ion: Logon {0CCE9215-69AE-11D9-BED3-505054503030} %%8448"], "");
        var e = CheckGuides.Explain(c);
        Assert.Contains(e.Codes, x => x.Code == "Security 4719" && x.What.Contains("Politica de audit"));
        Assert.Contains(e.Codes, x => x.Kind == "categorie de audit" && x.What.Contains("„Logon”"));
        Assert.Contains(e.Codes, x => x.Code == "%%8448" && x.What.Contains("DEZACTIVATĂ"));
        Assert.Contains("DE VERIFICAT", e.StatusMeaning);
        Assert.Contains("Ce faceți:", e.ToPlainText());
    }

    [Fact]
    public void Registry_checks_explain_the_value_in_plain_words()
    {
        var c = new ControlCheck("P04", "Politici", "UAC (Control cont utilizator) activ", ControlStatus.Neconform, "UAC este dezactivat.", [], "");
        var e = CheckGuides.Explain(c);
        Assert.Contains(e.Codes, x => x.Code.EndsWith("EnableLUA") && x.When.Contains("1 = activ"));
        Assert.Contains("NECONFORM", e.StatusMeaning);
        Assert.Contains("procesul-verbal", e.NextStep);
    }

    [Fact]
    public void Usb_and_sid_codes_are_recognised()
    {
        var found = CodeExplainer.FindIn(@"Microsoft-Windows-Partition/Diagnostic 1006 RecordID 9: Kingston; membru S-1-5-21-1-2-3-500; grup S-1-5-32-544; HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR → Start");
        Assert.Contains(found, x => x.Code == "Partition 1006");
        Assert.Contains(found, x => x.What.Contains("Administrator încorporat"));
        Assert.Contains(found, x => x.What.StartsWith("Administrators"));
        Assert.Contains(found, x => x.Code.EndsWith(@"USBSTOR\Start") && x.When.Contains("4 = blocat"));
    }

    [Fact]
    public void Unknown_codes_and_ordinary_words_get_no_invented_meaning()
    {
        Assert.Empty(CodeExplainer.FindIn("Security 9999 RecordID 1"));
        Assert.Empty(CodeExplainer.FindIn(@"ST-IZOLAT-07\ion DOMAIN\maria a pornit stația"));          // "DOMAIN" is not the firewall profile
        Assert.Empty(CodeExplainer.FindIn("{11111111-2222-3333-4444-555555555555}"));               // not an audit subcategory
    }

    [Fact]
    public void Undetermined_tells_how_to_retry_and_conform_needs_nothing()
    {
        var u = CheckGuides.Explain(new ControlCheck("A05", "Audit", "Jurnale șterse", ControlStatus.Nedeterminat, "Sursa nu a putut fi citită.", [], ""));
        Assert.Contains("Executare ca administrator", u.NextStep);
        var ok = CheckGuides.Explain(new ControlCheck("A05", "Audit", "Jurnale șterse", ControlStatus.Conform, "Nicio ștergere.", [], "Fiecare ștergere trebuie justificată."));
        Assert.Equal("Nu este nevoie de nicio acțiune.", ok.NextStep);
    }
}
