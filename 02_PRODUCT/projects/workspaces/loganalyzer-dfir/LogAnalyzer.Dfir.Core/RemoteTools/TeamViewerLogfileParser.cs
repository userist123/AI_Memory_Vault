using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.RemoteTools;

/// <summary>
/// TeamViewer main log <c>TeamViewer&lt;NN&gt;_Logfile.log</c> / <c>TeamViewer&lt;NN&gt;_Logfile_OLD.log</c> (<c>%ProgramFiles%\TeamViewer</c>, <c>%AppData%\TeamViewer</c>):
/// rows "yyyy/MM/dd HH:mm:ss.fff  pid  tid  LABEL  message" in the station's LOCAL time, after a header block whose "Start:" row gives the UTC offset of the station at
/// the time TeamViewer started. Only the markers of INCOMING sessions are read (session start, partner id and attempt, authentication method and result, participant,
/// UDP address, client version, session end); they are consolidated per file, in row order, into one event per session. A session whose last authentication result is
/// is negative ("was denied", LoginFailed, aborting login) is written as a <c>FailedAuthentication</c> event (one event per session, never two). File transfers are NOT parsed (no real sample exists).
/// The file is written on the analysed station and can be forged: nothing of it is trusted, every text is cut and cleaned, no text of it reaches the summary but
/// the validated ids, addresses and times.
/// </summary>
public sealed class TeamViewerLogfileParser : EvidenceParserBase
{
    public const string SourceTypeName = RemoteToolLogFormats.TeamViewerLogfile;
    public const string FingerprintName = RemoteToolLogFormats.TeamViewerLogfile;
    public const string EventSource = "Artifact:TeamViewerLogfile";
    public const string ToolId = "teamviewer";
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const int MaxEvents = 50_000;
    public const long MaxLinesScanned = 5_000_000;
    /// <summary>Sessions kept open at once; beyond it the oldest is written without an end (a hostile file that opens sessions and never closes them).</summary>
    public const int MaxOpenSessions = 64;
    private const int MaxIpsPerSession = 8;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "TeamViewerLogfileParser", Version = "1.0", Artifact = "Jurnalul principal TeamViewer (TeamViewer<NN>_Logfile.log, _OLD): sesiunile de intrare",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = [FingerprintName], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows cu TeamViewer (text scris de TeamViewer, citit în flux în cod gestionat)",
        FormatVersions = ["„AAAA/LL/ZZ HH:MM:SS.mmm pid tid ETICHETĂ mesaj”, ora locală; antet cu „Start: … (UTC±h:mm)” (verificat pe un jurnal real TeamViewer 15.55, august 2024)"],
        Limitations =
        [
            "Formatul și mesajele nu sunt documentate de TeamViewer și diferă între versiuni; markerii sunt cei dintr-un jurnal real TeamViewer 15.55 (august 2024), un singur eșantion: alte versiuni pot scrie alt text și atunci sesiunea nu este recunoscută.",
            "Se citesc doar sesiunile de intrare (început, ID și încercare, metodă și rezultat de autentificare, participant, adresă UDP, versiunea clientului, sfârșit). Transferurile de fișiere NU sunt interpretate: nu există o mostră reală. Restul rândurilor nu devin evenimente.",
            "Ora este ora locală a stației; decalajul UTC din rândul „Start:” al antetului se aplică rândurilor următoare până la următorul „Start:”. Fără antet se folosește fusul probei, iar fără acesta ora rămâne locală și neconvertită, fără oră pe cronologie.",
            "Adresa din „punch received” este adresa văzută de TeamViewer în încercarea de conexiune directă UDP: poate fi adresa locală (rețea privată, link-local) a partenerului sau a unui intermediar, nu este dovedită ca adresa lui publică. Portul și indicele de zonă (%n) se elimină.",
            "Markerii cu ID de partener (autentificare, participant, versiune) nu poartă ID-ul sesiunii: se leagă de sesiunea deschisă cu același ID de partener, altfel de cea mai recentă; când sunt deschise două sesiuni, atribuirea este marcată ca ambiguă.",
            "Acceptarea vine doar din ce arată jurnalul real: „was successful” = da; „was denied”, „CMD_ENDSESSION with reason: LoginFailed” și „aborting login” = nu; orice alt text de rezultat rămâne „necunoscut” (nu se ghicește). O sesiune neacceptată este scrisă ca FailedAuthentication, cu numărul maxim de încercări (attempt number).",
            "Numele partenerului este scris de partener și poate fi orice text: apare curățat și plafonat doar în câmpuri, nu în rezumat. Jurnalul poate fi modificat, trunchiat, rotit sau falsificat de cine are acces la stație (dovadă Directă, încredere Medie) și nu arată ce s-a făcut în sesiune.",
            $"Fișier de cel mult {MaxFileBytes / (1024 * 1024)} MiB, cel mult {MaxEvents} de evenimente și {MaxOpenSessions} de sesiuni deschise deodată; liniile se citesc până la {RemoteToolLogText.MaxLineChars} de caractere (restul se aruncă și se marchează). Peste plafon se scrie o lacună.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "TeamViewerLogfileTests: mostră sintetică TEST în formatul real, fără sesiune, sesiune fără sfârșit, sesiuni suprapuse, nume și adrese ostile, rând de 5 MB, fișier peste plafon, UTF-16, antet UTC+5:45 / UTC-3:30, lipsa „Start:”, oră în viitor, tipizare după nume și conținut",
    };

