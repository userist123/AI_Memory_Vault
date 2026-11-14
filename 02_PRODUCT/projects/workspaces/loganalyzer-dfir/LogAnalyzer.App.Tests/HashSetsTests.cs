using System.IO;
using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.HashSets;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>R7a: fingerprint sets (known-good / known-bad): offline import, library persistence, matching in the investigation. Small synthetic lists only.</summary>
[Collection(HashSetsTests.Serial)]
public sealed class HashSetsTests : IDisposable
{
    /// <summary>F4 measures memory with GC.GetTotalMemory, which counts the whole process: tests running in parallel made it fail now and then.</summary>
    public const string Serial = "Hash set memory measurement";

    private readonly GapsTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private static string Sha256Of(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private static string Sha1Of(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));
    private static string Md5Of(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s)));
    private HashSetLibrary Lib() => new(Path.Combine(_kit.Dir, "hashsets"));

    [Fact]
    public void Text_import_takes_hash_with_optional_name_dedupes_and_lists_invalid_lines_with_numbers()
    {
        var a = Sha256Of("a"); var b = Sha256Of("b");
        var text = $"# lista\n{a} fisier-a.exe\n{b.ToLowerInvariant()}\n{a.ToLowerInvariant()}\nnu-este-hash\n\n{Md5Of("a")}\t*x.dll\n123\n";
        var r = HashSetImporter.Parse(new StringReader(text), "T", HashSetCategory.KnownBad, "t.txt", "ab" + new string('0', 62));
        Assert.True(r.Ok);
        Assert.Equal(3, r.Set!.Info.EntryCount);          // a, b, md5(a); the repeated a is deduplicated
        Assert.Equal(1, r.DuplicateCount);
        Assert.Equal(2, r.InvalidLineCount);
        Assert.Equal([5, 8], r.InvalidLines.Select(i => i.Line).ToArray());
        Assert.Equal(["SHA-256", "MD5"], r.Set.Info.Algorithms);
    }

    [Fact]
    public void Csv_import_reads_nsrl_like_header_with_mixed_algorithms_and_counts_bad_rows()
    {
        var csv = "\"SHA-1\",\"MD5\",\"CRC32\",\"FileName\",\"FileSize\",\"SHA-256\"\n" +
                  $"\"{Sha1Of("1")}\",\"{Md5Of("1")}\",\"00\",\"one.exe\",\"10\",\"{Sha256Of("1")}\"\n" +
                  $"\"{Sha1Of("2")}\",\"\",\"00\",\"two, final.exe\",\"10\",\"\"\n" +
                  "\"ZZZ\",\"\",\"00\",\"bad.exe\",\"10\",\"\"\n";
        var r = HashSetImporter.Parse(new StringReader(csv), "N", HashSetCategory.KnownGood, "nsrl.csv", "00");
        Assert.True(r.Ok);
        Assert.Equal(4, r.Set!.Info.EntryCount);
        Assert.Equal(["SHA-256", "SHA-1", "MD5"], r.Set.Info.Algorithms);
        Assert.Equal(1, r.InvalidLineCount);
        Assert.Equal(4, r.InvalidLines[0].Line);
    }

    [Fact]
    public void Csv_with_lowercase_sha256_header_and_only_that_column_works()
    {
        var r = HashSetImporter.Parse(new StringReader($"name,sha256\nx,{Sha256Of("q")}\n"), "L", HashSetCategory.KnownBad, "l.csv", "00");
        Assert.True(r.Ok); Assert.Equal(1, r.Set!.Info.EntryCount);
    }

    [Fact]
    public void Only_the_first_20_invalid_lines_are_listed_but_all_are_counted()
    {
        var text = string.Join("\n", Enumerable.Range(0, 30).Select(i => "bad" + i)) + "\n" + Sha256Of("ok");
        var r = HashSetImporter.Parse(new StringReader(text), "T", HashSetCategory.KnownBad, "t.txt", "00");
        Assert.Equal(30, r.InvalidLineCount);
        Assert.Equal(20, r.InvalidLines.Count);
        Assert.Equal(20, r.InvalidLines[^1].Line);
    }

    [Fact]
    public void Entry_limit_and_size_limit_refuse_the_import_with_a_clear_message()
    {
        var text = string.Join("\n", Enumerable.Range(0, 5).Select(i => Sha256Of("e" + i)));
        var r = HashSetImporter.Parse(new StringReader(text), "T", HashSetCategory.KnownBad, "t.txt", "00", new HashSetImportOptions(MaxEntries: 3));
        Assert.False(r.Ok); Assert.Contains("limita de 3", r.Error); Assert.Null(r.Set);

        var f = _kit.File("big.txt", text);
        var s = HashSetImporter.ImportFile(f, "T", HashSetCategory.KnownBad, new HashSetImportOptions(MaxFileBytes: 10));
        Assert.False(s.Ok); Assert.Contains("peste limita", s.Error);
    }

    [Fact]
    public void Import_file_records_source_name_and_sha256_and_unknown_category_is_refused()
    {
        var f = _kit.File("lista.txt", Sha256Of("z") + "\n");
        var r = HashSetImporter.ImportFile(f, "Lista", "cunoscut-rău", importedBy: "ana");
        Assert.True(r.Ok);
        Assert.Equal("lista.txt", r.Set!.Info.SourceFileName);
        Assert.Equal(LogAnalyzer.Dfir.IO.Hashing.Sha256File(f), r.Set.Info.SourceSha256);
        Assert.Equal("ana", r.Set.Info.ImportedBy);
        Assert.False(HashSetImporter.ImportFile(f, "L", "altceva").Ok);
    }

    [Fact]
    public void Library_persists_sets_and_a_removed_set_is_gone()
    {
        var lib = Lib();
        var f = _kit.File("l.txt", Sha256Of("p") + "\n" + Sha1Of("p") + "\n");
        var r = lib.Import(f, "Persistent", HashSetCategory.KnownBad);
        Assert.True(r.Ok);
        var again = Lib().Load(out var problems);        // a fresh instance reads it from disk
        Assert.Empty(problems);
        var s = Assert.Single(again);
        Assert.Equal("Persistent", s.Info.Name); Assert.Equal(2, s.Info.EntryCount); Assert.True(s.Contains(Sha256Of("p").ToLowerInvariant()));
        Assert.Equal("", lib.Remove(s.Info.Id));
        Assert.Empty(Lib().Load());
        Assert.True(new LogAnalyzer.Dfir.Case.HashChain(Path.Combine(lib.Dir, HashSetLibrary.AuditFileName)).Verify().Status == LogAnalyzer.Dfir.Case.ChainStatus.Valid);
    }

    [Fact]
    public void A_set_whose_hash_file_was_altered_is_reported_not_used()
    {
        var lib = Lib();
        var r = lib.Import(_kit.File("l.txt", Sha256Of("p") + "\n"), "S", HashSetCategory.KnownBad);
        File.WriteAllText(Path.Combine(lib.Dir, r.Set!.Info.Id + ".hashes.txt"), Sha256Of("p") + "\n" + Sha256Of("extra") + "\n");
        var sets = lib.Load(out var problems);
        Assert.Empty(sets); Assert.Single(problems);
    }

    private static EvidenceItem Item(string id, string sha) => new() { EvidenceId = id, CaseId = "C", Source = "s", SourceType = "file", OriginalPath = @"C:\x\evil.exe", OriginalName = "evil.exe", Sha256 = sha };

    private static KnownHashSet SetOf(string name, string category, params string[] hashes) =>
        new(new KnownHashSetInfo { Id = name, Name = name, Category = category, EntryCount = hashes.Length, SourceFileName = name + ".txt", SourceSha256 = new string('A', 64) }, hashes.Select(h => h.ToUpperInvariant()).ToHashSet());

    [Fact]
    public void Known_bad_match_gives_a_high_finding_and_a_detection_with_full_evidence()
    {
        var h = Sha256Of("evil");
        var matches = HashSetMatcher.Match([SetOf("Rele", HashSetCategory.KnownBad, h)], [Item("EV-1", h)]);
        var m = Assert.Single(matches);
        var f = Assert.Single(HashSetMatcher.ToFindings(matches, () => "F-0001"));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal("EV-1", f.SupportingEvidence[0].EvidenceId); Assert.Equal(h, f.SupportingEvidence[0].Sha256);
        Assert.Contains("Rele", f.Description); Assert.Contains("Rele.txt", f.Description); Assert.Contains(h, f.Description); Assert.Contains(@"C:\x\evil.exe", f.Description);
        var d = Assert.Single(HashSetMatcher.ToDetections(matches));
        Assert.Equal(DetectionKind.Hash, d.Kind); Assert.Equal("EV-1", d.EvidenceId); Assert.Equal(m.SetSourceSha256, d.RuleSha256);
    }

    [Fact]
    public void Known_good_is_a_label_only_no_finding_no_detection_and_never_clean()
    {
        var h = Sha256Of("good");
        var matches = HashSetMatcher.Match([SetOf("Bune", HashSetCategory.KnownGood, h)], [Item("EV-1", h), Item("EV-2", Sha256Of("other"))]);
        var m = Assert.Single(matches);
        Assert.Contains("cunoscut-bun după setul „Bune”", m.Label); Assert.Contains("nu este declarată curată", m.Label);
        Assert.Empty(HashSetMatcher.ToFindings(matches, () => "F"));
        Assert.Empty(HashSetMatcher.ToDetections(matches));
    }

    [Fact]
    public void A_hash_in_both_categories_is_reported_under_both()
    {
        var h = Sha256Of("both");
        var matches = HashSetMatcher.Match([SetOf("Bune", HashSetCategory.KnownGood, h), SetOf("Rele", HashSetCategory.KnownBad, h)], [Item("EV-1", h)]);
        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.Category == HashSetCategory.KnownGood); Assert.Contains(matches, m => m.IsKnownBad);
    }

    [Fact]
    public void Timeline_hash_fields_of_any_algorithm_match_and_a_removed_set_no_longer_matches()
    {
        var md5 = Md5Of("t");
        var ev = GapsTestKit.Ev("Prefetch", 0, evidence: "EV-9", path: @"C:\t\t.exe", f: [("MD5", md5)]);
        var set = SetOf("Rele", HashSetCategory.KnownBad, md5);
        var m = Assert.Single(HashSetMatcher.Match([set], [], [ev]));
        Assert.Equal("MD5", m.Algorithm); Assert.Equal("EV-9", m.EvidenceId);
        Assert.Empty(HashSetMatcher.Match([], [Item("EV-1", Sha256Of("x"))], [ev]));
    }

    [Fact]
    public void DetectionEngine_turns_known_bad_matches_into_hash_detections_and_lists_the_set_as_a_rule()
    {
        var h = Sha256Of("engine");
        var engine = new DetectionEngine { KnownHashSets = [SetOf("Rele", HashSetCategory.KnownBad, h), SetOf("Bune", HashSetCategory.KnownGood, h)] };
        var res = engine.Run([], [Item("EV-1", h)], _ => []);
        Assert.Single(res);
        Assert.Contains(engine.Rules, r => r.Kind == DetectionKind.Hash && r.RuleId == "HASHSET:Rele");
        Assert.DoesNotContain(engine.Rules, r => r.RuleId == "HASHSET:Bune");
    }

    [Fact]
    public void Investigation_pipeline_reports_known_bad_as_finding_and_detection_and_keeps_known_good_as_label()
    {
        var bad = _kit.File("src/evil.bin", "evil-content"); var good = _kit.File("src/fine.bin", "fine-content");
        var lib = Lib();
        Assert.True(lib.Import(_kit.File("bad.txt", Sha256Of("evil-content") + "\n"), "Rele", HashSetCategory.KnownBad).Ok);
        Assert.True(lib.Import(_kit.File("good.txt", Sha256Of("fine-content") + "\n"), "Bune", HashSetCategory.KnownGood).Ok);
        var ws = _kit.Case(bad, good);
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, hashSets: lib);
        var f = Assert.Single(r.Findings, x => x.RuleId == "HASHSET-KNOWN-BAD");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Single(r.Detections, d => d.RuleId.StartsWith("HASHSET:", StringComparison.Ordinal));
        Assert.Equal(2, r.KnownHashMatches.Count);
        Assert.Contains(r.KnownHashMatches, m => m.Category == HashSetCategory.KnownGood && m.Label.Contains("Bune"));
        Assert.Equal(2, ws.LoadEvidence().Count(e => e.SourceType == "file"));   // nothing hidden from the case
        Assert.True(File.Exists(Path.Combine(ws.Root, "Analysis", "known_hashes.json")));
    }

    [Fact]
    public void View_model_lists_imports_and_removes_sets_with_romanian_status()
    {
        var vm = new HashSetsViewModel(Lib()) { SelectedCategory = HashSetCategory.KnownBad };
        var r = vm.ImportFile(_kit.File("v.txt", Sha256Of("v") + "\nnu\n"), "Setul meu");
        Assert.True(r.Ok);
        var row = Assert.Single(vm.Sets);
        Assert.Equal(1, row.Entries); Assert.Equal("cunoscut-rău", row.Category); Assert.Equal("v.txt", row.Source);
        Assert.Contains("1 linii invalide", vm.Status); Assert.Contains("linia 2", vm.Issues[0]);
        vm.SelectedSet = row; vm.RemoveCommand.Execute(null);
        Assert.Empty(vm.Sets); Assert.Contains("eliminat", vm.Status);
    }

    // ---- Review findings F1-F5 (security review of the fingerprint sets) ----

    private static string Meta(string? id, string name = "Plantat", string category = "cunoscut-rău", int entries = 1) =>
        "{\"id\":" + (id is null ? "null" : System.Text.Json.JsonSerializer.Serialize(id)) + ",\"name\":\"" + name + "\",\"category\":" + System.Text.Json.JsonSerializer.Serialize(category) +
        ",\"algorithms\":[\"SHA-256\"],\"entryCount\":" + entries + ",\"sourceFileName\":\"p.txt\",\"sourceSha256\":\"" + new string('A', 64) + "\",\"storedSha256\":\"" + new string('B', 64) + "\",\"importedUtc\":\"2026-01-01T00:00:00+00:00\",\"importedBy\":\"x\"}";

    private static void Plant(HashSetLibrary lib, string file, string meta, string? hashes = null, string? hashesName = null)
    {
        Directory.CreateDirectory(lib.Dir);
        File.WriteAllText(Path.Combine(lib.Dir, file + ".json"), meta);
        if (hashes is not null) File.WriteAllText(Path.Combine(lib.Dir, (hashesName ?? file) + ".hashes.txt"), hashes);
    }

    [Fact]
    public void F1_a_set_file_with_null_id_is_skipped_and_reported_and_never_breaks_load_pipeline_or_page()
    {
        var lib = Lib();
        var good = lib.Import(_kit.File("g.txt", Sha256Of("good1") + "\n"), "Bun", HashSetCategory.KnownBad);
        Assert.True(good.Ok);
        Plant(lib, "x", "{\"id\":null}");
        var sets = lib.Load(out var problems);
        Assert.Single(sets); Assert.Equal("Bun", sets[0].Info.Name);
        Assert.Contains(problems, p => p.Contains("x.json"));
        var vm = new HashSetsViewModel(lib); vm.Reload();
        Assert.Single(vm.Sets); Assert.Contains(vm.Problems, p => p.Contains("x.json"));
        var ws = _kit.Case(_kit.File("src/a.bin", "a-content"));
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, hashSets: lib);
        Assert.Contains(r.Gaps, g => g.Artifact == "Set de amprente" && g.Reason.Contains("x.json"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("A/B")]
    [InlineData("UPPER")]
    [InlineData("-start")]
    public void F1_invalid_ids_are_rejected(string id)
    {
        var lib = Lib();
        Plant(lib, "p1", Meta(id), Sha256Of("q") + "\n");
        var sets = lib.Load(out var problems);
        Assert.Empty(sets); Assert.Contains("identificator invalid", Assert.Single(problems));
    }

    [Fact]
    public void F1_wrong_category_and_non_hex_or_wrong_length_lines_are_rejected()
    {
        var lib = Lib();
        Plant(lib, "cat1", Meta("cat1", category: "cunoscut-rau"), Sha256Of("q") + "\n");
        Plant(lib, "hex1", Meta("hex1"), "ZZZ" + new string('0', 61) + "\n");
        Plant(lib, "len1", Meta("len1"), new string('A', 50) + "\n");
        var sets = lib.Load(out var problems);
        Assert.Empty(sets); Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void F2_a_planted_set_without_an_audit_entry_is_not_used_and_is_reported()
    {
        var lib = Lib();
        Plant(lib, "plant1", Meta("plant1"), Sha256Of("evil") + "\n");
        var sets = lib.Load(out var problems);
        Assert.Empty(sets); Assert.Contains(problems, p => p.Contains("audit", StringComparison.OrdinalIgnoreCase));
        var ws = _kit.Case(_kit.File("src/evil.bin", "evil"));
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, hashSets: lib);
        Assert.Empty(r.KnownHashMatches); Assert.Contains(r.Gaps, g => g.Artifact == "Set de amprente" && g.Reason.Contains("plant1"));
    }

    [Fact]
    public void F2_path_traversal_id_reads_nothing_outside_the_folder_and_remove_deletes_nothing_outside()
    {
        var lib = Lib();
        var outside = Path.Combine(_kit.Dir, "outside");
        File.WriteAllText(outside + ".json", "keep"); File.WriteAllText(outside + ".hashes.txt", Sha256Of("o") + "\n");
        Plant(lib, "tr", Meta("../outside"));
        Assert.Empty(lib.Load(out var problems)); Assert.Single(problems);
        Assert.NotEqual("", lib.Remove("../outside"));
        Assert.True(File.Exists(outside + ".json")); Assert.True(File.Exists(outside + ".hashes.txt"));
    }

    [Fact]
    public void F2_a_tampered_audit_chain_makes_every_set_unused()
    {
        var lib = Lib();
        Assert.True(lib.Import(_kit.File("a.txt", Sha256Of("a") + "\n"), "A", HashSetCategory.KnownBad).Ok);
        var audit = Path.Combine(lib.Dir, HashSetLibrary.AuditFileName);
        File.WriteAllText(audit, File.ReadAllText(audit).Replace("\"A\"", "\"B\""));
        var sets = lib.Load(out var problems);
        Assert.Empty(sets); Assert.Single(problems);
    }

    [Fact]
    public void R2_an_audited_set_whose_files_were_deleted_is_reported_not_dropped()
    {
        var lib = Lib();
        var id = lib.Import(_kit.File("a.txt", Sha256Of("a") + "\n"), "Rele", HashSetCategory.KnownBad).Set!.Info.Id;
        File.Delete(Path.Combine(lib.Dir, id + ".json")); File.Delete(Path.Combine(lib.Dir, id + ".hashes.txt"));
        Assert.Empty(lib.Load(out var problems));
        Assert.Contains(problems, p => p.Contains("Rele") && p.Contains("fișierele lui lipsesc"));
    }

    [Fact]
    public void R2_a_folder_a_standard_user_owns_or_can_write_is_not_trusted()
    {
        using var me = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (new System.Security.Principal.WindowsPrincipal(me).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) return;   // owner would be Administrators
        var lib = Lib();
        Assert.True(lib.Import(_kit.File("a.txt", Sha256Of("a") + "\n"), "A", HashSetCategory.KnownBad).Ok);
        // the folder created by this (non-administrator) test process is owned by the test account: exactly the pre-created folder of the round-2 review
        var problem = HashSetLibrary.FolderTrustProblem(lib.Dir, repair: false);
        Assert.Contains("niciun set nu este folosit", problem);
        var strict = new HashSetLibrary(lib.Dir, verifyFolderTrust: true);
        Assert.Empty(strict.Load(out var problems));
        Assert.Contains(problems, p => p.Contains("niciun set nu este folosit"));
        Assert.Single(lib.Load(out _));                                                     // the same folder without the check (test folders) still loads
        Assert.True(new HashSetLibrary().VerifyFolderTrust);                               // the station folder is always checked
    }

    [Fact]
    public void F2_a_removed_set_whose_files_are_put_back_is_not_used()
    {
        var lib = Lib();
        var r = lib.Import(_kit.File("a.txt", Sha256Of("a") + "\n"), "A", HashSetCategory.KnownBad);
        var id = r.Set!.Info.Id;
        var meta = File.ReadAllText(Path.Combine(lib.Dir, id + ".json")); var hashes = File.ReadAllText(Path.Combine(lib.Dir, id + ".hashes.txt"));
        Assert.Equal("", lib.Remove(id));
        File.WriteAllText(Path.Combine(lib.Dir, id + ".json"), meta); File.WriteAllText(Path.Combine(lib.Dir, id + ".hashes.txt"), hashes);
        Assert.Empty(lib.Load(out var problems)); Assert.Single(problems);
    }

    [Fact]
    public void F2_metadata_changed_after_import_category_flip_is_rejected()
    {
        var lib = Lib();
        var r = lib.Import(_kit.File("a.txt", Sha256Of("a") + "\n"), "A", HashSetCategory.KnownBad);
        var mp = Path.Combine(lib.Dir, r.Set!.Info.Id + ".json");
        File.WriteAllText(mp, File.ReadAllText(mp).Replace("cunoscut-rău", "cunoscut-bun"));
        Assert.Empty(lib.Load(out var problems)); Assert.Single(problems);
    }

    [Fact]
    public void F2_imported_hashes_are_stored_upper_case_and_match_regardless_of_input_case()
    {
        var lib = Lib();
        var h = Sha256Of("case");
        var r = lib.Import(_kit.File("c.txt", h.ToLowerInvariant() + "\n"), "C", HashSetCategory.KnownBad);
        Assert.True(r.Ok);
        var set = Assert.Single(lib.Load());
        Assert.True(set.Contains(h)); Assert.True(set.Contains(h.ToLowerInvariant()));
        Assert.Single(HashSetMatcher.Match([set], [Item("EV-1", h.ToLowerInvariant())]));
    }

    [Fact]
    public void F3_sysmon_Hashes_field_is_parsed_by_algorithm_and_imphash_is_not_taken_for_md5()
    {
        var sha = Sha256Of("sys"); var md5 = Md5Of("sys"); var imp = Md5Of("imp");
        var ev = GapsTestKit.Ev("Sysmon", 0, evidence: "EV-S", path: @"C:\s\s.exe", f: [("Hashes", $"SHA1={Sha1Of("sys")},MD5={md5},SHA256={sha},IMPHASH={imp}")]);
        Assert.Single(HashSetMatcher.Match([SetOf("A", HashSetCategory.KnownBad, sha)], [], [ev]));
        var m = Assert.Single(HashSetMatcher.Match([SetOf("B", HashSetCategory.KnownBad, md5)], [], [ev]));
        Assert.Equal("MD5", m.Algorithm);
        Assert.Empty(HashSetMatcher.Match([SetOf("C", HashSetCategory.KnownBad, imp)], [], [ev]));
    }

    [Fact]
    public void F5_detection_rule_id_is_the_listed_rule_and_the_hash_is_evidence_not_part_of_the_rule_id()
    {
        var h = Sha256Of("rid");
        var engine = new DetectionEngine { KnownHashSets = [SetOf("Rele", HashSetCategory.KnownBad, h)] };
        var d = Assert.Single(engine.Run([], [Item("EV-1", h)], _ => []));
        Assert.Equal("HASHSET:Rele", d.RuleId);
        Assert.Contains(engine.Rules, r => r.RuleId == d.RuleId);
        Assert.Contains(h, d.Match);
    }

    [Fact]
    public void F4_total_limit_over_all_sets_refuses_the_set_that_would_pass_it_with_a_clear_message()
    {
        var dir = Path.Combine(_kit.Dir, "limited");
        var lib = new HashSetLibrary(dir, maxTotalEntries: 3);
        Assert.True(lib.Import(_kit.File("a.txt", Sha256Of("1") + "\n" + Sha256Of("2") + "\n"), "Primul", HashSetCategory.KnownBad).Ok);
        Assert.True(lib.Import(_kit.File("b.txt", Sha256Of("3") + "\n" + Sha256Of("4") + "\n"), "Al doilea", HashSetCategory.KnownBad).Ok);
        var sets = lib.Load(out var problems);
        Assert.Equal("Primul", Assert.Single(sets).Info.Name);
        var p = Assert.Single(problems);
        Assert.Contains("Al doilea", p); Assert.Contains("limita totală de 3", p); Assert.Contains("nu a fost folosit", p);
    }

    [Fact]
    public void F4_load_is_memoized_until_a_file_changes_or_a_set_is_imported_or_removed()
    {
        var lib = Lib();
        var r = lib.Import(_kit.File("a.txt", Sha256Of("m1") + "\n"), "M", HashSetCategory.KnownBad);
        var first = Assert.Single(lib.Load());
        Assert.Same(first, Assert.Single(Lib().Load()));                 // another instance of the same folder: no re-read
        Assert.True(lib.Import(_kit.File("b.txt", Sha256Of("m2") + "\n"), "N", HashSetCategory.KnownBad).Ok);
        var two = lib.Load(); Assert.Equal(2, two.Count);                // import invalidated
        Assert.Same(two.Single(s => s.Info.Name == "M" && ReferenceEquals(s, first) == false), two.Single(s => s.Info.Name == "M"));   // re-read, new instance
        var again = lib.Load(); Assert.Same(again[0], lib.Load()[0]);   // and memoized again
        var meta = Path.Combine(lib.Dir, r.Set!.Info.Id + ".json");
        File.WriteAllText(meta, File.ReadAllText(meta).Replace("cunoscut-rău", "cunoscut-bun"));   // a changed file is seen: the set is now refused
        Assert.Single(Lib().Load(out var problems)); Assert.Single(problems);
        Assert.Equal("", lib.Remove(lib.Load()[0].Info.Id));
        Assert.Empty(lib.Load());
    }

    [Fact]
    public void F4_one_million_sha256_take_a_small_fraction_of_the_string_set_memory()
    {
        static IEnumerable<string> Gen(int n) { var buf = new byte[4]; for (int i = 0; i < n; i++) { BitConverter.TryWriteBytes(buf, i); yield return Convert.ToHexString(SHA256.HashData(buf)); } }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        var set = new KnownHashSet(new KnownHashSetInfo { Id = "big", Name = "big", Category = HashSetCategory.KnownBad }, Gen(1_000_000));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var compact = GC.GetTotalMemory(true) - before;
        Assert.Equal(1_000_000, set.Hashes.Count);
        Assert.True(set.Contains(Gen(1).First())); Assert.False(set.Contains(Sha256Of("absent")));
        // old representation, measured the same way: HashSet<string> of the same 1M values
        var before2 = GC.GetTotalMemory(true);
        var strings = Gen(1_000_000).ToHashSet(StringComparer.Ordinal);
        var old = GC.GetTotalMemory(true) - before2;
        GC.KeepAlive(strings); GC.KeepAlive(set);
        Console.WriteLine($"BENCH 1M SHA-256: compact={compact / 1048576.0:F1} MiB, HashSet<string>={old / 1048576.0:F1} MiB");
        Assert.True(compact < 48L * 1048576, $"compact = {compact / 1048576.0:F1} MiB");
        Assert.True(compact * 3 < old, $"compact {compact} vs strings {old}");
    }

    [Fact]
    public void F4_view_model_loads_in_the_background_and_a_newer_load_wins_over_an_older_one()
    {
        var lib = Lib();
        Assert.True(lib.Import(_kit.File("a.txt", Sha256Of("v1") + "\n"), "V", HashSetCategory.KnownBad).Ok);
        var vm = new HashSetsViewModel(lib);
        vm.LoadTask.GetAwaiter().GetResult();
        Assert.Single(vm.Sets);
        var stale = vm.ReloadAsync();                                    // starts a load, then a synchronous one supersedes it
        vm.Reload(); stale.GetAwaiter().GetResult();
        Assert.Single(vm.Sets);
        Assert.Contains("SHA-256", vm.Sets[0].Display);
    }

    [Fact]
    public void F3_card_and_result_state_the_coverage_and_a_md5_only_set_gets_a_partial_gap()
    {
        Assert.Contains("SHA-256", HashSetMatcher.CoverageNote); Assert.Contains("MD5", HashSetMatcher.CoverageNote); Assert.Contains("Sysmon", HashSetMatcher.CoverageNote);
        Assert.Equal(HashSetMatcher.CoverageNote, HashSetsViewModel.CoverageNote);
        var lib = Lib();
        Assert.True(lib.Import(_kit.File("m.txt", Md5Of("evil-content") + "\n"), "Doar MD5", HashSetCategory.KnownBad).Ok);
        var vm = new HashSetsViewModel(lib); vm.Reload();
        Assert.Contains("acoperire", vm.Sets[0].Display);
        var ws = _kit.Case(_kit.File("src/evil.bin", "evil-content"));
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false, hashSets: lib);
        Assert.Empty(r.KnownHashMatches);                                // evidence samples are SHA-256 only
        Assert.Contains(r.Gaps, g => g.Artifact == "Set de amprente" && g.Reason.Contains("Doar MD5") && g.Reason.Contains("SHA-256"));
    }

    [Fact]
    public void F5_source_sha256_comes_from_the_parsed_bytes_in_one_read_and_equals_the_file_hash()
    {
        var f = _kit.File("one.txt", Sha256Of("one") + "\n# c\n" + Sha1Of("one") + "\n");
        var r = HashSetImporter.ImportFile(f, "One", HashSetCategory.KnownBad);
        Assert.True(r.Ok);
        Assert.Equal(LogAnalyzer.Dfir.IO.Hashing.Sha256File(f), r.Set!.Info.SourceSha256);
        var lib = Lib(); var imp = lib.Import(f, "One", HashSetCategory.KnownBad);
        Assert.Equal(LogAnalyzer.Dfir.IO.Hashing.Sha256File(Path.Combine(lib.Dir, imp.Set!.Info.Id + ".hashes.txt")), imp.Set.Info.StoredSha256);
        var audit = File.ReadAllText(Path.Combine(lib.Dir, HashSetLibrary.AuditFileName));
        Assert.Contains(imp.Set.Info.StoredSha256, audit, StringComparison.OrdinalIgnoreCase); Assert.Contains(imp.Set.Info.SourceSha256, audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void F2_restricted_acl_gives_administrators_and_system_full_control_users_read_only_and_no_inheritance_and_failure_is_reported_not_thrown()
    {
        var dir = Path.Combine(_kit.Dir, "acl"); Directory.CreateDirectory(dir);
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        try
        {
            Assert.Equal("", HashSetLibrary.ApplyRestrictedAcl(dir));
            var sec = new DirectoryInfo(dir).GetAccessControl();
            Assert.True(sec.AreAccessRulesProtected);
            var rules = sec.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().ToList();
            var admins = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null);
            var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
            Assert.Contains(rules, r => r.IdentityReference == admins && r.FileSystemRights == System.Security.AccessControl.FileSystemRights.FullControl);
            Assert.Contains(rules, r => r.IdentityReference == system && r.FileSystemRights == System.Security.AccessControl.FileSystemRights.FullControl);
            var u = Assert.Single(rules, r => r.IdentityReference == users);
            Assert.Equal(System.Security.AccessControl.FileSystemRights.ReadAndExecute | System.Security.AccessControl.FileSystemRights.Synchronize, u.FileSystemRights);
            Assert.Equal(3, rules.Count);
        }
        finally
        {   // give the test's own account its access back so the temp folder can be deleted
            var back = new System.Security.AccessControl.DirectorySecurity();
            back.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(me, System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            try { new DirectoryInfo(dir).SetAccessControl(back); } catch (Exception) { }
        }
        var problem = HashSetLibrary.ApplyRestrictedAcl(Path.Combine(_kit.Dir, "does-not-exist"));
        Assert.Contains("nu a putut fi restricționat", problem);
    }

    [Fact]
    public void F4_self_test_runs_with_an_empty_library_not_with_the_sets_of_the_station()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "LogAnalyzer.Dfir.Windows", "Investigation", "SelfTest.cs"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!, "LogAnalyzer.Dfir.Windows", "Investigation", "SelfTest.cs"));
        Assert.Contains("hashSets: new LogAnalyzer.Dfir.HashSets.HashSetLibrary(Path.Combine(root,", src);
        Assert.DoesNotContain("Run(ws, CollectionProfile.Quick, collect: false);", src);
    }
}

[CollectionDefinition(HashSetsTests.Serial, DisableParallelization = true)]
public sealed class HashSetsSerialCollection { }
