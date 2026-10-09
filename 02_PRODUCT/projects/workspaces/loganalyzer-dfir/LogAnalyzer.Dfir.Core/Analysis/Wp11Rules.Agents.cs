using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    private enum AgentAction { Stopped, Disabled, Installed, ConfigChanged, Removed }

    private sealed record AgentHit(TimelineEvent Event, SecurityAgentRow Agent, AgentAction Action, string Detail);

    private static string ActionText(AgentAction a) => a switch
    {
        AgentAction.Stopped => "oprit", AgentAction.Disabled => "dezactivat (tip de pornire)", AgentAction.Installed => "serviciu nou cu numele produsului",
        AgentAction.ConfigChanged => "configurație schimbată", _ => "dezinstalat",
    };

    /// <summary>
    /// SECURITY-AGENT-STOPPED: a known AV/EDR/Sysmon product was stopped (System 7036, Sysmon 4), set to disabled (7040), removed
    /// (MsiInstaller 1034/11724), had its configuration changed (Sysmon 16) or a service with its name was registered (7045). Distinct from
    /// DEF-TAMPER (Defender's own log). A stop during shutdown, or one followed by a restart, is Info; a High finding nearby makes it High.
    /// The agent list is data (Data/security_agents.json).
    /// </summary>
    private static void Agents(Ctx c, List<Finding> o)
    {
        var cat = c.Data.Agents;
        var sysmonAgent = cat.Agents.FirstOrDefault(a => a.Kind.Equals("Sysmon", StringComparison.OrdinalIgnoreCase)) ?? new SecurityAgentRow { Id = "sysmon", Name = "Sysmon", Kind = "Sysmon" };
        var hits = new List<AgentHit>();
        var runs = new List<(SecurityAgentRow Agent, DateTimeOffset Time)>();
        foreach (var e in c.Events)
        {
            if (Ev(e, "System", 7036))
            {
                var agent = cat.ByService(F(e, "param1"));
                if (agent is null) continue;
                if (cat.IsStopped(F(e, "param2"))) hits.Add(new(e, agent, AgentAction.Stopped, F(e, "param1")));
                else if (cat.IsRunning(F(e, "param2")) && T(e) is { } t) runs.Add((agent, t));
            }
            else if (Ev(e, "System", 7040))
            {
                var agent = cat.ByService(F(e, "param1")) ?? cat.ByService(F(e, "param4"));
                if (agent is not null && cat.IsDisabled(F(e, "param3"))) hits.Add(new(e, agent, AgentAction.Disabled, F(e, "param1")));
            }
            else if (Ev(e, "System", 7045))
            {
                var agent = cat.ByService(F(e, "ServiceName"));
                if (agent is not null) hits.Add(new(e, agent, AgentAction.Installed, F(e, "ServiceName") + " " + F(e, "ImagePath")));
            }
            else if (Ev(e, Sysmon, 4) && F(e, "State").Contains("Stopped", StringComparison.OrdinalIgnoreCase))
                hits.Add(new(e, sysmonAgent, AgentAction.Stopped, "Sysmon (evenimentul de stare 4)"));
            else if (Ev(e, Sysmon, 16))
                hits.Add(new(e, sysmonAgent, AgentAction.ConfigChanged, F(e, "Configuration")));
            else if (Ev(e, "Application", 1034, 11724) && e.Provider.Contains("MsiInstaller", StringComparison.OrdinalIgnoreCase))
            {
                var agent = cat.ByProduct(AllText(e));
                if (agent is not null) hits.Add(new(e, agent, AgentAction.Removed, F(e, "Data0", "_message")));
            }
        }
        if (hits.Count == 0) return;

        var shutdownStarts = c.Events.Where(e => Ev(e, "System", 1074) && T(e) is not null).Select(T).Select(t => t!.Value).ToList();
        var logStops = c.Events.Where(e => Ev(e, "System", 6006) && T(e) is not null).Select(T).Select(t => t!.Value).ToList();
        string? Excuse(AgentHit h)
        {
            if (h.Action != AgentAction.Stopped || T(h.Event) is not { } t) return null;
            if (shutdownStarts.Any(s => t - s >= TimeSpan.Zero && t - s <= TimeSpan.FromMinutes(15)) || logStops.Any(s => s - t >= TimeSpan.Zero && s - t <= TimeSpan.FromMinutes(5)))
                return "oprirea coincide cu o oprire/repornire a sistemului (System 1074/6006)";
            if (h.Event.EventId == "7036" && runs.Any(r => r.Agent == h.Agent && r.Time > t && r.Time - t <= c.Options.RestartWindow))
                return $"serviciul a fost pornit din nou în cel mult {c.Options.RestartWindow.TotalMinutes:0} minute (7036 running)";
            return null;
        }

        foreach (var g in hits.GroupBy(h => (h.Agent.Id, h.Action)))
        {
            var items = g.OrderBy(h => T(h.Event)).ToList();
            var agent = items[0].Agent; var action = g.Key.Action;
            var excuses = items.Select(Excuse).ToList();
            bool allExcused = excuses.All(x => x is not null);
            var live = items.Where((h, i) => excuses[i] is null).ToList();

            Severity sev = action switch
            {
                AgentAction.ConfigChanged => Severity.Low,
                AgentAction.Installed => items.Any(h => Correlation.IsUserWritable(ServiceExe(F(h.Event, "ImagePath")))) ? Severity.Medium : Severity.Info,
                _ => allExcused ? Severity.Info : Severity.Medium,
            };
            var corroborating = new List<Finding>();
            if (sev == Severity.Medium && action != AgentAction.Installed)
            {
                var times = (live.Count > 0 ? live : items).Select(h => T(h.Event)).Where(t => t is not null).Select(t => t!.Value).ToList();
                corroborating = c.Existing.Concat(o).Where(f => f.Severity >= Severity.High && (f.FirstSeenUtc ?? f.LastSeenUtc) is { } ft && times.Any(t => (ft - t).Duration() <= c.Options.CorroborationWindow)).ToList();
                if (corroborating.Count > 0) sev = Severity.High;
            }
            var excuseText = excuses.Where(x => x is not null).Distinct().ToList();
            o.Add(new Finding
            {
                FindingId = c.NextId(), RuleId = "SECURITY-AGENT-STOPPED",
                Title = $"{agent.Name} ({agent.Kind}) {ActionText(action)}" + (items.Count > 1 ? $" — {items.Count} evenimente" : ""),
                Severity = sev, Category = "Defense Evasion", Classification = corroborating.Count > 0 ? Classification.Correlated : Classification.Direct,
                Confidence = Confidence.High, MitreTechniqueId = "T1562.001",
                FirstSeenUtc = T(items[0].Event), LastSeenUtc = T(items[^1].Event), User = F(items[0].Event, "AccountName", "SubjectUserName"),
                Description = $"{agent.Name}: {ActionText(action)} — {items.Count} evenimente, {Time(T(items[0].Event))}" + (items.Count > 1 ? $" – {Time(T(items[^1].Event))}" : "") + $". Detalii: {Join(items.Select(h => h.Detail), 4)}." +
                              (action == AgentAction.Installed && sev == Severity.Medium ? " Imaginea serviciului este într-o locație scriabilă de utilizatori: posibilă suprapunere de nume (nu s-a verificat semnătura)." : "") +
                              (corroborating.Count > 0 ? $" Constatări High în ±{c.Options.CorroborationWindow.TotalMinutes:0} min: {Join(corroborating.Select(f => f.Title), 3)}." : "") +
                              " Evenimentul nu arată cine sau de ce.",
                ClassificationReason = "Evenimente de serviciu/produs (System 7036/7040/7045, Sysmon 4/16, MsiInstaller 1034/11724) pentru un produs din lista de agenți cunoscuți. Distinct de DEF-TAMPER (jurnalul Defender).",
                SemanticType = SemanticType.Observation,
                SupportingEvidence = items.Take(20).Select(h => Ref(h.Event, $"{h.Event.EventId} {agent.Name}")).ToList(),
                RelatedFindingIds = corroborating.Select(f => f.FindingId).ToList(),
                ContradictingEvidence = excuseText.Select(x => x!).ToList(),
                AlternativeExplanations = ["Actualizarea, repornirea sau dezinstalarea planificată a produsului de securitate.", "Oprirea serviciilor la închiderea sistemului."],
            });
        }
    }
}
