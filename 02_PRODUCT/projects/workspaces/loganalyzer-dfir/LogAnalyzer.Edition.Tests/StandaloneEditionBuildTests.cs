using System.Reflection;
using Xunit;

namespace LogAnalyzer.Edition.Tests;

/// <summary>
/// Owner decision 13: the classified edition (P1) must not contain network code, any AI, host-modifying actions or updates -
/// absent from the build, not disabled. These tests read the metadata of every LogAnalyzer assembly in the P1 build output.
/// The same scan runs on the unclassified output as a positive control, so a scanner that finds nothing cannot pass silently.
/// </summary>
public sealed class ClassifiedEditionBuildTests
{
    // Assemblies that exist only in the unclassified edition.
    private static readonly string[] ExcludedAssemblies = ["LogAnalyzer.Connectors", "LogAnalyzer.Response", "LogAnalyzer.Ai"];

    // Referenced assemblies that mean network access (own assemblies only; the framework ships them whole in a self-contained publish).
    private static readonly string[] BannedAssemblyRefs =
        ["System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets", "System.Net.WebSockets.Client", "System.Net.Mail", "System.Net.Requests",
         "System.Net.WebClient", "System.Net.Ping", "System.DirectoryServices", "System.DirectoryServices.Protocols", "System.ServiceProcess.ServiceController"];

    private static readonly string[] BannedTypeRefs =
    [
        "System.Net.Http.HttpClient", "System.Net.WebClient", "System.Net.HttpWebRequest", "System.Net.WebRequest", "System.Net.FtpWebRequest",
        "System.Net.Sockets.Socket", "System.Net.Sockets.UdpClient", "System.Net.Sockets.TcpClient", "System.Net.Sockets.TcpListener",
        "System.Net.HttpListener", "System.Net.Dns", "System.Net.NetworkInformation.Ping", "System.Net.NetworkInformation.NetworkChange",
        "System.Net.Mail.SmtpClient", "System.Net.WebSockets.ClientWebSocket",
        "System.Diagnostics.Eventing.Reader.EventLogSession",
        "System.DirectoryServices.DirectoryEntry", "System.DirectoryServices.DirectorySearcher",
        "System.ServiceProcess.ServiceController",
    ];

    // Registry writes and service/driver control through the BCL.
    private static readonly string[] BannedMembers =
    [
        "Microsoft.Win32.RegistryKey::SetValue", "Microsoft.Win32.RegistryKey::DeleteValue", "Microsoft.Win32.RegistryKey::CreateSubKey",
        "Microsoft.Win32.RegistryKey::DeleteSubKey", "Microsoft.Win32.RegistryKey::DeleteSubKeyTree",
        "Microsoft.Win32.Registry::SetValue",
    ];

    private static readonly string[] BannedPInvokeDlls = ["ws2_32.dll", "wsock32.dll", "winhttp.dll", "wininet.dll", "urlmon.dll", "dnsapi.dll", "mswsock.dll"];

    private static readonly string[] BannedPInvokeFunctions =
        ["NtSuspendProcess", "NtResumeProcess", "SuspendThread", "TerminateProcess", "AuditSetSystemPolicy", "RegSetValueEx", "RegSetValueExW", "RegSetKeyValue",
         "RegDeleteKey", "RegDeleteValue", "RegCreateKeyEx", "CreateService", "ChangeServiceConfig", "ChangeServiceConfig2", "ControlService", "DeleteService",
         "NtUnloadDriver", "ZwUnloadDriver", "NtLoadDriver", "SetNamedSecurityInfo", "SetFirmwareEnvironmentVariable", "InitiateSystemShutdownEx"];

    // Literals that would start a station-changing command or contact something.
    /// <summary>
    /// Exact literals that contain a banned token but only PARSE text from collected evidence (no connection, no process). Each entry is
    /// one exact string in one named assembly; the same text anywhere else, or any other string, is still a violation.
    /// </summary>
    internal static readonly (string Assembly, string Literal)[] ParsingOnlyLiterals =
    [
        // WP15b PolicyTimeline: regex reading gPLink values out of Security 5136 event text.
        ("LogAnalyzer.Dfir.Core.dll", @"\[LDAP://([^;\]]*);(\d+)\]"),
    ];