    private static readonly TimeSpan RxTimeout = TimeSpan.FromMilliseconds(200);
    private const RegexOptions Opt = RegexOptions.CultureInvariant;
    private static readonly Regex StartRow = new(@"^\s*Start:\s+(?<d>\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}(?:\.\d{1,3})?)\s+\(UTC(?<sg>[+-])(?<h>\d{1,2}):(?<m>\d{2})\)\s*$", Opt, RxTimeout);
    private static readonly Regex Incoming = new(@"(?:AppSessionManager::HandleSessionIncoming|SessionManagerDesktop::IncomingConnection): Connection incoming, sessionID = (?<sid>-?\d{1,20})(?!\d)", Opt, RxTimeout);
    private static readonly Regex Allocate = new(@"AuthenticationBlocker::Allocate: allocate (?<res>\S{1,24}) for DyngateID (?<id>[^\s,]{1,40})(?:, attempt number (?<n>\d{1,6}))?", Opt, RxTimeout);
    private static readonly Regex AuthRun = new(@"RunAuthenticationMethod:\s*(?<txt>.*)$", Opt, RxTimeout);
    private static readonly Regex AuthUsing = new(@"^authentication using (?<m>.+?)(?: was (?<r>.+?)\.?)?$", Opt | RegexOptions.IgnoreCase, RxTimeout);
    private static readonly Regex Participant = new(@"CPersistentParticipantManager::AddParticipant:\s*\[(?<id>[^,\]\s]{1,40}),[^\]]{0,40}\]\s*type=(?<t>\S{1,8})(?:\s+name=(?<name>.*))?$", Opt, RxTimeout);
    private static readonly Regex Punch = new(@"punch received a=(?<tok>\S{1,100})", Opt, RxTimeout);
    private static readonly Regex Info = new(@"InfoCommandHandlerDesktop::ReceivedInfo: connected to (?<id>[^\s,]{1,40}), client version is (?<v>[^,]{1,60})(?:,\s*OS=(?<os>\S{1,16}))?", Opt, RxTimeout);
    private static readonly Regex Terminate = new(@"SessionManagerDesktop::SessionTerminate: removing session with tvsessionprotocol::TVSessionID = (?<sid>-?\d{1,20})(?!\d)", Opt, RxTimeout);
    private static readonly Regex Ended = new(@"SessionStatistics\[\d{1,4}\] Session to (?<id>[^\s]{1,40}) ended\.", Opt, RxTimeout);
    private static readonly Regex NoSession = new(@"SessionManagerDesktop::SessionTerminate: no session with ID=(?<sid>-?\d{1,20})(?!\d)", Opt, RxTimeout);
    private static readonly Regex AbortLogin = new(@"LoginManager::AbortLogin: aborting login for sessionID (?<sid>-?\d{1,20})(?!\d)", Opt, RxTimeout);
    private static readonly Regex EndCmd = new(@"CMD_ENDSESSION with reason: (?<r>\S{1,40})", Opt, RxTimeout);
    private static readonly Regex StartAny = new(@"^\s*Start:", Opt, RxTimeout);
    private static readonly Regex VersionText = new(@"^\d{1,3}(?:\.\d{1,5}){0,4}$", Opt, RxTimeout);
    private static readonly string[] Keywords =
        ["sessionID = ", "DyngateID", "RunAuthenticationMethod", "AddParticipant", "punch received", "ReceivedInfo", "SessionTerminate", "SessionStatistics", "CMD_ENDSESSION", "AbortLogin", "Step_Receive_VerifyClientSecret"];

