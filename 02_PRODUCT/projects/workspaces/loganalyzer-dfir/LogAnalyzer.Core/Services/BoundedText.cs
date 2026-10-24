using System;

namespace LogAnalyzer.Core.Services
{
    /// <summary>An on-screen log that keeps only its newest whole lines within a character budget (security audit #7: a flood cannot grow it forever).</summary>
    public static class BoundedText
    {
        public const int DefaultMaxChars = 200_000;

        public static string Append(string current, string line, int maxChars = DefaultMaxChars)
        {
            var text = (current ?? string.Empty) + line + Environment.NewLine;
            if (text.Length <= maxChars) return text;
            var cut = text.Length - maxChars;
            var nl = text.IndexOf('\n', cut);
            return nl < 0 || nl + 1 >= text.Length ? text[^Math.Min(maxChars, text.Length)..] : text[(nl + 1)..];
        }
    }
}
