using Xunit;

namespace LogAnalyzer.Edition.Tests;

/// <summary>
/// Owner decision (three editions): the Network edition is an air-gapped LAN with NO internet. It may contain the internal-network code
/// (LogAnalyzer.Connectors: read-only domain collection, syslog, internal SIEM, live watchers; LogAnalyzer.Response: CSIRT actions) but nothing
/// that exists only to reach the internet: the AI layer (it uses an HTTP client), cloud or web clients outside the reviewed Connectors assembly,
/// online downloaders or updaters, and external URLs. Absent from the build, not disabled. The same scan runs on the Internet output as a
/// positive control, so a scanner that finds nothing cannot pass silently.
/// </summary>
public sealed class NetworkEditionBuildTests
{
    private static string NetworkDir => BuildOutputInspector.Output("NetworkOutput");
    private static string InternetDir => BuildOutputInspector.Output("InternetOutput");

    private static bool NetworkIncludesLocalAi() =>
        string.Equals(BuildOutputInspector.Output("NetworkIncludeLocalAi"), "true", StringComparison.OrdinalIgnoreCase);

    // The AI layer exists only in the Internet edition (it reaches a model over HTTP); a build option can add it for the Network edition with owner approval.
    private static readonly string[] InternetOnlyAssemblies = ["LogAnalyzer.Ai"];

    private static readonly string[] InternetOnlyTypeNames = ["EvidenceReasoner", "LocalModelClient", "AiCaseAnalysis", "AiAnalysisViewModel", "AiAnalysisView"];

    // Web and download clients. Allowed in exactly one own assembly: LogAnalyzer.Connectors (the internal SIEM forwarder posts to a configured internal URL).
    internal static readonly string[] WebClientTypes =
    [
        "System.Net.Http.HttpClient", "System.Net.Http.HttpClientHandler", "System.Net.Http.SocketsHttpHandler", "System.Net.WebClient", "System.Net.HttpWebRequest",
        "System.Net.WebRequest", "System.Net.FtpWebRequest", "System.Net.WebSockets.ClientWebSocket", "System.Net.Mail.SmtpClient",
    ];

    internal const string WebClientHome = "LogAnalyzer.Connectors.dll";

    // The loopback endpoint of the local AI model server.
    private static readonly string[] BannedStrings = ["http://127.0.0.1:11434", "api.openai.com", "api.anthropic.com", "generativelanguage.googleapis.com", "huggingface.co", "github.com/repos", "api.github.com"];