    private sealed class Sess
    {
        public string SessionId = "", RemoteId = "", Name = "", Method = "", Version = "", OsCode = "", Accepted = "necunoscut", Ambiguity = "", Note = "", EndReason = "", OtherIds = "";
        public bool RemoteIdOk, Implicit, Truncated, ZoneIdStripped;
        public int Attempts, FailedAuth, BlockedAlloc, OtherParticipants, InvalidIps;
        public int MaxAttempt, TerminateRows;
        public bool HasStats, SrpMismatch, LoginAborted;
        public string EndCommand = "";
        public DateTime Start; public string StartRaw = ""; public long StartLine;
        public RemoteToolLogText.Zone Zone; public string ZoneSource = "";
        public DateTime? End; public string EndRaw = ""; public long EndLine;
        public readonly List<string> Ips = [];
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var file = Path.GetFileName(fullPath);
        var probeZone = RemoteToolLogText.ZoneOf(item);
        var now = DateTime.UtcNow;
        var zone = probeZone; var zoneSource = probeZone.Info is null ? "necunoscut" : "fusul probei";
        var open = new List<Sess>();
        var done = new List<Sess>();
        long line = 0, headerRows = 0, otherRows = 0, orphans = 0, badDates = 0, startRows = 0;
        bool capHit = false;
        // sessions already closed, by session id and by partner id (the real log writes the Terminate and Statistics rows in one order or the other, depending on the outcome)
        var closedBySid = new Dictionary<string, Sess>(StringComparer.Ordinal);
        var closedByRemote = new Dictionary<string, Sess>(StringComparer.Ordinal);

        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = RemoteToolLogText.OpenReader(fs);
        var sb = new StringBuilder();

        void Close(Sess s, DateTime? end, string raw, long atLine, string reason)
        {
            s.End = end; s.EndRaw = raw; s.EndLine = atLine; s.EndReason = reason;
            open.Remove(s);
            done.Add(s);
            if (s.SessionId.Length > 0) closedBySid[s.SessionId] = s;
            if (s.RemoteId.Length > 0) closedByRemote[s.RemoteId] = s;
        }
        Sess? BySid(string sid) => open.LastOrDefault(x => x.SessionId == sid) ?? (closedBySid.TryGetValue(sid, out var c) ? c : null);

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
            if (line == 1) l = l.TrimStart('﻿');
            if (l.Length == 0) continue;
            Match m;
            try { m = RemoteToolLogFormats.TeamViewerLogRow.Match(l); }
            catch (RegexMatchTimeoutException) { result.MalformedRecords++; continue; }
            if (!m.Success)
            {
                // header block (no date prefix): only the "Start:" row is read, the rest is not an event
                headerRows++;
                if (l.Contains("Start:", StringComparison.Ordinal) && IsStartRow(l))
                {
                    startRows++;
                    // TeamViewer restarted: a session still open cannot have continued
                    foreach (var s in open.ToList()) Close(s, null, "", 0, "TeamViewer a fost repornit (nou rând „Start:”) înainte de sfârșitul sesiunii");
                    if (TryStart(l, out var offsetZone, out var label)) { zone = offsetZone; zoneSource = "antetul „Start:” (" + label + ")"; }
                    else
                    {
                        // the previous offset belongs to the previous run of TeamViewer: it must not be applied to the rows after a header whose offset cannot be read
                        zone = probeZone;
                        zoneSource = (probeZone.Info is null ? "necunoscut" : "fusul probei") + " (decalajul din rândul „Start:” nu poate fi citit; decalajul anterior nu se mai aplică)";
                    }
                }
                continue;
            }
            var msg = m.Groups["msg"].Value;
            bool marker = false;
            foreach (var k in Keywords) if (msg.Contains(k, StringComparison.Ordinal)) { marker = true; break; }
            if (!marker) { otherRows++; continue; }

