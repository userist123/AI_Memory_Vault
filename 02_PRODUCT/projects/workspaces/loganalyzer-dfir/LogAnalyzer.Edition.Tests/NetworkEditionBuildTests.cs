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
    /// A literal that contains, ANYWHERE, an absolute http(s) URL with a host (the application would have an address to reach). Every occurrence of
    /// "http://" or "https://" in the literal is examined, not only a prefix. Not counted: a bare scheme with no host (text patterns used to recognise
    /// URLs in log lines) and a namespace identifier, which is a name and is never fetched. A namespace identifier is recognised only by an EXACT host
    /// from <see cref="NamespaceHosts"/> immediately followed by "/": no substring match, no port, no user-info, no longer host.
    /// </summary>
    internal static bool IsExternalUrlLiteral(string s) => ExternalUrls(s).Any();

    // Hosts of XML / JSON-LD namespace IRIs. Exact host match only.
    internal static readonly string[] NamespaceHosts =
    [
        "schemas.microsoft.com", "www.w3.org", "schemas.openxmlformats.org", "purl.org",
        // JSON-LD context IRIs of the CASE / UCO ontology written by the CASE export (LogAnalyzer.Core CaseUcoExportService): identifiers, not addresses.
        "ontology.caseontology.org", "ontology.unifiedcyberontology.org",
    ];

    // A URL ends at white space, a quote or an angle bracket.
    private static readonly char[] TokenEnd = [' ', '\t', '\r', '\n', '"', '\'', '<', '>', '`'];

    internal static IEnumerable<string> ExternalUrls(string s)
    {
        foreach (var scheme in new[] { "http://", "https://" })
            for (var i = s.IndexOf(scheme, StringComparison.OrdinalIgnoreCase); i >= 0; i = s.IndexOf(scheme, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                var rest = s[(i + scheme.Length)..];
                var tokenEnd = rest.IndexOfAny(TokenEnd);
                var token = tokenEnd < 0 ? rest : rest[..tokenEnd];
                var hostEnd = token.IndexOfAny(['/', '?', '#']);
                var host = hostEnd < 0 ? token : token[..hostEnd];
                if (!host.Any(char.IsLetterOrDigit)) continue;                                 // bare scheme ("http://", "(… http://)"): a pattern, not an address
                bool identifier = hostEnd >= 0 && token[hostEnd] == '/' && NamespaceHosts.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase));
                if (!identifier) yield return s.Substring(i, scheme.Length + token.Length);
            }
    }

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
        Assert.Contains("URL literal", text);                        // the URL-literal scan itself fires on the Internet build
    }

    [Theory]
    [InlineData("https://evil/x?schemas.microsoft.com")]               // review finding: an allowed namespace host anywhere in the string passed
    [InlineData("see https://download.example/setup.exe for the update")] // review finding: a URL inside a longer string was missed
    [InlineData("Error: http://evil.example/www.w3.org/")]
    [InlineData("http://schemas.microsoft.com.evil.example/xaml")]       // allowed host as a prefix of another host
    [InlineData("http://schemas.microsoft.com@evil.example/xaml")]       // user-info trick
    [InlineData("http://schemas.microsoft.com:8080/xaml")]               // a port turns an identifier into an address
    [InlineData("http://www.w3.org/2000/svg https://evil.example/")]     // second URL in the same literal
    [InlineData("HTTPS://EVIL.EXAMPLE/")]
    [InlineData("(vezi http://evil.example)")]
    public void Scanner_finds_urls_anywhere_and_does_not_accept_an_allowed_host_out_of_place(string literal) =>
        Assert.True(IsExternalUrlLiteral(literal), literal);

    [Theory]
    [InlineData("http://schemas.microsoft.com/winfx/2006/xaml")]
    [InlineData("xmlns=\"http://www.w3.org/2000/svg\"")]
    [InlineData("https://ontology.unifiedcyberontology.org/uco/core#")]
    [InlineData("https://ontology.caseontology.org/case/investigation/")]
    [InlineData("http://purl.org/dc/elements/1.1/")]
    [InlineData("http://schemas.openxmlformats.org/officeDocument/2006/relationships")]
    [InlineData("^https?://")]
    [InlineData("https://")]
    [InlineData("prefix http:// or https:// is a URL")]
    [InlineData("pachete MSI de la distanță (/q /i http://) sau")]
    public void Scanner_accepts_only_exact_namespace_identifiers_and_bare_scheme_patterns(string literal) =>
        Assert.False(IsExternalUrlLiteral(literal), literal);

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
