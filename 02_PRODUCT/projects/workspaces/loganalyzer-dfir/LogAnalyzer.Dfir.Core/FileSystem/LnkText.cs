using System.Globalization;
using System.Text;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>The argument field of a shortcut with its padding removed, how many invisible characters were removed and whether that counts as padding.</summary>
public sealed record DepaddedText(string Text, int Removed, int Leading, bool Padded);

/// <summary>
/// Shortcut argument text as a reader should see it. Recent LNK campaigns (CVE-2025-9491) put hundreds of spaces, line breaks or zero-width characters
/// before the command, so Explorer's Properties window and a raw report show an empty-looking field. <see cref="Depad"/> collapses every run of
/// invisible characters to one space and says how many were removed; the original text stays in the evidence untouched.
/// </summary>
public static class LnkText
{
    /// <summary>White space, control characters (line breaks, tabs, NUL) and format characters (zero-width space / joiner, bidi marks, BOM).</summary>
    public static bool IsInvisible(char c) =>
        char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format;

    /// <param name="minRun">Leading invisible characters from which the field counts as padded (research 2026-10-11 §4: 20).</param>
    /// <param name="ratio">Share of invisible characters (in a field of at least <paramref name="minRun"/> of them) from which the field counts as padded (0.8).</param>
    public static DepaddedText Depad(string? text, int minRun = 20, double ratio = 0.8)
    {
        var s = text ?? "";
        int leading = 0;
        while (leading < s.Length && IsInvisible(s[leading])) leading++;
        int invisible = 0;
        var sb = new StringBuilder(s.Length);
        bool inRun = false;
        foreach (var c in s)
        {
            if (IsInvisible(c)) { invisible++; if (!inRun && sb.Length > 0) sb.Append(' '); inRun = true; continue; }
            inRun = false;
            sb.Append(c);
        }
        var clean = sb.ToString().TrimEnd();
        // characters removed = all invisible characters minus the single spaces kept between words
        int kept = clean.Count(c => c == ' ');
        int removed = Math.Max(0, invisible - kept);
        bool padded = leading >= minRun || (invisible >= minRun && s.Length > 0 && invisible / (double)s.Length > ratio);
        return new DepaddedText(clean, removed, leading, padded);
    }
}
