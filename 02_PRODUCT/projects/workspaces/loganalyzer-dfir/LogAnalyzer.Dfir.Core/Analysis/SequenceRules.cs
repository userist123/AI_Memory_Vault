using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>What the sequence rules compare the run with: the case scope (classified or not, the period) and, optionally, their own data.</summary>
/// <remarks>There is deliberately no field for the signed-in application user: that is the examiner, not the subject. The "who" of a sequence is read from the evidence.</remarks>
public sealed class SequenceInput
{
    public CaseScope Scope { get; init; } = new();
    public SequenceData? Data { get; init; }
}

/// <summary>
/// WP14b: three combined sequences over findings and rows the run already has. They reuse POLICY-CONTROL-GAP, MEDIA-* / MEDIA-FILE-ACTIVITY, the Security 5140 / 5145 / 4663
/// rows and the Prefetch / BAM / Amcache rows; they add no parser and no second detection of those facts. The generic INCIDENT-CHAIN stays.
/// <list type="bullet">
/// <item>SEQ-CONTROL-GAP-MEDIA: a control gap (USB, device install, audit, Defender), then a removable medium seen during the gap or shortly after it.</item>
/// <item>SEQ-SMB-STAGING-USB: remote share access, local staging, write to a removable medium.</item>
/// <item>SEQ-PORTABLE-USB-ARCHIVE: portable software first seen in the period, an archive, and writes to a removable medium.</item>
/// </list>
/// Every sequence says what was observed, in time order, and which link is missing. A step whose source was not collected is "pas neobservat", never absence; a sequence
/// with fewer observed steps than its minimum (data) is not reported; none of them asserts purpose or a transfer of content.
/// </summary>
public static partial class SequenceRules
{
    public const string Category = "Combined sequences";

    public static List<Finding> Run(IReadOnlyList<TimelineEvent> events, IReadOnlyList<Finding> existing, Func<string> nextId, SequenceInput? input = null)
    {
        input ??= new SequenceInput();
        var c = new Ctx(events, existing, nextId, input, input.Data ?? SequenceData.Default);
        var found = new List<Finding>();
        ControlGapMedia(c, found);
        SmbStagingUsb(c, found);
        PortableUsbArchive(c, found);
        return found;
    }

    internal sealed class Ctx
    {
        public Ctx(IReadOnlyList<TimelineEvent> events, IReadOnlyList<Finding> existing, Func<string> nextId, SequenceInput input, SequenceData data)
        {
            Events = events; Existing = existing; NextId = nextId; Input = input; Data = data;
            Sources = events.Select(e => e.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var e in events) ByRef.TryAdd((e.EvidenceId, e.Locator), e);
        }
        public IReadOnlyList<TimelineEvent> Events { get; }
        public IReadOnlyList<Finding> Existing { get; }
        public Func<string> NextId { get; }
        public SequenceInput Input { get; }
        public SequenceData Data { get; }
        public CaseScope Scope => Input.Scope;
        public bool Classified => Scope.Classification == ClassificationLevel.Classified;
        public HashSet<string> Sources { get; }
        public Dictionary<(string, string), TimelineEvent> ByRef { get; } = [];
        public bool Has(params string[] sources) => sources.Any(Sources.Contains);

        /// <summary>The severity on a classified scope, one level lower on any other (the WP14a convention).</summary>
        public Severity Sev(Severity classified) => Classified ? classified : classified > Severity.Info ? classified - 1 : Severity.Info;
    }

    internal const string SecuritySource = "EventLog:Security";
    internal const string SysmonSource = "EventLog:" + Sysmon;

    // ---- helpers ----

    private static string Leaf(string path) => WinPath.GetFileName(path.TrimEnd('\\'));
    private static string Low(string s) => s.Trim().ToLowerInvariant();
    private static string LetterOfPath(string p) => p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':' ? char.ToUpperInvariant(p[0]).ToString() : "";
    private static bool IsUnc(string p) => p.StartsWith(@"\\", StringComparison.Ordinal);
    private static TimeSpan Min(int minutes) => TimeSpan.FromMinutes(minutes);
    private static string Stamp(DateTimeOffset? t) => SequenceEngine.Stamp(t);

    private static string SubjectAccount(TimelineEvent e)
    {
        var name = F(e, "SubjectUserName");
        return name.Length > 0 && name != "-" && !IsMachineAccount(name) ? Account(name, F(e, "SubjectDomainName")) : "";
    }

    private static string SizeText(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.#} MB" : $"{bytes} B";

    /// <summary>The serial a MEDIA-* finding or a MEDIA-FILE-ACTIVITY finding is about ("mediu SERIAL …"); "" when the medium has none.</summary>
    private static string SerialOf(Finding f)
    {
        var m = Regex.Match(f.AirGap?.ObjectName ?? "", @"^mediu (?<s>\S+)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        return m.Success && m.Groups["s"].Value != "fără" ? m.Groups["s"].Value : "";
    }

    private static bool IsMediaPresence(Finding f) =>
        f.RuleId is "MEDIA-AUTHORIZED" or "MEDIA-REGISTERED" or "MEDIA-UNREGISTERED" or "MEDIA-UNAUTHORIZED" or "MEDIA-UNKNOWN";

    private static bool IsWrite(Finding f) => f.AirGap?.TransferDirection.StartsWith("scriere", StringComparison.Ordinal) == true;

    private static DateTimeOffset? TimeOfRef(Ctx c, EvidenceRef r) => c.ByRef.TryGetValue((r.EvidenceId, r.Locator), out var e) ? T(e) : null;

    /// <summary>Builds the finding: facts in time order, the missing links, the constituent findings, and the honesty lines every sequence carries.</summary>
    private static Finding Build(Ctx c, string ruleId, string title, Severity severity, Confidence confidence, IReadOnlyList<SeqStep> declared, string window, string link,
        string rule, IEnumerable<string> related, string host, IEnumerable<string> alternatives)
    {
        var ordered = SequenceEngine.Order(declared);
        var refs = ordered.SelectMany(s => s.Evidence).GroupBy(r => (r.EvidenceId, r.Locator)).Select(g => g.First()).Take(30).ToList();
        var (first, last) = SequenceEngine.Span(ordered, refs.Select(r => TimeOfRef(c, r)));
        var missing = ordered.Where(s => !s.Observed).Select(s => $"{s.Name}: {s.Note}").ToList();
        var accounts = ordered.Where(s => s.Observed).Select(s => s.Account).Where(a => a.Length > 0 && !a.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var desc = $"Secvență observată, în ordine pe axa UTC: {SequenceEngine.Describe(ordered)} Legătura dintre pași: {link} Fereastră folosită: {window}. " +
                   "Textul descrie ordinea faptelor observate în sursele colectate; nu stabilește scopul, nu dovedește conținutul transferat și nu înlocuiește constatările componente, care rămân separate.";
        return new Finding
        {
            FindingId = c.NextId(), RuleId = ruleId, Title = title, Severity = severity, Category = Category, Classification = Classification.Correlated, Confidence = confidence,
            FirstSeenUtc = first, LastSeenUtc = last, Host = host, User = string.Join(", ", accounts), Description = desc,
            ClassificationReason = $"{rule} Combină constatări și rânduri deja produse de alte reguli; ordinea e cea din timpul înregistrărilor, nu o relație cauzală dovedită.",
            SemanticType = SemanticType.Correlation,
            SupportingEvidence = refs, RelatedFindingIds = related.Where(i => i.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
            MissingEvidence = missing, AlternativeExplanations = alternatives.ToList(),
            Sequence = new SequenceDetail(window, link, SequenceEngine.Infos(ordered)),
        };
    }
}
