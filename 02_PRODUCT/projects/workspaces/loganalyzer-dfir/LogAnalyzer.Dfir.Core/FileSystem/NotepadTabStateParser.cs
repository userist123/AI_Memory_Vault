using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// Windows 11 Notepad (Store app) tab state: one file per tab in
/// <c>%LOCALAPPDATA%\Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\TabState\&lt;guid&gt;.bin</c> (and <c>.0.bin</c> / <c>.1.bin</c>). The file exists for text
/// that was never saved and stays after Notepad is closed. Layout (verified on Notepad 11.2312.18.0, see the fixtures): "NP", sequence number, a "saved to a file" byte;
/// when 1: path length and UTF-16 path, saved content length (bytes of the file), encoding byte, line-ending byte, last write time of the file (FILETIME as uLEB128),
/// SHA-256 of the saved file, 2 unknown bytes; when 0: 1 unknown byte. Then selection start and end, 4 option bytes, content length (characters) and UTF-16 content, an
/// "unsaved changes" byte and a big-endian CRC32 of everything from the "saved" byte up to it. Unsaved "chunks" (position, deleted, added, added text, CRC32) may follow.
/// All numbers are uLEB128. The file is untrusted data: a number that does not end or does not fit 64 bits, a length that goes past the file, the file size, the chunk
/// count and the content shown are all capped; a wrong CRC32 keeps the event (with "CRC Valid" = nu and a written limitation); a damaged file gives what could be read and
/// never an exception; the path inside the tab is only text (it is never opened). WindowState files start with "NP" too but are not tabs: they are not interpreted.
/// </summary>
public sealed class NotepadTabStateParser : EvidenceParserBase
{
    public const string SourceTypeName = "notepad_tabstate";
    public const string FingerprintName = "notepad_tab";
    public const string EventSource = "Artifact:NotepadTabState";
    public const string CollectedBy = "UserArtifactsCollector";
    public const long MaxFileBytes = 4L * 1024 * 1024;
    public const int MaxChunks = 10_000;
    public const int MaxContentChars = 64 * 1024;
    public const int MaxSummaryChars = 300;
    public const int MaxPathChars = 32_768;

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "NotepadTabStateParser", Version = "1.0", Artifact = "Filele Notepad din Windows 11 (TabState)",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = [FingerprintName], MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows 11 cu aplicația Notepad din Store (format binar citit în cod gestionat)",
        FormatVersions = ["„NP”, uLEB128, cale UTF-16 opțională, conținut UTF-16, CRC32 big-endian; Notepad 11.2312.18.0"],
        Limitations =
        [
            "Formatul nu este documentat de Microsoft: câmpurile sunt cele verificate pe două mostre din laborator (filă nesalvată și filă a unui fișier salvat, cu text adăugat nesalvat); codurile de codificare și de sfârșit de rând sunt traduse doar pentru valorile văzute sau publicate.",
            "Fișierul arată ce text avea fila la ultima scriere a stării de către Notepad, nu când a fost tastat; o filă nesalvată nu are oră (nu se inventează una). Ora evenimentului este ora ultimei scrieri a fișierului salvat, când există.",
            "Bucățile nesalvate sunt numărate, nu aplicate peste conținut, iar CRC32-ul lor nu este verificat; doar CRC32-ul conținutului este validat.",
            "Un CRC32 greșit nu elimină înregistrarea: câmpul „CRC Valid” este „nu” și conținutul poate fi alterat sau scris parțial.",
            "Calea din filă este textul scris de Notepad; fișierul indicat nu este deschis și poate să nu mai existe sau să fie altul. Copiile .0.bin / .1.bin și <guid>.bin sunt evenimente separate (pot avea stări diferite).",
            "Fișierele WindowState (încep tot cu „NP”) nu sunt interpretate.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "NotepadTabStateTests: două mostre reale din laborator (Notepad 11.2312.18.0), mostrele trunchiate la fiecare decalaj, uLEB128 nesfârșit sau peste 64 de biți, lungimi uriașe, FILETIME invalid, CRC32 greșit, surogate rupte, caractere de control",
    };

