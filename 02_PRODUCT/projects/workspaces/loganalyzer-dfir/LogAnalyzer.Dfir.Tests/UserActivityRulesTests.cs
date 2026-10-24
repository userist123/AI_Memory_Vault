using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// E7 slice 3: USER-ACTIVITY-CLASSIFIED / -REMOVABLE / -DELETED over synthetic timeline events (ShellBags, RecentDocs, Office MRU, Recycle Bin). No real
/// names or paths: the folders, accounts and SIDs are invented for the test.
/// </summary>
public sealed class UserActivityRulesTests
{
    private const string Root = @"D:\Clasificat\Proiect";
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

    private static TimelineEvent Bag(string path, DateTimeOffset? time = null, string account = "utilizator.a", string id = "EV-1") => new()
    {
        Time = time is { } t ? new Timestamp(t, "", "test") : Timestamp.Unknown(), TimeSemantics = "parent BagMRU key last written", Source = "ShellBags", EvidenceId = id, Path = path, Process = "",
        Summary = $"ShellBags: Explorer are o intrare pentru „{path}”", Locator = @"UsrClass\BagMRU\0\1",
        Fields = { ["Account"] = account, ["ShellItemType"] = "0x31", ["ShellItemKind"] = "Folder", ["MruPosition"] = "0", ["BagPath"] = path },
    };

    private static TimelineEvent Recent(string name, DateTimeOffset? time = null) => new()
    {
        Time = time is { } t ? new Timestamp(t, "", "test") : Timestamp.Unknown(), Source = "RecentDocs", EvidenceId = "EV-2", Path = name, Summary = "Document recent " + name, Locator = @"NTUSER\RecentDocs\0",
        Fields = { ["Account"] = "utilizator.a", ["Name"] = name },
    };

    private static TimelineEvent Office(string path, DateTimeOffset time) => new()
    {
        Time = new Timestamp(time, "", "test"), Source = "OfficeMRU", EvidenceId = "EV-3", Path = path, Summary = "Office 16.0 Word: fișier recent " + path, Locator = @"NTUSER\Office\Item 1",
        Fields = { ["Account"] = "utilizator.a", ["App"] = "Word", ["OfficeVersion"] = "16.0", ["MruKind"] = "FileMRU" },
    };

    private static TimelineEvent Bin(string original, DateTimeOffset time, string rcontent = "present", string sid = "S-1-5-21-1-2-3-1001") => new()
    {
        Time = new Timestamp(time, "", "test"), Source = "RecycleBin", EvidenceId = "EV-4", Path = original, Summary = "Șters în Coșul de reciclare: " + original, Locator = "$IABCDEF.docx (v2)",
        Fields = { ["OriginalPath"] = original, ["OriginalSize"] = "2048", ["RContent"] = rcontent, ["OwnerSid"] = sid },
    };

    private static TimelineEvent UsbOn(string letter) => new()
    {
        Time = new Timestamp(T0, "", "test"), Source = "USB", EvidenceId = "EV-9", Summary = "USB", Locator = "L",
        Fields = { ["Serial"] = "AA11BB22", ["VidPid"] = "0951:1666", ["FriendlyName"] = "Stick de test", ["DriveLetter"] = letter },
    };

    private static ProcedureProfile Profile(params string[] folders) => new() { ClassifiedFolders = [.. folders.Select(f => new ClassifiedFolderRow { Path = f })] };

    private static List<Finding> Run(IEnumerable<TimelineEvent> events, ProcedureProfile? profile = null)
    {
        int n = 0;
        return Wp14Rules.Run([.. events], () => $"F-{++n:D4}", new Wp14Input { Scope = new CaseScope(), Profile = profile });
    }

    private static List<Finding> Of(List<Finding> all, string rule) => [.. all.Where(f => f.RuleId == rule)];

    // ---- USER-ACTIVITY-CLASSIFIED ----

