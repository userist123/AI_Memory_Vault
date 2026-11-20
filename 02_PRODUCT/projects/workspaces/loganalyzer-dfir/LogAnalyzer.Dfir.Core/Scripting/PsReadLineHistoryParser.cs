using System.Globalization;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.RemoteTools;

namespace LogAnalyzer.Dfir.Scripting;

/// <summary>
/// The PowerShell PSReadLine history (<c>%APPDATA%\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt</c>): plain text, one command per line, no times.
/// One timeline event per non-blank line, locator <c>line=N</c> (N = physical line, counted as <c>Get-Content</c> counts them), the original line kept unchanged in
/// <c>Fields["RawLine"]</c> so the quote of a finding is the text of the file. The file has no times: the events have an unknown time (never the file's modification
/// time presented as the time of a command). Only commands typed in an interactive console that loads PSReadLine are written there.
/// </summary>
public sealed class PsReadLineHistoryParser : EvidenceParserBase
{
    public const string SourceTypeName = "ps_readline_history";
    public const string EventSource = "PsReadLineHistory";
    public const string FileName = "ConsoleHost_history.txt";
    /// <summary>PSReadLine names the file after the host: <c>ConsoleHost_history.txt</c> (console), <c>Visual Studio Code Host_history.txt</c> (VS Code integrated terminal); both are the same text format.
    /// Windows PowerShell ISE does NOT use PSReadLine and writes no history file of this kind (there is no source to collect for it).</summary>
    public const string FileSuffix = "_history.txt";
    public static bool IsHistoryFileName(string name) => name.Length > FileSuffix.Length && name.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase);
    public const long MaxFileBytes = 16L * 1024 * 1024;
    public const int MaxLines = 100_000;
    public const int MaxLineChars = 32_768;
    /// <summary>Plain text has no signature: it is read as "unknown" (or "json" when a command starts with "[" or "{").</summary>
    public static readonly string[] AcceptedFormats = ["unknown", "json"];

    public override ParserDescriptor Descriptor { get; } = new()
    {
        ParserId = "PsReadLineHistoryParser", Version = "1.0", Artifact = "Istoricul PowerShell PSReadLine (ConsoleHost_history.txt)",
        SourceTypes = [SourceTypeName], FileNames = [], Fingerprints = AcceptedFormats, MaxInputBytes = MaxFileBytes,
        SupportedOs = "Windows cu PowerShell și modulul PSReadLine (text, citit în cod gestionat)",
        FormatVersions = ["Text, o comandă pe rând (PSReadLine 2.x din Windows PowerShell 5.1 și PowerShell 7); o comandă pe mai multe rânduri apare ca mai multe rânduri"],
        Limitations =
        [
            "Fișierul nu are ore: evenimentele nu au oră pe cronologie și nu se poate spune când a fost tastată o comandă.",
            "Cuprinde doar comenzile tastate într-o consolă care încarcă PSReadLine (nu ISE, nu scripturile, nu powershell.exe -Command); PSReadLine poate fi dezactivat, iar istoricul poate fi șters sau modificat de cine are acces la fișier.",
            "Păstrează cel mult 4096 de comenzi (valoarea implicită MaximumHistoryCount); cele mai vechi se pierd. Comenzile care par să conțină parole pot fi omise de PSReadLine.",
            "Un rând nu spune ce cont a tastat comanda: contul este cel al profilului din care a fost copiat fișierul (câmpul Profile).",
            $"Cel mult {MaxLineChars} de caractere pe rând și {MaxLines} de rânduri; restul este raportat ca lacună.",
        ],
        Status = ParserMaturity.Tested,
        Validation = "PsReadLineHistoryTests: fișiere sintetice (rânduri goale, BOM, comandă lungă, locator line=N, RawLine neschimbat)",
    };

    protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult result, CancellationToken ct)
    {
        var file = Path.GetFileName(fullPath);
        var profile = item.Source.Contains(':') ? item.Source[(item.Source.IndexOf(':') + 1)..] : "";
        int lineNo = 0, longLines = 0;
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = RemoteToolLogText.OpenReader(fs);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            lineNo++;
            if (lineNo > MaxLines)
            {
                result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"peste {MaxLines} de rânduri; restul nu au fost citite", "Comenzile de după limită lipsesc din cronologie", "Fișierul brut din caz", "Posibil"));
                break;
            }
            if ((lineNo & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            if (lineNo == 1) line = line.TrimStart('﻿');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var raw = line;
            if (raw.Length > MaxLineChars)
            {
                longLines++;
                raw = raw[..(char.IsHighSurrogate(raw[MaxLineChars - 1]) ? MaxLineChars - 1 : MaxLineChars)];
            }
            var cmd = RemoteToolLogText.Sane(raw.Trim(), MaxLineChars);
            sink.Add(new TimelineEvent
            {
                Time = Timestamp.Unknown(),
                TimeSemantics = "PSReadLine nu scrie ora comenzii: fișierul nu are ore",
                Source = EventSource, EvidenceId = item.EvidenceId, Provider = "PSReadLine", EventId = "Command",
                Summary = "Comandă PowerShell în istoricul PSReadLine: " + RemoteToolLogText.Sane(cmd, 200) + (cmd.Length > 200 ? "…" : ""),
                TemporalType = TemporalType.Historical, Classification = Classification.Direct, Confidence = Confidence.High,
                Locator = $"line={lineNo}",
                Fields =
                {
                    ["Artifact"] = "PSReadLine history", ["Command"] = cmd, ["RawLine"] = raw,
                    ["Line"] = lineNo.ToString(CultureInfo.InvariantCulture), ["Source File"] = RemoteToolLogText.Sane(file, 128),
                    ["Profile"] = RemoteToolLogText.Sane(profile, 128),
                },
            });
            result.Records++;
        }
        if (longLines > 0)
            result.Gaps.Add(new EvidenceGap(file, EvidenceStatus.Partial, $"{longLines} rânduri depășesc {MaxLineChars} de caractere și au fost tăiate în cronologie", "Sfârșitul rândurilor lungi lipsește din cronologie", "Fișierul brut din caz", "Posibil"));
    }
}
