using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.RemoteTools;

/// <summary>
/// RustDesk log <c>rustdesk_rCURRENT.log</c> / <c>rustdesk_rYYYY-MM-DD_HH-MM-SS.log</c> (<c>%AppData%\RustDesk\log\</c>, <c>log\server\</c>, <c>log\cm\</c>, and the same under the
/// service profile <c>C:\Windows\ServiceProfiles\LocalService</c>). Rows are written by <c>flexi_logger::opt_format</c>:
/// "[yyyy-MM-dd HH:mm:ss.ffffff ±HH:mm] LEVEL [file:line] message". The UTC offset is on EVERY row, so the UTC time is exact and does not depend on the probe's time zone.
/// Only the session markers of the server side are read: "#id Connection opened from addr", "#id Connection closed: reason", "#id connection loop exited",
/// "new wakelock, require display on: true|false", "drop wakelock". They are consolidated per file into one event per "#id" (an id reused after a rotation starts a new session).
/// File transfers (<c>libs/base/src/fs.rs</c>: <c>log::info!("new write {}", data_source)</c> / <c>"new read {}"</c>, where the data source prints as "File: &lt;path&gt;" or "Bytes") are read as separate
/// events FileWrite (a file written ON the station = received from the remote side; seen in <c>log\cm\</c>) and FileRead (a file read FROM the station = sent to the remote side;
/// seen in the main log). They are not sessions (no Direction). A transfer gets the Session Id only when exactly one partner session is open in the SAME file.
/// The log does not show whether the connection was accepted (<c>Accepted</c> = "necunoscut"), what was done in the session besides those transfers, or authentication: ordinary authentication
/// is NOT logged by RustDesk (the only message is "Permanent password accepted via logon-screen fallback", a rare case), so it is not parsed.
/// The file is written on the analysed station and can be forged: every text is cut and cleaned; a transfer path is the only free text that reaches an event (sanitised, at most 4096 characters).
/// </summary>
public sealed class RustDeskLogParser : EvidenceParserBase
{
    public const string SourceTypeName = RemoteToolLogFormats.RustDeskLog;
    public const string FingerprintName = RemoteToolLogFormats.RustDeskLog;
    public const string EventSource = "Artifact:RustDeskLog";
    /// <summary>The "id" of RustDesk in Data/remote_access_tools.json.</summary>
    public const string ToolId = "rustdesk";
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const int MaxEvents = 50_000;
    public const long MaxLinesScanned = 5_000_000;
    /// <summary>Sessions kept open at once; beyond it the oldest is written without an end (a hostile file that opens sessions and never closes them).</summary>
    public const int MaxOpenSessions = 64;
    private const int MaxReasonChars = 128;
    /// <summary>Longest transfer path kept (after cleaning); a line is read up to <see cref="RemoteToolLogText.MaxLineChars"/> characters anyway.</summary>
    public const int MaxPathChars = 4096;
    private const int MaxSummaryPathChars = 200;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "RustDeskLogParser", Version = "1.1", Artifact = "Jurnalul RustDesk (rustdesk_rCURRENT.log, rustdesk_rAAAA-LL-ZZ_HH-MM-SS.log): conexiunile de intrare și transferurile de fișiere",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = [FingerprintName], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows și Linux cu RustDesk (text scris de flexi_logger, citit în flux în cod gestionat)",
        FormatVersions = ["„[AAAA-LL-ZZ HH:MM:SS.uuuuuu ±HH:MM] NIVEL [fișier:linie] mesaj”, decalajul UTC pe fiecare rând (flexi_logger::opt_format; verificat în codul bibliotecii și pe rânduri publicate în tichete rustdesk/rustdesk, Windows și Linux)"],
        Limitations =
        [
            "Mesajele RustDesk nu sunt documentate și diferă între versiuni; markerii sunt cei din rânduri reale publicate (conexiune deschisă / închisă, buclă de conexiune încheiată, wakelock). Alte versiuni pot scrie alt text și atunci conexiunea nu este recunoscută.",
            "Transferurile de fișiere sunt citite din rândurile „new write File: <cale>” (fișier scris pe stație, primit de la distanță; apare în log\\cm\\) și „new read File: <cale>” (fișier citit de pe stație, trimis la distanță; apare în jurnalul principal), după libs/base/src/fs.rs din RustDesk; „Bytes” înseamnă date din memorie, fără cale. Calea vine din jurnalul stației și poate fi falsificată. Rândurile „new write/read” cu alt text sunt numărate, nu interpretate. Autentificarea obișnuită NU este jurnalizată de RustDesk (singurul mesaj este „Permanent password accepted via logon-screen fallback”, rar) și nu este interpretată. Jurnalul nu arată dacă conexiunea a fost acceptată (Accepted = necunoscut) și nici ce s-a făcut în ea în afara transferurilor.",
            "Un transfer primește Session Id doar când în ACELAȘI fișier este deschisă exact o sesiune de partener; altfel rămâne fără (jurnalul cm și cel principal sunt fișiere diferite, deci un transfer din cm de regulă nu are sesiune).",
            "Ora vine din decalajul UTC scris pe fiecare rând (exactă, nu depinde de fusul probei). Un decalaj imposibil (de exemplu +25:00) sau o dată imposibilă respinge rândul: este numărat, nu transformat în eveniment.",
            "Un id „#id” poate fi reutilizat după o rotație sau o repornire: o nouă „Connection opened” cu același id începe o sesiune nouă, iar cea veche rămâne fără sfârșit. Versiunile vechi scriu „Connection opened” fără „#id”: închiderea fără id se leagă de sesiunea fără id doar când este exact una deschisă.",
            "Adresa 127.0.0.0/8 sau ::1 este un proces local sau un releu, nu un partener: se scrie ca LocalConnection cu Session Evidence = nu. Adresa unui partener venită printr-un releu nu este dovedită ca adresa lui reală; portul este portul sursă al conexiunii.",
            "„new wakelock” / „drop wakelock” nu poartă id: se atașează sesiunii deschise doar când este exact una (fără cele locale); altfel sunt numărate și sesiunile deschise primesc o notă de ambiguitate. Câmpul Display arată doar „require display on” din wakelock.",
            "Jurnalul este scris la rotație zilnică sau la 16 MiB, se păstrează până la 90 de fișiere: sesiunile mai vechi pot lipsi. Poate fi modificat, trunchiat sau falsificat de cine are acces la stație (dovadă Directă, încredere Medie).",
            $"Fișier de cel mult {MaxFileBytes / (1024 * 1024)} MiB, cel mult {MaxEvents} de evenimente și {MaxOpenSessions} de sesiuni deschise deodată; liniile se citesc până la {RemoteToolLogText.MaxLineChars} de caractere (restul se aruncă și se marchează). Un mesaj pe mai multe rânduri: rândurile de continuare (fără prefix) sunt ignorate.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "RustDeskLogTests: mostră sintetică TEST în formatul real, fără sesiuni, deschidere fără închidere, închidere fără deschidere, sesiuni intercalate și wakelock ambiguu, motiv ostil (U+202E, control, 10.000 de caractere), transferuri de fișiere (cale ostilă U+202E / control / 10.000 de caractere / UNC / relativă, „Bytes”, „new write” fără „File:”, 10.000 de transferuri, două sesiuni deschise), IP invalide / 010.0.0.1 / IPv6 cu port, decalaje +05:45 / -03:30 / +25:00, rând de 5 MB, fișier peste plafon, UTF-16, continuare pe mai multe rânduri, oră în viitor, tipizare după nume și conținut, 2.000 de sesiuni",
    };

