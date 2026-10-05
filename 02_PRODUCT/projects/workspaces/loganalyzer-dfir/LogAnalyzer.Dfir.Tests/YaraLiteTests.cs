using System.Text;
using LogAnalyzer.Dfir.Detection;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>YARA-lite: language subset semantics, the shipped rule on the real MSBuild loader, and false positives on the .NET SDK.</summary>
public sealed class YaraLiteTests(ITestOutputHelper output)
{
    private static readonly string RulesDir = Path.Combine(AppContext.BaseDirectory, "Rules", "yara");

    private static bool Hits(string rules, byte[] data) => YaraLite.Parse(rules).Scan(data).Count > 0;
    private static byte[] A(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Text_hex_and_regex_strings_with_modifiers()
    {
        Assert.True(Hits("rule a { strings: $x = \"evil\" nocase condition: $x }", A("..EVIL..")));
        Assert.False(Hits("rule a { strings: $x = \"evil\" condition: $x }", A("..EVIL..")));
        Assert.True(Hits("rule a { strings: $x = \"evil\" wide condition: $x }", Encoding.Unicode.GetBytes("xx evil xx")));
        Assert.False(Hits("rule a { strings: $x = \"evil\" wide condition: $x }", A("xx evil xx")));
        Assert.True(Hits("rule a { strings: $x = \"evil\" ascii wide condition: $x }", A("xx evil xx")));
        Assert.False(Hits("rule a { strings: $x = \"evil\" fullword condition: $x }", A("xxevilxx")));
        Assert.True(Hits("rule a { strings: $x = \"evil\" fullword condition: $x }", A("x evil.x")));
        Assert.True(Hits("rule a { strings: $mz = { 4D 5A ?? 00 [2-4] 50 45 } condition: $mz }", [0x4D, 0x5A, 0x90, 0x00, 1, 2, 3, 0x50, 0x45]));
        Assert.False(Hits("rule a { strings: $mz = { 4D 5A ?? 00 [2-4] 50 45 } condition: $mz }", [0x4D, 0x5A, 0x90, 0x00, 1, 0x50, 0x45]));
        Assert.True(Hits("rule a { strings: $r = /ab[0-9]{2}cd/ condition: $r }", A("xxab42cdxx")));
    }

    [Fact]
    public void Conditions_counts_sets_filesize_and_logic()
    {
        const string r = """
            rule c { strings: $a1 = "aa" $a2 = "bb" $c = "cc"
                     condition: (2 of ($a*) and not $c) or (#c >= 3 and filesize < 1KB) }
            """;
        Assert.True(Hits(r, A("aa bb")));
        Assert.False(Hits(r, A("aa bb cc")));
        Assert.True(Hits(r, A("cc cc cc")));
        Assert.False(Hits(r, A("cc cc cc" + new string(' ', 2000))));
        Assert.True(Hits("rule d { strings: $a = \"x\" $b = \"y\" condition: all of them }", A("x y")));
        Assert.False(Hits("rule d { strings: $a = \"x\" $b = \"y\" condition: all of them }", A("x")));
        var m = Assert.Single(YaraLite.Parse("rule e { meta: version = \"7\" strings: $a = \"q\" condition: #a == 2 }").Scan(A("q..q")));
        Assert.Equal(2, m.Counts["$a"]);
        Assert.Equal("7", m.Rule.Identity.Version);
    }

    [Theory]
    [InlineData("rule a { strings: $x = \"e\" xor condition: $x }")]            // modifier outside the subset
    [InlineData("rule a { strings: $x = \"e\" condition: $y }")]                // undefined string
    [InlineData("rule a { strings: $x = \"e\" condition: $x at 0 }")]           // construct outside the subset
    [InlineData("rule a { strings: $x = \"e\" }")]                              // no condition
    [InlineData("nothing here")]
    public void Anything_outside_the_subset_is_a_parse_error(string text)
    {
        Assert.Throws<FormatException>(() => YaraLite.Parse(text));
    }

    /// <summary>The shipped rule fires on the real MSBuild loader file and on none of its sibling files.</summary>
    [CorpusFact("iocInventory")]
    public void Shipped_rule_matches_the_real_msbuild_loader_only()
    {
        var y = YaraLite.Parse(File.ReadAllText(Path.Combine(RulesDir, "msbuild_obfuscation.yar")), "msbuild_obfuscation.yar");
        var folder = Path.Combine(Corpus.Root, "25_TARGET_MSBUILD", "Conexant_CxUtilSvcHelper_COPY");
        var hits = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => y.Scan(File.ReadAllBytes(f)).Count > 0).Select(Path.GetFileName).ToList();
        Assert.Equal(["NetworkMonitor.targets"], hits);
    }

    /// <summary>No match on the .NET SDK's own MSBuild files, many of which use $([System.…]) property functions.</summary>
    [Fact]
    public void Shipped_rule_has_no_false_positive_on_the_dotnet_sdk()
    {
        var sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk");
        Assert.True(Directory.Exists(sdk), $"SDK-ul .NET lipsește ({sdk}): testul de alarme false nu poate rula");
        var y = YaraLite.Parse(File.ReadAllText(Path.Combine(RulesDir, "msbuild_obfuscation.yar")));
        int scanned = 0, withPropertyFunctions = 0, unreadable = 0;
        var hits = new List<string>();
        foreach (var f in Directory.EnumerateFiles(sdk, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".targets") || f.EndsWith(".props")))
        {
            byte[] data;
            try { data = File.ReadAllBytes(f); }
            catch (UnauthorizedAccessException) { unreadable++; continue; }
            scanned++;
            if (Encoding.Latin1.GetString(data).Contains("$([System.")) withPropertyFunctions++;
            if (y.Scan(data).Count > 0) hits.Add(f);
        }
        output.WriteLine($"{scanned} fișiere scanate, {withPropertyFunctions} cu funcții de proprietate, {unreadable} fără drept de citire");
        Assert.True(scanned > 300 && withPropertyFunctions > 50, $"{scanned} scanate, {withPropertyFunctions} relevante");
        Assert.Empty(hits);
    }
}
