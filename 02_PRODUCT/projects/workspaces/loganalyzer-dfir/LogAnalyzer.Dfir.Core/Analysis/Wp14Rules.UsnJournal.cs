using System.Text;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// USN-JOURNAL-DELETED (T1070.004, "File Deletion": removing the change journal is deleting the record of what happened to files; T1070 is the
/// parent and says less). The command <c>fsutil usn deletejournal</c> in Security 4688, Sysmon 1 or a PowerShell 4104 script block is High:
/// it shows the command was started, not that it succeeded. <c>fsutil usn createjournal</c> alone is Low (Windows and administrators run it
/// legitimately) and becomes part of the High finding when it follows a delete on the SAME, non-empty volume within one hour. <c>queryjournal</c> /
/// <c>readjournal</c> only read and never produce a finding.
/// How a command is recognised (no regular expression runs on the raw text, so no input can make it slow):
/// for 4688 / Sysmon 1 the PROCESS is the authority: when Image / NewProcessName / OriginalFileName is fsutil.exe, the arguments of the command line
/// (argv[0] is not read) must be "usn" and the verb; otherwise the line is read only as real statements: a lexer splits on ; | &amp; and new lines OUTSIDE
/// quotes, and the argument of <c>cmd /c</c> / <c>powershell -Command</c> is read again the same way (depth at most 3). For 4104 a small PowerShell lexer
/// leaves out '...' and "..." strings, here-strings, # and &lt;# #&gt; comments. Each event is read up to <see cref="UsnCommandScanner.MaxChars"/> characters;
/// a longer one is reported as not evaluated beyond that point.
/// </summary>
public static partial class Wp14Rules
{
    internal const string UsnJournalDeletedRuleId = "USN-JOURNAL-DELETED";

    /// <summary>A create this soon after a delete on the same volume is the delete-and-recreate sequence that wipes the journal.</summary>
    internal static readonly TimeSpan UsnRecreateWindow = TimeSpan.FromHours(1);
    /// <summary>One execution seen by both 4688 and Sysmon 1 is one command when the records are this close.</summary>
    internal static readonly TimeSpan UsnDuplicateWindow = TimeSpan.FromSeconds(2);
    private const int UsnMaxEvidence = 12;

    private sealed record UsnCmdHit(TimelineEvent Event, bool Delete, string Volume, string Command);

