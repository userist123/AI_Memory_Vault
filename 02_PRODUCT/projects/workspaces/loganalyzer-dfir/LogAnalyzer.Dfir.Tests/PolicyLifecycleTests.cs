using System.Diagnostics;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Policy;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>P6: native policy format, validation and lifecycle (no DRAFT apply, hash binding, no self-approval).</summary>
public sealed class PolicyLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_pol_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    public const string Sample = """
        policy:
          id: LA-STATION-AUDIT
          version: 1.0.0
          title: Audit minim pentru stații izolate
          author: DOMAIN\autor
          created: 2026-10-05
          description: Auditul creării de procese și al conexiunilor WFP, cerute de investigațiile anterioare.
          target: windows-workstation
          compatibility: [Windows 10, Windows 11]
        controls:
          - id: AUD-01
            title: Auditul creării de procese
            description: Fără 4688 nu se știe ce procese au pornit.
            scope: machine
            detection: { type: audit, name: Process Creation }
            desired_state: Success
            remediation: set
            verification: reread
            severity: high
            evidence_required: [auditpol, Security 4688]
          - id: REG-01
            title: Linia de comandă în 4688
            detection: { type: registry, hive: HKLM, key: 'SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit', value: ProcessCreationIncludeCmdLine_Enabled, value_type: dword }
            desired_state: { equals: '1' }
            remediation: set
            verification: reread
            severity: medium
            dependencies: [AUD-01]
        """;

    [Fact]
    public void Native_policy_loads_with_its_hash_and_controls()
    {
        var p = PolicyLoader.Parse(Sample);
        Assert.Equal(("LA-STATION-AUDIT", "1.0.0", 2), (p.Id, p.Version, p.Controls.Count));
        Assert.Equal(64, p.Sha256.Length);
        var reg = p.Controls[1];
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit\ProcessCreationIncludeCmdLine_Enabled", reg.Setting.Display);
        Assert.True(reg.Desired.IsSatisfiedBy("1"));
        Assert.False(reg.Desired.IsSatisfiedBy("0"));
        Assert.False(reg.Desired.IsSatisfiedBy(null));
        Assert.Empty(PolicyValidator.Validate(p));
        Assert.NotEqual(p.Sha256, PolicyLoader.Parse(Sample.Replace("1.0.0", "1.0.1")).Sha256);
    }

    [Fact]
    public void Json_is_accepted_and_unknown_keys_are_rejected()
    {
        var json = """{"policy":{"id":"J","version":"1","title":"t","author":"a"},"controls":[{"id":"C","title":"t","detection":{"type":"service","name":"Spooler"},"desired_state":"Disabled","remediation":"manual","verification":"reread"}]}""";
        var p = PolicyLoader.Parse(json, "json");
        Assert.Equal("service", p.Controls[0].Setting.Type);
        Assert.Throws<FormatException>(() => PolicyLoader.Parse(json.Replace("\"author\":\"a\"", "\"author\":\"a\",\"approved\":true"), "json"));
    }

    [Fact]
    public void Validation_names_every_problem()
    {
        var bad = PolicyLoader.Parse(Sample.Replace("value_type: dword", "value_type: blob").Replace("name: Process Creation", "name: Nonexistent")
                                           .Replace("dependencies: [AUD-01]", "dependencies: [AUD-99]").Replace("author: DOMAIN\\autor", "author: ''"));
        var errors = PolicyValidator.Validate(bad);
        Assert.Contains(errors, e => e.Contains("author"));
        Assert.Contains(errors, e => e.Contains("REG-01") && e.Contains("blob"));
        Assert.Contains(errors, e => e.Contains("AUD-01") && e.Contains("Nonexistent"));
        Assert.Contains(errors, e => e.Contains("AUD-99"));
    }

    [Fact]
    public void Lifecycle_blocks_draft_apply_self_approval_and_changed_text()
    {
        var store = new PolicyStore(Path.Combine(_dir, "store.json"));
        var p = PolicyLoader.Parse(Sample);
        store.Register(p, @"DOMAIN\operator");
        Assert.Throws<PolicyLifecycleException>(() => store.RequireApplicable(p));                       // DRAFT
        store.Validate(p, @"DOMAIN\operator");
        Assert.Throws<PolicyLifecycleException>(() => store.MarkTested(p, @"DOMAIN\operator", 1, 0));   // not every control read
        store.MarkTested(p, @"DOMAIN\operator", 2, 0);
        Assert.Throws<PolicyLifecycleException>(() => store.RequireApplicable(p));                       // TESTED
        Assert.Throws<PolicyLifecycleException>(() => store.Approve(p, @"domain\AUTOR", "ok"));           // author
        Assert.Throws<PolicyLifecycleException>(() => store.Approve(p, @"DOMAIN\operator", "ok"));        // registered it
        Assert.Throws<PolicyLifecycleException>(() => store.Approve(p, @"DOMAIN\sef", " "));              // no reason
        store.Approve(p, @"DOMAIN\sef", "revizuită împreună cu echipa de securitate");
        Assert.Equal(PolicyStatus.Approved, store.RequireApplicable(p).Status);

        var tampered = PolicyLoader.Parse(Sample.Replace("desired_state: { equals: '1' }", "desired_state: { equals: '0' }"));
        Assert.Throws<PolicyLifecycleException>(() => store.RequireApplicable(tampered));               // same id/version, other text
        Assert.Throws<PolicyLifecycleException>(() => store.Register(tampered, "x"));

        var reloaded = new PolicyStore(Path.Combine(_dir, "store.json"));
        var r = reloaded.Get(p)!;
        Assert.Equal(PolicyStatus.Approved, r.Status);
        Assert.Equal([PolicyStatus.Draft, PolicyStatus.Validated, PolicyStatus.Tested, PolicyStatus.Approved], r.History.Select(h => h.To).ToArray());
        Assert.All(r.History, h => Assert.Equal(p.Sha256, h.PolicySha256));
    }

    /// <summary>The subcategory GUIDs used to read and set audit policy are the ones this Windows reports (auditpol /list).</summary>
    [Fact]
    public void Audit_subcategory_guids_match_auditpol()
    {
        var psi = new ProcessStartInfo("auditpol.exe", "/list /subcategory:* /v") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var listed = Regex.Matches(text, @"^\s{2,}(.+?)\s+\{([0-9A-F-]{36})\}", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value.Trim(), m => Guid.Parse(m.Groups[2].Value), StringComparer.OrdinalIgnoreCase);
        Assert.True(listed.Count > 40, $"{listed.Count} subcategorii listate de auditpol");
        Assert.Equal(listed.Count, AuditSubcategories.ByName.Count);
        foreach (var (name, guid) in AuditSubcategories.ByName)
            Assert.True(listed.TryGetValue(name, out var g) && g == guid, $"{name}: tabel {guid}, auditpol {(listed.TryGetValue(name, out var x) ? x : "lipsă")}");
    }
}
