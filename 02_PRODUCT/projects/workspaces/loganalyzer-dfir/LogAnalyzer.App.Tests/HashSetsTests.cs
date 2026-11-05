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
public sealed class HashSetsTests : IDisposable
{
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
}
