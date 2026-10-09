using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Verification;

/// <summary>
/// The per-finding checks. Each is deterministic, has a stable id (<see cref="CheckIds"/>) and a Romanian reason, and reads only
/// <see cref="CaseFacts"/> (files of the case). A check that cannot be completed says UNKNOWN with the reason; it never throws on a missing input.
/// </summary>
public static class Checks
{
    private static string Ids(IEnumerable<string> ids) => string.Join(", ", ids);
    private static string T(DateTimeOffset t) => ContradictionRules.Time(t);

    // ---- R10.8 UNSUPPORTED ----
    public static CheckResult Unsupported(Finding f) =>
        f.SupportingEvidence.Count == 0
            ? CheckResult.Fail(CheckIds.Unsupported, StandardState.Rejected, "Constatarea nu are nicio probă de susținere: nu poate fi verificată și este respinsă.")
            : CheckResult.Pass(CheckIds.Unsupported, $"{f.SupportingEvidence.Count} referințe de probă.");

    // ---- R10.2 PROVENANCE ----
    public static CheckResult Provenance(Finding f, IReadOnlyList<ResolvedRef> refs, CaseFacts facts)
    {
        if (refs.Count == 0) return CheckResult.NotApplicable(CheckIds.Provenance, "Nu există probe de verificat.");
        var rejected = new List<string>(); var unknown = new List<string>();
        if (facts.InvalidatedFindings.TryGetValue(f.FindingId, out var inv)) rejected.Add($"constatarea e marcată INVALIDATED de reverificare: {inv}");
        foreach (var id in refs.Select(r => r.Ref.EvidenceId).Distinct(StringComparer.Ordinal))
        {
            if (!facts.Evidence.TryGetValue(id, out var e)) { rejected.Add($"proba {id} nu există în indexul probelor"); continue; }
            if (facts.BrokenEvidence.TryGetValue(id, out var why)) rejected.Add($"proba {id}: {why}");
            foreach (var r in refs.Where(x => x.Ref.EvidenceId == id && x.Ref.Sha256.Length > 0 && e.Sha256.Length > 0).Select(x => x.Ref.Sha256).Distinct())
                if (!string.Equals(r, e.Sha256, StringComparison.OrdinalIgnoreCase)) rejected.Add($"proba {id}: hash-ul din constatare diferă de cel din indexul probelor");
            if (e.Sha256.Length != 64) unknown.Add($"proba {id} nu are SHA-256 de achiziție: integritatea nu poate fi confirmată");
            if (!ParserRecorded(f, e, facts, out var parserNote))
            {
                if (facts.ParsersByEvidence is null) unknown.Add($"proba {id}: {parserNote}");
                else rejected.Add($"proba {id}: {parserNote}");
            }
        }
        if (rejected.Count > 0) return CheckResult.Fail(CheckIds.Provenance, StandardState.Rejected, "Proveniența nu e valabilă: " + string.Join("; ", rejected.Distinct()) + ".", rejected.Concat(unknown));
        if (unknown.Count > 0) return CheckResult.Unknown(CheckIds.Provenance, "Proveniența nu poate fi confirmată complet: " + string.Join("; ", unknown.Distinct()) + ".", unknown);
        return CheckResult.Pass(CheckIds.Provenance, $"{refs.Select(r => r.Ref.EvidenceId).Distinct().Count()} probe: în index, cu hash intact și cu parser înregistrat.");
    }

    private static bool ParserRecorded(Finding f, EvidenceItem e, CaseFacts facts, out string note)
    {
        note = "";
        if (e.Parser.Length > 0) return true;
        if (facts.ParsersByEvidence?.ContainsKey(e.EvidenceId) == true) return true;
        // The live snapshot is read by the pipeline's own analyzer, not by a registered parser; the finding's provenance names that producer.
        if (e.SourceType == "live_snapshot" && f.Provenance is { Producer.Length: > 0 }) return true;
        if (facts.ParsersByEvidence is null)
        {
            if (f.Provenance is { Parsers.Count: > 0 }) return true;   // parsing.json is absent (old case); the finding's own provenance names a parser
            note = "Analysis/parsing.json lipsește și constatarea nu numește un parser: parserul nu poate fi confirmat";
            return false;
        }
        note = "nicio înregistrare a unui parser care să fi produs rânduri din această probă (Analysis/parsing.json)";
        return false;
    }

