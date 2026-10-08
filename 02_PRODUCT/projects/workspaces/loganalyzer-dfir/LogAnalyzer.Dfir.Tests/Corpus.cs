using System.Text.Json;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Locates the real regression corpus (spec §4, §109). Tests are SKIPPED, not failed, when it is absent.</summary>
public static class Corpus
{
    private static readonly Lazy<JsonElement> TargetsLazy = new(() =>
        JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "NanAgentCase", "targets.json"))).RootElement);

    public static JsonElement Targets => TargetsLazy.Value;

    public static string Root =>
        Environment.GetEnvironmentVariable(Targets.GetProperty("corpusRootEnvVar").GetString()!) is { Length: > 0 } env
            ? env : Targets.GetProperty("corpusRoot").GetString()!;

    public static string File(string section) => Path.Combine(Root, Targets.GetProperty(section).GetProperty("file").GetString()!);

    public static string S(string section, string prop) => Targets.GetProperty(section).GetProperty(prop).GetString()!;
    public static long L(string section, string prop) => Targets.GetProperty(section).GetProperty(prop).GetInt64();
}

/// <summary>
/// A [Fact] that is skipped when the referenced corpus section's file is not on this machine. With LADFIR_REQUIRE_CORPUS=1
/// (a forensic validation run) it is never skipped: a missing corpus then fails the run instead of passing it.
/// </summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute(string section)
    {
        if (ForensicValidation.CorpusRequired) return;
        try
        {
            var f = Corpus.File(section);
            if (!System.IO.File.Exists(f)) Skip = $"Regression corpus not present: {f} — FORENSIC VALIDATION = UNAVAILABLE";
        }
        catch (Exception ex) { Skip = $"Regression corpus unavailable: {ex.Message} — FORENSIC VALIDATION = UNAVAILABLE"; }
    }
}

/// <summary>
/// States whether forensic (corpus) validation actually happened, so a green run without the corpus is never read as
/// validated (master spec §21: "corpus missing → skipped → green → valid" is forbidden).
/// </summary>
public static class ForensicValidation
{
    public static bool CorpusRequired => Environment.GetEnvironmentVariable("LADFIR_REQUIRE_CORPUS") == "1";

    public static IReadOnlyList<(string Section, string File, bool Present)> Sections()
    {
        var list = new List<(string, string, bool)>();
        foreach (var p in Corpus.Targets.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("file", out _))
            {
                var f = Corpus.File(p.Name);
                list.Add((p.Name, f, System.IO.File.Exists(f)));
            }
        return list;
    }

    public static string Summarize(IReadOnlyList<(string Section, string File, bool Present)> sections)
    {
        int present = sections.Count(s => s.Present);
        var state = present == 0 ? "UNAVAILABLE" : present == sections.Count ? "AVAILABLE" : "PARTIAL";
        var lines = new List<string> { $"FORENSIC VALIDATION = {state} ({present}/{sections.Count} secțiuni de corpus prezente)" };
        lines.AddRange(sections.Select(s => $"  {(s.Present ? "PRESENT" : "MISSING")}  {s.Section}  {s.File}"));
        if (state != "AVAILABLE")
            lines.Add("  Testele pe corpus pentru secțiunile lipsă au fost SĂRITE; rezultatul verde NU constituie validare forensică.");
        return string.Join(Environment.NewLine, lines);
    }
}