    private static bool IsFsutilName(string name) => name.Equals("fsutil.exe", StringComparison.OrdinalIgnoreCase);
    private static string UsnBaseName(string p) { var i = p.LastIndexOfAny(['\\', '/']); return (i >= 0 ? p[(i + 1)..] : p).Trim(); }
    private static string UsnNormalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    internal static void UsnJournalDeletedFindings(Ctx c, List<Finding> o)
    {
        var hits = new List<UsnCmdHit>();
        var capped = new List<TimelineEvent>();
        foreach (var e in c.Events)
        {
            string cmd; bool script = false;
            if (Ev(e, "Security", 4688) || Ev(e, Sysmon, 1)) cmd = F(e, "CommandLine");
            else if (Ev(e, "Microsoft-Windows-PowerShell/Operational", 4104)) { cmd = F(e, "ScriptBlockText"); script = true; }
            else continue;
            if (cmd.Length > UsnCommandScanner.MaxChars) { cmd = cmd[..UsnCommandScanner.MaxChars]; capped.Add(e); }
            if (cmd.IndexOf("journal", StringComparison.OrdinalIgnoreCase) < 0) continue;   // cheap filter before the lexer
            List<UsnCommandScanner.Hit> found;
            if (script) found = UsnCommandScanner.FromScript(cmd);
            else
            {
                var image = UsnBaseName(F(e, "NewProcessName", "Image", "ProcessName"));
                if (IsFsutilName(image) || IsFsutilName(F(e, "OriginalFileName"))) found = UsnCommandScanner.FromFsutilProcess(cmd);
                else if (image.Length == 0 || UsnCommandScanner.IsShell(image)) found = UsnCommandScanner.FromShellLine(cmd);
                else found = [];   // a process that is not fsutil and not a shell does not run the text of its own command line
            }
            foreach (var h in found) hits.Add(new(e, h.Delete, h.Volume, cmd));
        }

        // 4688 and Sysmon 1 both log one execution: same command, same host, a different log, at most 2 s apart = one command.
        var unique = new List<UsnCmdHit>();
        foreach (var h in hits.OrderBy(x => T(x.Event)))
        {
            bool dup = T(h.Event) is { } t && unique.Any(u => u.Delete == h.Delete && u.Volume == h.Volume && u.Event.Host.Equals(h.Event.Host, StringComparison.OrdinalIgnoreCase) &&
                !u.Event.Source.Equals(h.Event.Source, StringComparison.OrdinalIgnoreCase) && T(u.Event) is { } ut && (t - ut).Duration() <= UsnDuplicateWindow &&
                UsnNormalize(u.Command) == UsnNormalize(h.Command));
            if (!dup) unique.Add(h);
        }
        hits = unique;

        if (capped.Count > 0)
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = RulePatterns.NotEvaluatedRuleId, Category = "Defense Evasion", Severity = Severity.Info,
                Title = $"Regula {UsnJournalDeletedRuleId}: {LogAnalyzer.Dfir.Language.RoText.Count(capped.Count, "intrare citită doar parțial", "intrări citite doar parțial")} (plafon de lungime)",
                PlainWhat = "O parte din textul unor comenzi sau scripturi nu a fost verificată pentru ștergerea jurnalului USN, pentru că textul este foarte lung; ce urmează după plafon trebuie citit manual",
                Classification = Classification.Unproven, Confidence = Confidence.Low, SemanticType = SemanticType.Observation,
                FirstSeenUtc = capped.Select(T).Where(t => t is not null).Min(), LastSeenUtc = capped.Select(T).Where(t => t is not null).Max(),
                Description = $"Regula {UsnJournalDeletedRuleId} citește cel mult {UsnCommandScanner.MaxChars / 1024} KiB (256 KiB) din fiecare linie de comandă sau script; {LogAnalyzer.Dfir.Language.RoText.Count(capped.Count, "intrare este", "intrări sunt")} mai lungi. " +
                              "Partea citită a fost analizată; ce urmează după plafon este „neevaluat”, nu „curat”.",
                ClassificationReason = "Limită de evaluare a regulii, nu o concluzie despre probă.",
                SupportingEvidence = capped.Take(UsnMaxEvidence).Select(e => new EvidenceRef(e.EvidenceId, e.Locator, $"{e.Source} {e.EventId}".Trim() + " citit parțial")).ToList(),
                Limitations = [$"Textul de după primii {UsnCommandScanner.MaxChars / 1024} KiB al intrărilor listate nu a fost evaluat de regula {UsnJournalDeletedRuleId}; citiți-l manual."],
                RecommendedNextSteps = ["Citiți manual restul textului intrărilor listate."],
            });
        if (hits.Count == 0) return;

        var deletes = hits.Where(h => h.Delete).OrderBy(h => T(h.Event)).ToList();
        var creates = hits.Where(h => !h.Delete).ToList();
        // only the SAME and non-empty volume pairs; a missing volume pairs with nothing
        bool Paired(UsnCmdHit cr) => cr.Volume.Length > 0 && T(cr.Event) is { } ct &&
            deletes.Any(d => d.Volume == cr.Volume && T(d.Event) is { } dt && ct >= dt && ct - dt <= UsnRecreateWindow);
        var recreates = creates.Where(Paired).ToList();
        var lone = creates.Where(x => !recreates.Contains(x)).ToList();

        string Show(IEnumerable<UsnCmdHit> h) => Join(h.Select(x => x.Command.ReplaceLineEndings(" ").Trim() is { Length: > 160 } s ? s[..160] + "…" : x.Command.ReplaceLineEndings(" ").Trim()).Distinct(), 3);

        if (deletes.Count > 0)
        {
            var all = deletes.Concat(recreates).OrderBy(h => T(h.Event)).ToList();
            var times = all.Select(h => T(h.Event)).Where(t => t is not null).Select(t => t!.Value).ToList();
            bool processStart = all.Any(h => !Ev(h.Event, "Microsoft-Windows-PowerShell/Operational", 4104));
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = UsnJournalDeletedRuleId, Category = "Defense Evasion", Severity = Severity.High,
                Title = recreates.Count > 0 ? "Jurnalul USN a fost șters și recreat prin comandă" : "Comandă de ștergere a jurnalului USN înregistrată",
                PlainWhat = (PlainPhrases.KnownActor(F(deletes[0].Event, "SubjectUserName", "User")) ? "a lansat " : "S-a lansat ") +
                            "o comandă care șterge jurnalul de modificări al fișierelor de pe disc (jurnalul USN)" + (recreates.Count > 0 ? " și apoi a recreat un jurnal nou, deci istoricul vechi nu mai există" : "") +
                            "; înregistrarea arată că a fost lansată comanda, nu că ștergerea a reușit",
                Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1070.004",
                FirstSeenUtc = times.Count > 0 ? times.Min() : null, LastSeenUtc = times.Count > 0 ? times.Max() : null,
                User = F(deletes[0].Event, "SubjectUserName", "User"), Process = deletes[0].Event.Process,
                Description = $"{LogAnalyzer.Dfir.Language.RoText.Count(deletes.Count, "comandă", "comenzi")} de ștergere a jurnalului USN (fsutil usn deletejournal): {Show(deletes)}. " +
                              (recreates.Count > 0 ? $"Jurnalul a fost șters și recreat: a urmat o comandă createjournal pe același volum în cel mult o oră: {Show(recreates)}; ștergerea urmată de recreare este tiparul care șterge istoricul fișierelor. " : "") +
                              "Înregistrarea arată că a fost lansată comanda; nu arată că ștergerea a reușit. Jurnalul USN nou, dacă există, are alt ID și o dată de creare ulterioară (AF07).",
                ClassificationReason = "Comanda fsutil usn deletejournal ca enunț real în 4688 / Sysmon 1 / PowerShell 4104 (sau argumentele unui proces fsutil.exe); textul care doar afișează comanda, șirurile, comentariile și numele de fișiere nu se iau în calcul.",
                SemanticType = processStart ? SemanticType.Execution : SemanticType.Observation,
                SupportingEvidence = all.Take(UsnMaxEvidence).Select(h => Ref(h.Event, $"{h.Event.EventId} fsutil usn {(h.Delete ? "deletejournal" : "createjournal")}")).ToList(),
                AlternativeExplanations =
                [
                    "Un administrator poate șterge jurnalul USN ca să elibereze spațiu sau înainte de o reconfigurare a volumului; Windows însuși îl recreează după unele reparații.",
                    "Unele programe de backup sau de indexare cer recreerea jurnalului.",
                ],
                MissingEvidence = ["Rezultatul comenzii (4688 nu arată dacă a reușit) și jurnalul USN anterior ștergerii, care nu mai poate fi citit după ștergere."],
            });
        }

        if (lone.Count > 0)
        {
            var times = lone.Select(h => T(h.Event)).Where(t => t is not null).Select(t => t!.Value).ToList();
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = UsnJournalDeletedRuleId, Category = "Defense Evasion", Severity = Severity.Low,
                Title = "Comandă de creare a jurnalului USN înregistrată",
                PlainWhat = "S-a lansat o comandă care creează sau reconfigurează jurnalul de modificări al fișierelor (jurnalul USN); este informativ, fără o ștergere a jurnalului pe același volum chiar înaintea ei",
                Classification = Classification.Direct, Confidence = Confidence.Medium, MitreTechniqueId = "T1070.004",
                FirstSeenUtc = times.Count > 0 ? times.Min() : null, LastSeenUtc = times.Count > 0 ? times.Max() : null,
                User = F(lone[0].Event, "SubjectUserName", "User"), Process = lone[0].Event.Process,
                Description = $"{LogAnalyzer.Dfir.Language.RoText.Count(lone.Count, "comandă", "comenzi")} fsutil usn createjournal: {Show(lone)}. " +
                              "Este informativ: Windows și administratorii o rulează legitim (de exemplu pentru a mări sau a porni jurnalul). Devine semnal doar dacă urmează, în cel mult o oră, după o ștergere a jurnalului pe același volum (volumul trebuie să fie numit în ambele comenzi).",
                ClassificationReason = "Comanda fsutil usn createjournal fără o ștergere a jurnalului pe același volum, numit, cu cel mult o oră înainte.",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = lone.Take(UsnMaxEvidence).Select(h => Ref(h.Event, $"{h.Event.EventId} fsutil usn createjournal")).ToList(),
                AlternativeExplanations = ["Pornirea sau redimensionarea normală a jurnalului USN de către un administrator sau de un program de backup."],
                MissingEvidence = ["O comandă deletejournal ar apărea în același jurnal de procese; dacă jurnalele de procese (4688 cu linie de comandă, Sysmon 1) nu erau activate, ștergerea nu se vede."],
            });
        }
    }
}