    private static readonly TimeSpan RxTimeout = TimeSpan.FromMilliseconds(200);
    private const RegexOptions Opt = RegexOptions.CultureInvariant;
    private static readonly Regex Opened = new(@"^(?:#(?<id>\d{1,10}) )?Connection opened from (?<addr>\S{1,120})$", Opt, RxTimeout);
    private static readonly Regex Closed = new(@"^(?:#(?<id>\d{1,10}) )?Connection closed:(?: (?<r>.*))?$", Opt, RxTimeout);
    private static readonly Regex LoopExited = new(@"^(?:#(?<id>\d{1,10}) )?connection loop exited\.?\s*$", Opt, RxTimeout);
    private static readonly Regex NewWake = new(@"^new wakelock, require display on: (?<v>true|false)\.?\s*$", Opt, RxTimeout);
    private static readonly Regex DropWake = new(@"^drop wakelock\.?\s*$", Opt, RxTimeout);
    private static readonly Regex PortText = new(@"^\d{1,5}$", Opt, RxTimeout);

    private sealed class Xfer
    {
        public bool Write, Memory, Truncated;
        public string Path = "", SessionId = "", Note = "";
        public RowTime Time;
        public long Line;
    }

    private sealed class Sess
    {
        public string Id = "", Ip = "", IpRaw = "", Port = "", IpStatus = "valid", EndReason = "", CloseReason = "", Ambiguity = "", Display = "";
        public bool Loopback, Truncated, ClosedRow, LoopExitedRow, WakelockDropped;
        public int Wakelocks;
        public RowTime Start, End;
        public bool HasEnd;
        public long StartLine, EndLine;
    }

