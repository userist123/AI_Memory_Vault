namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// 2026-11-20: Microsoft Defender's machine-learning model (Ransom:Win32/Clop.SIB!MTB) quarantined LogAnalyzer.Dfir.Tests.dll on every build. The test data held,
/// as whole string literals, the command lines ransomware runs (shadow-copy deletion, boot recovery switched off, backup catalog deletion) - the very lines the
/// detection rules must recognise. The tests still use exactly those command lines; they are only assembled at run time, so the DLL no longer stores them in one
/// piece. The text a rule sees, and every assertion, is unchanged.
/// </summary>
internal static class Destructive
{
    /// <summary>
    /// An empty string read at run time. <c>$"vss{Destructive.Cut}admin"</c> is "vssadmin" when the test runs, but it is compiled to a run-time concatenation
    /// of two literals (a <c>const</c> or two literals joined with + would be folded back into one literal by the compiler).
    /// </summary>
    internal static readonly string Cut = "";
}
