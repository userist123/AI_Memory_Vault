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
        var validity = ValidationValidity(facts);
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
            if (Prop(st, "validation") is { ValueKind: JsonValueKind.Object } cv && Text(cv, "state") is { Length: > 0 } cs)
                v.ClaimValidation = LogAnalyzer.Dfir.AI.ClaimStates.ToSpec(LogAnalyzer.Dfir.AI.ClaimStates.Parse(cs));   // only the four known states; legacy "SUSTINUT" or any other string -> NEVERIFICABIL
            // the stored claim state is only as good as the validation file it came from: a stale, modified or missing validation makes it NEVERIFICABIL
            if (validity.Length > 0 && v.ClaimValidation != "NEVERIFICABIL")
            {
                v.ClaimValidation = "NEVERIFICABIL";
                v.Reason += " Validarea afirmației nu mai este de încredere: " + validity + ".";
            }
            // values confirmed next to a rejected / contradicted verdict is a contradiction in the report: never shown as confirmed
            if (v.ClaimValidation == "VALORI_CONFIRMATE" && v.Verdict is StandardState.Rejected or StandardState.Contradicted)
            {
                v.ClaimValidation = "NEVERIFICABIL";
                v.Reason += " Valorile citate sunt confirmate, dar verdictul de verificare este " + v.Verdict.ToSpec() + ": starea nu este prezentată ca valori confirmate.";
            }
            result.Add(v);
        }
        return result;
    }

    /// <summary>"" when ai_validation.json is present, matches its custody hash and is bound to the case's current evidence; otherwise why not.</summary>
    private static string ValidationValidity(CaseFacts facts)
    {
        var saved = LogAnalyzer.Dfir.AI.AiValidationStore.TryRead(facts.Case.Root, out var error);
        if (saved is null) return error.Length > 0 ? "ai_validation.json nu a putut fi citit (" + error + ")" : "ai_validation.json lipsește";
        var caseId = facts.Case.Info.CaseId;
        var current = new LogAnalyzer.Dfir.AI.AiCaseFacts(caseId, LogAnalyzer.Dfir.AI.AiCaseFacts.Binding(caseId, facts.Evidence.Values));
        var checkedNow = saved.CheckAgainst(current);
        return checkedNow.Stale ? checkedNow.StaleReason : "";
    }

    private static JsonElement? Prop(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string Text(JsonElement e, string name) => Prop(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";
}
