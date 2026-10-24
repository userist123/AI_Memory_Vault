using System.IO.Compression;
using System.Text;
using LogAnalyzer.Dfir.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Regression (owner GPOs, 2026-10-11): Group Policy Preferences and startup scripts were dropped silently, so the USB-storage block
/// (USBSTOR Start=4 via Registry.xml) and the disabled Bluetooth/Wi-Fi services (Services.xml) never became controls.
/// </summary>
public class GpoPreferencesImportTests
{
    private const string Root = "Backup/{11111111-2222-3333-4444-555555555555}/DomainSysvol/GPO/";

    private const string RegistryXml = @"<RegistrySettings>
<Registry name=""Start""><Properties action=""U"" hive=""HKEY_LOCAL_MACHINE"" key=""SYSTEM\CurrentControlSet\Services\USBSTOR"" name=""Start"" type=""REG_DWORD"" value=""00000004""/></Registry>
<Registry name=""BaselineID""><Properties action=""U"" hive=""HKEY_LOCAL_MACHINE"" key=""SOFTWARE\Org\GPOBaseline"" name=""BaselineID"" type=""REG_SZ"" value=""1103""/></Registry>
<Registry name=""Old""><Properties action=""D"" hive=""HKEY_LOCAL_MACHINE"" key=""SOFTWARE\Org"" name=""Old"" type=""REG_SZ"" value=""""/></Registry>
</RegistrySettings>";

    private const string ServicesXml = @"<NTServices>
<NTService name=""bthserv""><Properties startupType=""DISABLED"" serviceName=""bthserv"" serviceAction=""STOP""/></NTService>
<NTService name=""WlanSvc""><Properties startupType=""DISABLED"" serviceName=""WlanSvc"" serviceAction=""STOP""/></NTService>
</NTServices>";

    private static GpoBackup Backup()
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string text) { using var w = new StreamWriter(z.CreateEntry(path).Open(), new UTF8Encoding(false)); w.Write(text); }
            Add("Backup/{11111111-2222-3333-4444-555555555555}/Backup.xml",
                "<GroupPolicyBackupScheme><GroupPolicyObject><GroupPolicyCoreSettings><ID>{AAAA}</ID><DisplayName>Test GPO</DisplayName></GroupPolicyCoreSettings></GroupPolicyObject></GroupPolicyBackupScheme>");
            Add(Root + "Machine/Preferences/Registry/Registry.xml", RegistryXml);
            Add(Root + "Machine/Preferences/Services/Services.xml", ServicesXml);
            Add(Root + "Machine/Preferences/Drives/Drives.xml", "<Drives><Drive name=\"Z:\"/></Drives>");
            Add(Root + "Machine/Scripts/Startup/Enable-PrintService.ps1", "wevtutil set-log \"Microsoft-Windows-PrintService/Operational\" /e:true");
            Add(Root + "Machine/odd/thing.dat", "x");
        }
        ms.Position = 0;
        return GpoBackup.FromZip(ms);
    }

    [Fact]
    public void Preferences_become_controls_and_everything_else_is_listed_never_dropped()
    {
        var g = Backup();
        Assert.Equal(3, g.Preferences!.Count);
        Assert.Single(g.Scripts!);
        Assert.Contains(g.NotRead!, n => n.EndsWith("thing.dat"));

        var r = PolicyImport.FromGpoBackup(g, "1.0.0", "test");
        var reg = r.Policy.Controls.Where(c => c.Setting.Type == "registry").ToList();
        Assert.Contains(reg, c => c.Title.EndsWith(@"Services\USBSTOR\Start") && c.Desired.Values[0] == "4");
        Assert.Contains(reg, c => c.Title.EndsWith(@"GPOBaseline\BaselineID") && c.Desired.Values[0] == "1103");
        Assert.Contains(reg, c => c.Title.EndsWith(@"Services\bthserv\Start") && c.Desired.Values[0] == "4");
        Assert.Contains(reg, c => c.Title.EndsWith(@"Services\WlanSvc\Start") && c.Desired.Values[0] == "4");
        Assert.Contains(reg, c => c.Title.EndsWith(@"Org\Old"));                            // a delete becomes "absent"
        Assert.Contains(r.Unsupported, u => u.Contains("Enable-PrintService.ps1") && u.Contains("nu este executat"));
        Assert.Contains(r.Unsupported, u => u.Contains("Drives.xml") && u.Contains("nu este acceptată"));
        Assert.Contains(r.Unsupported, u => u.Contains("thing.dat"));
        Assert.Contains(r.Unsupported, u => u.Contains("bthserv") && u.Contains("STOP"));
        Assert.Empty(PolicyValidator.Validate(r.Policy));
    }
}