            var rawTime = m.Groups["d"].Value + " " + m.Groups["t"].Value;
            if (!DateTime.TryParseExact(rawTime, "yyyy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                badDates++; result.MalformedRecords++; continue;
            }
            try
            {
                Sess? Implicit(string why)
                {
                    if (done.Count + open.Count >= MaxEvents) { capHit = true; return null; }
                    var s = new Sess { Implicit = true, Start = local, StartRaw = rawTime, StartLine = line, Zone = zone, ZoneSource = zoneSource, Truncated = truncated, Note = why };
                    open.Add(s);
                    TrimOpen();
                    return s;
                }
                void TrimOpen()
                {
                    while (open.Count > MaxOpenSessions) Close(open[0], null, "", 0, $"plafonul de {MaxOpenSessions} de sesiuni deschise a fost atins");
                }
                // the session a marker without a session id belongs to. With a partner id: the MOST RECENT open session that has this id or has none yet (a partner that reconnects
                // while the old session is still open belongs to the new one). Without one: the most recent open session. Two open sessions and a session that had no id: flagged.
                Sess? Target(string partnerId, bool create, string why)
                {
                    Sess? t = null;
                    if (partnerId.Length > 0)
                        for (int i = open.Count - 1; i >= 0; i--)
                            if (open[i].RemoteId == partnerId || open[i].RemoteId.Length == 0) { t = open[i]; break; }
                    if (t is not null)
                    {
                        if (open.Count >= 2 && t.RemoteId.Length == 0) AddAmbiguity(t, $"{open.Count} sesiuni erau deschise deodată; markerul a fost atribuit celei mai recente fără ID de partener");
                        return t;
                    }
                    if (open.Count == 0) return create ? Implicit(why) : null;
                    t = open[^1];
                    if (open.Count >= 2) AddAmbiguity(t, $"{open.Count} sesiuni erau deschise deodată; markerul fără ID de sesiune a fost atribuit celei mai recente");
                    return t;
                }

                if (msg.Contains("sessionID = ", StringComparison.Ordinal) && Incoming.Match(msg) is { Success: true } inc)
                {
                    var sid = inc.Groups["sid"].Value;
                    if (open.Any(s => s.SessionId == sid)) continue;          // the same session is announced by two components
                    if (done.Count + open.Count >= MaxEvents) { capHit = true; continue; }
                    open.Add(new Sess { SessionId = sid, Start = local, StartRaw = rawTime, StartLine = line, Zone = zone, ZoneSource = zoneSource, Truncated = truncated });
                    TrimOpen();
                }
                else if (msg.Contains("SessionTerminate", StringComparison.Ordinal) && Terminate.Match(msg) is { Success: true } term)
                {
                    var sid = term.Groups["sid"].Value;
                    var s = open.LastOrDefault(x => x.SessionId == sid);
                    if (s is not null) { s.TerminateRows++; Close(s, local, rawTime, line, "SessionTerminate"); }
                    else if (closedBySid.TryGetValue(sid, out var closed) && closed.TerminateRows == 0) closed.TerminateRows++;   // Statistics came first: the session is already closed
                    else
                    {
                        // a log that starts in the middle of a session (rotation) has an implicit session without id: it is closed by the first terminate
                        s = open.LastOrDefault(x => x.SessionId.Length == 0);
                        if (s is null) orphans++;
                        else { s.SessionId = sid; s.TerminateRows++; s.Note = (s.Note + " ID-ul sesiunii vine din rândul de încheiere.").Trim(); Close(s, local, rawTime, line, "SessionTerminate"); }
                    }
                }
                else if (msg.Contains("no session with ID=", StringComparison.Ordinal) && NoSession.Match(msg) is { Success: true } ns)
                {
                    // a failed login: the session was already closed by the statistics row (or is still open): same session, by id
                    var s = BySid(ns.Groups["sid"].Value);
                    if (s is null) { orphans++; continue; }
                    s.TerminateRows++;
                    if (s.Accepted != "da") { s.Accepted = "nu"; s.LoginAborted = true; }
                    if (open.Contains(s)) Close(s, local, rawTime, line, "SessionTerminate (fără sesiune: autentificare abandonată)");
                }
                else if (msg.Contains("AbortLogin", StringComparison.Ordinal) && AbortLogin.Match(msg) is { Success: true } ab)
                {
                    var s = BySid(ab.Groups["sid"].Value);
                    if (s is null) { orphans++; continue; }
                    if (s.Accepted != "da") s.Accepted = "nu";
                    s.LoginAborted = true;
                }
                else if (msg.Contains("CMD_ENDSESSION", StringComparison.Ordinal) && EndCmd.Match(msg) is { Success: true } ec)
                {
                    var s = Target("", false, "");
                    if (s is null) { orphans++; continue; }
                    s.EndCommand = RemoteToolLogText.Sane(ec.Groups["r"].Value, 40);
                    if (s.EndCommand == "LoginFailed" && s.Accepted != "da") s.Accepted = "nu";
                }
                else if (msg.Contains("Step_Receive_VerifyClientSecret", StringComparison.Ordinal))
                {
                    var s = Target("", false, "");
                    if (s is null) orphans++; else s.SrpMismatch = true;
                }
                else if (msg.Contains("SessionStatistics", StringComparison.Ordinal) && Ended.Match(msg) is { Success: true } end)
                {
                    var id = RemoteToolLogText.IdField(end.Groups["id"].Value, out var ok);
                    var s = ok ? open.LastOrDefault(x => x.RemoteId == id) : null;
                    if (s is not null) { s.HasStats = true; Close(s, local, rawTime, line, "SessionStatistics"); }                // failed login: the statistics row comes before Terminate
                    else if (ok && closedByRemote.TryGetValue(id, out var closed) && !closed.HasStats) closed.HasStats = true;      // accepted session: Terminate came first
                    else orphans++;
                }
                else if (msg.Contains("DyngateID", StringComparison.Ordinal) && Allocate.Match(msg) is { Success: true } al)
                {
                    var id = RemoteToolLogText.IdField(al.Groups["id"].Value, out var ok);
                    var s = Target(ok ? id : "", true, "Începutul sesiunii lipsește din jurnal (fișier rotit sau tăiat); sesiune reconstituită din marcajul de autentificare.");
                    if (s is null) continue;
                    if (s.RemoteId.Length == 0) { s.RemoteId = id; s.RemoteIdOk = ok; }
                    else if (s.RemoteId != id) AddOther(s, id);
                    s.Attempts++;
                    if (int.TryParse(al.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > s.MaxAttempt) s.MaxAttempt = n;
                    if (!al.Groups["res"].Value.Equals("ok", StringComparison.OrdinalIgnoreCase)) s.BlockedAlloc++;
                }
                else if (msg.Contains("RunAuthenticationMethod", StringComparison.Ordinal) && AuthRun.Match(msg) is { Success: true } ar)
                {
                    var s = Target("", true, "Începutul sesiunii lipsește din jurnal (fișier rotit sau tăiat); sesiune reconstituită din marcajul de autentificare.");
                    if (s is null) continue;
                    var txt = ar.Groups["txt"].Value.Trim();
                    string method = "", resultText = "";
                    if (AuthUsing.Match(txt) is { Success: true } au) { method = au.Groups["m"].Value.Trim(); resultText = au.Groups["r"].Value.Trim(); }
                    else method = txt;
                    s.Method = RemoteToolLogText.Sane(method, 64);
                    // only what the real log shows: "was successful" = accepted, "was denied" = refused; any other text claims nothing and never overrides a known result
                    if (resultText.Equals("successful", StringComparison.OrdinalIgnoreCase)) s.Accepted = "da";
                    else if (resultText.Equals("denied", StringComparison.OrdinalIgnoreCase)) { s.Accepted = "nu"; s.FailedAuth++; }
                }
                else if (msg.Contains("AddParticipant", StringComparison.Ordinal) && Participant.Match(msg) is { Success: true } pa)
                {
                    var id = RemoteToolLogText.IdField(pa.Groups["id"].Value, out var ok);
                    if (pa.Groups["t"].Value != "6")
                    {
                        // other participant types are not the partner of an incoming session: counted on the open session only
                        if (open.Count > 0) open[^1].OtherParticipants++; else orphans++;
                        continue;
                    }
                    var s = Target(ok ? id : "", true, "Începutul sesiunii lipsește din jurnal (fișier rotit sau tăiat); sesiune reconstituită din marcajul de participant.");
                    if (s is null) continue;
                    if (s.RemoteId.Length == 0) { s.RemoteId = id; s.RemoteIdOk = ok; }
                    else if (s.RemoteId != id) AddOther(s, id);
                    if (pa.Groups["name"].Success) s.Name = RemoteToolLogText.Sane(pa.Groups["name"].Value, 128);
                }
                else if (msg.Contains("punch received", StringComparison.Ordinal) && Punch.Match(msg) is { Success: true } pu)
                {
                    var s = Target("", false, "");
                    if (s is null) { orphans++; continue; }
                    var ip = PunchAddress(pu.Groups["tok"].Value, out var zoneStripped);
                    if (ip.Length == 0) { s.InvalidIps++; continue; }
                    if (zoneStripped) s.ZoneIdStripped = true;
                    if (s.Ips.Count < MaxIpsPerSession && !s.Ips.Contains(ip)) s.Ips.Add(ip);
                }
                else if (msg.Contains("ReceivedInfo", StringComparison.Ordinal) && Info.Match(msg) is { Success: true } inf)
                {
                    var id = RemoteToolLogText.IdField(inf.Groups["id"].Value, out var ok);
                    var s = Target(ok ? id : "", true, "Începutul sesiunii lipsește din jurnal (fișier rotit sau tăiat); sesiune reconstituită din marcajul de versiune.");
                    if (s is null) continue;
                    if (s.RemoteId.Length == 0) { s.RemoteId = id; s.RemoteIdOk = ok; }
                    else if (s.RemoteId != id) AddOther(s, id);
                    var v = inf.Groups["v"].Value.Trim();
                    s.Version = VersionText.IsMatch(v) ? v : "format necunoscut: " + RemoteToolLogText.Sane(v, 32);
                    s.OsCode = RemoteToolLogText.Sane(inf.Groups["os"].Value, 16);
                }
                else otherRows++;
            }
            catch (RegexMatchTimeoutException) { result.MalformedRecords++; }
        }

        // sessions still open at the end of the file have no end in it
        foreach (var s in open.ToList()) Close(s, null, "", 0, "jurnalul se termină înainte de sfârșitul sesiunii");

        foreach (var s in done.OrderBy(x => x.StartLine))
        {
            ct.ThrowIfCancellationRequested();
            if (result.Records >= MaxEvents) { capHit = true; break; }
            sink.Add(BuildEvent(s, item, file, now));
            result.Records++;
        }
        if (capHit && !result.Gaps.Any(g => g.Reason.StartsWith("peste ", StringComparison.Ordinal)))
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"plafon de {MaxEvents} de evenimente atins; restul sesiunilor nu au fost scrise", "Sesiunile de după plafon lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
        if (orphans > 0 || badDates > 0)
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"{orphans} de marcaje fără sesiune deschisă și {badDates} de rânduri cu dată imposibilă au fost numărate, nu transformate în evenimente",
                "Marcajele nu apar în cronologie", "Fișierul brut din caz", "Posibil", $"rânduri de antet: {headerRows}, rânduri „Start:”: {startRows}, alte rânduri: {otherRows}"));
    }

    private static void AddOther(Sess s, string id)
    {
        var clean = RemoteToolLogText.Sane(id, RemoteToolLogText.MaxIdChars);
        if (s.OtherIds.Length < 200 && !s.OtherIds.Contains(clean, StringComparison.Ordinal)) s.OtherIds += (s.OtherIds.Length == 0 ? "" : ", ") + clean;
        AddAmbiguity(s, "markere cu un alt ID de partener decât cel deja atașat sesiunii (ID-ul inițial a fost păstrat)");
    }

    /// <summary>Adds a reason to the session's ambiguity note; an earlier reason is never overwritten.</summary>
    private static void AddAmbiguity(Sess s, string why)
    {
        if (s.Ambiguity.Contains(why, StringComparison.Ordinal) || s.Ambiguity.Length > 600) return;
        s.Ambiguity = s.Ambiguity.Length == 0 ? why : s.Ambiguity + "; " + why;
    }

    private static bool IsStartRow(string l)
    {
        try { return StartAny.IsMatch(l); } catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>"192.168.1.5:51641:" or "fe80::a503:bfed:c231:e6f1%5:51640:" to a validated address; the port and the zone index are removed (<paramref name="zoneStripped"/>). "" when it is not an address.</summary>
    private static string PunchAddress(string tok, out bool zoneStripped)
    {
        zoneStripped = false;
        tok = tok.TrimEnd(':');
        string host = tok;
        if (tok.StartsWith('[') && tok.IndexOf(']') is var close and > 0) host = tok[1..close];
        else
        {
            int colon = tok.LastIndexOf(':');
            // the port is always written by TeamViewer: after the last colon, digits only
            if (colon > 0 && tok[(colon + 1)..].Length is > 0 and <= 5 && tok[(colon + 1)..].All(char.IsAsciiDigit)) host = tok[..colon];
        }
        int pct = host.IndexOf('%');
        if (pct >= 0) { host = host[..pct]; zoneStripped = true; }
        return RemoteToolLogText.ValidIp(host);
    }

    private static bool IsLinkLocal(string ip) =>
        IPAddress.TryParse(ip, out var a) && (a.IsIPv6LinkLocal || (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && a.GetAddressBytes() is [169, 254, ..]));

    private static bool TryStart(string l, out RemoteToolLogText.Zone zone, out string label)
    {
        zone = default; label = "";
        Match m;
        try { m = StartRow.Match(l); } catch (RegexMatchTimeoutException) { return false; }
        if (!m.Success) return false;
        var date = m.Groups["d"].Value;
        if (!DateTime.TryParseExact(date, ["yyyy/MM/dd HH:mm:ss.FFF", "yyyy/MM/dd HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
        int h = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture), mi = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        if (mi > 59 || h > 14 || (h == 14 && mi > 0)) return false;
        var off = new TimeSpan(h, mi, 0);
        var sign = m.Groups["sg"].Value;
        if (sign == "-") off = off.Negate();
        label = $"UTC{sign}{h}:{mi:00}";
        var tz = TimeZoneInfo.CreateCustomTimeZone(label, off, label, label);
        zone = new RemoteToolLogText.Zone(tz, $"decalaj {label} din antetul „Start:” al jurnalului");
        return true;
    }

    private static TimelineEvent BuildEvent(Sess s, EvidenceItem item, string file, DateTime now)
    {
        var st = RemoteToolLogText.ToTimestamp(s.Start, s.StartRaw, s.Zone, now);
        RemoteToolLogText.LocalTime? en = s.End is { } e ? RemoteToolLogText.ToTimestamp(e, s.EndRaw, s.Zone, now) : null;
        string duration = "", durationProblem = "";
        if (s.End is { } end)
        {
            var span = end - s.Start;
            if (span < TimeSpan.Zero) durationProblem = "interval invers: sfârșitul este înaintea începutului";
            else duration = ((long)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }
        // the highest "attempt number K" the log shows, not the number of rows (the partner's retries are numbered by TeamViewer)
        int attempts = s.MaxAttempt > 0 ? s.MaxAttempt : s.Attempts;
        string kind = s.Accepted == "nu" ? "FailedAuthentication" : "IncomingSession";
        // first valid address that is not link-local; a link-local one only when it is the only kind seen
        var ip = s.Ips.FirstOrDefault(a => !IsLinkLocal(a)) ?? "";
        string ipNote = "";
        if (ip.Length == 0 && s.Ips.Count > 0) { ip = s.Ips[0]; ipNote = "doar adrese link-local (nu pot fi adrese din afara segmentului de rețea local)"; }
        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tool Id"] = ToolId, ["Tool"] = "TeamViewer", ["Session Evidence"] = "da",
            ["Kind"] = kind, ["Direction"] = "Incoming", ["Accepted"] = s.Accepted,
            ["Remote Id"] = s.RemoteId, ["Remote Id Valid"] = s.RemoteIdOk ? "da" : "nu",
            ["Partner Name"] = s.Name, ["Auth Method"] = s.Method, ["Auth Attempts"] = attempts.ToString(CultureInfo.InvariantCulture),
            ["Failed Authentications"] = s.FailedAuth.ToString(CultureInfo.InvariantCulture),
            ["Client Version"] = s.Version, ["Client OS Code"] = s.OsCode, ["Session Id"] = s.SessionId,
            ["Local Time"] = RemoteToolLogText.LocalText(s.Start), ["Time Raw"] = RemoteToolLogText.Sane(s.StartRaw, 40),
            ["End Local Time"] = s.End is { } el ? RemoteToolLogText.LocalText(el) : "",
            ["End Time UTC"] = en?.Time.Utc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            ["Duration Seconds"] = duration,
            ["End Reason"] = s.EndReason,
            ["Time Zone Used"] = s.Zone.Info?.Id ?? "necunoscut", ["Time Zone Source"] = s.ZoneSource,
            ["Source File"] = RemoteToolLogText.Sane(file, 128), ["Line"] = s.StartLine.ToString(CultureInfo.InvariantCulture),
            ["End Line"] = s.EndLine > 0 ? s.EndLine.ToString(CultureInfo.InvariantCulture) : "",
            ["Limitation"] = "Jurnalul poate fi falsificat de cine are acces la stație; ora este locală. Transferurile de fișiere nu sunt interpretate.",
        };
        if (ip.Length > 0)
        {
            f["IP"] = ip; f["IP Status"] = "valid";
            f["IP Meaning"] = "adresa văzută în „punch received” (UDP); nu este dovedită ca adresa publică a partenerului";
            if (ipNote.Length > 0) f["IP Note"] = ipNote;
            if (s.Ips.Count > 1) f["IP All"] = string.Join(", ", s.Ips);
        }
        if (s.InvalidIps > 0) f["IP Invalid Count"] = s.InvalidIps.ToString(CultureInfo.InvariantCulture);
        if (s.ZoneIdStripped) f["IP Zone Stripped"] = "da (indicele de zonă %n a fost eliminat din adresă)";
        if (s.SrpMismatch) f["SRP Secret Mismatch"] = "da";
        if (s.EndCommand.Length > 0) f["End Command Reason"] = s.EndCommand;
        if (s.LoginAborted) f["Login Aborted"] = "da";
        if (s.BlockedAlloc > 0) f["Allocate Not Ok"] = s.BlockedAlloc.ToString(CultureInfo.InvariantCulture);
        if (s.OtherParticipants > 0) f["Other Participants"] = s.OtherParticipants.ToString(CultureInfo.InvariantCulture);
        if (s.OtherIds.Length > 0) f["Other Remote Ids"] = s.OtherIds;
        if (s.Ambiguity.Length > 0) f["Ambiguity"] = s.Ambiguity;
        if (s.Implicit) f["Session Start Missing"] = "da";
        if (s.Note.Length > 0) f["Note"] = s.Note;
        if (s.Truncated) f["Line Truncated"] = $"da (peste {RemoteToolLogText.MaxLineChars} de caractere)";
        if (st.Problem.Length > 0) f["Time Problem"] = st.Problem;
        if (durationProblem.Length > 0) f["Duration Problem"] = durationProblem;

        var who = s.RemoteIdOk ? $"partener {s.RemoteId}" : s.RemoteId.Length == 0 ? "partener fără ID" : "partener cu ID de format necunoscut";
        var what = kind == "FailedAuthentication" ? "autentificare eșuată la o sesiune de intrare" + (attempts >= 2 ? $" ({attempts} încercări de autentificare)" : "") : s.Accepted == "da" ? "sesiune de intrare acceptată" : "sesiune de intrare (acceptarea nu reiese din jurnal)";
        return new TimelineEvent
        {
            Time = st.Time,
            TimeSemantics = "Ora rândului care deschide sesiunea în jurnalul principal TeamViewer: ora locală a stației (decalajul UTC vine din rândul „Start:” al antetului, dacă există)",
            TimeZoneBasis = st.Basis, TimeUncertainty = st.Uncertainty,
            Source = EventSource, EvidenceId = item.EvidenceId, Provider = "TeamViewer " + RemoteToolLogText.Sane(file, 64), EventId = kind,
            Summary = $"TeamViewer (jurnal): {what}, {who}" + (ip.Length > 0 ? $", adresa {ip}" : "") + $", început local {RemoteToolLogText.LocalText(s.Start)}" +
                      (duration.Length > 0 ? $", durată {duration} s" : ", fără sfârșit în jurnal"),
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.Medium,
            Locator = $"{RemoteToolLogText.Sane(file, 64)}:line={s.StartLine}", Fields = f,
        };
    }
}