/// <summary>
/// Linear reader of command lines and PowerShell scripts for <c>fsutil usn deletejournal / createjournal</c>. One pass over the text per call, no
/// backtracking, no regular expression; nested <c>cmd /c</c> / <c>powershell -Command</c> arguments are read again up to <see cref="MaxDepth"/> levels.
/// Not captured (the child fsutil process event covers them): Start-Process, Invoke-Expression, string concatenation, variables used as the verb.
/// </summary>
internal static class UsnCommandScanner
{
    /// <summary>Characters read from one event; the rest is reported as not evaluated.</summary>
    internal const int MaxChars = 256 * 1024;
    internal const int MaxDepth = 3;

    internal readonly record struct Hit(bool Delete, string Volume);
    private sealed record Tok(string Text, int Start, int End);
    private sealed class Stmt { public List<Tok> Toks { get; } = []; public int End; }

    internal static bool IsShell(string imageName) => ExeName(imageName) is "cmd" or "powershell" or "pwsh" or "powershell_ise";

    /// <summary>The process is fsutil.exe: the arguments (argv[0] left out) decide.</summary>
    internal static List<Hit> FromFsutilProcess(string commandLine)
    {
        var hits = new List<Hit>();
        var st = Lex(commandLine, ps: false, split: false);
        if (st.Count > 0) Verb(st[0].Toks.Skip(1).Select(t => t.Text).ToList(), hits);
        return hits;
    }

