using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Core.Services.Details;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Security audit #12: XML that comes from evidence or from imported policies (GPO backups, event XML) is parsed by one helper with DTD
/// processing prohibited and no resolver: a DOCTYPE, an internal entity (entity expansion) or an external entity is refused, never expanded.
/// </summary>
public sealed class SafeXmlTests
{
    private const string EntityExpansion =
        "<?xml version=\"1.0\"?><!DOCTYPE l [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">" +
        "<!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\"><!ENTITY e \"&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;\"><!ENTITY f \"&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;\">]><l>&f;</l>";

    private const string ExternalEntity = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY ext SYSTEM \"file:///C:/Windows/win.ini\">]><x>&ext;</x>";

    private const string EventXml =
        "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><Provider Name=\"Microsoft-Windows-Security-Auditing\"/>" +
        "<EventID>4625</EventID><Computer>LAB-PC</Computer></System><EventData><Data Name=\"TargetUserName\">a&amp;b &lt;x&gt; &#x41;</Data></EventData></Event>";

    [Theory]
    [InlineData("<!DOCTYPE x [<!ENTITY a \"hello\">]><x>&a;</x>")]
    [InlineData(EntityExpansion)]
    [InlineData(ExternalEntity)]
    [InlineData("<!DOCTYPE x SYSTEM \"http://example.invalid/x.dtd\"><x/>")]
    public void Doctype_and_entities_are_refused(string xml)
    {
        var sw = Stopwatch.StartNew();
        Assert.Throws<XmlException>(() => SafeXml.Parse(xml));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), "refused without expanding");
    }

    [Fact]
    public void Normal_event_xml_with_predefined_and_character_references_parses()
    {
        var doc = SafeXml.Parse(EventXml);
        var data = doc.Descendants().Single(e => e.Name.LocalName == "Data");
        Assert.Equal("a&b <x> A", data.Value);
        Assert.Equal("4625", doc.Descendants().Single(e => e.Name.LocalName == "EventID").Value);
    }

    [Fact]
    public void TryParse_returns_false_instead_of_throwing()
    {
        Assert.False(SafeXml.TryParse(EntityExpansion, out var none));
        Assert.Null(none);
        Assert.True(SafeXml.TryParse(EventXml, out var doc));
        Assert.NotNull(doc!.Root);
    }

    private static GpoBackup Zip(params (string Name, string Text)[] files)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (n, t) in files) { using var w = new StreamWriter(z.CreateEntry(n).Open(), new UTF8Encoding(false)); w.Write(t); }
        ms.Position = 0;
        return GpoBackup.FromZip(ms);
    }

    private const string Root = "Backup/{11111111-2222-3333-4444-555555555555}/DomainSysvol/GPO/";
    private const string BackupXml = "<GroupPolicyBackupScheme><GroupPolicyObject><GroupPolicyCoreSettings><ID>{AAAA}</ID><DisplayName>Test GPO</DisplayName></GroupPolicyCoreSettings></GroupPolicyObject></GroupPolicyBackupScheme>";

    [Fact]
    public void Gpo_preference_with_entities_is_listed_as_invalid_and_not_expanded()
    {
        var g = Zip(("Backup/{11111111-2222-3333-4444-555555555555}/Backup.xml", BackupXml),
                    (Root + "Machine/Preferences/Registry/Registry.xml", EntityExpansion),
                    (Root + "Machine/Preferences/Services/Services.xml",
                     "<NTServices><NTService name=\"bthserv\"><Properties startupType=\"DISABLED\" serviceName=\"bthserv\"/></NTService></NTServices>"));
        var r = PolicyImport.FromGpoBackup(g, "1.0.0", "test");
        Assert.Contains(r.Unsupported, u => u.Contains("Registry.xml") && u.Contains("XML invalid"));
        Assert.Single(r.Policy.Controls);             // only the service; nothing from the refused file
    }

    [Fact]
    public void Gpo_backup_xml_with_a_doctype_is_refused_with_a_clear_error()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Zip(("Backup/{1}/Backup.xml", ExternalEntity), ("Backup/{1}/DomainSysvol/GPO/Machine/registry.pol", "PReg")));
        Assert.Contains("Backup.xml", ex.Message);
    }

    [Fact]
    public void Detail_sheet_does_not_expand_entities_in_event_xml()
    {
        var sheet = DetailSheetBuilder.Build(new ParsedEvent { EventId = 4625, ProviderName = "x", XmlData = EntityExpansion });
        Assert.True(sheet.Raw.Length < EntityExpansion.Length + 100);
        Assert.DoesNotContain("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", sheet.Raw);

        var normal = DetailSheetBuilder.Build(new ParsedEvent { EventId = 4625, ProviderName = "x", XmlData = EventXml });
        Assert.Contains("TargetUserName", normal.Raw);
    }
}
