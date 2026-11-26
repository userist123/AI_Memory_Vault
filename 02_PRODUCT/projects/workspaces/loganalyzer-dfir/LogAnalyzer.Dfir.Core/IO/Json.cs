using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.IO;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static readonly JsonSerializerOptions Line = new(Options) { WriteIndented = false };

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Empty JSON: {path}");

    public static void AppendLine<T>(string path, T value) => SharedAppend.WriteText(path, JsonSerializer.Serialize(value, Line) + "\n");

    /// <summary>
    /// Streams a large JSONL output written once by its own run (parsed events): lazy, so a whole log is never held in memory (spec §77). Not for the
    /// small shared case files (evidence index, audit): those go through <see cref="ReadLines{T}"/>, which waits for another instance's writer.
    /// </summary>
    public static IEnumerable<T> StreamLines<T>(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadLines(path))
            if (!string.IsNullOrWhiteSpace(line))
                yield return JsonSerializer.Deserialize<T>(line, Line) ?? throw new InvalidDataException($"Bad JSONL line in {path}");
    }

    /// <summary>A small shared case file read whole under one handle, waiting for another instance's writer (<see cref="SharedRead"/>).</summary>
    public static IEnumerable<T> ReadLines<T>(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in SharedRead.Lines(path))
            if (!string.IsNullOrWhiteSpace(line))
                yield return JsonSerializer.Deserialize<T>(line, Line) ?? throw new InvalidDataException($"Bad JSONL line in {path}");
    }
}
