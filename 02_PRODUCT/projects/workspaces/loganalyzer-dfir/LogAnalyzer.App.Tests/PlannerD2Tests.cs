using System.IO;
using System.Text;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Planner #13 D2: files imported by hand get the right type (auditpol CSV, local accounts JSON, hosts, PSReadLine history) and the audit parser reads the numeric Setting Value column in any language. Tiny synthetic files only.</summary>
public sealed class PlannerD2Tests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private string Put(string name, string text)
    {
        var p = Path.Combine(_k.Dir, "in", name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text, new UTF8Encoding(false));
        return p;
    }

    private const string Guid1 = "0CCE9215-69AE-11D9-BED3-505054503030", Guid2 = "0CCE922B-69AE-11D9-BED3-505054503030";
    private const string EnCsv = "\r\nMachine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting\r\nWS,System,Logon,{" + Guid1 + "},Success and Failure,\r\n";
    private const string DeCsv = "\r\nComputername,Richtlinienziel,Unterkategorie,Unterkategorie-GUID,Einschlusseinstellung,Ausschlusseinstellung\r\nWS,System,Anmelden,{" + Guid1 + "},Erfolg und Fehler,\r\n";
    private const string AccountsJson = "{\n\"schema\":\"la.local_accounts/1\",\n\"collectedUtc\":\"2026-11-02T08:00:00Z\",\n\"machine\":\"WS\",\n\"accounts\":[\n{\"name\":\"Ana\",\"sid\":\"S-1-5-21-1-2-3-1001\",\"rid\":1001,\"enabled\":true}\n],\n\"administrators\":[\n]\n}\n";

    [Fact]
    public void Import_types_the_auditpol_csv_by_its_guid_columns_in_any_language_and_not_a_plain_csv()
    {
        Assert.Equal("auditpol_csv", InvestigationPipeline.ImportType(Put("a.csv", EnCsv)));
        Assert.Equal("auditpol_csv", InvestigationPipeline.ImportType(Put("b.txt", DeCsv)));   // localized header, the name does not matter
        Assert.Equal("file", InvestigationPipeline.ImportType(Put("c.csv", "a,b,c\r\n1,2,3\r\n")));
    }

    [Fact]
    public void Import_types_the_local_accounts_json_by_its_first_object_keys_and_not_another_json()
    {
        Assert.Equal("local_accounts_json", InvestigationPipeline.ImportType(Put("accounts.json", AccountsJson)));
        // no "schema" key (an older or hand-made file) but the collector's keys
        Assert.Equal("local_accounts_json", InvestigationPipeline.ImportType(Put("x.json", "{\"collectedUtc\":\"2026-11-02T08:00:00Z\",\"accounts\":[]}")));
        Assert.Equal("file", InvestigationPipeline.ImportType(Put("other.json", "{\"accounts\":[{\"name\":\"a\"}]}")));   // an "accounts" list alone proves nothing
        Assert.Equal("file", InvestigationPipeline.ImportType(Put("arr.json", "[1,2,3]")));
        Assert.Equal("file", InvestigationPipeline.ImportType(Put("broken.json", "{\"schema\":")));
    }

    [Fact]
    public void Import_types_hosts_and_every_psreadline_history_file_and_the_intake_scan_lists_the_new_family()
    {
        Assert.Equal("hosts_file", InvestigationPipeline.ImportType(Put("etc/hosts", "127.0.0.1 localhost\r\n")));
        Assert.Equal("ps_readline_history", InvestigationPipeline.ImportType(Put("ps/ConsoleHost_history.txt", "whoami\r\n")));
        Assert.Equal("ps_readline_history", InvestigationPipeline.ImportType(Put("ps/Visual Studio Code Host_history.txt", "Get-Process\r\n")));
        Assert.Equal("file", InvestigationPipeline.ImportType(Put("ps/history.txt", "not psreadline\r\n")));
        var scan = IncomingEvidence.Scan(Path.Combine(_k.Dir, "in"));
        var fam = scan.Families.Single(f => f.Id == "localsecurity");
        Assert.False(fam.Present);   // nothing of that kind imported yet at this point of the test
        Put("a.csv", EnCsv); Put("accounts.json", AccountsJson);
        Assert.Equal(2, IncomingEvidence.Scan(Path.Combine(_k.Dir, "in")).Families.Single(f => f.Id == "localsecurity").Files);
    }

    private static List<TimelineEvent> Parse(string path)
    {
        var item = new EvidenceItem { EvidenceId = "E-1", CaseId = "C", Source = "auditpol", SourceType = "auditpol_csv", AcquiredAtUtc = DateTimeOffset.UtcNow };
        var sink = new ListSink();
        new AuditPolicyCsvParser().Parse(item, path, sink, default);
        return sink.Events;
    }

    [Fact]
    public void A_german_row_with_the_numeric_Setting_Value_column_is_understood_without_the_text()
    {
        const string head = "Computername,Richtlinienziel,Unterkategorie,Unterkategorie-GUID,Einschlusseinstellung,Ausschlusseinstellung,Setting Value\r\n";
        var rows = Parse(Put("de.csv", head +
            "WS,System,Anmelden,{" + Guid1 + "},Erfolg und Fehler,,3\r\n" +
            "WS,System,Prozesserstellung,{" + Guid2 + "},Keine Überwachung,,0\r\n" +
            "WS,System,Anmeldeinformationen überprüfen,{0CCE923F-69AE-11D9-BED3-505054503030},Fehler,,2\r\n"));
        Assert.Equal(3, rows.Count);
        Assert.Equal(("da", "da"), (rows[0].Fields["AuditSuccess"], rows[0].Fields["AuditFailure"]));
        Assert.Equal(("nu", "nu"), (rows[1].Fields["AuditSuccess"], rows[1].Fields["AuditFailure"]));
        Assert.Equal(("nu", "da"), (rows[2].Fields["AuditSuccess"], rows[2].Fields["AuditFailure"]));
    }

    [Fact]
    public void Without_the_numeric_column_a_foreign_language_text_stays_unknown_and_English_still_works_and_a_bad_number_falls_back_to_the_text()
    {
        var de = Parse(Put("de2.csv", DeCsv));
        Assert.Equal("necunoscut", Assert.Single(de).Fields["AuditSuccess"]);   // the documented gap: never a guess
        var en = Parse(Put("en.csv", EnCsv));
        Assert.Equal(("da", "da"), (en[0].Fields["AuditSuccess"], en[0].Fields["AuditFailure"]));
        var odd = Parse(Put("odd.csv", "x\r\nWS,System,Logon,{" + Guid1 + "},Success,,9\r\n"));
        Assert.Equal(("da", "nu"), (odd[0].Fields["AuditSuccess"], odd[0].Fields["AuditFailure"]));
    }
}
