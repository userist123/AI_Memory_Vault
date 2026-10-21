using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// What the user-activity view needs to be the same after a case is reopened (Analysis/user_activity.json, written by the pipeline, read by the case loader): the classified
/// folders of the profile that was used, the drive letters the station facts tie to removable media, and the parser-specific fields of the user-activity rows
/// (timeline.csv does not keep them). A case without the file was written before this existed: its view is "neevaluabil la redeschidere", never "nedefinit".
/// </summary>
public sealed class UserActivityContext
{
    public const string RelPath = "Analysis/user_activity.json";
    public const string UnevaluableText = "neevaluabil la redeschidere: profilul și literele confirmate nu au fost salvate cu acest caz.";

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = "1.0";
    public List<ClassifiedFolderRow> ClassifiedFolders { get; set; } = [];
    public List<string> RemovableLetters { get; set; } = [];
    public List<SavedRow> Rows { get; set; } = [];

    public sealed class SavedRow
    {
        public string Key { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = [];
    }

    private static bool IsUserActivity(TimelineEvent e) => e.Source is UserActivityAnalysis.ShellBags or UserActivityAnalysis.RecentDocs or UserActivityAnalysis.OfficeMru or UserActivityAnalysis.RecycleBin;
    private static string KeyOf(TimelineEvent e) => $"{e.Source}|{e.EvidenceId}|{e.Locator}|{e.Path}";

    public static UserActivityContext From(IReadOnlyList<TimelineEvent> timeline, IEnumerable<ClassifiedFolderRow>? folders, IEnumerable<string>? removableLetters) => new()
    {
        ClassifiedFolders = [.. (folders ?? []).Select(f => new ClassifiedFolderRow { Path = f.Path, Note = f.Note })],
        RemovableLetters = [.. (removableLetters ?? []).Distinct(StringComparer.OrdinalIgnoreCase)],
        Rows = [.. timeline.Where(IsUserActivity).Select(e => new SavedRow { Key = KeyOf(e), Fields = new Dictionary<string, string>(e.Fields) })],
    };

    public void Write(string path) => Json.Write(path, this);

    /// <summary>The saved context, or null when the file does not exist. An unreadable file throws (the loader reports it), it is not read as "empty".</summary>
    public static UserActivityContext? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var c = JsonSerializer.Deserialize<UserActivityContext>(File.ReadAllText(path), Json.Options) ?? throw new JsonException("user_activity.json este gol");
        SchemaVersions.Accept(c.SchemaVersion, "user_activity.json");
        return c;
    }

    /// <summary>
    /// Puts the saved fields back on the user-activity rows of a timeline read from timeline.csv. The rows are matched one by one, in order, by source, evidence, locator and path;
    /// returns false (and changes nothing) when they do not match exactly, so a timeline that is not the one the context was written for is never mixed with it.
    /// </summary>
    public bool TryRestoreFields(IReadOnlyList<TimelineEvent> timeline)
    {
        var rows = timeline.Where(IsUserActivity).ToList();
        if (rows.Count != Rows.Count) return false;
        for (int i = 0; i < rows.Count; i++) if (KeyOf(rows[i]) != Rows[i].Key) return false;
        for (int i = 0; i < rows.Count; i++)
            foreach (var (k, v) in Rows[i].Fields) rows[i].Fields[k] = v;
        return true;
    }
}
