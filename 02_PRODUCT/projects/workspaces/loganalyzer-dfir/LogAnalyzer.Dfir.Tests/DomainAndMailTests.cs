using System.Text;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Windows.Audit;
using LogAnalyzer.Dfir.Windows.Domain;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

[Collection("AppMode")] // one test sets the process-wide operating mode
public class DomainAndMailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static DirectoryAccount U(string sam, Uac flags = Uac.NormalAccount, int lastLogonDaysAgo = 1, string[]? spns = null, int adminCount = 0,
                                      bool computer = false, string os = "", int pwdDaysAgo = 30) =>
        new(sam, sam, $"CN={sam},DC=corp,DC=local", $"S-1-5-21-9-9-9-{Math.Abs(sam.GetHashCode()) % 9000 + 1000}", flags,
            Now.AddDays(-lastLogonDaysAgo), Now.AddDays(-pwdDaysAgo), Now.AddYears(-2), adminCount, spns ?? [], [], "", "", computer, os);

    private static DomainSnapshot Domain() => new()
    {
        DomainName = "corp.local", CollectedUtc = Now,
        Users =
        {
            U("ion.popescu"),
            U("svc_sql", Uac.NormalAccount | Uac.DontExpirePassword, spns: ["MSSQLSvc/sql01.corp.local:1433"], adminCount: 1),
            U("legacy", Uac.NormalAccount | Uac.DontRequirePreauth),
            U("fost.admin", adminCount: 1),
            U("inactiv", lastLogonDaysAgo: 400),
            U("admin.vechi", Uac.NormalAccount | Uac.AccountDisabled),
            U("krbtgt", Uac.NormalAccount | Uac.AccountDisabled, pwdDaysAgo: 2000, spns: ["kadmin/changepw"]),
        },
        Computers =
        {
            U("DC01$", Uac.ServerTrust | Uac.TrustedForDelegation, computer: true, os: "Windows Server 2022"),
            U("APP02$", Uac.WorkstationTrust | Uac.TrustedForDelegation, computer: true, os: "Windows Server 2012 R2"),
        },
        PrivilegedGroups = { ["Domain Admins"] = ["Administrator", "svc_sql", "admin.vechi"] },
        Policy = new DomainPolicy(7, 0, null, 0),
    };

    private static ControlCheck Check(IEnumerable<ControlCheck> cs, string id) => Assert.Single(cs, c => c.Id == id);

    [Fact]
    public void Domain_weaknesses_are_found()
    {
        var c = DomainEvaluator.Evaluate(Domain());
        Assert.Equal(ControlStatus.Neconform, Check(c, "DM01").Status);   // privileged svc_sql with SPN
        Assert.DoesNotContain(Check(c, "DM01").Evidence, e => e.StartsWith("krbtgt"));
        Assert.Contains(Check(c, "DM01").Evidence, e => e.Contains("svc_sql (PRIVILEGIAT)"));
        Assert.Equal(ControlStatus.Neconform, Check(c, "DM02").Status);   // AS-REP roastable
        Assert.Equal(ControlStatus.Neconform, Check(c, "DM10").Status);   // Domain Admins with disabled + non-expiring member
        Assert.Equal(ControlStatus.DeVerificat, Check(c, "DM20").Status); // stale user
        Assert.Equal(ControlStatus.Neconform, Check(c, "DM21").Status);   // krbtgt 2000 days
        var deleg = Check(c, "DM22");
        Assert.Equal(ControlStatus.Neconform, deleg.Status);
        Assert.Contains("APP02$", deleg.Evidence);
        Assert.DoesNotContain("DC01$", deleg.Evidence);                  // DCs are expected to have it
        Assert.Contains(Check(c, "DM23").Evidence, e => e == "fost.admin");
        Assert.Equal(ControlStatus.Neconform, Check(c, "DM25").Status);   // Server 2012 R2
        Assert.Equal(ControlStatus.Neconform, Check(c, "DM30").Status);   // min length 7
        Assert.Equal(ControlStatus.Neconform, Check(c, "DM31").Status);   // no lockout
    }

    [Fact]
    public void Domain_inventory_enters_the_evidence_graph_with_its_evidence()
    {
        var snap = Domain();
        var checks = DomainEvaluator.Evaluate(snap);
        Assert.Equal(["legacy"], Check(checks, "DM02").Subjects);
        Assert.Equal(["svc_sql", "admin.vechi"], Check(checks, "DM10").Subjects!.OrderByDescending(s => s));

        var g = LogAnalyzer.Dfir.Windows.Domain.DomainGraph.Build(snap, checks, "EV-INV-1");
        Assert.All(g.Relationships, r => Assert.Equal("EV-INV-1", r.EvidenceId));
        Assert.All(g.Relationships, r => Assert.StartsWith("LDAP ", r.Locator));

        var da = g.Find("Group", "Domain Admins")!;
        var members = g.Edges(da.Id).Where(r => r.Type == LogAnalyzer.Dfir.Graph.RelationType.MemberOf).Select(r => r.SourceEntity).ToList();
        Assert.Equal(["Account:corp.local\\administrator", "Account:corp.local\\svc_sql", "Account:corp.local\\admin.vechi"], members);

        // Only NECONFORM checks produce VIOLATES, and only for the accounts the evaluator named.
        var violates = g.Relationships.Where(r => r.Type == LogAnalyzer.Dfir.Graph.RelationType.Violates).ToList();
        Assert.Contains(violates, r => r.SourceEntity == "Account:corp.local\\legacy" && r.TargetEntity == "Control:dm02" && r.Derivation == "DomainEvaluator DM02");
        Assert.Contains(violates, r => r.SourceEntity == "Host:app02" && r.TargetEntity == "Control:dm22");
        Assert.DoesNotContain(violates, r => r.SourceEntity == "Host:dc01");
        Assert.DoesNotContain(violates, r => r.TargetEntity == "Control:dm20"); // DE VERIFICAT is not a violation
        Assert.Equal("CN=legacy,DC=corp,DC=local", violates.First(r => r.TargetEntity == "Control:dm02").Locator["LDAP ".Length..]);

        // A computer account is the same Host entity a case graph builds for that host name.
        var caseGraph = LogAnalyzer.Dfir.Graph.EvidenceGraph.Build([], [], "APP02");
        Assert.Contains(caseGraph.Entities, e => e.Id == "Host:app02");
        var merged = LogAnalyzer.Dfir.Windows.Domain.DomainGraph.Build(snap, checks, "EV-INV-1", caseGraph);
        Assert.NotEmpty(merged.Path("Host:app02", "AdDomain:corp.local"));
        Assert.Throws<ArgumentException>(() => LogAnalyzer.Dfir.Windows.Domain.DomainGraph.Build(snap, checks, ""));
    }

    [Fact]
    public void Compromised_mailbox_pattern_is_detected_from_exported_csv()
    {
        var dir = Path.Combine(Path.GetTempPath(), "la-mail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string me = "user@mail.example.ro";
            var sb = new StringBuilder("\"Received\",\"SenderAddress\",\"RecipientAddress\",\"Subject\",\"Status\",\"FromIP\",\"MessageId\"\n");
            for (int i = 0; i < 400; i++)
                sb.Append($"\"2026-09-29T21:{i % 60:00}:{i % 59:00}Z\",\"{me}\",\"victim{i}@gmail.com\",\"Factura restanta\",\"Delivered\",\"45.141.215.80\",\"<m{i}@x>\"\n");
            sb.Append($"\"2026-09-20T08:00:00Z\",\"{me}\",\"coleg@mail.example.ro\",\"Raport\",\"Delivered\",\"10.1.1.5\",\"<n1@x>\"\n");
            File.WriteAllText(Path.Combine(dir, "message_trace.csv"), sb.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(dir, "inbox_rules.csv"),
                "\"Mailbox\",\"Name\",\"Enabled\",\"ForwardTo\",\"RedirectTo\",\"ForwardAsAttachmentTo\",\"DeleteMessage\",\"MoveToFolder\",\"Description\"\n" +
                $"\"{me}\",\".\",\"True\",\"\",\"x9@protonmail.com\",\"\",\"True\",\"RSS Feeds\",\"If the message includes 'factura'\"\n");
            File.WriteAllText(Path.Combine(dir, "mailbox_forwarding.csv"),
                "\"Mailbox\",\"ForwardingSmtpAddress\",\"ForwardingAddress\",\"DeliverToMailboxAndForward\"\n" + $"\"{me}\",\"smtp:drop@yandex.ru\",\"\",\"True\"\n");

            var r = MailInvestigation.ImportAndAnalyze(dir, me, ["mail.example.ro", "example.ro"]);
            Assert.Equal(401, r.Messages.Count);
            Assert.Equal(ControlStatus.Neconform, Check(r.Checks, "EM01").Status);
            Assert.Contains("45.141.215.80", Check(r.Checks, "EM01").Evidence[0]);
            Assert.Equal(ControlStatus.Neconform, Check(r.Checks, "EM03").Status);
            Assert.Equal(ControlStatus.Neconform, Check(r.Checks, "EM04").Status);
            Assert.Contains(Check(r.Checks, "EM05").Evidence, e => e.Contains("@gmail.com: 400"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Generated_scripts_quote_user_input_and_use_official_cmdlets()
    {
        var evil = "a'; Remove-Item C:\\ -Recurse; '";
        var online = MailInvestigation.ExchangeOnlineScript(evil, Now.AddDays(-30), Now, @"C:\Export");
        Assert.Contains("$mbx = 'a''; Remove-Item C:\\ -Recurse; '''", online);
        Assert.Contains("Get-MessageTraceV2", online);
        Assert.Contains("Get-InboxRule", online);
        Assert.DoesNotContain("-Password", online, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-MessageTrackingLog", MailInvestigation.ExchangeOnPremScript("u@x.ro", Now.AddDays(-30), Now, @"C:\Export"));
        Assert.Equal(@"\28admin\29\2a\5c", DirectoryCollector.Escape(@"(admin)*\"));
    }

    [Fact]
    public void Checks_report_pdf_is_generated()
    {
        var pdf = Path.Combine(Path.GetTempPath(), $"checks-{Guid.NewGuid():N}.pdf");
        try
        {
            ChecksReportPdf.Write(pdf, "Investigație", "test", DomainEvaluator.Evaluate(Domain()), ["notă"],
                new ChecksReportPdf.Table("Tabel", ["A", "B"], [["1", "2"]]));
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 5));
        }
        finally { File.Delete(pdf); }
    }

    [Fact]
    public void Directory_queries_are_blocked_in_airgapped_mode()
    {
        AppModeContext.ResetForTests();
        AppModeContext.Initialize(new ModeDecision(AppMode.AirGapped, true, "test", ConnectivitySnapshot.Unknown("test")));
        try
        {
            Assert.Throws<NetworkBlockedException>(() => DirectoryCollector.Collect("dc01.corp.local"));
        }
        finally { AppModeContext.ResetForTests(); }
    }
}