    [Fact]
    public void A_classified_folder_in_ShellBags_is_one_DE_VERIFICAT_finding_that_does_not_claim_a_file_was_read()
    {
        var f = Of(Run([Bag(Root, T0), Bag(Root + @"\Anexe", T0.AddMinutes(5)), Bag(@"C:\Users\Public", T0)], Profile(Root)), "USER-ACTIVITY-CLASSIFIED");
        var x = Assert.Single(f);
        Assert.Equal(Severity.Medium, x.Severity);                 // DE VERIFICAT: candidate, provisional severity, never above Medium from these traces alone
        Assert.Equal(Classification.Candidate, x.Classification);
        Assert.Equal(SemanticType.Presence, x.SemanticType);
        Assert.Contains(Root, x.Title);
        Assert.Contains("2 urme", x.Description);
        Assert.Contains("nu că un fișier a fost citit", x.Description);
        Assert.Contains("ultima actualizare a intrării folderului de către Explorer", x.Description);
        Assert.Contains("DE VERIFICAT", x.ClassificationReason);
        Assert.Equal("utilizator.a", x.User);
        Assert.Equal(T0, x.FirstSeenUtc);
        Assert.Equal(T0.AddMinutes(5), x.LastSeenUtc);
        Assert.Equal(2, x.SupportingEvidence.Count);
        Assert.DoesNotContain(@"C:\Users\Public", x.Description);
    }

    [Fact]
    public void Office_MRU_in_a_classified_folder_matches_and_RecentDocs_with_only_a_name_never_does()
    {
        var f = Of(Run([Office(Root + @"\plan.docx", T0), Recent("plan.docx", T0)], Profile(Root)), "USER-ACTIVITY-CLASSIFIED");
        var x = Assert.Single(f);
        Assert.Contains("Office MRU 1", x.Description);
        Assert.DoesNotContain("RecentDocs 1", x.Description);      // the name carries no folder: it cannot be placed
        Assert.Contains("ultima deschidere de către aplicația Office", x.Description);
    }

    [Fact]
    public void The_classified_match_follows_the_path_not_a_prefix_of_the_name_and_ignores_case_and_slashes()
    {
        var f = Of(Run([Bag(@"d:/clasificat/proiect/x", T0), Bag(@"D:\Clasificat\Proiect2", T0), Bag(@"D:\Clasificat", T0)], Profile(Root)), "USER-ACTIVITY-CLASSIFIED");
        Assert.Equal(1, Assert.Single(f).SupportingEvidence.Count);
    }

    [Fact]
    public void Without_classified_folders_in_the_profile_there_is_no_classified_finding_and_nothing_is_invented()
    {
        Assert.Empty(Of(Run([Bag(Root, T0)]), "USER-ACTIVITY-CLASSIFIED"));
        Assert.Empty(Of(Run([Bag(Root, T0)], Profile()), "USER-ACTIVITY-CLASSIFIED"));
    }

    [Fact]
    public void An_alias_of_the_classified_folder_matches_through_the_analysis()
    {
        var r = UserActivityAnalysis.Analyze([Bag(@"E:\Montat\Proiect\a", T0)], [new ClassifiedRoot(Root, [@"E:\Montat\Proiect"])], ["E"]);
        Assert.Equal(Root, Assert.Single(r.Classified).ClassifiedRoot);
    }

    // ---- USER-ACTIVITY-REMOVABLE ----

    [Fact]
    public void A_path_on_a_letter_the_device_facts_tie_to_a_stick_is_a_removable_finding_with_the_letter_reuse_caveat()
    {
        var f = Of(Run([UsbOn("E:"), Bag(@"E:\Documente", T0), Office(@"E:\raport.docx", T0.AddHours(1)), Bag(@"C:\Windows", T0)]), "USER-ACTIVITY-REMOVABLE");
        var x = Assert.Single(f);
        Assert.Contains("volumul amovibil E:", x.Title);
        Assert.Equal(Classification.Candidate, x.Classification);
        Assert.True(x.Severity <= Severity.Medium);
        Assert.Equal(Confidence.Medium, x.Confidence);
        Assert.Contains("o literă poate fi reutilizată", x.Description);
        Assert.Contains("nu că un fișier a fost citit sau copiat", x.Description);
        Assert.Contains("după literă, nu după seria volumului", x.ClassificationReason);
        Assert.DoesNotContain(@"C:\Windows", x.Description);
    }