    /// <summary>What one tab file says. Null / empty where the file does not say it.</summary>
    public sealed class Tab
    {
        public bool MagicOk { get; internal set; }
        /// <summary>False for a file that starts with "NP" but whose "saved" byte is not 0/1 (WindowState and other formats).</summary>
        public bool IsTab { get; internal set; }
        /// <summary>True when the "saved" byte is neither 0 nor 1: a WindowState file or another format, not a damaged tab.</summary>
        public bool NotTab { get; internal set; }
        public ulong Sequence { get; internal set; }
        public bool Saved { get; internal set; }
        /// <summary>The path was read completely (always true for an unsaved tab once the saved byte is read).</summary>
        public bool HeaderRead { get; internal set; }
        public string SavedPath { get; internal set; } = "";
        public ulong? SavedContentBytes { get; internal set; }
        public int? Encoding { get; internal set; }
        public int? LineEnding { get; internal set; }
        public DateTime? SavedFileTimeUtc { get; internal set; }
        public ulong? RawFileTime { get; internal set; }
        public string SavedSha256 { get; internal set; } = "";
        public ulong? SelectionStart { get; internal set; }
        public ulong? SelectionEnd { get; internal set; }
        public bool ContentRead { get; internal set; }
        public ulong ContentChars { get; internal set; }
        public string Content { get; internal set; } = "";
        public bool ContentTruncated { get; internal set; }
        public bool? UnsavedChanges { get; internal set; }
        public bool? CrcValid { get; internal set; }
        public int Chunks { get; internal set; }
        public bool Complete { get; internal set; }
        public string StopReason { get; internal set; } = "";
    }

    /// <summary>Quick content test for the fingerprint: "NP", a sequence number and a "saved" byte that is 0 or 1 (a WindowState file has another value there).</summary>
    public static bool LooksLikeTab(ReadOnlySpan<byte> head, long length)
    {
        // no upper size bound here: an oversized tab must still reach the parser, which records the gap ("depășește 4 MiB") instead of the file
        // silently becoming a plain "file"
        if (length < 8 || head.Length < 4 || head[0] != (byte)'N' || head[1] != (byte)'P') return false;
        int p = 2;
        if (!ULeb(head, ref p, out _) || p >= head.Length) return false;
        return head[p] <= 1;
    }

