using System.Text;

namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// Research backlog #3 (H8; Trojan Source, CVE-2021-42574): evidence-derived text printed in a report must look like what the evidence holds.
/// <see cref="Clean"/> removes characters that change how text renders without being visible — bidi embedding/override/isolate controls
/// (U+202A–U+202E, U+2066–U+2069, U+200E/U+200F, U+061C), zero-width and invisible characters (U+200B–U+200D, U+2060–U+2064, U+FEFF, U+00AD,
/// U+FFF9–U+FFFB) and C0/C1 control characters except TAB, LF and CR — and appends a visible marker naming them, so the reader knows the
/// original contained them (the evidence itself is unchanged). Applied at render by the PDF (<c>ReportPdfText</c>), HTML (<c>HtmlText</c>) and CSV
/// (<c>Csv.Escape</c>) writers.
/// </summary>
public static class ReportText
{
    public const string MarkerPrefix = "[caractere ascunse eliminate: ";

    public static bool IsHidden(char c) => c switch
    {
        '\t' or '\n' or '\r' => false,
        < ' ' => true,                                 // C0 controls
        >= '\u007F' and <= '\u009F' => true,                // DEL and C1 controls
        '­' or '؜' or '﻿' => true,           // soft hyphen, Arabic letter mark, BOM / zero-width no-break space
        >= '​' and <= '‏' => true,                // zero-width space / non-joiner / joiner, LRM, RLM
        >= '‪' and <= '‮' => true,                // LRE, RLE, PDF, LRO, RLO
        >= '⁠' and <= '⁤' => true,                // word joiner, invisible operators
        >= '⁦' and <= '⁩' => true,                // LRI, RLI, FSI, PDI
        >= '￹' and <= '￻' => true,                // interlinear annotation controls
        _ => false,
    };

    /// <summary>The text without hidden characters, plus a marker listing them; the same instance when there is nothing to remove.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        int first = -1;
        for (int i = 0; i < text.Length; i++) if (IsHidden(text[i])) { first = i; break; }
        if (first < 0) return text;

        var sb = new StringBuilder(text.Length + 48);
        sb.Append(text, 0, first);
        var found = new SortedSet<int>();
        for (int i = first; i < text.Length; i++)
        {
            var c = text[i];
            if (IsHidden(c)) found.Add(c); else sb.Append(c);
        }
        sb.Append(' ').Append(MarkerPrefix).Append(string.Join(", ", found.Select(c => $"U+{c:X4}"))).Append(']');
        return sb.ToString();
    }
}
