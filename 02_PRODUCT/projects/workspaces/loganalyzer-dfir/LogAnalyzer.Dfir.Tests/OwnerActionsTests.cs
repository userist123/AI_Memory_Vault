using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Planner #14 E1: every rule the configuration, host, persistence, defender, log, account, credential and remote families can emit has a "Ce faci" sentence or an intended fallback.</summary>
public sealed class OwnerActionsTests
{
    // the ids the code emits in the families named by the brief (constants in ConfigurationAudit, LocalSecurityAudit, HostsFileRules and the live, persistence, defender, log, account, credential, remote rules)
    private static readonly string[] Emitted =
    [
        ConfigurationAudit.RuleUac, ConfigurationAudit.RuleUacPrompt, ConfigurationAudit.RuleSmb1, ConfigurationAudit.RuleWdigest, ConfigurationAudit.RuleLsa,
        ConfigurationAudit.RuleDefenderOff, ConfigurationAudit.RuleDefenderExcl, ConfigurationAudit.RuleRdpNla,
        ConfigurationAudit.RuleFirewallOff, ConfigurationAudit.RuleRemoteRegistry, ConfigurationAudit.RuleAutologon, ConfigurationAudit.RuleTokenFilter, ConfigurationAudit.RuleAlwaysInstall,
        ConfigurationAudit.RuleSmbSigning, "SVC-UNQUOTED-PATH",
        LocalSecurityAudit.RuleProcess, LocalSecurityAudit.RuleLogon, LocalSecurityAudit.RuleCredVal, LocalSecurityAudit.RuleGuest, LocalSecurityAudit.RuleExtraAdmins,
        "PS-HISTORY-SUSPICIOUS", "HOSTS-REDIRECT", "HOSTS-SECURITY-DOMAIN", "LIVE-WMI-SUBSCRIPTION", "LIVE-STARTUP-FILE", "LIVE-STARTUP-LNK",
        "LIVE-UNSIGNED-USERPATH", "LIVE-UNSIGNED-USERPATH-NET", "LIVE-TASK-USERPATH", "LIVE-AUTORUN-USERPATH", "LIVE-SERVICE-USERPATH",
        "PERSIST-IFEO-DEBUGGER", "PERSIST-RUNKEY-USERPATH", "PERSIST-SERVICE-CONFIG", "PERSIST-SERVICE-USERPATH", "PERSIST-STARTUP-FOLDER", "PERSIST-TASK",
        "PERSIST-TASK-CACHE-MISMATCH", "PERSIST-TASK-CONFIG", "PERSIST-TASK-NO-SD", "PERSIST-TASK-NO-XML", "PERSIST-WINLOGON", "PERSIST-WMI",
        "DEF-TAMPER", "DEF-DETECTION", "DEF-HISTORY-ONLY", "LOG-TAMPER", "LOG-GAP",
        "ACCOUNT-ADDED-PRIVILEGED-GROUP", "ACCOUNT-CREATED", "ACCOUNT-CREATED-THEN-USED", "CRED-BRUTEFORCE", "CRED-SPRAY",
        "REMOTE-PSEXEC", "REMOTE-RDP-INTERNAL", "REMOTE-RDP-PUBLIC", "REMOTE-SSH", "REMOTE-TOOL-EXECUTED", "REMOTE-TOOL-PRESENT", "REMOTE-WINRM",
    ];

    private static Finding Fin(string rule) => new() { FindingId = "F-1", RuleId = rule, Title = "t", Description = "d" };

    [Fact]
    public void Every_emitted_rule_of_the_named_families_has_a_table_sentence_and_the_general_one_is_the_fallback()
    {
        foreach (var id in Emitted)
        {
            var s = OwnerActions.ForRule(id);
            Assert.True(s is { Length: > 10 }, $"{id} has no Ce faci sentence");
            Assert.NotEqual(OwnerActions.Fallback, s);
        }
        Assert.Null(OwnerActions.ForRule("UNKNOWN-RULE-X"));
        Assert.Equal("Verificați constatarea cu pașii „Cum verifici tu” și decideți dacă a fost o acțiune autorizată.", OwnerActions.Fallback);
        Assert.Equal(OwnerActions.Fallback, OwnerActions.For(Fin("UNKNOWN-RULE-X")));
    }

    [Fact]
    public void Exact_ids_beat_prefixes_and_a_prefix_covers_an_unlisted_id_and_sentences_are_sentences_without_verdict_words()
    {
        Assert.StartsWith("Setați din nou confirmarea pentru administratori", OwnerActions.ForRule("CFG-UAC-NOPROMPT"));
        Assert.Contains("auditpol /set", OwnerActions.ForRule("CFG-AUDIT-NEW-SUBCATEGORY"));
        Assert.NotNull(OwnerActions.ForRule("CRED-NEW"));
        foreach (var s in Emitted.Select(i => OwnerActions.ForRule(i)!).Append(OwnerActions.Fallback))
        {
            Assert.EndsWith(".", s);
            Assert.DoesNotContain("conform", s, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ş", s, StringComparison.Ordinal);   // cedilla forms are wrong; comma-below only
            Assert.DoesNotContain("ţ", s, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_findings_own_first_step_is_used_before_the_table()
    {
        var f = Fin("CFG-SMB1");
        Assert.Equal(OwnerActions.ForRule("CFG-SMB1"), OwnerActions.For(f));
        f.RecommendedNextSteps.AddRange(["Primul pas.", "Al doilea."]);
        Assert.Equal("Primul pas.", OwnerActions.For(f));
    }
}
