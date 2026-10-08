using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using LogAnalyzer.Dfir.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Owner policy samples (GPO backups, possibly zipped inside other zips) named by LADFIR_POLICY_SAMPLES (a file or a folder).
/// Skipped with "POLICY SAMPLES UNAVAILABLE" when absent; the samples are never copied into the repository.
/// </summary>
public sealed class PolicySamplesFactAttribute : FactAttribute
{
    public PolicySamplesFactAttribute()
    {
        var p = Environment.GetEnvironmentVariable("LADFIR_POLICY_SAMPLES");
        if (string.IsNullOrEmpty(p) || !(File.Exists(p) || Directory.Exists(p))) Skip = "LADFIR_POLICY_SAMPLES not set or missing — POLICY SAMPLES UNAVAILABLE";
    }
}

public sealed class PolicyImportTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>Writes a PReg v1 file the way Group Policy does: "[key;value;type;size;data]" in UTF-16 with LE integers.</summary>
    private static byte[] Pol(params (string Key, string Value, int Type, byte[] Data)[] entries)
    {
        var ms = new MemoryStream();
        ms.Write("PReg"u8);
        ms.Write(BitConverter.GetBytes(1));
        void C(char c) => ms.Write(Encoding.Unicode.GetBytes(c.ToString()));
        foreach (var (key, value, type, data) in entries)
        {
            C('['); ms.Write(Encoding.Unicode.GetBytes(key + "\0")); C(';');
            ms.Write(Encoding.Unicode.GetBytes(value + "\0")); C(';');
            ms.Write(BitConverter.GetBytes(type)); C(';');
            ms.Write(BitConverter.GetBytes(data.Length)); C(';');
            ms.Write(data); C(']');
        }
        return ms.ToArray();
    }

    private static byte[] Sz(string s) => Encoding.Unicode.GetBytes(s + "\0");

    [Fact]
    public void Registry_pol_entries_become_controls_and_the_rest_is_listed()
    {
        var pol = Pol(
            (@"Software\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging", "EnableScriptBlockLogging", PolEntry.RegDword, BitConverter.GetBytes(1u)),
            (@"Software\Policies\Microsoft\Windows\EventLog\Security", "MaxSize", PolEntry.RegDword, BitConverter.GetBytes(196608u)),
            (@"Software\Policies\Contoso", "Path", PolEntry.RegSz, Sz(@"C:\Program Files\x 'y'")),
            (@"Software\Policies\Contoso", "**del.Old", PolEntry.RegSz, Sz(" ")),
            (@"Software\Policies\Contoso\List", "**delvals.", PolEntry.RegSz, Sz(" ")),
            (@"Software\Policies\Contoso", "Blob", PolEntry.RegBinary, [1, 2, 3]));

        var r = PolicyImport.FromRegistryPol(pol, "HKLM", "GPO-TEST", "1.0.0", "owner", @"X:\Machine\Registry.pol");

        Assert.Equal(4, r.Policy.Controls.Count);
        var sbl = r.Policy.Controls[0];
        Assert.Equal(("registry", "HKLM", @"Software\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging", "EnableScriptBlockLogging", "dword"),
            (sbl.Setting.Type, sbl.Setting.Hive, sbl.Setting.Key, sbl.Setting.Name, sbl.Setting.ValueType));
        Assert.Equal("1", sbl.Desired.RemediationValue);
        Assert.Equal("196608", r.Policy.Controls[1].Desired.Values[0]);
        Assert.Equal(@"C:\Program Files\x 'y'", r.Policy.Controls[2].Desired.Values[0]);
        var del = r.Policy.Controls[3];
        Assert.Equal(("Old", "absent", "manual"), (del.Setting.Name, del.Desired.Operator, del.Remediation));
        Assert.Equal(2, r.Unsupported.Count);
        Assert.Contains(r.Unsupported, u => u.Contains("deletevalues") && u.Contains(@"Contoso\List"));
        Assert.Contains(r.Unsupported, u => u.Contains("binary") && u.Contains("Blob"));
        Assert.Empty(PolicyValidator.Validate(r.Policy));
        Assert.Equal(r.Policy.Sha256, PolicyLoader.Parse(r.Yaml).Sha256);
    }

    [Fact]
    public void Header_only_registry_pol_has_no_entries_and_damage_is_reported()
    {
        Assert.Empty(RegistryPol.Parse(Pol()));
        var good = Pol((@"Software\Policies\A", "V", PolEntry.RegDword, BitConverter.GetBytes(7u)));
        Assert.Throws<InvalidDataException>(() => RegistryPol.Parse(good[..^6]));
        Assert.Throws<InvalidDataException>(() => RegistryPol.Parse("PReg\u0002\0\0\0"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => RegistryPol.Parse(new byte[8]));
    }

    [Fact]
    public void Security_template_maps_account_policy_and_registry_values_and_lists_the_rest()
    {
        const string inf = """
            [Unicode]
            Unicode=yes
            [System Access]
            MinimumPasswordLength = 14
            LockoutBadCount = 5
            PasswordComplexity = 1
            [Privilege Rights]
            SeDebugPrivilege = *S-1-5-32-544
            [Service General Setting]
            "AppIDSvc",2,""
            [Registry Values]
            MACHINE\System\CurrentControlSet\Control\Lsa\LmCompatibilityLevel=4,5
            MACHINE\System\CurrentControlSet\Services\LanManServer\Parameters\NullSessionPipes=7,
            MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\LegalNoticeCaption=1,"Notice"
            [Version]
            signature="$CHICAGO$"
            """;
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(inf)).ToArray();
        var r = PolicyImport.FromSecurityTemplate(SecurityTemplate.Decode(bytes), "INF-TEST", "1.0.0", "owner", "GptTmpl.inf");

        var sec = r.Policy.Controls.Where(c => c.Setting.Type == "secpol").ToDictionary(c => c.Setting.Name, c => c.Desired.Values[0]);
        Assert.Equal(new Dictionary<string, string> { ["MinimumPasswordLength"] = "14", ["LockoutBadCount"] = "5" }, sec);
        var lm = r.Policy.Controls.Single(c => c.Setting.Name == "LmCompatibilityLevel");
        Assert.Equal(("HKLM", @"System\CurrentControlSet\Control\Lsa", "dword", "5"), (lm.Setting.Hive, lm.Setting.Key, lm.Setting.ValueType, lm.Desired.Values[0]));
        var pipes = r.Policy.Controls.Single(c => c.Setting.Name == "NullSessionPipes");
        Assert.Equal(("multi_string", ""), (pipes.Setting.ValueType, pipes.Desired.Values[0]));
        Assert.Equal("Notice", r.Policy.Controls.Single(c => c.Setting.Name == "LegalNoticeCaption").Desired.Values[0]);
        Assert.Contains(r.Unsupported, u => u.Contains("PasswordComplexity"));
        Assert.Contains(r.Unsupported, u => u.Contains("[Privilege Rights]") && u.Contains("1 setări"));
        Assert.Contains(r.Unsupported, u => u.Contains("[Service General Setting]") && u.Contains("1 setări"));
        Assert.Equal(2, r.Metadata.Count);
        Assert.Empty(PolicyValidator.Validate(r.Policy));
    }

    [Fact]
    public void Audit_csv_rows_become_audit_controls_by_guid()
    {
        const string csv = """
            Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value
            ,System,Audit Process Creation,{0cce922b-69ae-11d9-bed3-505054503030},Success,,1
            ,System,Audit Logon,{0cce9215-69ae-11d9-bed3-505054503030},Success and Failure,,3
            ,System,Audit Filtering Platform Connection,{0cce9226-69ae-11d9-bed3-505054503030},No Auditing,,0
            ,System,Audit Unknown Thing,{11111111-2222-3333-4444-555555555555},Failure,,2
            """;
        var r = PolicyImport.FromAuditCsv(csv, "AUD-TEST", "1.0.0", "owner", "audit.csv");
        var got = r.Policy.Controls.ToDictionary(c => c.Setting.Name, c => c.Desired.Values[0]);
        Assert.Equal(new Dictionary<string, string>
        {
            ["Process Creation"] = "Success", ["Logon"] = "Success and Failure", ["Filtering Platform Connection"] = "No Auditing",
        }, got);
        Assert.Single(r.Unsupported, u => u.Contains("11111111-2222-3333-4444-555555555555"));
        Assert.Empty(PolicyValidator.Validate(r.Policy));
    }

    [Fact]
    public void Lgpo_text_imports_in_groups_of_four_and_rejects_malformed_text()
    {
        const string text = """
            ; LGPO text
            Computer
            Software\Policies\Microsoft\Windows\System
            EnableSmartScreen
            DWORD:1

            User
            Software\Policies\Microsoft\Windows\Explorer
            NoAutoplayfornonVolume
            DELETE
            """;
        var r = PolicyImport.FromLgpoText(text, "LGPO-TEST", "1.0.0", "owner", "x.txt");
        Assert.Equal(2, r.Policy.Controls.Count);
        Assert.Equal(("HKLM", "machine", "1"), (r.Policy.Controls[0].Setting.Hive, r.Policy.Controls[0].Scope, r.Policy.Controls[0].Desired.Values[0]));
        Assert.Equal(("HKCU", "user", "absent"), (r.Policy.Controls[1].Setting.Hive, r.Policy.Controls[1].Scope, r.Policy.Controls[1].Desired.Operator));
        Assert.Throws<InvalidDataException>(() => LgpoText.Parse("Computer\nKey\nName"));
        Assert.Throws<InvalidDataException>(() => LgpoText.Parse("Machine\nKey\nName\nDWORD:1"));
        Assert.Throws<InvalidDataException>(() => LgpoText.Parse("Computer\nKey\nName\nBINARY:00"));
    }

    [Fact]
    public void An_imported_policy_is_a_draft_and_cannot_be_applied()
    {
        var r = PolicyImport.FromAuditCsv("Subcategory,Subcategory GUID,Setting Value\nAudit Logon,{0cce9215-69ae-11d9-bed3-505054503030},3\n",
            "AUD-DRAFT", "1.0.0", "owner", "audit.csv");
        var dir = Path.Combine(Path.GetTempPath(), "ladfir_polimp_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PolicyStore(Path.Combine(dir, "store.json"));
            Assert.Equal(PolicyStatus.Draft, store.Register(r.Policy, "owner").Status);
            Assert.Throws<PolicyLifecycleException>(() => store.RequireApplicable(r.Policy));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Gpo_backup_zip_is_found_and_not_a_backup_is_rejected()
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string n, byte[] b) { using var s = z.CreateEntry(n).Open(); s.Write(b); }
            Add("{AB}/Backup.xml", Encoding.UTF8.GetBytes("<GroupPolicyBackupScheme><GroupPolicyObject><GroupPolicyCoreSettings><ID>{AB}</ID><DisplayName>Test GPO</DisplayName></GroupPolicyCoreSettings></GroupPolicyObject></GroupPolicyBackupScheme>"));
            Add("{AB}/DomainSysvol/GPO/Machine/registry.pol", Pol((@"Software\Policies\A", "V", PolEntry.RegDword, BitConverter.GetBytes(2u))));
        }
        ms.Position = 0;
        var g = GpoBackup.FromZip(ms);
        Assert.Equal(("Test GPO", "{AB}"), (g.DisplayName, g.Guid));
        var r = PolicyImport.FromGpoBackup(g, "1.0.0");
        Assert.Equal(("GPO-AB", 1), (r.Policy.Id, r.Policy.Controls.Count));

        var other = new MemoryStream();
        using (var z = new ZipArchive(other, ZipArchiveMode.Create, leaveOpen: true)) { using var s = z.CreateEntry("readme.txt").Open(); s.Write("x"u8); }
        other.Position = 0;
        Assert.Throws<InvalidDataException>(() => GpoBackup.FromZip(other));
    }

    // ---- owner samples: every GPO backup found is imported and cross-checked against the gpreport.xml GPMC wrote for it ----

    private static IEnumerable<(string Name, GpoBackup Gpo)> SampleBackups()
    {
        var root = Environment.GetEnvironmentVariable("LADFIR_POLICY_SAMPLES")!;
        var zips = File.Exists(root) ? [root] : Directory.GetFiles(root, "*.zip", SearchOption.AllDirectories);
        foreach (var f in zips)
            foreach (var x in FromZipBytes(Path.GetFileName(f), File.ReadAllBytes(f))) yield return x;
    }

    private static IEnumerable<(string, GpoBackup)> FromZipBytes(string name, byte[] bytes)
    {
        using var z = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var inner = z.Entries.Where(e => e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Select(e => (e.FullName, Read(e))).ToList();
        bool isBackup = z.Entries.Any(e => e.FullName.EndsWith("/Backup.xml", StringComparison.OrdinalIgnoreCase) && e.Length > 0);
        if (isBackup) yield return (name, GpoBackup.FromZip(new MemoryStream(bytes)));
        foreach (var (n, b) in inner)
            foreach (var x in FromZipBytes(name + "!" + n, b)) yield return x;
    }

    private static byte[] Read(ZipArchiveEntry e) { using var s = e.Open(); var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }

    private static IEnumerable<XElement> All(XDocument x, string local) => x.Descendants().Where(e => e.Name.LocalName == local);
    private static string? Child(XElement e, string local) => e.Elements().FirstOrDefault(c => c.Name.LocalName == local)?.Value;
    private static string Unquote(string s) => s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    [PolicySamplesFact]
    public void Owner_gpo_backups_import_and_agree_with_gpreport()
    {
        var backups = SampleBackups().ToList();
        Assert.NotEmpty(backups);
        foreach (var (name, g) in backups)
        {
            Assert.True(g.GpReport is not null, $"{name}: gpreport.xml lipsește — nu există referință");
            var rep = XDocument.Parse(g.GpReport!);
            var r = PolicyImport.FromGpoBackup(g, "1.0.0", name);
            var errors = PolicyValidator.Validate(r.Policy);
            Assert.True(errors.Count == 0, $"{name}: {string.Join(" | ", errors.Take(5))}");
            Assert.Equal(Child(All(rep, "GPO").First(), "Name"), g.DisplayName);
            // Backup.xml ID is the GPO's own GUID (the backup folder is named after the backup ID, not the GPO).
            Assert.Equal(All(rep, "Identifier").First(e => !e.HasElements).Value, g.Guid, StringComparer.OrdinalIgnoreCase);
            output.WriteLine($"{g.DisplayName}: {r.Policy.Controls.Count} controale ({string.Join(", ", r.Policy.Controls.GroupBy(c => c.Setting.Type).Select(x => $"{x.Key} {x.Count()}"))}), {r.Unsupported.Count} neacceptate, sha256 {r.Policy.Sha256[..12]}");
            foreach (var u in r.Unsupported.Where(u => !u.Contains("registry.pol"))) output.WriteLine("  " + u);
            output.WriteLine($"  registry.pol neacceptate: {r.Unsupported.Count(u => u.Contains("registry.pol"))}");

            // Advanced audit: every AuditSetting GPMC reports is a control with the same subcategory and setting.
            var audit = r.Policy.Controls.Where(c => c.Setting.Type == "audit").ToDictionary(c => AuditSubcategories.ByName[c.Setting.Name], c => c.Desired.Values[0]);
            var expectedAudit = All(rep, "AuditSetting").ToList();
            Assert.Equal(expectedAudit.Count, audit.Count);
            foreach (var a in expectedAudit)
            {
                var guid = Guid.Parse(Child(a, "SubcategoryGuid")!.Trim('{', '}'));
                Assert.Equal(AuditSubcategories.FromFlags(uint.Parse(Child(a, "SettingValue")!)), audit[guid]);
            }

            // Account policy: numeric settings the engine reads match GPMC's SettingNumber.
            foreach (var acc in All(rep, "Account").Where(a => PolicyImport.SecpolNames.Contains(Child(a, "Name")) && Child(a, "SettingNumber") is not null))
                Assert.Equal(Child(acc, "SettingNumber"), r.Policy.Controls.Single(c => c.Setting.Type == "secpol" && c.Setting.Name == Child(acc, "Name")).Desired.Values[0]);

            // Security options backed by a registry value: same hive, key, value name and data.
            foreach (var so in All(rep, "SecurityOptions").Where(s => Child(s, "KeyName")?.StartsWith(@"MACHINE\", StringComparison.OrdinalIgnoreCase) == true))
            {
                var path = Child(so, "KeyName")!;
                var (key, valueName) = (path[8..path.LastIndexOf('\\')], path[(path.LastIndexOf('\\') + 1)..]);
                // GPMC echoes the template literal of a REG_SZ ("1"); the quotes delimit it and are not part of the value
                // (the registry holds ScRemoveOption as 0, not "0").
                var expected = Child(so, "SettingNumber") ?? (Child(so, "SettingString") is { } str ? Unquote(str)
                    : string.Join(" ", so.Elements().Where(e => e.Name.LocalName == "SettingStrings").Elements().Select(v => v.Value)));
                var c = r.Policy.Controls.SingleOrDefault(c => c.Setting.Type == "registry" && c.Setting.Hive == "HKLM"
                    && c.Setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && c.Setting.Name.Equals(valueName, StringComparison.OrdinalIgnoreCase));
                Assert.True(c is not null, $"{name}: {path} din gpreport nu are control");
                Assert.Equal(expected, c!.Desired.Values[0]);
            }

            // Non-ADMX registry settings GPMC lists come from registry.pol keys the parser read.
            var polKeys = new[] { g.MachinePol, g.UserPol }.Where(p => p is not null).SelectMany(p => RegistryPol.Parse(p!)).Select(e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var rs in All(rep, "RegistrySetting"))
                Assert.Contains(Child(rs, "KeyPath")!, polKeys);

            // Nothing is dropped: each registry.pol entry is a control or an unsupported line.
            foreach (var (pol, source) in new[] { (g.MachinePol, "Machine/registry.pol"), (g.UserPol, "User/registry.pol") })
            {
                if (pol is null) continue;
                int entries = RegistryPol.Parse(pol).Count;
                int controls = r.Policy.Controls.Count(c => c.References.Contains(source));
                int listed = r.Unsupported.Count(u => u.StartsWith(source + ":"));
                Assert.Equal(entries, controls + listed);
            }
        }
    }
}
