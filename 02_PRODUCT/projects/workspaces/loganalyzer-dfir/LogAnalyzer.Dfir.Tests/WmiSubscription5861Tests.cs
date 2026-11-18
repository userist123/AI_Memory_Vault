using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;
using static LogAnalyzer.Dfir.Tests.W11;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>WMI-Activity 5861 (permanent event subscription, T1546.003): structure taken from a real event captured on a clean lab VM (LAB-WS02), CreatorSID removed.</summary>
public class WmiSubscription5861Tests
{
    private const string Wmi = "Microsoft-Windows-WMI-Activity/Operational";

    private const string LogFileCause =
        "Binding EventFilter: \ninstance of __EventFilter\n{\n\tEventNamespace = \"root\\\\cimv2\";\n\tName = \"LA-TEST-Filter\";\n" +
        "\tQuery = \"SELECT * FROM __InstanceModificationEvent WITHIN 60 WHERE TargetInstance ISA 'Win32_LocalTime' AND TargetInstance.Second = 0\";\n\tQueryLanguage = \"WQL\";\n};\n" +
        "Perm. Consumer: \ninstance of LogFileEventConsumer\n{\n\tFilename = \"C:\\\\LA\\\\la-test-wmi.txt\";\n\tName = \"LA-TEST-Consumer\";\n\tText = \"LA TEST WMI consumer fired\";\n};\n";

    private static TimelineEvent Binding(double minute, string ess, string consumer, string cause) =>
        Ev(Wmi, 5861, minute, "Microsoft-Windows-WMI-Activity", ("Namespace", "//./root/subscription"), ("ESS", ess), ("CONSUMER", consumer), ("PossibleCause", cause));

    [Fact]
    public void The_real_LogFileEventConsumer_binding_is_one_Medium_finding_with_query_consumer_and_file()
    {
        var f = One(Correlation.Run([Binding(0, "LA-TEST-Filter", "LogFileEventConsumer=\"LA-TEST-Consumer\"", LogFileCause)]), "PERSIST-WMI");
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal("T1546.003", f.MitreTechniqueId);
        Assert.Contains("LA-TEST-Filter", f.Description);
        Assert.Contains("SELECT * FROM __InstanceModificationEvent WITHIN 60", f.Description);
        Assert.Contains("LogFileEventConsumer", f.Description);
        Assert.Contains("LA-TEST-Consumer", f.Description);
        Assert.Contains(@"C:\LA\la-test-wmi.txt", f.Description);
        Assert.Contains("//./root/subscription", f.Description);
        Assert.Contains("nu cine l-a creat", f.Description);
    }

    [Fact]
    public void The_Windows_default_SCM_binding_is_not_reported_but_a_look_alike_with_another_filter_is()
    {
        var scm = Binding(0, "SCM Event Log Filter", "NTEventLogEventConsumer=\"SCM Event Log Consumer\"",
            "Binding EventFilter: \ninstance of __EventFilter\n{\n\tName = \"SCM Event Log Filter\";\n\tQuery = \"select * from MSFT_SCMEventLogEvent\";\n};\nPerm. Consumer: \ninstance of NTEventLogEventConsumer\n{\n\tName = \"SCM Event Log Consumer\";\n\tSourceName = \"Service Control Manager\";\n};\n");
        Assert.DoesNotContain(Correlation.Run([scm]), x => x.RuleId == "PERSIST-WMI");
        var fake = Binding(0, "Other Filter", "NTEventLogEventConsumer=\"SCM Event Log Consumer\"", "");
        Assert.Contains(Correlation.Run([fake]), x => x.RuleId == "PERSIST-WMI");
    }

    [Fact]
    public void A_CommandLineEventConsumer_is_High_and_the_command_line_is_in_the_finding()
    {
        var cause = LogFileCause.Replace("instance of LogFileEventConsumer", "instance of CommandLineEventConsumer")
            .Replace("\tFilename = \"C:\\\\LA\\\\la-test-wmi.txt\";", "\tCommandLineTemplate = \"cmd.exe /c \\\"C:\\\\LA\\\\run.bat\\\"\";");
        var f = One(Correlation.Run([Binding(0, "LA-TEST-Filter", "CommandLineEventConsumer=\"LA-TEST-Consumer\"", cause)]), "PERSIST-WMI");
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains(@"CommandLineTemplate = cmd.exe /c ""C:\LA\run.bat""", f.Description);
    }

    [Fact]
    public void Repeated_5861_for_the_same_filter_consumer_pair_aggregate_into_one_finding_and_other_pairs_stay_separate()
    {
        var c = "LogFileEventConsumer=\"LA-TEST-Consumer\"";
        var found = Correlation.Run([Binding(0, "LA-TEST-Filter", c, LogFileCause), Binding(5, "LA-TEST-Filter", c, LogFileCause), Binding(6, "LA-OTHER-Filter", c, "")]);
        Assert.Equal(2, found.Count(x => x.RuleId == "PERSIST-WMI"));
        var two = found.Single(x => x.RuleId == "PERSIST-WMI" && x.SupportingEvidence.Count == 2);
        Assert.True(two.LastSeenUtc > two.FirstSeenUtc);
    }

    [Fact]
    public void EvtxParser_keeps_the_UserData_leaf_fields_of_a_5861_event()
    {
        var xml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>5861</EventID><Level>4</Level></System>" +
                  "<UserData><Operation_ESStoConsumerBinding xmlns='http://manifests.microsoft.com/win/2006/windows/WMI'><Namespace>//./root/subscription</Namespace>" +
                  "<ESS>LA-TEST-Filter</ESS><CONSUMER>LogFileEventConsumer=\"LA-TEST-Consumer\"</CONSUMER><PossibleCause>x = \"y\";</PossibleCause></Operation_ESStoConsumerBinding></UserData></Event>";
        var fields = new Dictionary<string, string>();
        EvtxParser.ExtractFields(xml, fields);
        Assert.Equal("//./root/subscription", fields["Namespace"]);
        Assert.Equal("LA-TEST-Filter", fields["ESS"]);
        Assert.Equal("LogFileEventConsumer=\"LA-TEST-Consumer\"", fields["CONSUMER"]);
        Assert.Equal("x = \"y\";", fields["PossibleCause"]);
    }
}