    // ---- dependency ledger ----
    public static CheckResult Dependencies(Finding f, CaseFacts facts)
    {
        if (facts.Dependencies is null)
            return CheckResult.Unknown(CheckIds.Dependencies, "Analysis/dependencies.json lipsește sau nu a putut fi citit: dependența constatare → probe nu poate fi confirmată pe o a doua cale (caz vechi).");
        if (!facts.Dependencies.Findings.TryGetValue(f.FindingId, out var deps))
            return CheckResult.Unknown(CheckIds.Dependencies, "Constatarea nu apare în Analysis/dependencies.json.");
        var missing = f.SupportingEvidence.Select(r => r.EvidenceId).Distinct(StringComparer.Ordinal).Where(id => !deps.Contains(id)).ToList();
        return missing.Count > 0
            ? CheckResult.Unknown(CheckIds.Dependencies, $"dependencies.json nu listează probele {Ids(missing)} pe care se sprijină constatarea.", missing)
            : CheckResult.Pass(CheckIds.Dependencies, $"dependencies.json listează toate cele {deps.Count} probe de care depinde constatarea.");
    }

    // ---- R10.1 SUFFICIENCY ----
    public static CheckResult Sufficiency(Finding f, IReadOnlyList<ResolvedRef> refs)
    {
        if (!f.HasSemanticType)
            return CheckResult.NotApplicable(CheckIds.Sufficiency, "Constatarea nu declară un tip semantic (caz vechi, fără contract): suficiența probelor nu poate fi evaluată.");
        if (refs.Count == 0) return CheckResult.NotApplicable(CheckIds.Sufficiency, "Nu există probe.");
        var resolved = refs.Where(r => r.IsResolved).ToList();
        var families = resolved.Select(r => r.Family).Distinct(StringComparer.Ordinal).ToList();
        switch (f.SemanticType)
        {
            case SemanticType.Execution:
                if (resolved.Any(ArtifactKinds.IsExecution)) return CheckResult.Pass(CheckIds.Sufficiency, "Există cel puțin un artefact de execuție: " + Ids(resolved.Where(ArtifactKinds.IsExecution).Select(r => r.Kind).Distinct()) + ".");
                if (resolved.Any(r => r.Family == "EventLog"))
                    return CheckResult.Unknown(CheckIds.Sufficiency, "Probele sunt jurnale de evenimente, dar evenimentul exact nu poate fi stabilit (timeline.csv lipsește sau nu conține rândul): nu se poate spune dacă e 4688 sau Sysmon 1.");
                return CheckResult.Fail(CheckIds.Sufficiency, StandardState.Unproven,
                    "O afirmație de execuție cere un artefact de execuție (" + Ids(ArtifactKinds.ExecutionKinds) + "); probele sunt doar: " + (families.Count == 0 ? "neidentificate" : Ids(families)) +
                    ". Prezența unui fișier nu dovedește execuția.", ArtifactKinds.ExecutionKinds.Select(k => "lipsește: " + k));
            case SemanticType.Configuration:
                if (resolved.Any(ArtifactKinds.IsConfiguration)) return CheckResult.Pass(CheckIds.Sufficiency, "Există un artefact de configurație: " + Ids(resolved.Where(ArtifactKinds.IsConfiguration).Select(r => r.Kind).Distinct()) + ".");
                return CheckResult.Fail(CheckIds.Sufficiency, StandardState.Unproven, $"O afirmație de configurație cere {ArtifactKinds.ConfigurationKindsText}; probele sunt doar: {(families.Count == 0 ? "neidentificate" : Ids(families))}.",
                    [ "lipsește: " + ArtifactKinds.ConfigurationKindsText ]);
            case SemanticType.Correlation:
                if (families.Count >= 2) return CheckResult.Pass(CheckIds.Sufficiency, $"Corelația are {families.Count} surse distincte: {Ids(families)}.");
                return CheckResult.Fail(CheckIds.Sufficiency, StandardState.Unproven,
                    $"O corelație cere cel puțin două surse distincte; există {(families.Count == 1 ? "una singură (" + families[0] + ")" : "nici una identificabilă")}.", ["lipsește: a doua sursă independentă"]);
            case SemanticType.Inference:
                return CheckResult.Fail(CheckIds.Sufficiency, StandardState.Unproven, "Este o deducție: probele pot susține premisele, dar nu demonstrează concluzia (nicio observație directă).", ["lipsește: observația directă care ar demonstra concluzia"]);
            case SemanticType.Attribution:
                return CheckResult.Fail(CheckIds.Sufficiency, StandardState.Unproven, "Atribuirea (cine/de ce) cere dovezi separate, care nu există în caz: artefactele singure nu o pot demonstra.", ["lipsește: dovezi separate de atribuire"]);
            default: // Observation, Presence: the record exists
                return resolved.Count > 0
                    ? CheckResult.Pass(CheckIds.Sufficiency, $"{(f.SemanticType == SemanticType.Presence ? "Prezența" : "Observația")} e înregistrată în: {Ids(families)}.")
                    : CheckResult.Unknown(CheckIds.Sufficiency, "Tipul artefactului nu a putut fi stabilit din probe (lipsește din indexul probelor).");
        }
    }

