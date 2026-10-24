using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class SequenceRules
{
    /// <summary>A removable medium with a drive letter and at least one trace of writing on its volume (from the WP14a activity reader), tied to its MEDIA-* findings where they exist.</summary>
    internal sealed record Medium(string Serial, string Letter, string Label, HashSet<string> Users, List<Wp14Rules.Activity> Writes, Finding? Presence, Finding? Activity)
    {
        public string Name => Serial.Length > 0 ? Serial : Label.Length > 0 ? Label : "fără serie";
        public IEnumerable<string> FindingIds => new[] { Presence?.FindingId, Activity?.FindingId }.Where(i => !string.IsNullOrEmpty(i)).Select(i => i!);
    }

    private static List<Medium> MediaWithWrites(Ctx c)
    {
        var list = new List<Medium>();
        foreach (var o in Wp14Rules.ObservedUsb(c.Events))
        {
            if (o.Letter.Length == 0) continue;
            var writes = Wp14Rules.ActivityOn(c.Events, o.Letter).Where(a => a.Write && T(a.Event) is not null).ToList();
            if (writes.Count == 0) continue;
            var presence = c.Existing.FirstOrDefault(f => IsMediaPresence(f) && o.Serial.Length > 0 && SerialOf(f) == o.Serial);
            var act = c.Existing.FirstOrDefault(f => f.RuleId == "MEDIA-FILE-ACTIVITY" && o.Serial.Length > 0 && SerialOf(f) == o.Serial);
            list.Add(new Medium(o.Serial, o.Letter, o.Label, new HashSet<string>(o.Users, StringComparer.OrdinalIgnoreCase), writes, presence, act));
        }
        return list;
    }

    /// <summary>Every drive letter a removable medium was seen with, writes or not: paths on them are never "local staging".</summary>
    private static HashSet<string> RemovableLetters(Ctx c) =>
        Wp14Rules.ObservedUsb(c.Events).Select(o => o.Letter).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The account behind a write: the 4663 subject if the row has one, else the only account known for the medium; "" = the sources do not say.</summary>
    private static string WriteAccount(Medium m, Wp14Rules.Activity a)
    {
        var s = SubjectAccount(a.Event);
        if (s.Length > 0) return s;
        return m.Users.Count == 1 ? m.Users.First() : "";
    }

    /// <summary>
    /// False only when the sources name the account of the write (or of the medium) and it is a different account from <paramref name="account"/>. An unknown account joins
    /// by time alone and the sequence says so.
    /// </summary>
    private static bool WriteCompatible(string account, Medium m, Wp14Rules.Activity a)
    {
        var s = SubjectAccount(a.Event);
        if (s.Length > 0) return SequenceEngine.JoinAccount(account, s) != JoinResult.Different;
        if (m.Users.Count == 0) return true;
        var joins = m.Users.Select(u => SequenceEngine.JoinAccount(account, u)).ToList();
        return joins.Any(j => j != JoinResult.Different);
    }
}
