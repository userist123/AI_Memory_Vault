using System.Runtime.CompilerServices;

namespace LogAnalyzer.Dfir;

/// <summary>
/// Security audit #11: a process-wide default match timeout for every <see cref="System.Text.RegularExpressions.Regex"/> that does not set one
/// (including <c>[GeneratedRegex]</c> without a timeout and the static <c>Regex.IsMatch/Match/Replace</c> helpers). It is set through the
/// documented AppContext switch <c>REGEX_DEFAULT_MATCH_TIMEOUT</c> by this assembly's module initializer and again at application start, so a
/// pattern with catastrophic backtracking on hostile evidence ends in a <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/>
/// instead of stalling the analysis. Patterns that come from rule lists or data packs use <see cref="RuleMatchTimeout"/> explicitly.
/// </summary>
/// <remarks>
/// .NET reads the switch once, when the Regex type is first initialised; it must therefore be set before the first regex of the process is built.
/// A value already set by the host (another TimeSpan) is kept.
/// </remarks>
public static class RegexDefaults
{
    public const string AppContextKey = "REGEX_DEFAULT_MATCH_TIMEOUT";

    /// <summary>Default for regexes without an explicit timeout. Generous: built-in patterns are linear; this only stops a runaway.</summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Explicit timeout for patterns taken from rule lists and data packs.</summary>
    public static readonly TimeSpan RuleMatchTimeout = TimeSpan.FromMilliseconds(200);

    public static void Apply()
    {
        if (AppContext.GetData(AppContextKey) is TimeSpan) return;
        AppContext.SetData(AppContextKey, DefaultMatchTimeout);
    }
}

internal static class RegexDefaultsModuleInitializer
{
#pragma warning disable CA2255 // Intentional: the default must be in place before any code of this library builds a regex.
    [ModuleInitializer]
    internal static void Initialize() => RegexDefaults.Apply();
#pragma warning restore CA2255
}