    /// <summary>The time of one row: the wall clock written in it, its offset and the exact UTC time.</summary>
    private readonly record struct RowTime(DateTime Local, TimeSpan Offset, string Raw)
    {
        public DateTime Utc => Local - Offset;
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var file = Path.GetFileName(fullPath);
        var now = DateTime.UtcNow;
        var open = new List<Sess>();
        var done = new List<Sess>();
        // the last closed session by id: a "connection loop exited" row can come before or after the "Connection closed" row of the same id
        var closedById = new Dictionary<string, Sess>(StringComparer.Ordinal);
        var transfers = new List<Xfer>();
        long transferIgnored = 0;
        long line = 0, otherRows = 0, orphans = 0, badRows = 0, wakeUnattached = 0, wakeAttached = 0, continuation = 0, invalidIps = 0;
        bool capHit = false;
        Sess? lastClosedNoId = null;                                    // old versions write no id: the latest closed session without one can still receive its loop-exited row / reason
        // The session a close / loop-exited row belongs to, and whether it is still open. With an id: the open session of that id, else the last closed one of that id when
        // <paramref name="eligible"/> says it can still take the row (the other row of the pair came first). Without an id (old versions): ONLY when exactly one session is
        // a candidate (open without id, or the last one closed without id that can still take the row); with two or more it is not guessed and the caller counts an orphan.
        (Sess? S, bool IsOpen) Pick(string id, Func<Sess, bool> eligible)
        {
            if (id.Length > 0)
            {
                if (open.LastOrDefault(x => x.Id == id) is { } o) return (o, true);
                return closedById.TryGetValue(id, out var c) && eligible(c) ? (c, false) : (null, false);
            }
            Sess? one = null; int n = 0;
            foreach (var s in open) if (s.Id.Length == 0) { n++; one = s; }
            if (lastClosedNoId is { } lc && eligible(lc)) { n++; one = lc; }
            return n == 1 ? (one, open.Contains(one!)) : (null, false);
        }

        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = RemoteToolLogText.OpenReader(fs);
        var sb = new StringBuilder();

        void Close(Sess s, bool hasEnd, RowTime end, long atLine, string reason)
        {
            s.HasEnd = hasEnd; s.End = end; s.EndLine = atLine; s.EndReason = reason;
            open.Remove(s);
            done.Add(s);
            if (s.Id.Length > 0) closedById[s.Id] = s; else lastClosedNoId = s;
        }
        void TrimOpen()
        {
            while (open.Count > MaxOpenSessions) Close(open[0], false, default, 0, $"plafonul de {MaxOpenSessions} de sesiuni deschise a fost atins");
        }

        while (RemoteToolLogText.ReadLine(sr, sb, out var truncated) is { } l)
        {
            ct.ThrowIfCancellationRequested();
            line++;
            if (line > MaxLinesScanned)
            {
                result.MalformedRecords++; capHit = true;
                result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxLinesScanned} de rânduri citite; restul nu au fost cercetate", "Sesiunile din restul jurnalului lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                break;
            }
            if (line == 1) l = l.TrimStart('\uFEFF');
            if (l.Length == 0) continue;
            if (l[0] != '[') { continuation++; continue; }                 // continuation of the previous message: not an event
            Match m;
            try { m = RemoteToolLogFormats.RustDeskLogRow.Match(l); }
            catch (RegexMatchTimeoutException) { result.MalformedRecords++; badRows++; continue; }
            if (!m.Success) { continuation++; continue; }

            // the time of EVERY row is checked: an impossible offset or date rejects the row, even when the message is not a marker
            if (!TryTime(m, out var time)) { badRows++; result.MalformedRecords++; continue; }
            var msg = m.Groups["msg"].Success ? m.Groups["msg"].Value : "";
            try
            {
                if (msg.Contains("Connection opened from", StringComparison.Ordinal) && Opened.Match(msg) is { Success: true } op)
                {
                    var id = op.Groups["id"].Value;
                    if (id.Length > 0 && open.LastOrDefault(x => x.Id == id) is { } reused)
                        Close(reused, false, default, 0, "id reutilizat: o nouă conexiune cu același id a început fără sfârșitul celei vechi în jurnal");   // rotation or restart
                    if (done.Count + open.Count >= MaxEvents) { capHit = true; continue; }
                    var s = new Sess { Id = id, Start = time, StartLine = line, Truncated = truncated };
                    var tok = op.Groups["addr"].Value;
                    if (tok.EndsWith('.')) tok = tok[..^1];
                    if (ParseAddress(tok, out var ip, out var port))
                    {
                        s.Ip = ip; s.Port = port;
                        s.Loopback = IsLoopback(ip);
                    }
                    else { s.IpStatus = "invalid"; s.IpRaw = RemoteToolLogText.Sane(tok, 64); invalidIps++; }
                    open.Add(s);
                    TrimOpen();
                }
                else if (msg.Contains("Connection closed:", StringComparison.Ordinal) && Closed.Match(msg) is { Success: true } cl)
                {
                    var id = cl.Groups["id"].Value;
                    var reason = RemoteToolLogText.Sane(cl.Groups["r"].Value, MaxReasonChars);
                    var (s, isOpen) = Pick(id, x => !x.ClosedRow);
                    if (s is null) orphans++;
                    else
                    {
                        s.ClosedRow = true; s.CloseReason = reason; s.Truncated |= truncated;
                        // an open session is closed here; one already closed by its "connection loop exited" row (it came first) only receives the reason
                        if (isOpen) Close(s, true, time, line, "Connection closed");
                    }
                }
                else if (msg.Contains("connection loop exited", StringComparison.Ordinal) && LoopExited.Match(msg) is { Success: true } lp)
                {
                    var id = lp.Groups["id"].Value;
                    var (s, isOpen) = Pick(id, x => !x.LoopExitedRow);
                    if (s is null) orphans++;
                    else
                    {
                        s.LoopExitedRow = true; s.Truncated |= truncated;
                        if (isOpen) Close(s, true, time, line, "connection loop exited");
                    }
                }
                else if (msg.StartsWith("new write ", StringComparison.Ordinal) || msg.StartsWith("new read ", StringComparison.Ordinal))
                {
                    // libs/base/src/fs.rs: "new write {data_source}" / "new read {data_source}"; the data source prints as "File: <path>" or "Bytes"
                    bool write = msg.StartsWith("new write ", StringComparison.Ordinal);
                    var src = msg[(write ? "new write ".Length : "new read ".Length)..].TrimEnd('\r', '\n');
                    bool memory = src == "Bytes";
                    bool isFile = src.StartsWith("File: ", StringComparison.Ordinal);
                    var rawPath = isFile ? src["File: ".Length..] : "";
                    if (!(memory || (isFile && rawPath.Trim().Length > 0))) { transferIgnored++; continue; }
                    if (done.Count + open.Count + transfers.Count >= MaxEvents) { capHit = true; continue; }
                    var x = new Xfer { Write = write, Memory = memory, Path = memory ? "" : RemoteToolLogText.Sane(rawPath, MaxPathChars), Time = time, Line = line, Truncated = truncated };
                    // the session only when exactly one partner session is open in this same file; never guessed
                    var partners = open.Where(o => !o.Loopback).ToList();
                    if (partners.Count == 1) x.SessionId = partners[0].Id;
                    else if (partners.Count > 1) x.Note = $"{partners.Count} sesiuni de partener erau deschise deodată; transferul nu a fost atribuit nici uneia";
                    transfers.Add(x);
                }
                else if (msg.Contains("wakelock", StringComparison.Ordinal) && (NewWake.IsMatch(msg) || DropWake.IsMatch(msg)))
                {
                    // no id on these rows: they belong to the one open session of a partner, never guessed when there are two or none
                    var cands = open.Where(x => !x.Loopback).ToList();
                    if (cands.Count == 1)
                    {
                        wakeAttached++;
                        var s = cands[0];
                        if (DropWake.IsMatch(msg)) s.WakelockDropped = true;
                        else
                        {
                            s.Wakelocks++;
                            // "require display on: true" at any time of the session makes it a session with a screen
                            if (NewWakeValue(msg) == "true") s.Display = "da"; else if (s.Display.Length == 0) s.Display = "nu";
                        }
                    }
                    else
                    {
                        wakeUnattached++;
                        foreach (var c in cands) AddAmbiguity(c, $"{cands.Count} sesiuni erau deschise deodată la un rând wakelock fără id; rândul nu a fost atribuit nici uneia");
                    }
                }
                else otherRows++;
            }
            catch (RegexMatchTimeoutException) { result.MalformedRecords++; }
        }

