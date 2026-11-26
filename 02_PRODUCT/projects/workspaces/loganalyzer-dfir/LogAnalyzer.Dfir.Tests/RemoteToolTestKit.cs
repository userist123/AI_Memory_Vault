using System.Text;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Shared helpers of the remote-tool session log tests: the synthetic TEST corpus, a run helper and the "nothing hidden reaches an event" check.</summary>
internal sealed class RemoteToolTestKit : IDisposable
{
    public const string Zone = "W. Europe Standard Time";
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "ladfir_rtl_" + Guid.NewGuid().ToString("N"));

    public RemoteToolTestKit() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Dir, true);
        }
        catch (IOException) { }
    }

    public static string Corpus(string rel) => Path.Combine(AppContext.BaseDirectory, "Corpus", "RemoteToolLogs", rel);

    /// <summary>Writes a file with the exact name the tool uses (the name is part of the format) in a folder of its own.</summary>
    public string Write(string name, byte[] data)
    {
        var d = Path.Combine(Dir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        var p = Path.Combine(d, name);
        File.WriteAllBytes(p, data);
        return p;
    }

    public string Write(string name, string text, Encoding? enc = null) => Write(name, (enc ?? new UTF8Encoding(false)).GetBytes(text));

    public static EvidenceItem Item(string type, string path, string tz = "") =>
        new() { EvidenceId = "EV-RT", CaseId = "C", Source = "test", SourceType = type, StoredPath = path, Timezone = tz };

    public static (ParseResult R, List<TimelineEvent> E) Run(IEvidenceParser parser, string type, string path, string tz = "")
    {
        var sink = new ListSink();
        var r = parser.Parse(Item(type, path, tz), path, sink, default);
        return (r, sink.Events);
    }

    /// <summary>No hidden / bidi / control character and no Tag-block character in the summary, the locator or any field of the event.</summary>
    public static void AssertNothingHidden(TimelineEvent e)
    {
        foreach (var text in new[] { e.Summary, e.Locator, e.Provider, e.TimeSemantics }.Concat(e.Fields.Values))
        {
            Assert.DoesNotContain(text, ch => ReportText.IsHidden(ch) && ch is not ('\t' or '\n' or '\r'));
            Assert.False(text.Contains('\uDB40'), "Tag block character left in: " + text);
            Assert.DoesNotContain('\n', text);
        }
    }
}