    /// <summary>
    /// A literal that is an absolute http(s) URL with a host (the application would have an address to reach). Not counted: the bare scheme prefixes
    /// "http://" and "https://" (text patterns used to recognise URLs in log lines) and namespace identifiers, which are names and are never fetched.
    /// </summary>
    internal static bool IsExternalUrlLiteral(string s) =>
        (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        && !s.Equals("http://", StringComparison.OrdinalIgnoreCase) && !s.Equals("https://", StringComparison.OrdinalIgnoreCase)
        && !IsNamespaceIdentifier(s);

    private static bool IsNamespaceIdentifier(string s) =>
        s.Contains("schemas.microsoft.com", StringComparison.OrdinalIgnoreCase) || s.Contains("www.w3.org", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("schemas.openxmlformats.org", StringComparison.OrdinalIgnoreCase) || s.Contains("purl.org", StringComparison.OrdinalIgnoreCase) ||
        // JSON-LD context IRIs of the CASE / UCO ontology written by the CASE export (LogAnalyzer.Core CaseUcoExportService): identifiers, not addresses.
        s.StartsWith("https://ontology.caseontology.org/", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://ontology.unifiedcyberontology.org/", StringComparison.OrdinalIgnoreCase);

    internal static List<string> Violations(string dir, bool allowAiAssembly)
    {
        var found = new List<string>();
        foreach (var a in InternetOnlyAssemblies)
        {
            if (allowAiAssembly) continue;
            if (File.Exists(Path.Combine(dir, a + ".dll"))) found.Add($"assembly present: {a}.dll");
        }
        foreach (var path in BuildOutputInspector.OwnAssemblies(dir).Concat(Directory.EnumerateFiles(dir, "LogAnalyzer*.exe")))
        {
            AssemblyFacts f;
            try { f = BuildOutputInspector.Read(path); } catch (BadImageFormatException) { continue; }
            var isAi = allowAiAssembly && (f.File.StartsWith("LogAnalyzer.Ai", StringComparison.OrdinalIgnoreCase) || f.File.StartsWith("LogAnalyzer.Retea", StringComparison.OrdinalIgnoreCase));
            foreach (var r in f.AssemblyRefs)
                if (InternetOnlyAssemblies.Contains(r) && !allowAiAssembly) found.Add($"{f.File}: references {r}");
            if (!f.File.Equals(WebClientHome, StringComparison.OrdinalIgnoreCase) && !isAi)
                foreach (var t in WebClientTypes.Where(f.TypeRefs.Contains)) found.Add($"{f.File}: uses web client type {t}");
            if (!isAi)
            {
                foreach (var n in f.DefinedTypes) if (InternetOnlyTypeNames.Contains(n[(n.LastIndexOf('.') + 1)..])) found.Add($"{f.File}: defines {n}");
                foreach (var s in f.UserStrings)
                {
                    if (BannedStrings.FirstOrDefault(b => s.Contains(b, StringComparison.OrdinalIgnoreCase)) is { } hit) found.Add($"{f.File}: string \"{hit}\"");
                    if (IsExternalUrlLiteral(s)) found.Add($"{f.File}: URL literal \"{s}\"");
                }
            }
        }
        return found;
    }

    [Fact]
    public void Network_build_output_contains_no_internet_only_code()
    {
        Assert.True(Directory.Exists(NetworkDir), "Network build output not found: " + NetworkDir);
        Assert.True(BuildOutputInspector.OwnAssemblies(NetworkDir).Any(), "no LogAnalyzer assemblies in " + NetworkDir);
        var violations = Violations(NetworkDir, allowAiAssembly: NetworkIncludesLocalAi());
        Assert.True(violations.Count == 0, "The Network edition contains internet-only code:\n" + string.Join("\n", violations.Distinct()));
    }

    [Fact]
    public void Network_build_contains_the_internal_network_features_it_exists_for()
    {
        Assert.True(File.Exists(Path.Combine(NetworkDir, "LogAnalyzer.Connectors.dll")));
        Assert.True(File.Exists(Path.Combine(NetworkDir, "LogAnalyzer.Response.dll")));
        var exe = BuildOutputInspector.Read(Path.Combine(NetworkDir, "LogAnalyzer.Retea.dll"));
        Assert.Contains("LogAnalyzer.Connectors", exe.AssemblyRefs);
        Assert.Contains("LogAnalyzer.Response", exe.AssemblyRefs);
        Assert.Contains("LogAnalyzer.UI.ViewModels.ContainmentViewModel", exe.DefinedTypes);
        Assert.Contains("LogAnalyzer.UI.ViewModels.DomainInvestigationViewModel", exe.DefinedTypes);
        var connectors = BuildOutputInspector.Read(Path.Combine(NetworkDir, "LogAnalyzer.Connectors.dll"));
        Assert.Contains("LogAnalyzer.Dfir.Windows.Domain.DirectoryCollector", connectors.DefinedTypes);
    }

    [Fact]
    public void Network_ai_layer_is_absent_unless_the_explicit_option_was_used()
    {
        var present = File.Exists(Path.Combine(NetworkDir, "LogAnalyzer.Ai.dll"));
        Assert.Equal(NetworkIncludesLocalAi(), present);
        var deps = File.ReadAllText(Path.Combine(NetworkDir, "LogAnalyzer.Retea.deps.json"));
        if (!NetworkIncludesLocalAi()) Assert.DoesNotContain("LogAnalyzer.Ai", deps);
        var exe = BuildOutputInspector.Read(Path.Combine(NetworkDir, "LogAnalyzer.Retea.dll"));
        if (!NetworkIncludesLocalAi()) Assert.DoesNotContain(exe.DefinedTypes, t => t.EndsWith("UnclassifiedFeatureViews", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_positive_control_finds_the_internet_only_code_in_the_Internet_build()
    {
        var text = string.Join("\n", Violations(InternetDir, allowAiAssembly: false));
        Assert.Contains("LogAnalyzer.Ai.dll", text);                 // the AI assembly
        Assert.Contains("EvidenceReasoner", text);                   // its model client
        Assert.Contains("web client type System.Net.Http.HttpClient", text);   // HTTP client outside Connectors
        Assert.Contains("http://127.0.0.1:11434", text);             // the local model endpoint
    }

    [Fact]
    public void Scanner_rules_are_exact_synthetic_cases()
    {
        Assert.True(IsExternalUrlLiteral("https://example.org/update.json"));
        Assert.True(IsExternalUrlLiteral("http://download.example/setup.exe"));
        Assert.False(IsExternalUrlLiteral("http://schemas.microsoft.com/winfx/2006/xaml"));
        Assert.False(IsExternalUrlLiteral("http-like text"));
        Assert.False(IsExternalUrlLiteral("https://"));
        Assert.False(IsExternalUrlLiteral("https://ontology.unifiedcyberontology.org/uco/core#"));
        Assert.Contains("System.Net.Http.HttpClient", WebClientTypes);
        Assert.Equal("LogAnalyzer.Connectors.dll", WebClientHome);
    }
}