    [Fact]
    public void A_letter_with_no_device_fact_is_only_possibly_removable_with_low_confidence_and_C_is_never_flagged()
    {
        var f = Of(Run([Bag(@"F:\Date", T0), Bag(@"C:\Date", T0)]), "USER-ACTIVITY-REMOVABLE");
        var x = Assert.Single(f);
        Assert.Contains("posibil amovibilă", x.Title);
        Assert.Equal(Severity.Low, x.Severity);
        Assert.Equal(Confidence.Low, x.Confidence);
        Assert.Contains("incertitudine neînlăturată", x.Description);
        Assert.Contains("disc intern", x.Description);
    }

    [Fact]
    public void Letters_are_grouped_one_finding_each_and_a_confirmed_letter_is_not_also_reported_as_possible()
    {
        var f = Of(Run([UsbOn("E"), Bag(@"E:\a", T0), Bag(@"E:\b", T0), Bag(@"G:\c", T0)]), "USER-ACTIVITY-REMOVABLE");
        Assert.Equal(2, f.Count);
        Assert.Contains(f, x => x.Title.Contains("volumul amovibil E:") && x.Description.Contains("2 urme"));
        Assert.Contains(f, x => x.Title.Contains("G:") && x.Title.Contains("posibil"));
    }

    [Fact]
    public void Network_and_virtual_paths_and_names_without_a_drive_never_create_a_removable_finding()
    {
        Assert.Empty(Of(Run([Bag(@"\\server\share\x", T0), Bag(@"Rețea\Imprimante", T0), Recent("raport.docx", T0), Office(@"raport.docx", T0)]), "USER-ACTIVITY-REMOVABLE"));
    }

    // ---- USER-ACTIVITY-DELETED ----

    [Fact]
    public void Deleted_from_a_classified_folder_is_reported_with_the_deletion_time_and_the_absent_R_says_emptied_or_removed()
    {
        var f = Of(Run([Bin(Root + @"\plan.docx", T0, "absent"), Bin(Root + @"\anexa.xlsx", T0.AddMinutes(1), "present"), Bin(@"C:\Temp\x.txt", T0)], Profile(Root)), "USER-ACTIVITY-DELETED");
        var x = Assert.Single(f);
        Assert.Contains("2 elemente șterse din folderul clasificat", x.Title);
        Assert.Contains("conținut golit sau eliminat", x.Description);
        Assert.Contains("nu înseamnă că elementul nu a existat", x.Description);
        Assert.Contains("Pentru 1 dintre ele fișierul $R lipsește", x.Description);
        Assert.Contains("poate fi legitimă", x.Description);
        Assert.Equal(Severity.Medium, x.Severity);
        Assert.Equal(Classification.Candidate, x.Classification);
        Assert.Equal(SemanticType.Observation, x.SemanticType);
        Assert.Equal("SID S-1-5-21-1-2-3-1001", x.User);
        Assert.Equal(T0, x.FirstSeenUtc);
        Assert.Equal(T0.AddMinutes(1), x.LastSeenUtc);
        Assert.DoesNotContain(@"C:\Temp", x.Description);
    }

    [Fact]
    public void Deleted_from_a_stick_is_reported_and_a_deleted_item_elsewhere_is_not()
    {
        var f = Of(Run([UsbOn("E:"), Bin(@"E:\foto.jpg", T0, "absent"), Bin(@"C:\Users\x\poza.jpg", T0)]), "USER-ACTIVITY-DELETED");
        var x = Assert.Single(f);
        Assert.Contains("de pe volumul amovibil E:", x.Title);
        Assert.Contains("fișierul $R lipsește", x.Description);
        Assert.Empty(Of(Run([Bin(@"C:\Users\x\poza.jpg", T0)]), "USER-ACTIVITY-DELETED"));
    }

    [Fact]
    public void Deleted_from_an_unexplained_letter_is_low_and_says_the_uncertainty()
    {
        var x = Assert.Single(Of(Run([Bin(@"H:\a.txt", T0)]), "USER-ACTIVITY-DELETED"));
        Assert.Equal(Severity.Low, x.Severity);
        Assert.Contains("posibil amovibilă", x.Title);
        Assert.Contains("incertitudine neînlăturată", x.Description);
    }