    // ---- R10.4 TEMPORAL ----
    public static CheckResult Temporal(Finding f, IReadOnlyList<ResolvedRef> refs, CaseFacts facts, VerificationOptions o)
    {
        if (refs.Count == 0) return CheckResult.NotApplicable(CheckIds.Temporal, "Nu există probe.");
        var bad = new List<string>(); var unknown = new List<string>();
        DateTimeOffset? first = f.FirstSeenUtc, last = f.LastSeenUtc;
        if (first is { } a && last is { } b && a > b) bad.Add($"prima apariție ({T(a)}) e după ultima ({T(b)})");
        if (facts.LatestAcquisitionUtc is { } acq)
            foreach (var (label, t) in new[] { ("prima apariție", first), ("ultima apariție", last) })
                if (t is { } tv && tv > acq + o.ClockSkewTolerance) bad.Add($"{label} ({T(tv)}) e după momentul achiziției cazului ({T(acq)})");

        bool anyEventTime = false;
        foreach (var r in refs)
        {
            if (r.Rows.Count == 0)
            {
                if (r.Item?.SourceType == "live_snapshot") continue;   // the snapshot time is the finding's own time; checked against the acquisition above
                if (facts.Timeline is null) unknown.Add($"timeline.csv lipsește: ora din proba {r.Ref.EvidenceId} ({r.Ref.Locator}) nu poate fi verificată");
                else if (r.Item is not null) unknown.Add($"rândul {r.Ref.EvidenceId} {r.Ref.Locator} nu e în timeline.csv");
                continue;
            }
            var known = r.Rows.Where(x => x.Time is not null).ToList();
            if (known.Count == 0) { unknown.Add($"evenimentul {r.Ref.EvidenceId} {r.Ref.Locator} are oră necunoscută (brut: „{r.Rows[0].TimeRaw}”)"); continue; }
            anyEventTime = true;
            if (r.Item is not null)
                foreach (var row in known.Where(x => x.Time > r.Item.AcquiredAtUtc + o.ClockSkewTolerance))
                    bad.Add($"evenimentul {r.Ref.EvidenceId} {r.Ref.Locator} are ora {T(row.Time!.Value)}, după achiziția probei ({T(r.Item.AcquiredAtUtc)})");
            if (first is not null || last is not null)
            {
                var lo = first - o.WindowTolerance; var hi = last + o.WindowTolerance;
                if (!known.Any(x => (lo is null || x.Time >= lo) && (hi is null || x.Time <= hi)))
                    bad.Add($"evenimentul {r.Ref.EvidenceId} {r.Ref.Locator} ({Ids(known.Select(x => T(x.Time!.Value)).Distinct())}) iese din fereastra revendicată [{(first is { } fv ? T(fv) : "…")} – {(last is { } lv ? T(lv) : "…")}]");
            }
        }
        if (bad.Count > 0) return CheckResult.Fail(CheckIds.Temporal, StandardState.Contradicted, "Încălcări de ordine în timp: " + string.Join("; ", bad.Distinct()) + ".", bad.Concat(unknown));
        if (first is null && last is null && !anyEventTime && f.SemanticType == SemanticType.Execution)
            return CheckResult.Unknown(CheckIds.Temporal, "O afirmație de execuție fără nicio oră (nici a constatării, nici a evenimentelor): momentul execuției nu e cunoscut.", unknown);
        if (unknown.Count > 0 && (first is not null || last is not null))
            return CheckResult.Unknown(CheckIds.Temporal, "Ora revendicată nu poate fi confirmată din toate probele: " + string.Join("; ", unknown.Distinct()) + ".", unknown);
        if (first is null && last is null && !anyEventTime)
            return CheckResult.NotApplicable(CheckIds.Temporal, "Constatarea nu revendică o oră.");
        return CheckResult.Pass(CheckIds.Temporal, "Ordinea în timp e consecventă: prima ≤ ultima, nicio oră după achiziție, evenimentele sunt în fereastră." +
            (unknown.Count > 0 ? " Ore necunoscute notate: " + string.Join("; ", unknown.Distinct()) + "." : ""));
    }