        // sessions still open at the end of the file have no end in it
        foreach (var s in open.ToList()) Close(s, false, default, 0, "jurnalul se termină înainte de sfârșitul conexiunii");

        // sessions and transfers in the order of the rows
        var ordered = done.Select(s => (Line: s.StartLine, S: (Sess?)s, X: (Xfer?)null)).Concat(transfers.Select(x => (Line: x.Line, S: (Sess?)null, X: (Xfer?)x))).OrderBy(t => t.Line);
        foreach (var (_, s, x) in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Records >= MaxEvents) { capHit = true; break; }
            sink.Add(s is not null ? BuildEvent(s, item, file, now) : BuildTransferEvent(x!, item, file, now));
            result.Records++;
        }
        if (capHit && !result.Gaps.Any(g => g.Reason.StartsWith("peste ", StringComparison.Ordinal)))
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"plafon de {MaxEvents} de evenimente atins; restul conexiunilor nu au fost scrise", "Conexiunile de după plafon lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
        if (orphans > 0 || badRows > 0 || transferIgnored > 0)
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"{orphans} de închideri fără conexiune deschisă, {badRows} de rânduri cu dată sau decalaj imposibil și {transferIgnored} de rânduri „new write/read” cu alt text decât „File: <cale>” sau „Bytes” au fost numărate, nu transformate în evenimente",
                "Închiderile, transferurile neinterpretate și rândurile respinse nu apar în cronologie", "Fișierul brut din caz", "Posibil",
                $"rânduri wakelock atribuite: {wakeAttached}, neatribuite: {wakeUnattached}, adrese invalide: {invalidIps}, rânduri de continuare sau fără prefix: {continuation}, alte rânduri: {otherRows}"));
    }

    private static string NewWakeValue(string msg) => NewWake.Match(msg) is { Success: true } m ? m.Groups["v"].Value : "";

    private static void AddAmbiguity(Sess s, string why)
    {
        if (s.Ambiguity.Contains(why, StringComparison.Ordinal) || s.Ambiguity.Length > 400) return;
        s.Ambiguity = s.Ambiguity.Length == 0 ? why : s.Ambiguity + "; " + why;
    }

    /// <summary>The date, the time and the offset of a row; false when the date does not exist or the offset is impossible (more than ±14:00 or minutes over 59).</summary>
    private static bool TryTime(Match m, out RowTime time)
    {
        time = default;
        var raw = m.Groups["d"].Value + " " + m.Groups["t"].Value + " " + m.Groups["sg"].Value + m.Groups["oh"].Value + ":" + m.Groups["om"].Value;
        if (!DateTime.TryParseExact(m.Groups["d"].Value + " " + m.Groups["t"].Value, "yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        int h = int.Parse(m.Groups["oh"].Value, CultureInfo.InvariantCulture), mi = int.Parse(m.Groups["om"].Value, CultureInfo.InvariantCulture);
        if (mi > 59 || h > 14 || (h == 14 && mi > 0)) return false;
        var off = new TimeSpan(h, mi, 0);
        if (m.Groups["sg"].Value == "-") off = off.Negate();
        // a wall clock so close to the ends of DateTime that the UTC time cannot be computed is not a time
        if (local.Year < 1000 || local.Year > 9990) return false;
        time = new RowTime(local, off, raw);
        return true;
    }

    /// <summary>"1.2.3.4:5678", "[2001:db8::1]:443" or a bare address to a validated address and port. A bare IPv6 (more than one colon, no brackets) is not what RustDesk writes: invalid.</summary>
    private static bool ParseAddress(string tok, out string ip, out string port)
    {
        ip = ""; port = "";
        string host, portText = "";
        if (tok.StartsWith('['))
        {
            int close = tok.IndexOf(']');
            if (close < 2) return false;
            host = tok[1..close];
            var rest = tok[(close + 1)..];
            if (rest.Length > 0) { if (rest[0] != ':') return false; portText = rest[1..]; }
        }
        else
        {
            int colons = tok.Count(c => c == ':');
            if (colons > 1) return false;
            if (colons == 1) { int c = tok.IndexOf(':'); host = tok[..c]; portText = tok[(c + 1)..]; }
            else host = tok;
        }
        if (host.Contains('[') || host.Contains(']')) return false;
        if (portText.Length > 0)
        {
            bool okPort;
            try { okPort = PortText.IsMatch(portText) && int.Parse(portText, CultureInfo.InvariantCulture) <= 65535; }
            catch (RegexMatchTimeoutException) { okPort = false; }
            if (!okPort) return false;
        }
        var valid = RemoteToolLogText.ValidIp(host);
        if (valid.Length == 0) return false;
        ip = valid; port = portText;
        return true;
    }

    private static bool IsLoopback(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return false;
        if (a.AddressFamily == AddressFamily.InterNetworkV6 && a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        return IPAddress.IsLoopback(a);
    }

    private static (Timestamp Time, string Problem) Stamp(RowTime t, DateTime now)
    {
        if (t.Local.Year < RemoteToolLogText.MinYear) return (Timestamp.Unknown(t.Raw), $"dată imposibilă (înainte de {RemoteToolLogText.MinYear})");
        var utc = DateTime.SpecifyKind(t.Utc, DateTimeKind.Utc);
        if (utc > now + RemoteToolLogText.FutureTolerance) return (Timestamp.Unknown(t.Raw), "ora este în viitor");
        return (Timestamp.FromUtc(utc, t.Raw, "decalajul UTC scris pe rândul din jurnal"), "");
    }

    private static string OffsetText(TimeSpan o) => (o < TimeSpan.Zero ? "-" : "+") + o.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private static TimelineEvent BuildTransferEvent(Xfer x, EvidenceItem item, string file, DateTime now)
    {
        var (st, timeProblem) = Stamp(x.Time, now);
        string kind = x.Write ? "FileWrite" : "FileRead";
        string transfer = x.Write ? "fișier scris pe stație (primit de la distanță)" : "fișier citit de pe stație (trimis la distanță)";
        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tool Id"] = ToolId, ["Tool"] = "RustDesk", ["Session Evidence"] = "da", ["Kind"] = kind, ["Direction"] = "",
            ["Transfer"] = transfer, ["Transfer Data"] = x.Memory ? "memorie" : "fișier", ["File Path"] = x.Path,
            ["Session Id"] = x.SessionId,
            ["Local Time"] = RemoteToolLogText.LocalText(x.Time.Local), ["Time Offset"] = OffsetText(x.Time.Offset), ["Time Raw"] = RemoteToolLogText.Sane(x.Time.Raw, 48),
            ["Source File"] = RemoteToolLogText.Sane(file, 128), ["Line"] = x.Line.ToString(CultureInfo.InvariantCulture),
            ["Limitation"] = "Calea vine din jurnalul stației și poate fi falsificată; rândul nu arată dimensiunea, conținutul, partenerul sau dacă transferul s-a terminat.",
        };
        if (x.Note.Length > 0) f["Ambiguity"] = x.Note;
        if (x.Truncated) f["Line Truncated"] = $"da (peste {RemoteToolLogText.MaxLineChars} de caractere)";
        if (timeProblem.Length > 0) f["Time Problem"] = timeProblem;
        var shown = x.Memory ? "date din memorie (fără cale)" : x.Path.Length > MaxSummaryPathChars ? x.Path[..(char.IsHighSurrogate(x.Path[MaxSummaryPathChars - 1]) ? MaxSummaryPathChars - 1 : MaxSummaryPathChars)] + "…" : x.Path;   // review 2026-11-16: never split a surrogate pair
        return new TimelineEvent
        {
            Time = st,
            TimeSemantics = "Ora rândului „new write/read” din jurnalul RustDesk: decalajul UTC este scris pe rând, deci ora UTC este exactă",
            TimeZoneBasis = st.IsKnown ? "decalajul UTC scris pe rând (" + OffsetText(x.Time.Offset) + ")" : "oră a rândului neutilizabilă (" + timeProblem + ")",
            TimeUncertainty = "",
            Source = EventSource, EvidenceId = item.EvidenceId, Provider = "RustDesk " + RemoteToolLogText.Sane(file, 64), EventId = kind,
            Summary = $"RustDesk (jurnal): {transfer}: {shown}" + (x.SessionId.Length > 0 ? $", sesiunea #{x.SessionId}" : "") + $", ora locală {RemoteToolLogText.LocalText(x.Time.Local)} ({OffsetText(x.Time.Offset)})",
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
            Locator = $"{RemoteToolLogText.Sane(file, 64)}:line={x.Line}", Fields = f,
        };
    }

    private static TimelineEvent BuildEvent(Sess s, EvidenceItem item, string file, DateTime now)
    {
        var (st, timeProblem) = Stamp(s.Start, now);
        string duration = "", durationProblem = "", endUtc = "";
        if (s.HasEnd)
        {
            var (et, endProblem) = Stamp(s.End, now);
            endUtc = et.Utc?.ToString("o", CultureInfo.InvariantCulture) ?? "";
            var span = s.End.Utc - s.Start.Utc;
            if (span < TimeSpan.Zero) durationProblem = "interval invers: sfârșitul este înaintea începutului";
            else if (timeProblem.Length == 0 && endProblem.Length == 0) duration = ((long)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            else durationProblem = "durata nu se calculează: o oră a rândurilor nu este utilizabilă";
        }
        string kind = s.Loopback ? "LocalConnection" : "IncomingConnection";
        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tool Id"] = ToolId, ["Tool"] = "RustDesk", ["Session Evidence"] = s.Loopback ? "nu" : "da",
            ["Kind"] = kind, ["Direction"] = "Incoming", ["Accepted"] = "necunoscut",
            ["Session Id"] = s.Id, ["Close Reason"] = s.CloseReason, ["Display"] = s.Display,
            ["Local Time"] = RemoteToolLogText.LocalText(s.Start.Local), ["Time Offset"] = OffsetText(s.Start.Offset), ["Time Raw"] = RemoteToolLogText.Sane(s.Start.Raw, 48),
            ["End Local Time"] = s.HasEnd ? RemoteToolLogText.LocalText(s.End.Local) : "",
            ["End Time UTC"] = endUtc, ["Duration Seconds"] = duration, ["End Reason"] = s.EndReason,
            ["Source File"] = RemoteToolLogText.Sane(file, 128), ["Line"] = s.StartLine.ToString(CultureInfo.InvariantCulture),
            ["End Line"] = s.EndLine > 0 ? s.EndLine.ToString(CultureInfo.InvariantCulture) : "",
            ["Limitation"] = "Jurnalul poate fi falsificat de cine are acces la stație; nu arată acceptarea, autentificarea sau ce s-a făcut în sesiune; transferurile de fișiere sunt evenimente separate, fără legătură sigură cu sesiunea.",
        };
        if (s.Ip.Length > 0)
        {
            f["IP"] = s.Ip; f["IP Status"] = "valid";
            f["IP Meaning"] = s.Loopback ? "adresă loopback: proces local sau releu, nu un partener la distanță" : "adresa din „Connection opened from”: poate fi a unui releu, nu este dovedită ca adresa reală a partenerului";
            if (s.Port.Length > 0) f["Remote Port"] = s.Port;
        }
        else { f["IP Status"] = "invalid"; f["IP Raw"] = s.IpRaw; }
        if (s.LoopExitedRow) f["Connection Loop Exited"] = "da";
        if (s.Wakelocks > 0) f["Wakelock Rows"] = s.Wakelocks.ToString(CultureInfo.InvariantCulture);
        if (s.WakelockDropped) f["Wakelock Dropped"] = "da";
        if (s.Ambiguity.Length > 0) f["Ambiguity"] = s.Ambiguity;
        if (s.Truncated) f["Line Truncated"] = $"da (peste {RemoteToolLogText.MaxLineChars} de caractere)";
        if (timeProblem.Length > 0) f["Time Problem"] = timeProblem;
        if (durationProblem.Length > 0) f["Duration Problem"] = durationProblem;

        var who = s.Ip.Length > 0 ? $"de la {s.Ip}" + (s.Port.Length > 0 ? $":{s.Port}" : "") : "de la o adresă care nu poate fi validată";
        var what = s.Loopback ? "conexiune locală (adresă loopback: proces local sau releu, nu un partener)" : "conexiune de intrare (acceptarea nu reiese din jurnal)";
        return new TimelineEvent
        {
            Time = st,
            TimeSemantics = "Ora rândului „Connection opened from” din jurnalul RustDesk: decalajul UTC este scris pe rând, deci ora UTC este exactă",
            TimeZoneBasis = st.IsKnown ? "decalajul UTC scris pe rând (" + OffsetText(s.Start.Offset) + ")" : "oră a rândului neutilizabilă (" + timeProblem + ")",
            TimeUncertainty = "",
            Source = EventSource, EvidenceId = item.EvidenceId, Provider = "RustDesk " + RemoteToolLogText.Sane(file, 64), EventId = kind,
            Summary = $"RustDesk (jurnal): {what} {who}" + (s.Id.Length > 0 ? $", sesiunea #{s.Id}" : "") + $", început local {RemoteToolLogText.LocalText(s.Start.Local)} ({OffsetText(s.Start.Offset)})" +
                      (duration.Length > 0 ? $", durată {duration} s" : ", fără sfârșit în jurnal"),
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
            Locator = $"{RemoteToolLogText.Sane(file, 64)}:line={s.StartLine}", Fields = f,
        };
    }
}