    internal static bool IsParsingOnlyLiteral(string file, string literal) =>
        ParsingOnlyLiterals.Any(p => p.Assembly.Equals(file, StringComparison.OrdinalIgnoreCase) && p.Literal == literal);

    private static readonly string[] BannedStrings =
        ["netsh.exe", "HNetCfg.FWRule", "/failure:enable", "-ExecutionPolicy Bypass", "LogAnalyzer-Containment-", "LDAP://", "http://127.0.0.1:11434"];

    // Types of the excluded features, by simple name, in case they ever get compiled into a shared assembly.
    private static readonly string[] BannedTypeNames =
    [
        "SystemDefenseExecutionService", "HostDefense", "NetshRunner", "WindowsFirewallController", "ProcessSuspension", "ProcessContainmentService",
        "AuditPolicyChange", "RegistryValueWriter", "PowerShellAuditCollector", "UdpSyslogReceiver", "LiveEventLogWatcherService", "LiveEventSourceFactory",
        "NetworkChangeWatcher", "M365LiveConnectorService", "SiemForwarderService", "LiveThreatIntelService", "DirectoryCollector", "UserInvestigation",
        "EvidenceReasoner", "LocalModelClient", "AiCaseAnalysis", "WindowsConnectivityProbe", "ContainmentViewModel", "DomainInvestigationViewModel",
    ];

    private static string ClassifiedDir => BuildOutputInspector.Output("ClassifiedOutput");
    private static string UnclassifiedDir => BuildOutputInspector.Output("UnclassifiedOutput");

