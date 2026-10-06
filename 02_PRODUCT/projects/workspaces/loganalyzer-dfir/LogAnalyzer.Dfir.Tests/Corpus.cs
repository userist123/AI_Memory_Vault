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

/// <summary>A [Fact] that is skipped when the referenced corpus section's file is not on this machine.</summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute(string section)
    {
        try
        {
            var f = Corpus.File(section);
            if (!System.IO.File.Exists(f)) Skip = $"Regression corpus not present: {f}";
        }
        catch (Exception ex) { Skip = $"Regression corpus unavailable: {ex.Message}"; }
    }
}