    // ---- R10.5 GRAPH ----
    public static CheckResult Graph(Finding f, IReadOnlyList<ResolvedRef> refs, CaseFacts facts)
    {
        if (facts.Graph is null) return CheckResult.Unknown(CheckIds.Graph, "Analysis/graph.json lipsește sau nu a putut fi citit: legăturile din graf nu pot fi verificate (caz vechi).");
        var node = "Finding:" + f.FindingId.Trim().ToLowerInvariant();
        if (!facts.Graph.EntityIds.Contains(node)) return CheckResult.Unknown(CheckIds.Graph, $"Nodul {node} lipsește din graf.");
        var edges = facts.Graph.Edges.Where(e => e.Source.Equals(node, StringComparison.OrdinalIgnoreCase) || e.Target.Equals(node, StringComparison.OrdinalIgnoreCase)).ToList();
        var own = f.SupportingEvidence.Select(r => r.EvidenceId).ToHashSet(StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var e in edges)
        {
            foreach (var end in new[] { e.Source, e.Target })
                if (!facts.Graph.EntityIds.Contains(end)) bad.Add($"muchia {e.Id} indică spre nodul inexistent {end}");
            if (e.EvidenceId.Length > 0)
            {
                if (!facts.Evidence.ContainsKey(e.EvidenceId)) bad.Add($"muchia {e.Id} indică spre proba inexistentă {e.EvidenceId}");
                else if (e.Type == "SUPPORTS" && !own.Contains(e.EvidenceId)) bad.Add($"muchia {e.Id} (SUPPORTS) aduce proba {e.EvidenceId}, care nu e printre probele constatării");
            }
        }
        if (bad.Count > 0) return CheckResult.Unknown(CheckIds.Graph, "Graful nu corespunde constatării: " + string.Join("; ", bad.Distinct()) + ".", bad);
        return CheckResult.Pass(CheckIds.Graph, edges.Count == 0
            ? "Nodul constatării există în graf; nicio muchie (probele nu au produs entități de cronologie)."
            : $"Nodul constatării există; cele {edges.Count} muchii indică spre noduri și probe existente.");
    }

    // ---- R10.6 CONTRADICTIONS ----
    public static (CheckResult Result, List<string> Found) Contradictions(Finding f, IReadOnlyList<ResolvedRef> refs, CaseFacts facts, VerificationOptions o)
    {
        var ctx = new RuleContext { Finding = f, Refs = refs, Facts = facts, Options = o };
        var found = new List<string>();
        var ran = new List<string>();
        foreach (var rule in ContradictionRules.Default.Concat(o.ExtraContradictionRules))
        {
            ran.Add(rule.RuleId);
            found.AddRange(rule.Evaluate(ctx));
        }
        found = found.Distinct(StringComparer.Ordinal).ToList();
        if (found.Count > 0)
            return (CheckResult.Fail(CheckIds.Contradictions, StandardState.Contradicted, $"{found.Count} contradicții găsite de reguli: {string.Join(" | ", found)}", found), found);
        var note = facts.Timeline is null ? " (timeline.csv lipsește: regulile bazate pe cronologie nu au avut ce citi)" : "";
        // Caveats the producing rule itself recorded (e.g. "taskul este dezactivat") are shown, not scored: they qualify the claim, they do not refute it.
        var declared = f.ContradictingEvidence.Count == 0 ? "" : $" Rezerve declarate de regula producătoare ({f.ContradictingEvidence.Count}), păstrate ca atare și nepunctate: {string.Join(" | ", f.ContradictingEvidence)}.";
        return (new CheckResult(CheckIds.Contradictions, CheckOutcome.Pass, null,
            $"Nicio contradicție găsită de {ran.Count} reguli ({Ids(ran)}); lipsa unei contradicții nu dovedește că nu există.{note}{declared}", f.ContradictingEvidence.ToList()), found);
    }

    // ---- R10.7 MISSING EVIDENCE ----
    public static (CheckResult Result, List<string> Missing) MissingEvidence(Finding f, CaseFacts facts)
    {
        var missing = new List<string>(f.MissingEvidence);
        var absent = f.HasSemanticType ? ArtifactKinds.Expected(f.SemanticType, facts).Where(x => !x.Present).Select(x => x.Label).ToList() : [];
        if (facts.Timeline is not null && f.HasSemanticType && absent.Count > 0)
            missing.Add($"Artefacte așteptate pentru o afirmație de tip {f.SemanticType.ToSpec()}, absente din caz: {Ids(absent)}.");
        missing = missing.Distinct(StringComparer.Ordinal).ToList();
        return (CheckResult.Info(CheckIds.MissingEvidence, missing.Count == 0 ? "Nicio probă lipsă cunoscută; asta nu înseamnă că nu mai există probe relevante." : $"{missing.Count} elemente de probă lipsă (nu schimbă singure verdictul).", missing), missing);
    }
}
