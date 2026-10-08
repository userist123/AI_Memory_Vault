using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Detection;

/// <summary>
/// One rule evaluation that did not finish within its regex match timeout. The rule is neither matched nor cleared on that input:
/// the outcome is "not evaluated", and it is reported so that a missing detection is never read as "nothing found".
/// </summary>
public sealed record RuleTimeout(string RuleId, string Title, string Detail, string EvidenceId = "", string Locator = "");

/// <summary>Raised by <see cref="SigmaLite"/> and <see cref="YaraLite"/> when a regex times out and the caller gave no list to record it in.</summary>
public sealed class RuleEvaluationTimeoutException(RuleTimeout timeout, RegexMatchTimeoutException inner)
    : TimeoutException($"Evaluarea regulii „{timeout.Title}” a depășit timpul: {timeout.Detail}", inner)
{
    public RuleTimeout Timeout { get; } = timeout;
}

internal static class RuleRegex
{
    /// <summary>
    /// A regex over untrusted evidence, always with a match timeout (catastrophic backtracking must not stall the investigation).
    /// An invalid pattern is a <see cref="FormatException"/> at load time, which the engine reports as a rule that did not load.
    /// </summary>
    public static Regex Create(string rule, string what, string pattern, RegexOptions options, TimeSpan timeout)
    {
        try { return new Regex(pattern, options, timeout); }
        catch (ArgumentException ex) { throw new FormatException($"{rule}: expresie regulată invalidă în {what}: {ex.Message}", ex); }
    }
}