    /// <summary>A 4688 / Sysmon 1 line of a shell (or of an unknown process): real statements only.</summary>
    internal static List<Hit> FromShellLine(string commandLine)
    {
        var hits = new List<Hit>();
        // Review 2026-11-14 (L1, round 2): a powershell.exe line is NOT split at ; | & by cmd rules (they are PowerShell's own characters, often
        // inside single quotes that cmd does not know); it is one statement whose -Command argument is read again with PowerShell rules.
        var whole = Lex(commandLine, ps: false, split: false);
        if (whole.Count > 0 && whole[0].Toks.Count > 0 && ExeName(whole[0].Toks[0].Text) is "powershell" or "pwsh")
            Analyze(commandLine, whole[0], whole[0].Toks, 0, hits);
        else
            Scan(commandLine, ps: false, 0, hits);
        return hits;
    }

    /// <summary>A PowerShell script block.</summary>
    internal static List<Hit> FromScript(string script) { var hits = new List<Hit>(); Scan(script, ps: true, 0, hits); return hits; }

    private static void Scan(string s, bool ps, int depth, List<Hit> hits)
    {
        foreach (var st in Lex(s, ps, split: true)) Analyze(s, st, st.Toks, depth, hits);
    }

    private static string ExeName(string text)
    {
        var t = text.Trim();
        var i = t.LastIndexOfAny(['\\', '/']);
        if (i >= 0) t = t[(i + 1)..];
        t = t.ToLowerInvariant();
        return t.EndsWith(".exe", StringComparison.Ordinal) || t.EndsWith(".com", StringComparison.Ordinal) ? t[..^4] : t;
    }

    private static void Analyze(string s, Stmt st, List<Tok> toks, int depth, List<Hit> hits)
    {
        int idx = 0;
        while (idx < toks.Count && toks[idx].Text is "&" or ".") idx++;   // PowerShell call / dot-source operators
        if (idx >= toks.Count) return;
        switch (ExeName(toks[idx].Text))
        {
            case "fsutil":
                Verb(toks.Skip(idx + 1).Select(t => t.Text).ToList(), hits);
                break;
            case "cmd":
                for (int k = idx + 1; k < toks.Count && depth < MaxDepth; k++)
                    if (toks[k].Text.Equals("/c", StringComparison.OrdinalIgnoreCase) || toks[k].Text.Equals("/k", StringComparison.OrdinalIgnoreCase))
                    {
                        var rest = s[toks[k].End..st.End].Trim();
                        // cmd /c removes one pair of outer quotes
                        if (rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"' && rest.Count(ch => ch == '"') % 2 == 0) rest = rest[1..^1];
                        Scan(rest, ps: false, depth + 1, hits);
                        break;
                    }
                break;
            case "powershell" or "pwsh":
                for (int k = idx + 1; k < toks.Count && depth < MaxDepth; k++)
                    if (toks[k].Text.Length >= 2 && toks[k].Text[0] is '-' or '/' && "command".StartsWith(toks[k].Text[1..], StringComparison.OrdinalIgnoreCase))
                    {
                        Scan(string.Join(' ', toks.Skip(k + 1).Select(t => t.Text)), ps: true, depth + 1, hits);   // PowerShell joins the remaining arguments with spaces
                        break;
                    }
                break;
            case "start" or "call":
                {
                    int j = idx + 1;
                    while (j < toks.Count && (toks[j].Text.Length == 0 || toks[j].Text[0] == '/')) j++;
                    if (j < toks.Count && depth < MaxDepth) Analyze(s, st, toks.GetRange(j, toks.Count - j), depth + 1, hits);
                    break;
                }
        }
    }

    /// <summary>args are what follows fsutil: "usn", the verb, then the options and the volume.</summary>
    private static void Verb(List<string> args, List<Hit> hits)
    {
        if (args.Count < 2 || !args[0].Equals("usn", StringComparison.OrdinalIgnoreCase)) return;
        bool del = args[1].Equals("deletejournal", StringComparison.OrdinalIgnoreCase);
        if (!del && !args[1].Equals("createjournal", StringComparison.OrdinalIgnoreCase)) return;
        string vol = "";
        foreach (var a in args.Skip(2))
        {
            if (a.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)) { vol = a.TrimEnd('\\').ToLowerInvariant(); break; }
            var t = a.TrimEnd('}', ')', '"').TrimEnd('\\');
            if (t.Length == 2 && char.IsAsciiLetter(t[0]) && t[1] == ':') { vol = char.ToUpperInvariant(t[0]) + ":"; break; }
        }
        hits.Add(new(del, vol));
    }

