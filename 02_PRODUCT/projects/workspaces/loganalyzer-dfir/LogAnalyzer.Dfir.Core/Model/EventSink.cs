using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Model;

/// <summary>Receives normalized events one by one so parsers never buffer whole logs in memory (spec §77, §115).</summary>
public interface IEventSink
{
    void Add(TimelineEvent e);
}

public sealed class ListSink : IEventSink
{
    public List<TimelineEvent> Events { get; } = [];
    public void Add(TimelineEvent e) => Events.Add(e);
}

/// <summary>Appends events as JSON lines to a file inside the case (Parsed/…).</summary>
public sealed class JsonlSink : IEventSink, IDisposable
{
    private readonly StreamWriter _w;
    public string Path { get; }
    public int Count { get; private set; }

    public JsonlSink(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        _w = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(false));
    }

    public void Add(TimelineEvent e)
    {
        _w.WriteLine(System.Text.Json.JsonSerializer.Serialize(e, Json.Line));
        Count++;
    }

    public void Dispose() => _w.Dispose();

    public static IEnumerable<TimelineEvent> Read(string path) => Json.ReadLines<TimelineEvent>(path);
}
