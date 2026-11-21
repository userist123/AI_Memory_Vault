using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.RemoteTools;

namespace LogAnalyzer.Dfir.Scripting;

/// <summary>
/// A PowerShell transcript (<c>PowerShell_transcript.&lt;HOST&gt;.&lt;random&gt;.&lt;yyyyMMddHHmmss&gt;.txt</c>), written by the PowerShell host when transcription is on
/// (policy, <c>Start-Transcript</c>). Format read from two real Windows PowerShell 5.1.26100 transcripts (lab WS02, 2026-11-18): a UTF-8 file with BOM; a header between
/// asterisk lines ("Windows PowerShell transcript start", "Start time: yyyyMMddHHmmss" in LOCAL time, Username, RunAs User, Machine, Host Application, Process ID, PSVersion...);
/// with the invocation header each command is preceded by "Command start time: ..." between asterisk lines, then "PS&gt;command"; then the output; a footer "Windows PowerShell transcript end" + "End time".
/// One session event and one event per typed command. The OUTPUT of the commands is never stored (it can hold passwords, tokens, personal data): only the count of its non-blank lines.
/// Not seen in a real transcript and read conservatively (lower confidence, noted on the event): the interactive prompt "PS &lt;path&gt;&gt; command" and the "PowerShell transcript start"
/// header of PowerShell 7.
/// </summary>
public sealed partial class PowerShellTranscriptParser : EvidenceParserBase
{
    public const string SourceTypeName = RemoteToolLogFormats.PsTranscript;
    public const string FingerprintName = RemoteToolLogFormats.PsTranscript;
    public const string EventSource = "PowerShellTranscript";
    public const long MaxFileBytes = 5L * 1024 * 1024;
    public const int MaxLines = 200_000;
    public const int MaxCommands = 5_000;
    public const int MaxSessions = 200;
    public const int MaxCommandChars = 32_768;
    private const int MaxHeaderLines = 60;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "PowerShellTranscriptParser", Version = "1.0", Artifact = "Transcriere PowerShell (PowerShell_transcript.*.txt)",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = [FingerprintName], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows cu transcrierea PowerShell pornită (politică sau Start-Transcript); text scris de gazda PowerShell, citit în cod gestionat",
        FormatVersions = ["Windows PowerShell 5.1 (build 26100), cu și fără antetul de invocare (Command start time)", "Antetul „PowerShell transcript start” (PowerShell 7) și promptul „PS cale>” nu au fost văzute într-o transcriere reală; sunt citite cu încredere mai mică"],
        Limitations =
        [
            "O transcriere arată ce comenzi s-au executat și ce s-a afișat în acea sesiune (comanda poate fi tastată în consolă sau primită de la un program, de exemplu prin powershell.exe -Command, și apare la fel ca „PS>”), înregistrat sub contul din antet; nu arată cine a lansat comanda sau cine stătea la tastatură. Fișierul poate fi modificat sau șters de cine are acces la el, deci evenimentele au încredere Medie sau Mare, nu certitudine.",
            "Ieșirea comenzilor NU este păstrată (poate conține parole, jetoane sau date personale); se păstrează doar numărul rândurilor nevide de ieșire. Comanda însăși este păstrată așa cum a fost executată și poate conține date sensibile.",
            "Orele din transcriere sunt ore locale, fără fus orar; sunt convertite în UTC doar când proba poartă fusul orar al stației, altfel rămân neconvertite, fără oră pe cronologie.",
            "Fără antetul de invocare (Command start time) comanda nu are oră proprie: se folosește ora de început a sesiunii, cu încredere Medie.",
            "Rândul „PS>$global:?” scris de PowerShell la sfârșitul sesiunii nu este o comandă executată și nu este listat (este numărat în câmpul sesiunii). Comenzile native (ex. hostname.exe) nu își scriu ieșirea în transcriere.",
            "Comenzile pe mai multe rânduri (continuare „>>”) nu au fost văzute într-o transcriere reală: rândurile de continuare sunt tratate ca ieșire.",
            "Promptul interactiv „PS cale> comandă” nu a fost văzut într-o transcriere reală; este recunoscut, dar evenimentul are încredere mai mică și o notă. Un rând de ieșire care începe ca un prompt poate fi luat drept comandă.",
            $"Cel mult {MaxFileBytes / (1024 * 1024)} MiB, {MaxLines} de rânduri, {MaxCommands} de comenzi și {MaxSessions} de sesiuni pe fișier; restul este raportat ca lacună. O sesiune fără linia de sfârșit (fișier trunchiat sau sesiune încheiată brusc) este raportată ca lacună.",
            "Se citesc doar fișierele colectate: directorul din politică (HKLM) și Documents (rădăcina și subfoldere yyyyMMdd) al fiecărui profil; un director de transcriere în rețea sau un Documents redirecționat nu sunt urmate.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "PowerShellTranscriptParserTests: două transcrieri reale Windows PowerShell 5.1 (WS02, cu și fără antet de invocare, cu BOM), variante sintetice marcate (prompt cu cale, fișier trunchiat, sesiuni multiple, ieșire neprezentă)",
    };