    /// <summary>
    /// A tab file name: a GUID with ".bin", ".0.bin" or ".1.bin". <paramref name="copy"/> is "" for the plain name, otherwise "0" or "1".
    /// </summary>
    public static bool TryTabFileName(string name, out string tabId, out string copy)
    {
        tabId = ""; copy = "";
        if (!name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = name[..^4];
        if (stem.EndsWith(".0", StringComparison.Ordinal)) { copy = "0"; stem = stem[..^2]; }
        else if (stem.EndsWith(".1", StringComparison.Ordinal)) { copy = "1"; stem = stem[..^2]; }
        if (!Guid.TryParseExact(stem, "D", out var g)) { copy = ""; return false; }
        tabId = g.ToString("D");
        return true;
    }

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        byte[] data;
        var file = Path.GetFileName(fullPath);
        using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var buf = new byte[(int)Math.Min(fs.Length, MaxFileBytes + 1)];
            int n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            if (n > MaxFileBytes)
            {
                result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Failed, $"fișierul depășește {MaxFileBytes} octeți", "Fila nu a fost citită", "Fișierul brut din caz", "Posibil"));
                return;
            }
            data = buf.AsSpan(0, n).ToArray();
        }
        var t = Decode(data);
        if (!t.MagicOk)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, "fișierul nu începe cu „NP”", "Fila nu poate fi folosită", "Fișierul brut din caz", "Posibil"));
            return;
        }
        if (!t.IsTab && !t.NotTab)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"antet nevalid după „NP” ({t.StopReason})", "Fila nu poate fi folosită", "Fișierul brut din caz", "Posibil"));
            return;
        }
        if (t.NotTab)
        {
            // WindowState (window and tab order) or a format this parser does not know: noted, not an error
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.NotAvailable, "fișier „NP” care nu este o filă (probabil WindowState): nu este interpretat", "Ordinea filelor nu este citită", "Fișierul brut din caz", "Nu"));
            return;
        }
        if (!t.HeaderRead && !t.ContentRead)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"fișier trunchiat sau deteriorat înainte de conținut ({t.StopReason})", "Fila nu poate fi folosită", "Fișierul brut din caz", "Posibil"));
            return;
        }
        if (!t.Complete)
        {
            result.MalformedRecords++;
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"citirea s-a oprit înaintea sfârșitului fișierului ({t.StopReason})", "Pot lipsi conținutul sau bucățile nesalvate ale filei", "Fișierul brut din caz", "Posibil"));
        }
        if (t.CrcValid == false)
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, "CRC32-ul conținutului nu corespunde", "Conținutul filei poate fi alterat sau scris parțial; evenimentul este păstrat", "Fișierul brut din caz", "Posibil"));

        TryTabFileName(file, out var tabId, out var copy);
        var inv = CultureInfo.InvariantCulture;
        // the text stays evidence (line breaks kept) but without hidden format / control characters (RLO, BEL …)
        var shown = ContentClean(t.Content);
        // hash of exactly what the Content field holds (cleaned, before the truncation note), so it can be recomputed from the field
        var contentSha = t.ContentRead ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.Unicode.GetBytes(shown))) : "";
        if (t.ContentTruncated) shown += $" […trunchiat: primele {MaxContentChars} din {t.ContentChars} de caractere]";
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Saved File Path"] = Safe(t.SavedPath),
            ["Saved File SHA256"] = t.SavedSha256,
            ["Saved File Time"] = t.SavedFileTimeUtc?.ToString("o", inv) ?? "",
            ["Unsaved Changes"] = t.UnsavedChanges is { } u ? (u ? "da" : "nu") : "",
            ["Content Length"] = t.ContentRead ? t.ContentChars.ToString(inv) : "",
            ["Content"] = shown,
            ["Content SHA256"] = contentSha,
            ["Content Sensitivity"] = "Textul filei poate conține date personale sau secrete (parole, chei) ale utilizatorului analizat: tratați-l ca probă sensibilă; nu apare în rezumat.",
            ["Unsaved Chunks"] = t.Chunks.ToString(inv),
            ["CRC Valid"] = t.CrcValid is { } c ? (c ? "da" : "nu") : "necunoscut",
            ["Tab ID"] = tabId,
            ["Copy Index"] = copy,
            ["Sequence Number"] = t.Sequence.ToString(inv),
            ["Saved To File"] = t.Saved ? "da" : "nu",
            ["Encoding"] = t.Encoding is { } e ? EncodingName(e) : "",
            ["Line Ending"] = t.LineEnding is { } l ? LineEndingName(l) : "",
            ["Saved File Size"] = t.SavedContentBytes?.ToString(inv) ?? "",
        };
        if (t.CrcValid == false)
            fields["Limitation"] = "CRC32-ul conținutului nu corespunde: conținutul poate fi alterat sau scris parțial; evenimentul este păstrat ca semnal.";
        // the summary goes to timeline.csv, reports and the AI validation base: it carries the size of the text only. Neither the text nor a hash
        // of it: an unsalted hash plus the exact length lets a short secret ("Parola2024!") be found by a dictionary in seconds (review r2).
        var what = t.Saved ? $"pentru fișierul {Safe(t.SavedPath)}" : "nesalvată";
        var preview = !t.ContentRead ? "" : t.ContentChars == 0 ? "" : $"{LogAnalyzer.Dfir.Language.RoText.Count((long)Math.Min(t.ContentChars, (ulong)long.MaxValue), "caracter", "caractere")} de text (textul este în câmpul Content, probă sensibilă)";
        sink.Add(new TimelineEvent
        {
            Time = t.SavedFileTimeUtc is { } w ? Timestamp.FromUtc(w, t.RawFileTime?.ToString(inv) ?? "", "Notepad TabState saved-file FILETIME") : Timestamp.Unknown(),
            TimeSemantics = t.SavedFileTimeUtc is not null ? "Last write time of the saved file as recorded in the tab (not the time of typing)" : "Notepad tab state has no time (unsaved tab or no valid time in the file)",
            TimeZoneBasis = "UTC (native)",
            Source = EventSource, EvidenceId = item.EvidenceId, Provider = "Windows Notepad TabState",
            Path = Safe(t.SavedPath),
            Summary = $"Filă Notepad {what}{(t.UnsavedChanges == true ? " (cu modificări nesalvate)" : "")}: {(preview.Length > 0 ? preview : "(fără text)")}",
            TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = t.CrcValid == false || !t.Complete ? Confidence.Medium : Confidence.High,
            Locator = "TabState/" + file,
            Fields = fields,
        });
        result.Records++;
    }

    /// <summary>Text for display: line breaks and tabs become spaces, other hidden and bidi control characters are removed and named (<see cref="DefenderDetectionHistoryParser.Safe"/>).</summary>
    public static string Safe(string s) => DefenderDetectionHistoryParser.Safe(s);

    /// <summary>
    /// Content without format characters (bidi overrides, zero-width, the Tag block U+E0000-E007F used for "ASCII smuggling") and control characters
    /// other than CR, LF and TAB; U+2028 / U+2029 become LF; a lone surrogate becomes U+FFFD. Judged per code point, not per UTF-16 unit.
    /// </summary>
    internal static string ContentClean(string content)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var sb = new System.Text.StringBuilder(content.Length);
        for (int i = 0; i < content.Length;)
        {
            if (System.Text.Rune.DecodeFromUtf16(content.AsSpan(i), out var r, out int used) != System.Buffers.OperationStatus.Done) { sb.Append('�'); i += Math.Max(used, 1); continue; }
            i += used;
            var cat = System.Text.Rune.GetUnicodeCategory(r);
            if (cat == System.Globalization.UnicodeCategory.Format) continue;
            if (cat is System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator) { sb.Append('\n'); continue; }
            if (cat == System.Globalization.UnicodeCategory.Control && r.Value is not ('\r' or '\n' or '\t')) continue;
            sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    /// <summary>At most <see cref="MaxSummaryChars"/> characters of the text, sanitised, without cutting a surrogate pair.</summary>
    public static string Preview(string content)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var cut = content.Length > MaxSummaryChars ? content[..MaxSummaryChars] : content;
        if (cut.Length > 0 && char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        var s = Safe(cut).Trim();
        return content.Length > cut.Length ? s + "…" : s;
    }

    private static string EncodingName(int e) => e switch
    {
        1 => "ANSI (1)", 2 => "UTF-16 LE (2)", 3 => "UTF-16 BE (3)", 4 => "UTF-8 cu BOM (4)", 5 => "UTF-8 (5)",
        _ => $"necunoscut ({e})",
    };

    private static string LineEndingName(int e) => e switch { 1 => "CRLF (1)", 2 => "CR (2)", 3 => "LF (3)", _ => $"necunoscut ({e})" };

    /// <summary>Reads one tab file. Never throws, whatever the bytes are.</summary>
    public static Tab Decode(ReadOnlySpan<byte> bytes)
    {
        var t = new Tab();
        try { DecodeCore(bytes.ToArray(), t); }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException)
        {
            t.Complete = false;
            if (t.StopReason.Length == 0) t.StopReason = "structură nevalidă";
        }
        return t;
    }

    /// <summary>uLEB128 of at most 10 bytes that fits 64 bits; false when it does not end inside the buffer or needs more than 64 bits.</summary>
    private static bool ULeb(ReadOnlySpan<byte> d, ref int p, out ulong value)
    {
        value = 0;
        for (int i = 0; i < 10; i++)
        {
            if (p >= d.Length) return false;
            byte b = d[p++];
            if (i == 9 && b > 1) return false;   // the 10th byte may only carry bit 63
            value |= (ulong)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }

    private static void DecodeCore(byte[] d, Tab t)
    {
        if (d.Length < 2 || d[0] != (byte)'N' || d[1] != (byte)'P') { t.StopReason = "fără „NP”"; return; }
        t.MagicOk = true;
        int p = 2;
        if (!ULeb(d, ref p, out var seq)) { t.StopReason = "număr de secvență nevalid"; return; }
        t.Sequence = seq;
        if (p >= d.Length) { t.StopReason = "fișier prea scurt"; return; }
        int flagIdx = p;
        byte saved = d[p++];
        if (saved > 1) { t.NotTab = true; t.StopReason = "octetul „salvat” nu este 0/1"; return; }
        t.IsTab = true;
        t.Saved = saved == 1;
        if (t.Saved)
        {
            if (!ULeb(d, ref p, out var pathLen)) { t.StopReason = "lungimea căii nevalidă"; return; }
            if (pathLen > MaxPathChars || pathLen * 2 > (ulong)(d.Length - p))
            {
                t.StopReason = "lungimea căii depășește fișierul sau limita";
                return;
            }
            t.SavedPath = Encoding.Unicode.GetString(d, p, (int)pathLen * 2); p += (int)pathLen * 2;
            if (!ULeb(d, ref p, out var savedBytes)) { t.HeaderRead = true; t.StopReason = "lungimea fișierului salvat nevalidă"; return; }
            t.SavedContentBytes = savedBytes;
            if (p + 2 > d.Length) { t.HeaderRead = true; t.StopReason = "fișier trunchiat în antet"; return; }
            t.Encoding = d[p++]; t.LineEnding = d[p++];
            if (!ULeb(d, ref p, out var ft)) { t.HeaderRead = true; t.StopReason = "ora fișierului salvat nevalidă"; return; }
            t.RawFileTime = ft;
            if (ft > 0 && ft <= (ulong)Timestamp.MaxFileTime) t.SavedFileTimeUtc = DateTime.FromFileTimeUtc((long)ft);
            if (p + 32 + 2 > d.Length) { t.HeaderRead = true; t.StopReason = "fișier trunchiat în antet"; return; }
            t.SavedSha256 = Convert.ToHexString(d.AsSpan(p, 32)).ToLowerInvariant(); p += 32;
            p += 2;   // two unknown bytes
        }
        else
        {
            if (p >= d.Length) { t.HeaderRead = true; t.StopReason = "fișier trunchiat în antet"; return; }
            p++;      // one unknown byte (01 on the sample)
        }
        t.HeaderRead = true;
        if (!ULeb(d, ref p, out var selStart) || !ULeb(d, ref p, out var selEnd)) { t.StopReason = "selecție nevalidă"; return; }
        t.SelectionStart = selStart; t.SelectionEnd = selEnd;
        if (p + 4 > d.Length) { t.StopReason = "fișier trunchiat în opțiuni"; return; }
        p += 4;       // word wrap, RTL, show unicode, one more
        if (!ULeb(d, ref p, out var chars)) { t.StopReason = "lungimea conținutului nevalidă"; return; }
        t.ContentChars = chars;
        ulong avail = (ulong)(d.Length - p) / 2;
        if (chars > avail)
        {
            // the declared length goes past the file: what is there is shown, an odd trailing byte is dropped
            int take = (int)Math.Min(avail, MaxContentChars);
            t.Content = Encoding.Unicode.GetString(d, p, take * 2);
            t.ContentTruncated = avail > MaxContentChars;
            t.ContentRead = true;
            t.StopReason = "lungimea conținutului depășește fișierul";
            return;
        }
        int shown = (int)Math.Min(chars, MaxContentChars);
        t.Content = Encoding.Unicode.GetString(d, p, shown * 2);
        t.ContentTruncated = chars > MaxContentChars;
        p += (int)chars * 2;
        t.ContentRead = true;
        if (p + 5 > d.Length) { t.StopReason = "fișier trunchiat înaintea CRC32"; return; }
        t.UnsavedChanges = d[p++] != 0;
        uint stored = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p));
        t.CrcValid = Crc32(d.AsSpan(flagIdx, p - flagIdx)) == stored;
        p += 4;

        // unsaved chunks: position, deleted, added, added text, CRC32 (the CRC32 of a chunk is read, not checked)
        while (p < d.Length)
        {
            if (t.Chunks >= MaxChunks) { t.StopReason = $"peste {MaxChunks} de bucăți nesalvate"; return; }
            if (!ULeb(d, ref p, out _) || !ULeb(d, ref p, out _) || !ULeb(d, ref p, out var added)) { t.StopReason = "bucată nesalvată trunchiată"; return; }
            if (added > (ulong)(d.Length - p) / 2) { t.StopReason = "bucată nesalvată cu lungime peste fișier"; return; }
            p += (int)added * 2;
            if (p + 4 > d.Length) { t.StopReason = "bucată nesalvată fără CRC32"; return; }
            p += 4;
            t.Chunks++;
        }
        t.Complete = true;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    /// <summary>Standard CRC-32 (IEEE, as zlib).</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