    /// <summary>Backslashes before a quote follow the Windows rule: 2n of them give n and the quote opens or closes, 2n+1 give n and a literal quote.</summary>
    private static int Backslashes(string s, int i, StringBuilder sb, out bool literalQuote)
    {
        int run = 0;
        while (i + run < s.Length && s[i + run] == '\\') run++;
        literalQuote = false;
        if (i + run < s.Length && s[i + run] == '"')
        {
            sb.Append('\\', run / 2);
            if (run % 2 == 1) { sb.Append('"'); literalQuote = true; return i + run + 1; }
            return i + run;
        }
        sb.Append('\\', run);
        return i + run;
    }

    /// <summary>
    /// One pass. split: statements are cut at ; | &amp; and new lines (and, for PowerShell, at ( ) { } at the start of a word) outside quotes; otherwise the whole
    /// text is one statement (the arguments of a process). ps: PowerShell strings, here-strings and comments; otherwise Windows command-line quoting.
    /// </summary>
    /// <summary>PowerShell keywords after which '{' or '(' opens a block even with no space (try{, if(, foreach(, catch{ ...).</summary>
    private static readonly HashSet<string> BlockKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "try", "catch", "finally", "if", "elseif", "else", "foreach", "for", "while", "do", "until", "switch", "trap", "function", "filter", "begin", "process", "end", "clean" };

    /// <summary>PowerShell single quotes: ' and the typographic ‘ ’ ‚ ‛ (U+2018-U+201B), which the PowerShell parser treats alike.</summary>
    private static bool IsSq(char c) => c is '\'' or '‘' or '’' or '‚' or '‛';

    /// <summary>PowerShell double quotes: " and the typographic “ ” „ (U+201C-U+201E).</summary>
    private static bool IsDq(char c) => c is '"' or '“' or '”' or '„';

    /// <summary>
    /// Index just after the end of a here-string whose body starts at <paramref name="from"/>: a line that begins with a closing quote of the
    /// same class followed by '@'. Linear; an unclosed here-string runs to the end of the text (PowerShell would not parse it either).
    /// </summary>
    private static int HereStringEnd(string s, int from, bool dq)
    {
        for (int k = s.IndexOf('\n', from); k >= 0 && k + 2 < s.Length; k = s.IndexOf('\n', k + 1))
            if ((dq ? IsDq(s[k + 1]) : IsSq(s[k + 1])) && s[k + 2] == '@') return k + 3;
        return s.Length;
    }

    private static List<Stmt> Lex(string s, bool ps, bool split)
    {
        var res = new List<Stmt>();
        var cur = new Stmt();
        var sb = new StringBuilder();
        bool inTok = false; int tokStart = 0, wordBraces = 0;
        void EndTok(int end) { if (inTok) { cur.Toks.Add(new Tok(sb.ToString(), tokStart, end)); sb.Clear(); inTok = false; } wordBraces = 0; }
        void EndStmt(int end) { EndTok(end); if (cur.Toks.Count > 0) { cur.End = end; res.Add(cur); cur = new Stmt(); } }
        void Begin(int at) { if (!inTok) { inTok = true; tokStart = at; } }
        int i = 0;
        while (i < s.Length)
        {
            char ch = s[i];
            if (ch is '\n' or '\r') { if (split) EndStmt(i); else EndTok(i); i++; continue; }
            if (char.IsWhiteSpace(ch)) { EndTok(i); i++; continue; }
            bool boundary = !inTok;
            if (ps)
            {
                if (ch == '`' && i + 1 < s.Length)
                {
                    if (s[i + 1] is '\n' or '\r') { EndTok(i); i += s[i + 1] == '\r' && i + 2 < s.Length && s[i + 2] == '\n' ? 3 : 2; continue; }
                    Begin(i); sb.Append(s[i + 1]); i += 2; continue;
                }
                if (boundary)
                {
                    // comments start only at the beginning of a word (in PowerShell a#b is one word)
                    if (ch == '#') { while (i < s.Length && s[i] is not ('\n' or '\r')) i++; continue; }
                    if (ch == '<' && i + 1 < s.Length && s[i + 1] == '#') { int e = s.IndexOf("#>", i + 2, StringComparison.Ordinal); i = e < 0 ? s.Length : e + 2; continue; }
                }
                // Review 2026-11-14 (L1, round 2): a here-string also starts right after '=' or '(' ($t=@'...'@), and PowerShell treats the
                // typographic quotes as quotes; both are recognised anywhere outside a string, not only at the start of a word.
                if (ch == '@' && i + 1 < s.Length && (IsSq(s[i + 1]) || IsDq(s[i + 1])))
                {
                    int j = i + 2;
                    while (j < s.Length && s[j] is ' ' or '\t') j++;
                    if (j < s.Length && s[j] is '\n' or '\r')
                    {
                        bool dq = IsDq(s[i + 1]);
                        int e = HereStringEnd(s, j, dq);
                        EndTok(i); Begin(i); i = e; EndTok(i); continue;   // the whole here-string is one word that carries no command
                    }
                }
                // Review 2026-11-14 (L1, round 2): statement boundaries inside a word too (try{fsutil ...}catch{}, if($x){fsutil ...}), the way
                // PowerShell tokenizes: inside a word, '{' / '(' open a block only after a keyword; otherwise '{' belongs to the bare word
                // (\\?\Volume{GUID}\) and its matching '}' stays in it.
                if (ch is '(' or ')' or '{' or '}')
                {
                    if (boundary || ch is ')' || (ch is '{' or '(' && BlockKeywords.Contains(sb.ToString())) || (ch == '}' && wordBraces == 0))
                    { EndStmt(i); wordBraces = 0; i++; continue; }
                    Begin(i); sb.Append(ch); wordBraces += ch == '{' ? 1 : ch == '}' ? -1 : 0; i++; continue;
                }
                if (IsSq(ch))
                {
                    Begin(i); i++;
                    while (i < s.Length)
                    {
                        if (IsSq(s[i])) { if (i + 1 < s.Length && IsSq(s[i + 1])) { sb.Append('\''); i += 2; continue; } i++; break; }
                        sb.Append(s[i]); i++;
                    }
                    continue;
                }
                if (IsDq(ch))
                {
                    Begin(i); i++;
                    while (i < s.Length)
                    {
                        if (s[i] == '`' && i + 1 < s.Length) { sb.Append(s[i + 1]); i += 2; continue; }
                        if (IsDq(s[i])) { if (i + 1 < s.Length && IsDq(s[i + 1])) { sb.Append('"'); i += 2; continue; } i++; break; }
                        sb.Append(s[i]); i++;
                    }
                    continue;
                }
            }
            else
            {
                if (ch == '\\') { Begin(i); i = Backslashes(s, i, sb, out _); continue; }
                if (ch == '"')
                {
                    Begin(i); i++;
                    while (i < s.Length)
                    {
                        if (s[i] == '\\') { i = Backslashes(s, i, sb, out var lit); if (!lit && i < s.Length && s[i] == '"') { i++; break; } continue; }
                        if (s[i] == '"') { i++; break; }
                        sb.Append(s[i]); i++;
                    }
                    continue;
                }
            }
            if (split && ch is ';' or '|') { EndStmt(i); i++; continue; }
            if (split && ch == '&')
            {
                if (ps && !inTok && cur.Toks.Count == 0 && !(i + 1 < s.Length && s[i + 1] == '&')) { cur.Toks.Add(new Tok("&", i, i + 1)); i++; continue; }   // call operator
                EndStmt(i); i++; continue;
            }
            Begin(i); sb.Append(ch); i++;
        }
        EndStmt(s.Length);
        return res;
    }
}
