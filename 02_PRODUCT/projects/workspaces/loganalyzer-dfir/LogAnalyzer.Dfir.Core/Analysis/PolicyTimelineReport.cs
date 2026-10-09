using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public enum PolicyLineStyle { Heading, Normal, Muted, Warn, Bad }

public sealed record PolicyReportLine(string Text, PolicyLineStyle Style);

/// <summary>Text of the "Cronologie politici" section (investigation view and investigation PDF): one source, so both show the same words.</summary>
public static class PolicyTimelineReport
{
    private static string Level(LevelState s) => ContractNames.Snake(s.ToString());
    private static string AppText(LevelState s) => s switch { LevelState.Observed => "observată", LevelState.NotObserved => "neobservată", _ => "neevaluabilă (necunoscută)" };

    public static List<PolicyReportLine> Lines(PolicyTimelineResult r, int maxChains = 40, int maxOther = 25)
    {
        var l = new List<PolicyReportLine>();
        if (r.ExpectedNote.Length > 0) l.Add(new(r.ExpectedNote, PolicyLineStyle.Warn));

        if (r.Chains.Count > 0) l.Add(new("Modificări de politică (înainte → modificare → după → aplicare → comportament)", PolicyLineStyle.Heading));
        foreach (var c in r.Chains.Take(maxChains))
        {
            var ch = c.Change;
            l.Add(new($"[{ch.ChangeId}] {ch.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC · {ch.EventId} {ch.Kind} · {ch.Who} · {ch.TargetKind} {(ch.TargetName.Length > 0 ? ch.TargetName : ch.TargetId)}{(ch.Attribute.Length > 0 ? " · " + ch.Attribute : "")}", PolicyLineStyle.Normal));
            l.Add(new($"    înainte: {(ch.OldValue.Length > 0 ? ch.OldValue : "(nu e în eveniment)")} · după: {(ch.NewValue.Length > 0 ? ch.NewValue : "(nu e în eveniment)")}" +
                      (ch.Lifecycle.Length > 0 ? $" · {ch.Lifecycle}" : ""), PolicyLineStyle.Muted));
            l.Add(new($"    Aplicare: {AppText(c.ApplicationState)} — {c.ApplicationReason}", PolicyLineStyle.Muted));
            l.Add(new($"    Comportament: {PolicyChain.BehaviorText(c.Behavior)} — {c.BehaviorReason}",
                c.Behavior == BehaviorState.NotObserved ? PolicyLineStyle.Warn : PolicyLineStyle.Muted));
        }
        if (r.Chains.Count > maxChains) l.Add(new($"… {r.Chains.Count - maxChains} modificări suplimentare în Analysis/policy_timeline.json.", PolicyLineStyle.Muted));

        if (r.Levels.Count > 0) l.Add(new("Configurat / aplicat / impus / observat (per setare din politica așteptată)", PolicyLineStyle.Heading));
        foreach (var s in r.Levels.Take(maxOther))
        {
            l.Add(new($"{s.Setting.Id} „{s.Setting.Title}” ({s.Setting.Source}) cere {s.Setting.DesiredDisplay}: " +
                      string.Join(", ", SettingLevels.Names.Select(n => $"{n}={Level(s.Level(n).State)}")), PolicyLineStyle.Normal));
            foreach (var n in SettingLevels.Names.Skip(1).Where(n => s.Level(n).State != LevelState.Observed))
                l.Add(new($"    {n}: {s.Level(n).Reason}", s.Level(n).State == LevelState.NotObserved ? PolicyLineStyle.Warn : PolicyLineStyle.Muted));
        }
        if (r.Levels.Count > maxOther) l.Add(new($"… {r.Levels.Count - maxOther} setări suplimentare în Analysis/policy_timeline.json.", PolicyLineStyle.Muted));

        foreach (var g in r.Gaps.Take(maxOther))
            l.Add(new($"Decalaj de control [{g.Severity}] {g.SettingId} „{g.Title}”: se rupe la nivelul „{g.BrokenLevel}” — {g.Reason}", PolicyLineStyle.Bad));

        var jumps = r.Time.Jumps.Where(j => !j.Benign).ToList();
        if (jumps.Count > 0 || r.Time.ZoneChanges.Count > 0 || r.Time.Inversions.Count > 0) l.Add(new("Ora sistemului", PolicyLineStyle.Heading));
        foreach (var j in jumps.Take(maxOther))
            l.Add(new($"Ceas mutat {(j.Magnitude.Length > 0 ? j.Magnitude : "(magnitudine necunoscută)")} la {j.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC ({j.Source["EventLog:".Length..]} {j.EventId}) de {(j.Actor.Length > 0 ? j.Actor : "cont necunoscut")} / {j.Process}", PolicyLineStyle.Bad));
        foreach (var z in r.Time.ZoneChanges.Take(maxOther))
            l.Add(new($"Schimbare de fus orar la {z.TimeUtc:yyyy-MM-dd HH:mm} UTC ({z.Detail}); nu e, singură, manipulare.", PolicyLineStyle.Warn));
        foreach (var v in r.Time.Inversions.Take(maxOther))
            l.Add(new($"Ordine contrazisă de oră: {v.Source["EventLog:".Length..]} RecordID {v.PreviousRecordId} → {v.RecordId}, ora a coborât cu {TimeManipulation.Human(v.Back)}", PolicyLineStyle.Bad));
        if (r.Time.Windows.Count > 0)
            l.Add(new($"{r.Time.Windows.Count} ferestre de oră nesigură: verificarea TEMPORAL raportează UNKNOWN (nu CONTRADICTED) pentru constatările din ele.", PolicyLineStyle.Warn));
        if (l.Count == 0) l.Add(new("Nicio modificare de politică și nicio manipulare a orei în probele analizate (asta nu dovedește că nu au existat: jurnalele relevante pot lipsi).", PolicyLineStyle.Muted));
        return l;
    }
}