    // ---- none of the rules reaches NECONFORM severity, and the contract is complete ----

    [Fact]
    public void No_rule_here_is_stronger_than_Medium_candidate_and_each_one_has_a_catalog_contract()
    {
        var all = Run([UsbOn("E:"), Bag(Root, T0), Bag(@"E:\x", T0), Office(Root + @"\a.docx", T0), Bin(Root + @"\b.docx", T0, "absent"), Bin(@"E:\c.docx", T0)], Profile(Root));
        var mine = all.Where(f => f.RuleId.StartsWith("USER-ACTIVITY-", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, mine.Select(f => f.RuleId).Distinct().Count());
        Assert.All(mine, f => { Assert.True(f.Severity <= Severity.Medium); Assert.Equal(Classification.Candidate, f.Classification); Assert.NotEmpty(f.SupportingEvidence); });
        foreach (var id in new[] { "USER-ACTIVITY-CLASSIFIED", "USER-ACTIVITY-REMOVABLE", "USER-ACTIVITY-DELETED" })
        {
            var c = RuleContracts.For(id);
            Assert.NotNull(c);
            Assert.Contains(c!.Limitations, l => l.Contains("DE VERIFICAT") && l.Contains("NECONFORM"));
            Assert.NotEmpty(c.MissingEvidence);
            Assert.NotEmpty(c.NextSteps);
        }
        Assert.DoesNotContain("nicio regulă nu le folosește", RuleContracts.For("CLASSIFIED-ACCESS")!.Limitations.Aggregate("", (a, b) => a + b));
    }

    [Fact]
    public void Without_user_activity_events_the_rules_add_nothing()
    {
        Assert.Empty(Run([UsbOn("E:")], Profile(Root)).Where(f => f.RuleId.StartsWith("USER-ACTIVITY-", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_classified_folder_on_a_data_letter_is_not_also_reported_as_a_possible_removable_volume()
    {
        var all = Run([Bag(Root, T0), Bin(Root + @"\a.docx", T0, "absent")], Profile(Root));
        Assert.Empty(Of(all, "USER-ACTIVITY-REMOVABLE"));
        Assert.Single(Of(all, "USER-ACTIVITY-CLASSIFIED"));
        Assert.Equal(DriveMatch.None, UserActivityAnalysis.Analyze([Bag(Root, T0)], [new ClassifiedRoot(Root)]).Items[0].Drive);
        // but a letter the device facts tie to a stick stays removable even for a classified path
        Assert.Equal(DriveMatch.Removable, UserActivityAnalysis.Analyze([Bag(Root, T0)], [new ClassifiedRoot(Root)], ["D"]).Items[0].Drive);
    }

    // ---- the analysis, the page rows and the report lines ----

    [Fact]
    public void Each_source_says_what_its_time_means_and_an_undated_row_says_it_has_no_time()
    {
        var r = UserActivityAnalysis.Analyze([Bag(Root, T0), Bag(Root + @"\b"), Recent("x.docx", T0), Office(@"D:\o.docx", T0), Bin(@"D:\d.docx", T0)], [new ClassifiedRoot(Root)]);
        string M(string src) => r.Items.First(i => i.Source == src && i.TimeUtc is not null).TimeMeaning;
        Assert.Contains("nu dovedește că un fișier a fost citit", M("ShellBags"));
        Assert.Contains("doar celei mai noi intrări", M("RecentDocs"));
        Assert.Contains("ultima deschidere de către aplicația Office", M("OfficeMRU"));
        Assert.Contains("ora ștergerii", M("RecycleBin"));
        var undated = r.Items.Single(i => i.TimeUtc is null);
        Assert.Equal("fără oră", undated.TimeText);
        Assert.Contains("fără oră proprie", undated.TimeMeaning);
    }

    [Fact]
    public void The_page_rows_put_classified_first_then_removable_then_possible_and_carry_source_path_time_who_and_detail()
    {
        var r = UserActivityAnalysis.Analyze([Bag(@"C:\x", T0.AddDays(3)), Bag(@"F:\y", T0), Bag(@"E:\z", T0), Bag(Root, T0.AddDays(-1))], [new ClassifiedRoot(Root)], ["E:"]);
        Assert.Equal([Root, @"E:\z", @"F:\y", @"C:\x"], r.Items.Select(i => i.Path));
        var first = r.Items[0];
        Assert.Equal("ShellBags (folder văzut în Explorer)", first.SourceLabel);
        Assert.Equal("utilizator.a", first.Who);
        Assert.Contains("tip element 0x31", first.Detail);
        Assert.StartsWith("Folder clasificat:", first.Relevance);
        Assert.Equal("—", r.Items[3].Relevance);
        Assert.Contains("ShellBags 4", r.Summary);
        Assert.Contains("posibil amovibile: 1", r.Summary);
    }

    [Fact]
    public void A_Recycle_Bin_row_shows_the_owner_SID_and_the_R_state_in_words()
    {
        var i = UserActivityAnalysis.Analyze([Bin(@"D:\a.docx", T0, "present_folder"), Bin(@"D:\b.docx", T0, "not_checked")]).Items;
        Assert.All(i, x => Assert.StartsWith("SID S-1-5-21", x.Who));
        Assert.Contains(i, x => x.Detail.Contains("folderul șters ($R) există"));
        Assert.Contains(i, x => x.Detail.Contains("nu a fost verificată"));
    }

    [Fact]
    public void The_report_lines_give_counts_the_strongest_rows_with_the_time_meaning_and_the_limit_and_never_NECONFORM()
    {
        var r = UserActivityAnalysis.Analyze([Bag(Root, T0), Office(@"D:\o.docx", T0), Bin(Root + @"\d.docx", T0, "absent"), Bag(@"C:\x", T0)], [new ClassifiedRoot(Root)]);
        var lines = UserActivityAnalysis.ReportLines(r);
        Assert.True(lines[0].Head);
        Assert.Contains(lines, l => l.Text.Contains("în foldere clasificate: 1") && l.Text.Contains("șterse din foldere clasificate sau de pe volume amovibile: 1"));
        Assert.Contains(lines, l => l.Text.Contains(Root) && l.Text.Contains("nu dovedește că un fișier a fost citit"));
        Assert.Contains(lines, l => l.Text.Contains("conținut golit sau eliminat"));
        Assert.DoesNotContain(lines, l => l.Text.Contains(@"C:\x"));   // an unrelated row is counted, not listed as a strong one
        var last = lines[^1].Text;
        Assert.Contains("nu dovedesc citirea, copierea sau scoaterea", last);
        Assert.Contains("DE VERIFICAT, nu NECONFORM", last);
    }

    [Fact]
    public void A_report_that_did_not_evaluate_user_activity_says_so_and_an_empty_timeline_does_not_say_clean()
    {
        var none = UserActivityAnalysis.ReportLines(null);
        Assert.Contains("nu a fost evaluată de acest raport", Assert.Single(none).Text);
        var empty = UserActivityAnalysis.ReportLines(UserActivityAnalysis.Analyze([]));
        Assert.Contains("nu dovedește că nu s-a întâmplat nimic", empty.Last().Text);
    }

    [Fact]
    public void Hostile_paths_do_not_throw_and_a_long_list_is_capped_in_the_report()
    {
        var events = new List<TimelineEvent> { Bag("", T0), Bag(@"\\?\", T0), Bag(new string('x', 5000), T0), Bag(@":\", T0), Bin("", T0), Bin(@"Z:", T0) };
        for (int n = 0; n < 40; n++) events.Add(Bag($@"E:\d{n}", T0.AddMinutes(n)));
        var r = UserActivityAnalysis.Analyze(events, [new ClassifiedRoot("")], ["E"]);
        var lines = UserActivityAnalysis.ReportLines(r, 15);
        Assert.Contains(lines, l => l.Text.Contains("… încă"));
        Assert.True(lines.Count <= 15 + 6);
    }
}