    /// <summary>True only when the test run was started with -p:ClassifiedIncludeLocalAi=true (the explicit, owner-approved option).</summary>
    private static bool ClassifiedIncludesLocalAi() =>
        string.Equals(BuildOutputInspector.Output("ClassifiedIncludeLocalAi"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Violations found in one set of own assemblies. The Ai assembly is allowed only when the explicit option was used.</summary>
    private static List<string> Violations(string dir, bool allowAiAssembly)
    {
        var found = new List<string>();
        foreach (var excluded in ExcludedAssemblies)
        {
            if (excluded == "LogAnalyzer.Ai" && allowAiAssembly) continue;
            if (File.Exists(Path.Combine(dir, excluded + ".dll"))) found.Add($"assembly present: {excluded}.dll");
        }
        foreach (var path in BuildOutputInspector.OwnAssemblies(dir).Concat(Directory.EnumerateFiles(dir, "LogAnalyzer*.exe")))
        {
            AssemblyFacts f;
            try { f = BuildOutputInspector.Read(path); } catch (BadImageFormatException) { continue; }   // apphost stub is not managed
            // With the explicit local-AI option the Ai assembly and the exe's AI screen may use loopback HTTP; nothing else is relaxed.
            var aiAllowedHere = allowAiAssembly && (f.File.StartsWith("LogAnalyzer.Ai", StringComparison.OrdinalIgnoreCase) || f.File.StartsWith("LogAnalyzer.Classified", StringComparison.OrdinalIgnoreCase));
            foreach (var r in f.AssemblyRefs)
            {
                if (ExcludedAssemblies.Contains(r) && !(r == "LogAnalyzer.Ai" && allowAiAssembly)) found.Add($"{f.File}: references {r}");
                if (BannedAssemblyRefs.Contains(r) && !aiAllowedHere) found.Add($"{f.File}: references {r}");
            }
            foreach (var t in BannedTypeRefs.Where(f.TypeRefs.Contains)) if (!aiAllowedHere) found.Add($"{f.File}: uses type {t}");
            foreach (var m in BannedMembers.Where(f.MemberRefs.Contains)) found.Add($"{f.File}: calls {m}");
            foreach (var (dll, fn) in f.PInvokes)
            {
                if (BannedPInvokeDlls.Contains(dll, StringComparer.OrdinalIgnoreCase)) found.Add($"{f.File}: P/Invoke into {dll} ({fn})");
                if (BannedPInvokeFunctions.Contains(fn, StringComparer.OrdinalIgnoreCase)) found.Add($"{f.File}: P/Invoke {dll}!{fn}");
            }
            foreach (var s in BannedStrings.Where(b => f.UserStrings.Any(u => u.Contains(b, StringComparison.OrdinalIgnoreCase) && !IsParsingOnlyLiteral(f.File, u)))) if (!aiAllowedHere) found.Add($"{f.File}: string \"{s}\"");
            foreach (var n in f.DefinedTypes) if (!aiAllowedHere && BannedTypeNames.Contains(n[(n.LastIndexOf('.') + 1)..])) found.Add($"{f.File}: defines {n}");
        }
        return found;
    }

    [Fact]
    public void Classified_build_output_contains_no_network_ai_or_host_modifying_code()
    {
        Assert.True(Directory.Exists(ClassifiedDir), "classified build output not found: " + ClassifiedDir);
        Assert.True(BuildOutputInspector.OwnAssemblies(ClassifiedDir).Any(), "no LogAnalyzer assemblies in " + ClassifiedDir);
        var violations = Violations(ClassifiedDir, allowAiAssembly: ClassifiedIncludesLocalAi());
        Assert.True(violations.Count == 0, "The classified edition contains excluded code:\n" + string.Join("\n", violations.Distinct()));
    }

    [Fact]
    public void Parsing_only_literal_exception_is_exact_and_bound_to_its_assembly()
    {
        var (asm, lit) = ParsingOnlyLiterals[0];
        Assert.True(IsParsingOnlyLiteral(asm, lit));
        Assert.False(IsParsingOnlyLiteral("LogAnalyzer.Dfir.Windows.dll", lit));     // same text, other assembly: still banned
        Assert.False(IsParsingOnlyLiteral(asm, "LDAP://dc01.example/"));             // any other LDAP string: still banned
        Assert.False(IsParsingOnlyLiteral(asm, lit + " "));                           // exact match only
    }

    [Fact]
    public void Local_ai_is_excluded_from_the_classified_build_unless_the_explicit_option_was_used()
    {
        // The default build has no Ai assembly. A build made with -p:ClassifiedIncludeLocalAi=true is an explicit, documented
        // option that requires owner approval (docs/dfir/EDITIONS.md); CI never builds it.
        if (ClassifiedIncludesLocalAi()) Assert.True(File.Exists(Path.Combine(ClassifiedDir, "LogAnalyzer.Ai.dll")));
        else Assert.False(File.Exists(Path.Combine(ClassifiedDir, "LogAnalyzer.Ai.dll")), "LogAnalyzer.Ai.dll is in the classified output without the explicit option");
    }

    [Fact]
    public void Classified_deps_manifest_lists_no_excluded_assembly()
    {
        var deps = File.ReadAllText(Path.Combine(ClassifiedDir, "LogAnalyzer.Classified.deps.json"));
        foreach (var a in new[] { "LogAnalyzer.Connectors", "LogAnalyzer.Response" }) Assert.DoesNotContain(a, deps);
    }

    [Fact]
    public void Scanner_positive_control_finds_the_excluded_code_in_the_unclassified_build()
    {
        var violations = Violations(UnclassifiedDir, allowAiAssembly: false);
        var text = string.Join("\n", violations);
        Assert.Contains("LogAnalyzer.Connectors.dll", text);
        Assert.Contains("LogAnalyzer.Response.dll", text);
        Assert.Contains("System.Net.Http", text);                    // HTTP clients (connectors / AI)
        Assert.Contains("UdpClient", text);                          // syslog receiver
        Assert.Contains("NtSuspendProcess", text);                   // process suspension
        Assert.Contains("HNetCfg.FWRule", text);                     // firewall rules
        Assert.Contains("SetValue", text);                           // registry writes
    }

    [Fact]
    public void Classified_exe_shares_the_ui_sources_but_not_the_edition_specific_ones()
    {
        var f = BuildOutputInspector.Read(Path.Combine(ClassifiedDir, "LogAnalyzer.Classified.dll"));
        Assert.Contains("LogAnalyzer.UI.ViewModels.MainViewModel", f.DefinedTypes);
        Assert.Contains("LogAnalyzer.UI.Services.EditionComposition", f.DefinedTypes);
        Assert.DoesNotContain("LogAnalyzer.UI.ViewModels.ContainmentViewModel", f.DefinedTypes);
        if (!ClassifiedIncludesLocalAi()) Assert.DoesNotContain("LogAnalyzer.UI.ViewModels.AiAnalysisViewModel", f.DefinedTypes);
        Assert.DoesNotContain(f.DefinedTypes, t => t.Contains("UnclassifiedFeatureViews"));
    }
}
