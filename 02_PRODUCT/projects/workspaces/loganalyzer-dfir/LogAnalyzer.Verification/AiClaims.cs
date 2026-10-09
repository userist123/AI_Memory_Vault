using System.Text.Json;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Verification;

/// <summary>
/// R10.9: a verdict for each statement of the AI analysis (Analysis/ai_reasoning.json, read as JSON; this module never references the AI assembly).
/// A statement is never VERIFIED (<see cref="AntiOverclaim.Constrain"/>): the best it can be is INFERRED. Citing a REJECTED or CONTRADICTED
/// finding makes it CONTRADICTED; citing evidence that is no longer intact makes it REJECTED.
/// </summary>
public static class AiClaims
{
    private static readonly StandardState[] Severity = [StandardState.Rejected, StandardState.Contradicted, StandardState.Unproven, StandardState.Unknown, StandardState.NotAssessed, StandardState.Supported, StandardState.Verified];

    public static List<AiStatementVerdict> Evaluate(CaseFacts facts, IReadOnlyDictionary<string, FindingVerdict> findings)
    {
        var result = new List<AiStatementVerdict>();
        if (facts.AiReasoning is null) return result;
        var root = facts.AiReasoning.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return result;
        var accepted = Prop(root, "accepted");
        if (accepted is not { ValueKind: JsonValueKind.Array } arr) return result;
        foreach (var st in arr.EnumerateArray())
        {
            var v = new AiStatementVerdict { Section = Text(st, "section"), Text = Text(st, "text") };
            var citations = Prop(st, "citations") is { ValueKind: JsonValueKind.Array } c ? c.EnumerateArray().ToList() : [];
            var evidence = new List<string>(); var cited = new List<string>(); var findingIds = new List<string>();
            foreach (var cit in citations)
            {
                var item = Text(cit, "item"); var kind = Text(cit, "kind");
                if (item.Length > 0) cited.Add(item);
                if (kind == "finding" || findings.ContainsKey(item)) findingIds.Add(item);
                if (Prop(cit, "evidence") is { ValueKind: JsonValueKind.Array } ev)
                    evidence.AddRange(ev.EnumerateArray().Select(e => Text(e, "evidenceId")).Where(x => x.Length > 0));
            }
            v.Cites = cited.Distinct().ToList();
            var broken = evidence.Distinct().Where(facts.BrokenEvidence.ContainsKey).ToList();
            var states = findingIds.Distinct().Select(id => (id, state: findings.TryGetValue(id, out var fv) ? fv.Verdict : (StandardState?)null)).ToList();
            var bad = states.Where(s => s.state is StandardState.Rejected or StandardState.Contradicted).ToList();

            StandardState state; string reason;
            if (bad.Count > 0)
            {
                state = StandardState.Contradicted;
                reason = "Citează constatări respinse sau contrazise de verificare: " + string.Join(", ", bad.Select(b => $"{b.id} ({b.state!.Value.ToSpec()})")) + ".";
            }
            else if (broken.Count > 0)
            {
                state = StandardState.Rejected;
                reason = "Citează probe care nu mai sunt intacte: " + string.Join(", ", broken) + ".";
            }
            else if (states.Count == 0)
            {
                state = StandardState.Unproven;
                reason = "Nu citează nicio constatare verificată (doar evenimente, urme anti-forensics sau goluri): nu există un verdict de verificare pe care să se sprijine.";
            }
            else if (states.Any(s => s.state is null))
            {
                state = StandardState.Unknown;
                reason = "Citează constatări care nu apar în raportul de verificare: " + string.Join(", ", states.Where(s => s.state is null).Select(s => s.id)) + ".";
            }
            else
            {
                var worst = states.Select(s => s.state!.Value).OrderBy(s => Array.IndexOf(Severity, s)).First();
                state = AntiOverclaim.Constrain(worst, SemanticType.Inference, fromAi: true);
                reason = $"Afirmație generată de model: cel mult {StandardState.Inferred.ToSpec()}, niciodată VERIFIED. Constatările citate: {string.Join(", ", states.Select(s => $"{s.id} ({s.state!.Value.ToSpec()})"))}.";
            }
            v.Verdict = state == StandardState.Verified ? StandardState.Inferred : state;   // belt and braces: never VERIFIED
            v.Reason = reason;
            result.Add(v);
        }
        return result;
    }

    private static JsonElement? Prop(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string Text(JsonElement e, string name) => Prop(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";
}
