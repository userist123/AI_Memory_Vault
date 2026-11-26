using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using YamlDotNet.RepresentationModel;

namespace LogAnalyzer.Dfir.Detection;

public enum SigmaCorrelationType { EventCount, ValueCount, Temporal, TemporalOrdered }

/// <summary>One reported correlation: a group and one maximal run of overlapping satisfying windows, with every supporting event (capped).</summary>
public sealed record SigmaCorrelationHit(
    SigmaCorrelation Correlation, IReadOnlyList<(string Field, string Value)> Group, DateTimeOffset WindowStart, DateTimeOffset WindowEnd,
    int Count, IReadOnlyList<TimelineEvent> Events, int SupportingTotal)
{
    public bool Truncated => SupportingTotal > Events.Count;
}

/// <summary>What a correlation evaluation produced: hits, and the gaps (what was excluded or cut) that must be shown next to them.</summary>
public sealed class SigmaCorrelationRun
{
    public List<SigmaCorrelationHit> Hits { get; } = [];
    public List<string> Gaps { get; } = [];
}

/// <summary>
/// A Sigma 2.x correlation rule (<c>correlation:</c>) evaluated on the matches of base rules over the timeline. Supported subset:
/// type event_count | value_count | temporal | temporal_ordered; rules (names or ids of base rules); group-by; timespan (Ns, Nm, Nh, Nd);
/// condition (gt, gte, eq; for value_count also field); generate (accepted, no effect). Everything else, and every condition that
/// would turn the absence of events into a finding (lt, lte, eq 0), is a load error: an unsupported correlation never degrades silently.
/// Semantics (all deterministic) are described in docs/dfir/RULE_LAB.md.
/// </summary>
public sealed class SigmaCorrelation
{
    public const int MaxEventsPerGroup = 10_000;
    public const int MaxResults = 1_000;
    public const int MaxRefs = 200;

    public string Title { get; }
    public string Id { get; }
    public string Name { get; }
    public string Level { get; }
    public string Description { get; }
    public SigmaCorrelationType Type { get; }
    public IReadOnlyList<string> RuleRefs { get; }
    /// <summary>Identity of the referenced base rules, in the order of <see cref="RuleRefs"/>.</summary>
    public IReadOnlyList<DetectionRule> Referenced { get; }
    public IReadOnlyList<string> GroupBy { get; }
    public TimeSpan Timespan { get; }
    public string Field { get; }
    public long? Gt { get; }
    public long? Gte { get; }
    public long? Eq { get; }
    public bool Generate { get; }
    public DetectionRule Identity { get; }

    private SigmaCorrelation(string title, string id, string name, string level, string description, SigmaCorrelationType type, IReadOnlyList<string> refs,
        IReadOnlyList<DetectionRule> referenced, IReadOnlyList<string> groupBy, TimeSpan timespan, string field, long? gt, long? gte, long? eq, bool generate, DetectionRule identity)
    {
        (Title, Id, Name, Level, Description, Type, RuleRefs, Referenced, GroupBy, Timespan, Field, Gt, Gte, Eq, Generate, Identity) =
            (title, id, name, level, description, type, refs, referenced, groupBy, timespan, field, gt, gte, eq, generate, identity);
    }

    private const long MaxTimespanSeconds = 366L * 86400;
    private static readonly string[] TopLevelKnownKeys = ["title", "id", "name", "level", "description", "correlation", "status", "author", "date", "modified", "references", "tags", "falsepositives", "taxonomy", "license", "related", "generate"];

