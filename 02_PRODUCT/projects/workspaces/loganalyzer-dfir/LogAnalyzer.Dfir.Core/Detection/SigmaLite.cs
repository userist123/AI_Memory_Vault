using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using YamlDotNet.RepresentationModel;

namespace LogAnalyzer.Dfir.Detection;

/// <summary>
/// A documented subset of Sigma (implemented here, not pySigma) evaluated directly on timeline events. Supported:
/// logsource product windows with service security | system | application | powershell | windefend | taskscheduler |
/// sysmon | firewall; detection with selections as a map (AND of fields, list values OR) or a list of maps (OR);
/// field modifiers contains, startswith, endswith, all, re; condition with selection names, and, or, not, parentheses,
/// "1 of name*", "all of name*", "1 of them", "all of them". Field names are the event's EventData names; EventID and
/// Provider_Name map to the event header. Strings compare case-insensitively, as in Sigma. Anything else is a load
/// error, never ignored. Every <c>|re</c> pattern is compiled at load (an invalid one is a load error) with a match timeout:
/// a pattern that times out on an event is recorded as a <see cref="RuleTimeout"/>, the rule is not evaluated on that event
/// (never a match, and never a silent "no match"), and it is stopped for the rest of that run.
/// </summary>
public sealed class SigmaLite
{
    private static readonly Dictionary<string, string> Channels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["security"] = "Security", ["system"] = "System", ["application"] = "Application",
        ["powershell"] = "Microsoft-Windows-PowerShell/Operational", ["windefend"] = "Microsoft-Windows-Windows Defender/Operational",
        ["taskscheduler"] = "Microsoft-Windows-TaskScheduler/Operational", ["sysmon"] = "Microsoft-Windows-Sysmon/Operational",
        ["firewall"] = "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall",
    };

    /// <summary>Per-pattern match timeout of <c>|re</c> field tests (a field value is short; this is generous for a sane pattern).</summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(2);

    private sealed record FieldTest(string Field, string Modifier, bool All, IReadOnlyList<string> Values, IReadOnlyList<Regex> Patterns);
    private sealed record Selection(string Name, IReadOnlyList<IReadOnlyList<FieldTest>> Alternatives);   // OR of ANDs

    public sealed record SigmaRule(string Title, string Id, string Level, string Channel, string Condition,
                                   IReadOnlyList<string> Tags, DetectionRule Identity, string Description);

    private readonly List<(SigmaRule Rule, Dictionary<string, Selection> Selections)> _rules = [];
    private readonly TimeSpan _matchTimeout;

    private SigmaLite(TimeSpan matchTimeout) => _matchTimeout = matchTimeout;
    public IReadOnlyList<SigmaRule> Rules => _rules.Select(r => r.Rule).ToList();

    public static SigmaLite Load(params (string Name, string Yaml)[] documents) => Load(DefaultMatchTimeout, documents);

    public static SigmaLite Load(TimeSpan matchTimeout, params (string Name, string Yaml)[] documents)
    {
        var s = new SigmaLite(matchTimeout);
        foreach (var (name, yaml) in documents) s.Add(name, yaml);
        return s;
    }

    public static SigmaLite LoadDirectory(string dir) =>
        Load(Directory.GetFiles(dir, "*.yml").Concat(Directory.GetFiles(dir, "*.yaml")).Order(StringComparer.Ordinal)
             .Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToArray());

    private void Add(string name, string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root) throw new FormatException($"{name}: nu este o regulă Sigma.");
        string S(YamlMappingNode m, string k) => m.Children.TryGetValue(new YamlScalarNode(k), out var v) && v is YamlScalarNode sc ? sc.Value ?? "" : "";
        var title = S(root, "title");
        if (title.Length == 0) throw new FormatException($"{name}: lipsește title.");
        if (!root.Children.TryGetValue(new YamlScalarNode("logsource"), out var lsNode) || lsNode is not YamlMappingNode ls) throw new FormatException($"{name}: lipsește logsource.");
        if (S(ls, "product") is var product && product.Length > 0 && !product.Equals("windows", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"{name}: product „{product}” nu este acceptat.");
        if (S(ls, "category").Length > 0) throw new FormatException($"{name}: logsource.category nu este acceptat (doar service).");
        if (!Channels.TryGetValue(S(ls, "service"), out var channel)) throw new FormatException($"{name}: service „{S(ls, "service")}” nu este acceptat.");
        if (!root.Children.TryGetValue(new YamlScalarNode("detection"), out var detNode) || detNode is not YamlMappingNode det) throw new FormatException($"{name}: lipsește detection.");

        var selections = new Dictionary<string, Selection>(StringComparer.Ordinal);
        string condition = "";
        foreach (var (k, v) in det.Children)
        {
            var key = ((YamlScalarNode)k).Value!;
            if (key == "condition") { condition = v is YamlScalarNode c ? c.Value ?? "" : throw new FormatException($"{name}: condiția trebuie să fie un text."); continue; }
            if (key == "timeframe") throw new FormatException($"{name}: timeframe nu este acceptat.");
            selections[key] = new Selection(key, v switch
            {
                YamlMappingNode m => [Fields(name, m)],
                YamlSequenceNode seq => seq.Children.Select(x => x is YamlMappingNode mm ? Fields(name, mm) : throw new FormatException($"{name}: selecția {key} conține un element care nu e hartă.")).ToList(),
                _ => throw new FormatException($"{name}: selecția {key} are o formă neacceptată."),
            });
        }
        if (condition.Length == 0) throw new FormatException($"{name}: lipsește condition.");
        var tags = root.Children.TryGetValue(new YamlScalarNode("tags"), out var t) && t is YamlSequenceNode ts ? ts.Children.Select(x => ((YamlScalarNode)x).Value ?? "").ToList() : [];
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml)));
        var mitre = tags.FirstOrDefault(x => Regex.IsMatch(x, @"^attack\.t\d{4}", RegexOptions.IgnoreCase))?[7..].ToUpperInvariant() ?? "";
        var rule = new SigmaRule(title, S(root, "id"), S(root, "level"), channel, condition, tags,
            new DetectionRule($"SIGMA:{(S(root, "id") is { Length: > 0 } id ? id : name)}", S(root, "modified") is { Length: > 0 } mod ? mod : S(root, "date") is { Length: > 0 } d ? d : sha[..12],
                DetectionKind.Sigma, title, sha, name, mitre, S(root, "level")), S(root, "description"));
        var entry = (rule, selections);
        _ = Evaluate(entry, null);                                     // condition syntax checked at load time
        _rules.Add(entry);
    }

    private List<FieldTest> Fields(string rule, YamlMappingNode m) => m.Children.Select(kv =>
    {
        var parts = ((YamlScalarNode)kv.Key).Value!.Split('|');
        var mods = parts.Skip(1).Select(p => p.ToLowerInvariant()).ToList();
        if (mods.Except(["contains", "startswith", "endswith", "all", "re"]).FirstOrDefault() is { } bad) throw new FormatException($"{rule}: modificatorul „{bad}” nu este acceptat.");
        var values = kv.Value switch
        {
            YamlScalarNode s => [s.Value ?? ""],
            YamlSequenceNode seq => seq.Children.Select(x => ((YamlScalarNode)x).Value ?? "").ToList(),
            _ => throw new FormatException($"{rule}: valoare neacceptată pentru {parts[0]}."),
        };
        var modifier = mods.FirstOrDefault(x => x != "all") ?? "equals";
        var patterns = modifier == "re" ? values.Select(v => RuleRegex.Create(rule, $"câmpul {parts[0]}", v, RegexOptions.None, _matchTimeout)).ToList() : [];
        return new FieldTest(parts[0], modifier, mods.Contains("all"), values, patterns);
    }).ToList();

    /// <param name="events">Timeline events to test.</param>
    /// <param name="timeouts">Receives each regex timeout. When null, a timeout throws <see cref="RuleEvaluationTimeoutException"/> instead of being dropped.</param>
    public IEnumerable<(SigmaRule Rule, TimelineEvent Event)> Match(IEnumerable<TimelineEvent> events, ICollection<RuleTimeout>? timeouts = null)
    {
        var byChannel = _rules.ToLookup(r => "EventLog:" + r.Rule.Channel, StringComparer.OrdinalIgnoreCase);
        var stopped = new Dictionary<string, (SigmaRule Rule, int Skipped)>(StringComparer.Ordinal);   // rules that timed out in this run
        foreach (var e in events)
            foreach (var r in byChannel[e.Source])
            {
                var ruleKey = r.Rule.Identity.RuleId + "|" + r.Rule.Identity.Sha256;
                if (stopped.TryGetValue(ruleKey, out var st)) { stopped[ruleKey] = (st.Rule, st.Skipped + 1); continue; }
                bool hit;
                try { hit = Evaluate(r, e); }
                catch (RegexMatchTimeoutException ex)
                {
                    var timeout = new RuleTimeout(r.Rule.Identity.RuleId, r.Rule.Title,
                        $"expresia regulată a depășit {ex.MatchTimeout.TotalSeconds:0.##} s pe evenimentul {e.EventId}; regula nu a fost evaluată pe acesta și este oprită pentru restul evenimentelor",
                        e.EvidenceId, e.Locator);
                    if (timeouts is null) throw new RuleEvaluationTimeoutException(timeout, ex);
                    timeouts.Add(timeout);
                    stopped.Add(ruleKey, (r.Rule, 0));
                    continue;
                }
                if (hit) yield return (r.Rule, e);
            }
        foreach (var (rule, skipped) in stopped.Values.Where(v => v.Skipped > 0))      // what the stop cost: events never evaluated
            timeouts!.Add(new RuleTimeout(rule.Identity.RuleId, rule.Title, $"{skipped} evenimente ulterioare nu au fost evaluate cu această regulă după timeout"));
    }

    public IEnumerable<DetectionResult> Detect(IEnumerable<TimelineEvent> events, ICollection<RuleTimeout>? timeouts = null) => Match(events, timeouts).Select(m => new DetectionResult(
        m.Rule.Identity.RuleId, m.Rule.Identity.Version, m.Rule.Identity.Sha256, DetectionKind.Sigma, m.Rule.Title,
        m.Event.EvidenceId, m.Event.Locator, m.Event.SourceSha256, $"{m.Event.Source} {m.Event.EventId}: {m.Event.Summary}",
        Classification.Direct, m.Rule.Level is "high" or "critical" ? Confidence.High : Confidence.Medium,
        $"Evenimentul îndeplinește regula Sigma „{m.Rule.Title}”. {m.Rule.Description}", m.Event.Time.Utc));

    private static string Value(TimelineEvent e, string field) => field switch
    {
        "EventID" => e.EventId,
        "Provider_Name" => e.Provider,
        _ => e.Fields.TryGetValue(field, out var v) ? v : "",
    };

    private static bool Test(TimelineEvent e, FieldTest t)
    {
        var actual = Value(e, t.Field);
        bool One(int i)
        {
            var expected = t.Values[i];
            return t.Modifier switch
            {
                "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
                "startswith" => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
                "endswith" => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
                "re" => t.Patterns[i].IsMatch(actual),                          // may throw RegexMatchTimeoutException: the whole rule is then not evaluated
                _ => actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
            };
        }
        var indexes = Enumerable.Range(0, t.Values.Count);
        return t.All ? indexes.All(One) : indexes.Any(One);
    }

    /// <summary>With a null event only the condition syntax is checked.</summary>
    private static bool Evaluate((SigmaRule Rule, Dictionary<string, Selection> Selections) r, TimelineEvent? e)
    {
        var tokens = Regex.Matches(r.Rule.Condition, @"\(|\)|[A-Za-z0-9_\*]+").Select(m => m.Value).ToList();
        int p = 0;
        string Peek() => p < tokens.Count ? tokens[p] : "";
        string Next() => p < tokens.Count ? tokens[p++] : throw new FormatException($"{r.Rule.Title}: condiție incompletă.");
        bool Sel(string name)
        {
            if (!r.Selections.TryGetValue(name, out var s)) throw new FormatException($"{r.Rule.Title}: selecția „{name}” nu există.");
            return e is not null && s.Alternatives.Any(and => and.All(t => Test(e, t)));
        }
        bool Or() { var v = And(); while (Peek() == "or") { Next(); var x = And(); v = v || x; } return v; }
        bool And() { var v = Not(); while (Peek() == "and") { Next(); var x = Not(); v = v && x; } return v; }
        bool Not() { if (Peek() == "not") { Next(); return !Not(); } return Primary(); }
        bool Primary()
        {
            var t = Next();
            if (t == "(") { var v = Or(); if (Next() != ")") throw new FormatException($"{r.Rule.Title}: paranteză neînchisă."); return v; }
            if (t is "1" or "all" && Peek() == "of")
            {
                Next();
                var target = Next();
                var names = target == "them" ? r.Selections.Keys.ToList()
                          : target.EndsWith('*') ? r.Selections.Keys.Where(k => k.StartsWith(target[..^1], StringComparison.Ordinal)).ToList()
                          : [target];
                if (names.Count == 0) throw new FormatException($"{r.Rule.Title}: „{target}” nu corespunde niciunei selecții.");
                return t == "1" ? names.Any(Sel) : names.All(Sel);
            }
            return Sel(t);
        }
        var result = Or();
        if (p != tokens.Count) throw new FormatException($"{r.Rule.Title}: text în plus în condiție („{tokens[p]}”).");
        return result;
    }
}
