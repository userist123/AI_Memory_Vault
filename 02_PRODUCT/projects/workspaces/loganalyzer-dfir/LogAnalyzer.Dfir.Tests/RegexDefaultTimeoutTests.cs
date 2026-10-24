using System.Diagnostics;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir;
using LogAnalyzer.Dfir.Analysis;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security audit #11: every regex in the process gets a default match timeout (AppContext REGEX_DEFAULT_MATCH_TIMEOUT, set by a Dfir.Core module
/// initializer and at application start), and the patterns that come from rule lists carry an explicit, shorter timeout.
/// </summary>
public sealed class RegexDefaultTimeoutTests
{
    private static readonly string Hostile = new string('a', 40) + "!";

    [Fact]
    public void Loading_Dfir_Core_sets_the_process_wide_default_match_timeout()
    {
        RegexDefaults.Apply();     // idempotent; the module initializer has already run when this type is touched
        var configured = Assert.IsType<TimeSpan>(AppContext.GetData(RegexDefaults.AppContextKey));
        Assert.True(configured > TimeSpan.Zero && configured <= TimeSpan.FromSeconds(10), configured.ToString());
    }

    /// <summary>
    /// .NET reads the switch once, at the first use of the Regex type; in a test host the framework has already built regexes before this library
    /// loads, so the effective default is observed through the runtime's own reader (the value a fresh process such as the application picks up).
    /// The application sets it first thing in App's static constructor (App.Tests checks that ordering).
    /// </summary>
    [Fact]
    public void The_runtime_reads_the_configured_default_as_a_valid_timeout()
    {
        RegexDefaults.Apply();
        var init = typeof(Regex).GetMethod("InitDefaultMatchTimeout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(init);
        Assert.Equal(RegexDefaults.DefaultMatchTimeout, Assert.IsType<TimeSpan>(init!.Invoke(null, null)));
    }

    [Fact]
    public void A_pathological_input_ends_within_the_rule_timeout()
    {
        var rx = new Regex("^(a+)+$", RegexOptions.None, RegexDefaults.RuleMatchTimeout);
        var sw = Stopwatch.StartNew();
        Assert.Throws<RegexMatchTimeoutException>(() => rx.IsMatch(Hostile));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
    }

    [Fact]
    public void Log_clear_subject_extraction_completes_quickly_on_a_huge_hostile_xml()
    {
        var xml = "<EventData>" + string.Concat(Enumerable.Repeat("<Data Name=\"SubjectUserName\"   ", 20_000)) + "</EventData>";
        var sw = Stopwatch.StartNew();
        var (user, domain) = LogClearAssessment.SubjectFromXml(xml);
        Assert.Equal(("", ""), (user, domain));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
        Assert.Equal(("adm", "LAB"), LogClearAssessment.SubjectFromXml("<EventData><Data Name=\"SubjectUserName\">adm</Data><Data Name=\"SubjectDomainName\">LAB</Data></EventData>"));
    }
}