    /// <param name="file">File the document came from (names the rule in errors and as source).</param>
    /// <param name="baseRules">Every base rule loaded (any file of the loaded folders).</param>
    /// <param name="correlationRefs">Ids and names of every correlation document loaded, to refuse chaining with a clear message.</param>
    public static SigmaCorrelation Parse(string file, string yaml, IReadOnlyList<SigmaLite.SigmaRule> baseRules, IReadOnlyCollection<string> correlationRefs)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root) throw new FormatException($"{file}: nu este o regulă de corelare Sigma.");
        string? Scalar(YamlMappingNode m, string k) => m.Children.TryGetValue(new YamlScalarNode(k), out var v) && v is YamlScalarNode sc ? sc.Value : null;
        var title = Scalar(root, "title") ?? "";
        if (title.Length == 0) throw new FormatException($"{file}: lipsește title.");
        var who = $"{file} „{title}”";
        foreach (var k in root.Children.Keys.Select(k => (k as YamlScalarNode)?.Value ?? ""))
        {
            if (k == "aliases") throw new FormatException($"{who}: aliases nu este acceptat (corelarea folosește câmpurile evenimentelor, aceleași în toate regulile referite).");
            if (!TopLevelKnownKeys.Contains(k)) throw new FormatException($"{who}: cheia „{k}” nu este acceptată într-o regulă de corelare (action, scope și altele nu se aplică aici).");
        }
        if (root.Children.ContainsKey(new YamlScalarNode("detection")) || root.Children.ContainsKey(new YamlScalarNode("logsource"))) throw new FormatException($"{who}: o regulă de corelare nu are detection sau logsource.");
        if (!root.Children.TryGetValue(new YamlScalarNode("correlation"), out var cn) || cn is not YamlMappingNode c) throw new FormatException($"{who}: correlation trebuie să fie o hartă.");

        var allowed = new HashSet<string>(["type", "rules", "group-by", "timespan", "condition", "generate"], StringComparer.Ordinal);
        foreach (var k in c.Children.Keys.Select(k => (k as YamlScalarNode)?.Value ?? ""))
        {
            if (k == "aliases") throw new FormatException($"{who}: aliases nu este acceptat.");
            if (!allowed.Contains(k)) throw new FormatException($"{who}: cheia correlation.{k} nu este acceptată.");
        }

        var typeText = Scalar(c, "type") ?? throw new FormatException($"{who}: lipsește correlation.type.");
        var type = typeText switch
        {
            "event_count" => SigmaCorrelationType.EventCount,
            "value_count" => SigmaCorrelationType.ValueCount,
            "temporal" => SigmaCorrelationType.Temporal,
            "temporal_ordered" => SigmaCorrelationType.TemporalOrdered,
            _ => throw new FormatException($"{who}: tipul de corelare „{typeText}” nu este acceptat (event_count, value_count, temporal, temporal_ordered)."),
        };

        if (!c.Children.TryGetValue(new YamlScalarNode("rules"), out var rn)) throw new FormatException($"{who}: lipsește correlation.rules.");
        var refs = rn switch
        {
            YamlSequenceNode seq => seq.Children.Select(x => x is YamlScalarNode s && !string.IsNullOrWhiteSpace(s.Value) ? s.Value!.Trim() : throw new FormatException($"{who}: rules conține o intrare care nu e un nume sau un id.")).ToList(),
            YamlScalarNode s when !string.IsNullOrWhiteSpace(s.Value) => [s.Value!.Trim()],
            _ => throw new FormatException($"{who}: rules trebuie să fie o listă de nume sau id-uri."),
        };
        if (refs.Count == 0) throw new FormatException($"{who}: rules este goală.");
        if (refs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != refs.Count) throw new FormatException($"{who}: rules conține aceeași regulă de două ori.");
        if (type is SigmaCorrelationType.Temporal or SigmaCorrelationType.TemporalOrdered && refs.Count < 2)
            throw new FormatException($"{who}: {typeText} cere cel puțin două reguli în rules.");

        var referenced = new List<DetectionRule>();
        foreach (var r in refs)
        {
            if (correlationRefs.Contains(r, StringComparer.OrdinalIgnoreCase))
                throw new FormatException($"{who}: regula referită „{r}” este ea însăși o regulă de corelare; înlănțuirea corelărilor nu este acceptată.");
            var found = baseRules.Where(b => (b.Id.Length > 0 && b.Id.Equals(r, StringComparison.OrdinalIgnoreCase)) || (b.Name.Length > 0 && b.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
                .Select(b => b.Identity).DistinctBy(i => i.RuleId + "|" + i.Sha256).ToList();
            if (found.Count == 0) throw new FormatException($"{who}: regula referită „{r}” nu există printre regulile Sigma încărcate (nume sau id).");
            if (found.Count > 1) throw new FormatException($"{who}: regula referită „{r}” este ambiguă ({found.Count} reguli încărcate o au ca nume sau id).");
            referenced.Add(found[0]);
        }

        var groupBy = new List<string>();
        if (c.Children.TryGetValue(new YamlScalarNode("group-by"), out var gn))
            groupBy = gn switch
            {
                YamlSequenceNode seq => seq.Children.Select(x => x is YamlScalarNode s && !string.IsNullOrWhiteSpace(s.Value) ? s.Value!.Trim() : throw new FormatException($"{who}: group-by conține o intrare care nu e un nume de câmp.")).ToList(),
                YamlScalarNode s when !string.IsNullOrWhiteSpace(s.Value) => [s.Value!.Trim()],
                _ => throw new FormatException($"{who}: group-by trebuie să fie o listă de câmpuri."),
            };

        var tsText = Scalar(c, "timespan") ?? throw new FormatException($"{who}: lipsește correlation.timespan.");
        var tm = Regex.Match(tsText.Trim(), @"^(\d{1,7})([smhd])$");
        if (!tm.Success || long.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture) == 0) throw new FormatException($"{who}: timespan „{tsText}” nu este acceptat (număr pozitiv urmat de s, m, h sau d).");
        var n = long.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = tm.Groups[2].Value switch { "s" => n, "m" => n * 60, "h" => n * 3600, _ => n * 86400 };
        // a window longer than a year is not a correlation over a case; it also keeps every window end inside DateTimeOffset
        if (seconds > MaxTimespanSeconds) throw new FormatException($"{who}: timespan „{tsText}” depășește limita de 366 de zile.");
        var timespan = TimeSpan.FromSeconds(seconds);

        string field = "";
        long? gt = null, gte = null, eq = null;
        bool hasCondition = c.Children.TryGetValue(new YamlScalarNode("condition"), out var condNode);
        if (type is SigmaCorrelationType.Temporal or SigmaCorrelationType.TemporalOrdered)
        {
            if (hasCondition) throw new FormatException($"{who}: {typeText} nu are condition (toate regulile trebuie să se potrivească în fereastră).");
        }
        else
        {
            if (!hasCondition || condNode is not YamlMappingNode cond) throw new FormatException($"{who}: lipsește correlation.condition (o hartă cu gt, gte sau eq).");
            foreach (var (kn, vn) in cond.Children)
            {
                var k = ((YamlScalarNode)kn).Value ?? "";
                var v = (vn as YamlScalarNode)?.Value;
                if (k == "field")
                {
                    if (type != SigmaCorrelationType.ValueCount) throw new FormatException($"{who}: condition.field se folosește doar la value_count.");
                    field = string.IsNullOrWhiteSpace(v) ? throw new FormatException($"{who}: condition.field este gol.") : v.Trim();
                    continue;
                }
                if (k is "lt" or "lte")
                    throw new FormatException($"{who}: condition.{k} nu este acceptat: „mai puțin de N” nu este dovadă pe un caz care poate fi incomplet (absența unei potriviri nu înseamnă că evenimentul nu s-a întâmplat).");
                if (k is not ("gt" or "gte" or "eq")) throw new FormatException($"{who}: operatorul „{k}” din condition nu este acceptat (gt, gte, eq).");
                if (!long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var num)) throw new FormatException($"{who}: condition.{k} trebuie să fie un număr întreg nenegativ.");
                if (k == "eq" && num == 0) throw new FormatException($"{who}: condition.eq 0 nu este acceptat: absența evenimentelor nu este dovadă pe un caz care poate fi incomplet.");
                if (k == "gt") gt = num; else if (k == "gte") gte = num; else eq = num;
            }
            if (gt is null && gte is null && eq is null) throw new FormatException($"{who}: condition trebuie să aibă gt, gte sau eq.");
            if (type == SigmaCorrelationType.ValueCount && field.Length == 0) throw new FormatException($"{who}: value_count cere condition.field.");
        }

        bool generate = false;
        var gv = Scalar(c, "generate") ?? Scalar(root, "generate");
        if (gv is not null)
        {
            if (!bool.TryParse(gv, out generate)) throw new FormatException($"{who}: generate trebuie să fie true sau false.");
        }

        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml)));
        var id = Scalar(root, "id") ?? "";
        var name = Scalar(root, "name") ?? "";
        var level = Scalar(root, "level") ?? "";
        var version = Scalar(root, "modified") is { Length: > 0 } mod ? mod : Scalar(root, "date") is { Length: > 0 } d ? d : sha[..12];
        var tags = root.Children.TryGetValue(new YamlScalarNode("tags"), out var t) && t is YamlSequenceNode ts ? ts.Children.Select(x => ((x as YamlScalarNode)?.Value) ?? "").ToList() : [];
        var mitre = tags.FirstOrDefault(x => Regex.IsMatch(x, @"^attack\.t\d{4}", RegexOptions.IgnoreCase))?[7..].ToUpperInvariant() ?? "";
        var identity = new DetectionRule($"SIGMA-CORR:{(id.Length > 0 ? id : name.Length > 0 ? name : file)}", version, DetectionKind.Correlation, title, sha, file, mitre, level);
        return new SigmaCorrelation(title, id, name, level, Scalar(root, "description") ?? "", type, refs, referenced, groupBy, timespan, field, gt, gte, eq, generate, identity);
    }

    private bool Holds(long count) => (Gt is null || count > Gt) && (Gte is null || count >= Gte) && (Eq is null || count == Eq);

    private sealed record Item(TimelineEvent Ev, DateTimeOffset T, int[] Rules, string Val);

    /// <summary>
    /// Evaluates the correlation on the matches of base rules. Windows are sliding and anchored at each event: [t, t + timespan], both ends inclusive.
    /// </summary>
    /// <param name="matches">Every base-rule match of the run (any rule; only the referenced ones are used).</param>
    public SigmaCorrelationRun Evaluate(IEnumerable<(DetectionRule Rule, TimelineEvent Event)> matches)
    {
        var run = new SigmaCorrelationRun();
        var ruleIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Referenced.Count; i++) ruleIndex[Referenced[i].RuleId + "|" + Referenced[i].Sha256] = i;

        var perEvent = new Dictionary<TimelineEvent, SortedSet<int>>(ReferenceEqualityComparer.Instance);
        foreach (var (rule, ev) in matches)
        {
            if (!ruleIndex.TryGetValue(rule.RuleId + "|" + rule.Sha256, out var idx)) continue;
            if (!perEvent.TryGetValue(ev, out var set)) perEvent[ev] = set = [];
            set.Add(idx);
        }

        var unknownTime = perEvent.Keys.Count(e => e.Time.Utc is null);
        if (unknownTime > 0)
            run.Gaps.Add($"corelarea „{Title}”: {RoText.Count(unknownTime, "eveniment potrivit", "evenimente potrivite")} de regulile referite {(unknownTime == 1 ? "a fost exclus" : "au fost excluse")} din corelare, fiindcă ora lor este necunoscută; o corelare în timp nu le poate plasa într-o fereastră.");

        var items = new List<Item>();
        var noField = 0;
        var farFuture = 0;
        foreach (var (ev, set) in perEvent)
        {
            if (ev.Time.Utc is not { } t) continue;
            // a time so late that its window would pass the end of the calendar (a corrupt or crafted record) cannot be placed in a window
            if (t > DateTimeOffset.MaxValue - Timespan) { farFuture++; continue; }
            var val = "";
            if (Type == SigmaCorrelationType.ValueCount)
            {
                val = SigmaLite.Value(ev, Field).Trim().ToLowerInvariant();
                if (val.Length == 0) { noField++; continue; }
            }
            items.Add(new Item(ev, t, [.. set], val));
        }
        if (farFuture > 0)
            run.Gaps.Add($"corelarea „{Title}”: {RoText.Count(farFuture, "eveniment", "evenimente")} cu o oră aproape de anul 9999 {(farFuture == 1 ? "a fost exclus" : "au fost excluse")}: o asemenea oră indică o înregistrare coruptă.");
        if (noField > 0)
            run.Gaps.Add($"corelarea „{Title}”: {RoText.Count(noField, "eveniment", "evenimente")} fără câmpul „{Field}” sau cu el gol {(noField == 1 ? "nu a fost numărat" : "nu au fost numărate")} la valori distincte.");

        // as Sigma backends do, an event without a group-by value is not grouped (a reopened case keeps no parser fields: every user would fall in one group)
        int noGroup = items.RemoveAll(it => GroupBy.Any(f => SigmaLite.Value(it.Ev, f).Trim().Length == 0));
        if (noGroup > 0)
            run.Gaps.Add($"corelarea „{Title}”: {RoText.Count(noGroup, "eveniment", "evenimente")} fără valoare pentru group-by ({string.Join(", ", GroupBy)}) {(noGroup == 1 ? "nu a fost luat" : "nu au fost luate")} în calcul.");
        var groups = items.GroupBy(it => string.Join("\u001f", GroupBy.Select(f => SigmaLite.Value(it.Ev, f).Trim().ToLowerInvariant())), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var g in groups)
        {
            if (run.Hits.Count >= MaxResults)
            {
                run.Gaps.Add($"corelarea „{Title}”: s-au raportat doar primele {MaxResults:N0} de rezultate; grupurile următoare nu au fost evaluate.");
                break;
            }
            var ordered = g.OrderBy(it => it.T).ThenBy(it => it.Ev.EvidenceId, StringComparer.Ordinal).ThenBy(it => it.Ev.Locator, StringComparer.Ordinal).ToList();
            var groupValues = GroupBy.Select(f => (f, SigmaLite.Value(ordered[0].Ev, f).ToLowerInvariant())).ToList();
            var label = groupValues.Count == 0 ? "toate evenimentele" : string.Join(", ", groupValues.Select(v => $"{v.f}={(v.Item2.Length == 0 ? "(gol)" : v.Item2)}"));
            if (ordered.Count > MaxEventsPerGroup)
            {
                run.Gaps.Add($"corelarea „{Title}”, grupul {label}: {ordered.Count:N0} de evenimente; doar primele {MaxEventsPerGroup:N0} în ordine cronologică au fost luate în calcul.");
                ordered = ordered.Take(MaxEventsPerGroup).ToList();
            }
            EvaluateGroup(ordered, groupValues, run);
        }
        return run;
    }

    private void EvaluateGroup(List<Item> a, IReadOnlyList<(string Field, string Value)> group, SigmaCorrelationRun run)
    {
        var n = a.Count;
        var k = Referenced.Count;
        var ruleCounts = new int[k];
        var distinct = new Dictionary<string, int>(StringComparer.Ordinal);
        int j = -1;
        void Add(int x)
        {
            if (Type == SigmaCorrelationType.ValueCount) distinct[a[x].Val] = distinct.GetValueOrDefault(a[x].Val) + 1;
            else foreach (var r in a[x].Rules) ruleCounts[r]++;
        }
        void Remove(int x)
        {
            if (Type == SigmaCorrelationType.ValueCount) { if (--distinct[a[x].Val] == 0) distinct.Remove(a[x].Val); }
            else foreach (var r in a[x].Rules) ruleCounts[r]--;
        }

        // The current run of overlapping satisfying windows.
        bool open = false;
        DateTimeOffset runEnd = default;
        int lo = 0, hi = 0, peak = 0;
        SortedSet<int>? chain = null;

        void Flush()
        {
            if (!open) return;
            var idx = chain is not null ? chain.ToList() : Enumerable.Range(lo, hi - lo + 1).ToList();
            var evs = idx.Take(MaxRefs).Select(x => a[x].Ev).ToList();
            var count = chain is not null ? idx.Count : peak;
            if (run.Hits.Count < MaxResults)
                run.Hits.Add(new SigmaCorrelationHit(this, group, a[idx[0]].T, a[idx[^1]].T, count, evs, idx.Count));
            open = false; chain = null;
        }

        for (int i = 0; i < n; i++)
        {
            if (i > 0) Remove(i - 1);
            while (j + 1 < n && a[j + 1].T - a[i].T <= Timespan) Add(++j);   // the edge (exactly timespan) is inside

            int cnt = 0;
            SortedSet<int>? thisChain = null;
            bool ok;
            switch (Type)
            {
                case SigmaCorrelationType.EventCount: cnt = j - i + 1; ok = Holds(cnt); break;
                case SigmaCorrelationType.ValueCount: cnt = distinct.Count; ok = Holds(cnt); break;
                case SigmaCorrelationType.Temporal: ok = ruleCounts.All(c => c > 0); break;
                default:
                    ok = false;
                    if (a[i].Rules.Contains(0))
                    {
                        thisChain = [i];
                        var prevT = a[i].T;
                        int need = 1;
                        for (int x = i + 1; x <= j && need < k; x++)
                            if (a[x].T > prevT && a[x].Rules.Contains(need)) { thisChain.Add(x); prevT = a[x].T; need++; }   // strictly later: equal times carry no order
                        ok = need == k;
                    }
                    break;
            }
            if (!ok) continue;

            if (open && a[i].T > runEnd) Flush();
            var end = a[i].T + Timespan;
            if (!open)
            {
                open = true; runEnd = end; lo = i; hi = j; peak = cnt;
                chain = Type == SigmaCorrelationType.TemporalOrdered ? new SortedSet<int>(thisChain!) : null;
            }
            else
            {
                if (end > runEnd) runEnd = end;
                hi = Math.Max(hi, j); peak = Math.Max(peak, cnt);
                if (chain is not null) chain.UnionWith(thisChain!);
            }
        }
        Flush();
    }

    /// <summary>The result for a hit: the first supporting event carries the evidence id and locator, <c>Match</c> lists every supporting reference.</summary>
    public static DetectionResult ToResult(SigmaCorrelationHit h)
    {
        var c = h.Correlation;
        var first = h.Events[0];
        var group = h.Group.Count == 0 ? "fără grupare" : string.Join(", ", h.Group.Select(g => $"{g.Field}={(g.Value.Length == 0 ? "(gol)" : g.Value)}"));
        var refs = string.Join("; ", h.Events.Select(e => $"{e.EvidenceId}|{e.Locator}"));
        var countText = c.Type switch
        {
            SigmaCorrelationType.EventCount => $"{h.Count} evenimente în fereastră",
            SigmaCorrelationType.ValueCount => $"{h.Count} valori distincte de {c.Field}",
            _ => $"{h.SupportingTotal} evenimente care susțin corelarea",
        };
        var match = $"corelare {TypeName(c.Type)} [{group}]: {countText}; fereastra {h.WindowStart:yyyy-MM-dd HH:mm:ss}Z – {h.WindowEnd:yyyy-MM-dd HH:mm:ss}Z; "
                  + $"{h.SupportingTotal} evenimente: {refs}" + (h.Truncated ? $" (… încă {h.SupportingTotal - h.Events.Count}, listă tăiată la {MaxRefs})" : "");
        return new DetectionResult(c.Identity.RuleId, c.Identity.Version, c.Identity.Sha256, DetectionKind.Correlation, c.Title,
            first.EvidenceId, first.Locator, first.SourceSha256, match, Classification.Correlated, c.Level is "high" or "critical" ? Confidence.High : Confidence.Medium,
            $"Corelare Sigma „{c.Title}” ({TypeName(c.Type)}, interval {c.Timespan}, reguli: {string.Join(", ", c.RuleRefs)}). Corelația în timp nu dovedește cauzalitate. {c.Description}".TrimEnd(), h.WindowStart);
    }

    public static string TypeName(SigmaCorrelationType t) => t switch
    {
        SigmaCorrelationType.EventCount => "event_count", SigmaCorrelationType.ValueCount => "value_count",
        SigmaCorrelationType.Temporal => "temporal", _ => "temporal_ordered",
    };
}
