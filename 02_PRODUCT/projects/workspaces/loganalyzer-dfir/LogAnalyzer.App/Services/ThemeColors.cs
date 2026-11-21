namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// The "Registru" theme colours as strings for view-model properties that a view binds to a brush (for example <c>RiskColor</c>).
    /// Every value mirrors a token of <c>Themes/Tokens/ColorTokens.xaml</c>; the test <c>ThemeColorsTests</c> reads that file and fails when a constant drifts.
    /// View-models must use these constants instead of hex literals, so a theme change is made in one place.
    /// </summary>
    public static class ThemeColors
    {
        public const string Critical = "#A3201C";
        public const string High = "#A84B0C";
        public const string Medium = "#7A5D00";
        public const string Low = "#285F7E";
        public const string Success = "#2E6638";
        public const string PrimaryAccent = "#1F56A3";
        public const string EvidenceAccent = "#4F4677";
        public const string TextPrimary = "#1C2024";
        public const string TextSecondary = "#454B52";
        public const string TextMuted = "#61676E";
        public const string Surface1 = "#E7EBF0";
        public const string Surface2 = "#FFFFFF";
        public const string Surface3 = "#E1E7EF";
        public const string Border = "#D4DAE2";
        public const string StrongBorder = "#9EA8B5";
        public const string BadgeCriticalBg = "#F6E1DF";
    }
}