    // ---- format ----

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex StartLine = new(@"^(?<w>Windows )?PowerShell transcript start$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex EndLine = new(@"^(?:Windows )?PowerShell transcript end$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex CommandStartLine = new(@"^Command start time: (?<t>\d{14})$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex TimeLine = new(@"^(?<k>Start|End) time: (?<t>\d{14})$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex HeaderField = new(@"^(?<k>[A-Za-z][A-Za-z ]{0,40}):(?: (?<v>.*))?$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Prompt = new(@"^PS(?: (?<path>[^>\r\n]{1,260}))?>(?<cmd>.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline, Timeout);
    private static readonly Regex MachineLine = new(@"^(?<m>[^ ]+)(?: \(.*\))?$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex ExePath = new(@"^\s*""?(?<p>[^""]*?\.exe)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);

    /// <summary>A line of asterisks only (the transcript writes 22).</summary>
    internal static bool IsStars(string line)
    {
        if (line.Length < 10) return false;
        foreach (var c in line) if (c != '*') return false;
        return true;
    }

    /// <summary>True when the first lines are an asterisk line followed by the transcript start line (BOM tolerated). Used by the evidence fingerprint.</summary>
    internal static bool HeadLooksLikeTranscript(IReadOnlyList<string> lines)
    {
        try
        {
            for (int i = 0; i + 1 < lines.Count && i < 6; i++)
            {
                var l = lines[i].TrimStart('﻿').TrimEnd();
                if (l.Length == 0) continue;
                return IsStars(l) && StartLine.IsMatch(lines[i + 1].TrimEnd());
            }
        }
        catch (RegexMatchTimeoutException) { }
        return false;
    }

    private sealed class Cmd
    {
        public string Text = "";
        public long Line;
        public string? StartRaw;
        public string Style = "";
        public int Output;
    }

    private sealed class Session
    {
        public int Index;
        public long StartLine;
        public bool Windows = true;
        public string StartRaw = "", EndRaw = "";
        public readonly Dictionary<string, string> Header = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<Cmd> Commands = [];
        public bool Ended;
        public int PreCommandOutput;
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var file = Path.GetFileName(fullPath);
        var zone = RemoteToolLogText.ZoneOf(item);
        var now = DateTime.UtcNow;
        var lines = ReadLines(fullPath, out bool tooManyLines, ct);
        if (tooManyLines)
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxLines} de rânduri; restul nu au fost citite", "Comenzile de după limită lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));

        var sessions = new List<Session>();
        Session? cur = null;
        string? pendingTime = null;
        int stray = 0;
        int n = lines.Count;
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = lines[i];
            if (i == 0) line = line.TrimStart('﻿');
            var trimmed = line.TrimEnd();
            if (IsStars(trimmed))
            {
                var next = i + 1 < n ? lines[i + 1].TrimEnd() : null;
                if (next is not null && Safe(StartLine, next) is { Success: true } sm)
                {
                    if (cur is { Ended: false })
                        Gap(result, file, cur, "nu are linia de sfârșit și a început o altă sesiune în același fișier");
                    if (sessions.Count >= MaxSessions)
                    {
                        result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxSessions} de sesiuni în fișier; restul nu au fost citite", "Sesiunile de după limită lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                        cur = null; break;
                    }
                    cur = new Session { Index = sessions.Count + 1, StartLine = i + 2, Windows = sm.Groups["w"].Success };
                    sessions.Add(cur);
                    pendingTime = null;
                    int j = i + 2;
                    bool closed = false;
                    for (; j < n && j - i <= MaxHeaderLines; j++)
                    {
                        var h = lines[j].TrimEnd();
                        if (IsStars(h)) { closed = true; break; }
                        if (Safe(TimeLine, h) is { Success: true } tm && tm.Groups["k"].Value == "Start") { cur.StartRaw = tm.Groups["t"].Value; continue; }
                        if (Safe(HeaderField, h) is { Success: true } hm) cur.Header[hm.Groups["k"].Value.Trim()] = hm.Groups["v"].Value.Trim();
                    }
                    if (!closed)
                    {
                        result.MalformedRecords++;
                        result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, "antetul sesiunii nu se încheie cu linia de asteriscuri", "Câmpurile antetului pot fi incomplete", "Fișierul brut din caz", "Posibil"));
                    }
                    i = closed ? j : Math.Min(j, n) - 1;
                    continue;
                }
                if (cur is not null && next is not null && EndLine.IsMatch(next))
                {
                    cur.Ended = true;
                    if (i + 2 < n && Safe(TimeLine, lines[i + 2].TrimEnd()) is { Success: true } em && em.Groups["k"].Value == "End") cur.EndRaw = em.Groups["t"].Value;
                    i += 2;
                    if (i + 1 < n && IsStars(lines[i + 1].TrimEnd())) i++;
                    pendingTime = null;
                    continue;
                }
                if (cur is not null && next is not null && Safe(CommandStartLine, next) is { Success: true } cm && i + 2 < n && IsStars(lines[i + 2].TrimEnd()))
                {
                    pendingTime = cm.Groups["t"].Value;
                    i += 2;
                    continue;
                }
                // asterisks that do not open a known block: output text
            }
            if (cur is null) { if (trimmed.Length > 0) stray++; continue; }
            if (cur.Ended) { if (trimmed.Length > 0) stray++; continue; }
            if (trimmed.StartsWith("PS", StringComparison.Ordinal) && Safe(Prompt, trimmed) is { Success: true } pm)
            {
                if (sessions.Sum(s => s.Commands.Count) >= MaxCommands)
                {
                    result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxCommands} de comenzi în fișier; restul nu au fost citite", "Comenzile de după limită lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                    break;
                }
                bool pathStyle = pm.Groups["path"].Success;
                var text = pm.Groups["cmd"].Value;
                if (pathStyle && text.StartsWith(' ')) text = text[1..];
                cur.Commands.Add(new Cmd { Text = text.TrimEnd(), Line = i + 1, StartRaw = pendingTime, Style = pathStyle ? "PS cale>" : "PS>" });
                pendingTime = null;
                continue;
            }
            if (trimmed.Length > 0)
            {
                if (cur.Commands.Count > 0) cur.Commands[^1].Output++; else cur.PreCommandOutput++;
            }
        }
        if (cur is { Ended: false } && !tooManyLines)
            Gap(result, file, cur, "nu are linia de sfârșit (fișier trunchiat sau sesiune încheiată brusc)");
        if (sessions.Count == 0)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Failed, "fișierul nu conține antetul unei transcrieri PowerShell („Windows PowerShell transcript start”)",
                "Nicio sesiune și nicio comandă din acest fișier nu apare în cronologie", "Fișierul brut din caz", "Posibil"));
            return;
        }
        if (stray > 0)
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"{stray} rânduri în afara unei sesiuni (înainte de primul antet sau după un sfârșit) nu au fost interpretate", "Pot lipsi comenzi dintr-un fișier cu început tăiat sau cu conținut adăugat", "Fișierul brut din caz", "Posibil"));

        foreach (var s in sessions) Emit(item, file, zone, now, s, sink, result);
    }

    private static void Gap(ParseResult result, string file, Session s, string why) =>
        result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"sesiunea {s.Index} (început {Shown(s.StartRaw)}) {why}",
            "Comenzile de după ultimul rând citit pot lipsi din cronologie", "Fișierul brut din caz; jurnalul Microsoft-Windows-PowerShell/Operational", "Posibil"));

    private static string Shown(string raw) => raw.Length == 0 ? "necunoscut" : raw;

    private static Match? Safe(Regex rx, string s)
    {
        try { return rx.Match(s); }
        catch (RegexMatchTimeoutException) { return null; }
    }

    private static List<string> ReadLines(string path, out bool tooMany, CancellationToken ct)
    {
        tooMany = false;
        var list = new List<string>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = RemoteToolLogText.OpenReader(fs);
        var sb = new StringBuilder();
        int c;
        bool any = false;
        while (true)
        {
            sb.Clear(); any = false;
            while ((c = sr.Read()) >= 0)
            {
                any = true;
                if (c == '\n') break;
                if (c == '\r') continue;
                if (sb.Length < MaxCommandChars) sb.Append((char)c);
            }
            if (!any) break;
            if (sb.Length > 0 && char.IsHighSurrogate(sb[^1])) sb.Length--;
            list.Add(sb.ToString());
            if (list.Count >= MaxLines) { tooMany = sr.Peek() >= 0; break; }
            if ((list.Count & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
        }
        return list;
    }

    // ---- events ----

    private static (Timestamp Time, string Basis, string Uncertainty, string Problem, string LocalText) Convert(string raw, RemoteToolLogText.Zone zone, DateTime now)
    {
        if (raw.Length == 0) return (Timestamp.Unknown(), "oră locală a stației", "", "ora lipsește", "");
        if (!DateTime.TryParseExact(raw, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return (Timestamp.Unknown(raw), "oră locală a stației", "", "dată sau oră imposibilă", "");
        var lt = RemoteToolLogText.ToTimestamp(local, raw, zone, now);
        return (lt.Time, lt.Basis, lt.Uncertainty, lt.Problem, RemoteToolLogText.LocalText(local));
    }

    private void Emit(EvidenceItem item, string file, RemoteToolLogText.Zone zone, DateTime now, Session s, IEventSink sink, ParseResult result)
    {
        string H(string k) => s.Header.TryGetValue(k, out var v) ? v : "";
        var user = RemoteToolLogText.Sane(H("Username"), 128);
        var machineRaw = H("Machine");
        var machine = RemoteToolLogText.Sane(Safe(MachineLine, machineRaw) is { Success: true } mm ? mm.Groups["m"].Value : machineRaw, 128);
        var hostApp = RemoteToolLogText.Sane(H("Host Application"), 2048);
        var proc = Safe(ExePath, H("Host Application")) is { Success: true } pm ? RemoteToolLogText.Sane(Path.GetFileName(pm.Groups["p"].Value.Replace('\\', '/')), 128) : "";
        int? pid = int.TryParse(H("Process ID"), NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : null;
        var start = Convert(s.StartRaw, zone, now);
        var end = Convert(s.EndRaw, zone, now);

        // PowerShell's own "PS>$global:?" before the footer is not typed by the user
        int skipped = 0;
        var cmds = s.Commands;
        if (cmds.Count > 0 && cmds[^1].Style == "PS>" && cmds[^1].Text == "$global:?") { skipped = 1; cmds = cmds.Take(cmds.Count - 1).ToList(); }

        Dictionary<string, string> Common(string kind) => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Artifact"] = "PowerShell transcript", ["Kind"] = kind,
            ["Username"] = user, ["RunAs User"] = RemoteToolLogText.Sane(H("RunAs User"), 128), ["Configuration Name"] = RemoteToolLogText.Sane(H("Configuration Name"), 128),
            ["Machine"] = machine, ["Host Application"] = hostApp, ["Process ID"] = pid?.ToString(CultureInfo.InvariantCulture) ?? RemoteToolLogText.Sane(H("Process ID"), 16),
            ["PSVersion"] = RemoteToolLogText.Sane(H("PSVersion"), 64), ["PSEdition"] = RemoteToolLogText.Sane(H("PSEdition"), 32),
            ["Session"] = s.Index.ToString(CultureInfo.InvariantCulture), ["Session Start Raw"] = s.StartRaw,
            ["Time Zone Used"] = zone.Info?.Id ?? "necunoscut",
            ["Source File"] = RemoteToolLogText.Sane(file, 128),
        };

        var f = Common("session");
        f["Session End Raw"] = s.EndRaw;
        f["Session Ended"] = s.Ended ? "da" : "nu (fără linia de sfârșit)";
        f["Session Start Local"] = start.LocalText; f["Session End Local"] = end.LocalText;
        if (end.Time.IsKnown) f["Session End Utc"] = end.Time.Utc!.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        f["Command Count"] = cmds.Count.ToString(CultureInfo.InvariantCulture);
        f["Generated Prompt Lines Skipped"] = skipped.ToString(CultureInfo.InvariantCulture);
        f["Header Style"] = s.Windows ? "Windows PowerShell transcript start (văzut în transcrieri reale)" : "PowerShell transcript start (nevăzut într-o transcriere reală; citit cu încredere mai mică)";
        f["Output Stored"] = "nu (doar numărul rândurilor)";
        if (start.Problem.Length > 0) f["Time Problem"] = start.Problem;
        sink.Add(new TimelineEvent
        {
            Time = start.Time,
            TimeSemantics = "Ora de început a sesiunii de transcriere PowerShell: ora locală a stației, fără fus orar în fișier",
            TimeZoneBasis = start.Basis, TimeUncertainty = start.Uncertainty,
            Source = EventSource, EvidenceId = item.EvidenceId, Provider = "PowerShell transcript", EventId = "Session",
            Host = machine, User = user, Process = proc, Pid = pid, Path = proc.Length > 0 ? RemoteToolLogText.Sane(Safe(ExePath, H("Host Application"))!.Groups["p"].Value, 260) : "",
            Summary = $"Sesiune PowerShell transcrisă ({cmds.Count} comenzi), cont {(user.Length > 0 ? user : "necunoscut")}, gazdă {(machine.Length > 0 ? machine : "necunoscută")}" +
                      (start.LocalText.Length > 0 ? $", ora locală {start.LocalText}" : ""),
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = s.Windows ? Confidence.High : Confidence.Medium,
            Locator = $"{RemoteToolLogText.Sane(file, 64)}:session={s.Index}", Fields = f,
        });
        result.Records++;

        foreach (var c in cmds)
        {
            bool own = c.StartRaw is not null;
            var ct = own ? Convert(c.StartRaw!, zone, now) : start;
            var pathStyle = c.Style == "PS cale>";
            // High with the command's own start time, Medium with the session's; one level lower for the prompt style that was not seen in a real transcript
            var conf = own ? Confidence.High : Confidence.Medium;
            if (pathStyle || !s.Windows) conf = conf == Confidence.High ? Confidence.Medium : Confidence.Low;
            var cf = Common("command");
            cf["Command"] = RemoteToolLogText.Sane(c.Text, MaxCommandChars);
            cf["Prompt Style"] = pathStyle ? "PS cale> (nevăzut într-o transcriere reală; recunoscut conservator)" : "PS> (văzut în transcrieri reale)";
            cf["Command Start Raw"] = c.StartRaw ?? "";
            cf["Command Time Source"] = own ? "Command start time (antetul de invocare)" : "ora de început a sesiunii";
            cf["Local Time"] = ct.LocalText;
            cf["Output Line Count"] = c.Output.ToString(CultureInfo.InvariantCulture);
            cf["Output Stored"] = "nu (doar numărul rândurilor nevide)";
            cf["Line"] = c.Line.ToString(CultureInfo.InvariantCulture);
            if (ct.Problem.Length > 0) cf["Time Problem"] = ct.Problem;
            if (pathStyle) cf["Note"] = "Prompt de forma „PS cale>”, nevăzut într-o transcriere reală; poate fi un rând de ieșire care arată ca un prompt.";
            sink.Add(new TimelineEvent
            {
                Time = ct.Time,
                TimeSemantics = own ? "Command start time din transcrierea PowerShell: ora locală a stației, fără fus orar în fișier"
                                    : "ora de început a sesiunii; comanda nu are oră proprie",
                TimeZoneBasis = ct.Basis, TimeUncertainty = ct.Uncertainty,
                Source = EventSource, EvidenceId = item.EvidenceId, Provider = "PowerShell transcript", EventId = "Command",
                Host = machine, User = user, Process = proc, Pid = pid,
                Summary = "Comandă PowerShell în transcriere: " + RemoteToolLogText.Sane(c.Text, 200) + (c.Text.Length > 200 ? "…" : ""),
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = conf,
                Locator = $"{RemoteToolLogText.Sane(file, 64)}:line={c.Line}", Fields = cf,
            });
            result.Records++;
        }
    }
}
