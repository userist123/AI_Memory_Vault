using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Detection;

/// <summary>
/// A documented subset of the YARA rule language, implemented here (not libyara). Supported: <c>rule name [: tags] { meta:
/// strings: condition: }</c>; text strings with ascii / wide / nocase / fullword; hex strings with <c>??</c> and jumps
/// <c>[n-m]</c>; regular expressions <c>/…/</c> (evaluated with .NET regex over the bytes as Latin-1, optional nocase);
/// conditions with <c>$a</c>, <c>#a op N</c>, <c>any|all|N of them</c>, <c>any|all|N of ($p*)</c>, <c>filesize op N[KB|MB]</c>,
/// <c>and</c>, <c>or</c>, <c>not</c>, parentheses. Anything else is a parse error, never silently ignored. Every string pattern is
/// compiled with a match timeout; a rule whose pattern times out on a file is not evaluated on it and the timeout is reported
/// (<see cref="RuleTimeout"/>), never counted as a match or as a clean file.
/// </summary>
public sealed class YaraLite
{
    public sealed record YaraString(string Id, Regex Pattern);
    public sealed record YaraRule(string Name, IReadOnlyList<string> Tags, IReadOnlyDictionary<string, string> Meta,
                                  IReadOnlyList<YaraString> Strings, string Condition, DetectionRule Identity);
    public sealed record YaraMatch(YaraRule Rule, IReadOnlyDictionary<string, int> Counts, IReadOnlyDictionary<string, long> FirstOffsets);

    /// <summary>Per-pattern match timeout. Longer than for Sigma: a pattern is run over the whole content of a file (up to 256 MB).</summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(10);

    public IReadOnlyList<YaraRule> Rules { get; }
    public string Source { get; }

    private YaraLite(IReadOnlyList<YaraRule> rules, string source) => (Rules, Source) = (rules, source);

    public static YaraLite Parse(string text, string source = "inline", TimeSpan? matchTimeout = null)
    {
        var timeout = matchTimeout ?? DefaultMatchTimeout;
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var clean = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        clean = Regex.Replace(clean, @"(?m)^\s*//.*$", "");
        var rules = new List<YaraRule>();
        var header = new Regex(@"\brule\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([A-Za-z0-9_ ]+?))?\s*\{");
        int pos = 0;
        while (header.Match(clean, pos) is { Success: true } m)
        {
            int depth = 1, i = m.Index + m.Length;
            for (; i < clean.Length && depth > 0; i++)
            {
                if (clean[i] == '"') { i++; while (i < clean.Length && clean[i] != '"') { if (clean[i] == '\\') i++; i++; } }
                else if (clean[i] == '{') depth++;
                else if (clean[i] == '}') depth--;
            }
            if (depth != 0) throw new FormatException($"Regula {m.Groups[1].Value}: acoladă neînchisă.");
            var body = clean[(m.Index + m.Length)..(i - 1)];
            rules.Add(ParseRule(m.Groups[1].Value, m.Groups[2].Value, body, sha, source, timeout));
            pos = i;
        }
        if (rules.Count == 0) throw new FormatException("Nicio regulă YARA în text.");
        return new YaraLite(rules, source);
    }

