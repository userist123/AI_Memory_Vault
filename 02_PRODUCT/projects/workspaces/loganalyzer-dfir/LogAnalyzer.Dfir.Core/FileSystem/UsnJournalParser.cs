using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// NTFS change journal as exported by Windows itself: <c>fsutil usn readjournal C: csv</c>. One event per completed operation
/// (the "Close" record, which carries every reason accumulated for that open of the file). fsutil prints times without a zone,
/// in the local time of the station that ran it; they are converted with an explicit zone and the raw text is kept.
/// The journal ID is kept on every event: read as a FILETIME it is the moment the journal was created.
/// </summary>
public sealed class UsnJournalParser(TimeZoneInfo? zone = null) : EvidenceParserBase
{
    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "UsnJournalParser", Version = "1.0", Artifact = "Jurnalul de modificări NTFS (USN), export fsutil",
        SourceTypes = ["usn_journal"], FileNames = [], Fingerprints = ["usn_fsutil"],
        SupportedOs = "Oricare (text produs de fsutil usn readjournal … csv pe Windows 10/11)",
        FormatVersions = ["fsutil usn readjournal csv (înregistrări USN v2–v4)"],
        Limitations =
        [
            "Ora din fsutil este ora locală a stației care a rulat exportul; se convertește cu fusul orar al stației de analiză (corect dacă e aceeași stație sau același fus). Pe corpus, conversia a fost verificată față de orele de rulare din Prefetch (UTC).",
            "Se emite câte un eveniment pentru fiecare înregistrare „Close”; înregistrările intermediare ale aceleiași operații sunt incluse în motivele ei.",
            "Calea completă este reconstruită din înregistrările jurnalului (nume + ID părinte); când un părinte nu apare în jurnal, calea începe cu „…”.",
            "Jurnalul acoperă doar intervalul păstrat de Windows (implicit 32 MB); ce e mai vechi nu mai există în el.",
        ],
        Status = ParserMaturity.Validated,
        Validation = "UsnJournalTests: două exporturi reale (corpus) identice pe USN-urile comune; orele convertite comparate cu Prefetch; ID-ul jurnalului comparat cu data instalării (SOFTWARE)",
    };

    public sealed record JournalHeader(string JournalId, long FirstUsn, long NextUsn);

    /// <summary>The header block above the CSV (USN Journal ID, First USN, Next USN) and the line where the CSV header starts.</summary>
    public static (JournalHeader Header, int CsvStart) ReadHeader(IReadOnlyList<string> lines)
    {
        string id = ""; long first = -1, next = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i].TrimStart('﻿');
            if (l.StartsWith("Usn,", StringComparison.Ordinal)) return (new JournalHeader(id, first, next), i);
            int colon = l.IndexOf(':');
            if (colon < 0) continue;
            var key = l[..colon].Trim();
            var val = l[(colon + 1)..].Trim();
            if (key.Equals("USN Journal ID", StringComparison.OrdinalIgnoreCase)) id = val;
            else if (key.Equals("First USN", StringComparison.OrdinalIgnoreCase)) first = ParseNumber(val);
            else if (key.Equals("Next USN", StringComparison.OrdinalIgnoreCase)) next = ParseNumber(val);
        }
        throw new InvalidDataException("Exportul nu conține antetul CSV „Usn,File name,…”.");
    }

    private static long ParseNumber(string v) =>
        v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? long.Parse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                                                               : long.Parse(v, CultureInfo.InvariantCulture);

    /// <summary>The journal ID read as a FILETIME: when the journal was created. Null when it is not a plausible time.</summary>
    public static DateTimeOffset? JournalCreatedUtc(string journalId)
    {
        try
        {
            var ft = ParseNumber(journalId);
            var t = DateTime.FromFileTimeUtc(ft);
            return t.Year is >= 2000 and <= 2100 ? new DateTimeOffset(t) : null;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException) { return null; }
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var lines = File.ReadAllLines(fullPath, Encoding.UTF8);   // a UTF-16 export is recognised by its BOM
        var (header, start) = ReadHeader(lines);
        var rows = CsvReader.ReadRows(new StringReader(string.Join("\n", lines.Skip(start)))).ToList();
        var h = rows[0].Select(x => x.Trim()).ToList();
        int I(string name) => h.IndexOf(name) is var i and >= 0 ? i : throw new InvalidDataException($"Exportul USN nu are coloana „{name}”.");
        int iUsn = I("Usn"), iName = I("File name"), iReasonCode = I("Reason #"), iReason = I("Reason"), iTime = I("Time stamp"),
            iAttr = I("File attributes"), iId = I("File ID"), iParent = I("Parent file ID");

        // Names and parents from every record, for path reconstruction (the last name seen for an ID wins).
        var names = new Dictionary<string, (string Name, string Parent)>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows.Skip(1).Where(r => r.Length > iParent)) names[r[iId]] = (r[iName], r[iParent]);
        string PathOf(string id)
        {
            var parts = new List<string>();
            for (int depth = 0; depth < 64 && names.TryGetValue(id, out var n); depth++)
            {
                parts.Add(n.Name);
                if (n.Parent == id) break;
                id = n.Parent;
            }
            parts.Reverse();
            return (names.ContainsKey(id) ? "" : "…\\") + string.Join("\\", parts);
        }

        foreach (var r in rows.Skip(1))
        {
            ct.ThrowIfCancellationRequested();
            if (r.Length <= iParent) { result.MalformedRecords++; continue; }
            if (!r[iReason].Contains("Close", StringComparison.Ordinal)) continue;
            Timestamp time;
            if (DateTime.TryParseExact(r[iTime], "dd-MMM-yy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);
                time = Timestamp.FromUtc(utc, r[iTime], $"fsutil, ora locală convertită cu {_zone.Id}" + (_zone.IsAmbiguousTime(local) ? " (oră ambiguă la schimbarea orei)" : ""));
            }
            else time = Timestamp.Unknown(r[iTime]);
            var path = PathOf(r[iId]);
            sink.Add(new TimelineEvent
            {
                Time = time, Source = "USN", EvidenceId = item.EvidenceId, Path = path, Process = "",
                Summary = $"USN: {r[iName]} — {r[iReason]}", TimeSemantics = "change journal record (operation closed)",
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = $"USN={r[iUsn]}",
                Fields =
                {
                    ["Usn"] = r[iUsn], ["FileName"] = r[iName], ["Reason"] = r[iReason], ["ReasonCode"] = r[iReasonCode],
                    ["Attributes"] = r[iAttr], ["FileId"] = r[iId], ["ParentFileId"] = r[iParent], ["JournalId"] = header.JournalId,
                    ["FirstUsn"] = header.FirstUsn.ToString(CultureInfo.InvariantCulture), ["NextUsn"] = header.NextUsn.ToString(CultureInfo.InvariantCulture),
                },
            });
            result.Records++;
        }
    }
}