    private static YaraRule ParseRule(string name, string tags, string body, string fileSha, string source, TimeSpan timeout)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var strings = new List<YaraString>();
        string condition = "";
        foreach (var (section, content) in Sections(name, body))
            switch (section)
            {
                case "meta":
                    foreach (Match mm in Regex.Matches(content, @"(\w+)\s*=\s*(""(?:[^""\\]|\\.)*""|\S+)"))
                        meta[mm.Groups[1].Value] = mm.Groups[2].Value.Trim('"');
                    break;
                case "strings":
                    foreach (var def in StringDefinitions(name, content)) strings.Add(ParseString(name, def, timeout));
                    break;
                case "condition":
                    condition = content.Trim();
                    break;
            }
        if (condition.Length == 0) throw new FormatException($"Regula {name} nu are condiție.");
        var version = meta.GetValueOrDefault("version", fileSha[..12]);
        var identity = new DetectionRule($"YARA:{name}", version, DetectionKind.Yara, meta.GetValueOrDefault("description", name), fileSha, source,
            meta.GetValueOrDefault("mitre", ""), meta.GetValueOrDefault("severity", ""));
        var rule = new YaraRule(name, tags.Split(' ', StringSplitOptions.RemoveEmptyEntries), meta, strings, condition, identity);
        _ = Evaluate(rule, new Dictionary<string, int>(), 0);      // validates the condition syntax now, not at scan time
        return rule;
    }

    /// <summary>Splits a rule body at meta:/strings:/condition: found outside string, hex and regex literals.</summary>
    private static List<(string Section, string Content)> Sections(string rule, string body)
    {
        var cuts = new List<(int At, int ContentStart, string Name)>();
        for (int i = 0; i < body.Length; i++)
        {
            i = SkipLiteral(rule, body, i);
            if (i >= body.Length) break;
            foreach (var kw in new[] { "meta", "strings", "condition" })
                if (string.CompareOrdinal(body, i, kw, 0, kw.Length) == 0 && (i == 0 || !char.IsLetterOrDigit(body[i - 1]) && body[i - 1] != '_'))
                {
                    int j = i + kw.Length;
                    while (j < body.Length && char.IsWhiteSpace(body[j])) j++;
                    if (j < body.Length && body[j] == ':') { cuts.Add((i, j + 1, kw)); i = j; break; }
                }
        }
        return cuts.Select((c, k) => (c.Name, body[c.ContentStart..(k + 1 < cuts.Count ? cuts[k + 1].At : body.Length)])).ToList();
    }

    /// <summary>Index of the last char of a literal starting at i ("…", {…} or /…/ after '='), or i itself.</summary>
    private static int SkipLiteral(string rule, string s, int i)
    {
        char c = s[i];
        char close = c switch { '"' => '"', '{' => '}', '/' when PrevNonSpace(s, i) == '=' => '/', _ => '\0' };
        if (close == '\0') return i;
        for (int j = i + 1; j < s.Length; j++)
        {
            if (s[j] == '\\' && close != '}') { j++; continue; }
            if (s[j] == close) return j;
        }
        throw new FormatException($"Regula {rule}: literal neînchis ({c}).");
    }

    private static char PrevNonSpace(string s, int i)
    {
        for (int j = i - 1; j >= 0; j--) if (!char.IsWhiteSpace(s[j])) return s[j];
        return '\0';
    }

    /// <summary>"$a = "x" nocase $b = { 4D } …" → one definition per string, whatever the line layout.</summary>
    private static IEnumerable<string> StringDefinitions(string rule, string content)
    {
        int i = 0;
        while (true)
        {
            while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            if (i >= content.Length) yield break;
            if (content[i] != '$') throw new FormatException($"Regula {rule}: în strings se aștepta „$”, nu „{content[i]}”.");
            int start = i;
            int eq = content.IndexOf('=', i);
            if (eq < 0) throw new FormatException($"Regula {rule}: lipsește „=” în strings.");
            i = eq + 1;
            while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            if (i >= content.Length) throw new FormatException($"Regula {rule}: șir fără valoare.");
            i = SkipLiteral(rule, content, i) + 1;
            // modifiers: words up to the next "$" (start of the next definition)
            while (i < content.Length && content[i] != '$') i++;
            yield return content[start..i].Trim();
        }
    }

    private static YaraString ParseString(string rule, string line, TimeSpan timeout)
    {
        var m = Regex.Match(line, @"^(\$[A-Za-z0-9_]*)\s*=\s*(.+)$");
        if (!m.Success) throw new FormatException($"Regula {rule}: șir invalid „{line}”.");
        var id = m.Groups[1].Value;
        var def = m.Groups[2].Value.Trim();
        if (def.StartsWith('"'))
        {
            int end = def.IndexOf('"', 1);
            while (end > 0 && def[end - 1] == '\\') end = def.IndexOf('"', end + 1);
            if (end < 0) throw new FormatException($"Regula {rule}: șirul {id} nu e închis.");
            var literal = Regex.Unescape(def[1..end]);
            var mods = def[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x.ToLowerInvariant()).ToHashSet();
            if (mods.Except(["ascii", "wide", "nocase", "fullword"]).FirstOrDefault() is { } bad) throw new FormatException($"Regula {rule}: modificator neacceptat „{bad}”.");
            var forms = new List<string>();
            if (!mods.Contains("wide") || mods.Contains("ascii")) forms.Add(Latin1(Encoding.UTF8.GetBytes(literal)));
            if (mods.Contains("wide")) forms.Add(Latin1(Encoding.Unicode.GetBytes(literal)));
            var alt = string.Join("|", forms.Select(Regex.Escape));
            if (mods.Contains("fullword")) alt = $@"(?<![A-Za-z0-9_])(?:{alt})(?![A-Za-z0-9_])";
            return new YaraString(id, RuleRegex.Create(rule, $"șirul {id}", alt, Options(mods.Contains("nocase")), timeout));
        }
        if (def.StartsWith('{'))
        {
            var hex = def.Trim('{', '}', ' ');
            var sb = new StringBuilder();
            foreach (Match t in Regex.Matches(hex, @"\[\s*(\d+)\s*-\s*(\d+)\s*\]|\[\s*(\d+)\s*\]|\?\?|[0-9A-Fa-f]{2}|\S"))
            {
                if (t.Value == "??") sb.Append("[\\x00-\\xFF]");
                else if (t.Groups[1].Success) sb.Append($"[\\x00-\\xFF]{{{t.Groups[1].Value},{t.Groups[2].Value}}}");
                else if (t.Groups[3].Success) sb.Append($"[\\x00-\\xFF]{{{t.Groups[3].Value}}}");
                else if (t.Value.Length == 2 && Uri.IsHexDigit(t.Value[0]) && Uri.IsHexDigit(t.Value[1])) sb.Append($"\\x{t.Value.ToUpperInvariant()}");
                else throw new FormatException($"Regula {rule}: element hex neacceptat „{t.Value}” în {id}.");
            }
            return new YaraString(id, RuleRegex.Create(rule, $"șirul hex {id}", sb.ToString(), Options(false), timeout));
        }
        if (def.StartsWith('/'))
        {
            int end = def.LastIndexOf('/');
            if (end <= 0) throw new FormatException($"Regula {rule}: regex neînchis în {id}.");
            var mods = def[(end + 1)..].Trim();
            return new YaraString(id, RuleRegex.Create(rule, $"regex-ul {id}", def[1..end], Options(mods.Contains("nocase") || mods.Contains('i')), timeout));
        }
        throw new FormatException($"Regula {rule}: tip de șir necunoscut pentru {id}.");
    }

    private static RegexOptions Options(bool nocase) =>
        RegexOptions.CultureInvariant | RegexOptions.Singleline | (nocase ? RegexOptions.IgnoreCase : RegexOptions.None);

    private static string Latin1(byte[] b) => Encoding.Latin1.GetString(b);

    /// <summary>Scans content (bytes read as Latin-1, one char per byte) and returns the rules whose condition holds.</summary>
    /// <param name="data">The content to scan.</param>
    /// <param name="timeouts">Receives a <see cref="RuleTimeout"/> for each rule that did not finish. When null, a timeout throws
    /// <see cref="RuleEvaluationTimeoutException"/> instead of being dropped.</param>
    public IReadOnlyList<YaraMatch> Scan(byte[] data, ICollection<RuleTimeout>? timeouts = null, string evidenceId = "")
    {
        var text = Latin1(data);
        var result = new List<YaraMatch>();
        foreach (var rule in Rules)
        {
            var counts = new Dictionary<string, int>();
            var first = new Dictionary<string, long>();
            try
            {
                foreach (var s in rule.Strings)
                {
                    var ms = s.Pattern.Matches(text);
                    counts[s.Id] = ms.Count;
                    if (ms.Count > 0) first[s.Id] = ms[0].Index;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                var timeout = new RuleTimeout(rule.Identity.RuleId, rule.Identity.Title,
                    $"un șir al regulii {rule.Name} a depășit {ex.MatchTimeout.TotalSeconds:0.##} s pe {data.LongLength:N0} octeți; regula nu a fost evaluată pe acest conținut", evidenceId, "file");
                if (timeouts is null) throw new RuleEvaluationTimeoutException(timeout, ex);
                timeouts.Add(timeout);
                continue;                                                  // neither a match nor a clean result for this rule
            }
            if (Evaluate(rule, counts, data.LongLength)) result.Add(new YaraMatch(rule, counts, first));
        }
        return result;
    }

    // ------------------------------------------------------------------ condition

    private static bool Evaluate(YaraRule rule, IReadOnlyDictionary<string, int> counts, long filesize)
    {
        var tokens = Regex.Matches(rule.Condition, @"\(\s*\$[A-Za-z0-9_]*\*?\s*(?:,\s*\$[A-Za-z0-9_]*\*?\s*)*\)|#[A-Za-z0-9_]+|\$[A-Za-z0-9_]*|\d+(?:KB|MB)?|>=|<=|==|!=|[<>()]|[A-Za-z_]+")
            .Select(t => t.Value).ToList();
        int p = 0;
        string Peek() => p < tokens.Count ? tokens[p] : "";
        string Next() => p < tokens.Count ? tokens[p++] : throw new FormatException($"Regula {rule.Name}: condiție incompletă.");
        int Count(string id) => counts.TryGetValue(id, out var c) ? c
            : rule.Strings.Any(s => s.Id == id) ? 0 : throw new FormatException($"Regula {rule.Name}: șirul {id} nu este definit.");
        long Number(string t) => t.EndsWith("KB") ? long.Parse(t[..^2], CultureInfo.InvariantCulture) * 1024
                               : t.EndsWith("MB") ? long.Parse(t[..^2], CultureInfo.InvariantCulture) * 1024 * 1024
                               : long.Parse(t, CultureInfo.InvariantCulture);
        bool Compare(long a, string op, long b) => op switch
        {
            ">" => a > b, "<" => a < b, ">=" => a >= b, "<=" => a <= b, "==" => a == b, "!=" => a != b,
            _ => throw new FormatException($"Regula {rule.Name}: operator „{op}”."),
        };
        bool Or()
        {
            var v = And();
            while (Peek() == "or") { Next(); var r = And(); v = v || r; }
            return v;
        }
        bool And()
        {
            var v = Not();
            while (Peek() == "and") { Next(); var r = Not(); v = v && r; }
            return v;
        }
        bool Not()
        {
            if (Peek() == "not") { Next(); return !Not(); }
            return Primary();
        }
        bool Primary()
        {
            var t = Next();
            if (t == "(") { var v = Or(); if (Next() != ")") throw new FormatException($"Regula {rule.Name}: paranteză neînchisă."); return v; }
            if (t.StartsWith('$')) return Count(t) > 0;
            if (t.StartsWith('#')) { var op = Next(); return Compare(Count("$" + t[1..]), op, Number(Next())); }
            if (t == "filesize") { var op = Next(); return Compare(filesize, op, Number(Next())); }
            if (t is "any" or "all" || char.IsDigit(t[0]))
            {
                if (Next() != "of") throw new FormatException($"Regula {rule.Name}: lipsește „of”.");
                var set = Next();
                var ids = set == "them" ? rule.Strings.Select(s => s.Id).ToList()
                        : set.Trim('(', ')').Split(',').Select(x => x.Trim())
                             .SelectMany(x => x.EndsWith('*') ? rule.Strings.Where(s => s.Id.StartsWith(x[..^1], StringComparison.Ordinal)).Select(s => s.Id) : [x]).ToList();
                if (ids.Count == 0) throw new FormatException($"Regula {rule.Name}: mulțime goală în „{set}”.");
                int hit = ids.Count(id => Count(id) > 0);
                return t == "any" ? hit > 0 : t == "all" ? hit == ids.Count : hit >= int.Parse(t, CultureInfo.InvariantCulture);
            }
            if (t is "true") return true;
            if (t is "false") return false;
            throw new FormatException($"Regula {rule.Name}: element de condiție neacceptat „{t}”.");
        }
        var result = Or();
        if (p != tokens.Count) throw new FormatException($"Regula {rule.Name}: text în plus după condiție („{tokens[p]}”).");
        return result;
    }
}
