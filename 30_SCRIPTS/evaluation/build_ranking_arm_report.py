"""Render REPORT.md from results.json. No number in the report is written by hand.

    python 30_SCRIPTS/evaluation/build_ranking_arm_report.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT_DIR = REPO / "07_EVALUATION" / "ranking_formula"
RESULTS = OUT_DIR / "results.json"
REPORT = OUT_DIR / "REPORT.md"


def pct(fraction: dict) -> str:
    ci = fraction
    return f"{ci['proportion'] * 100:.2f}% [{ci['lower'] * 100:.2f}, {ci['upper'] * 100:.2f}]"


def main() -> int:
    r = json.loads(RESULTS.read_text(encoding="utf-8"))
    lines: list[str] = []
    add = lines.append

    add("# Ranking arms on benchmark v3 — results")
    add("")
    add(f"Preregistration: `{r['preregistration']}`, committed before this run.")
    add(f"Benchmark SHA-256 `{r['benchmark_sha256']}`, {r['n_cases']} cases, "
        f"{r['n_notes_in_index']} notes in the index.")
    add("")
    add(r["note"])
    add("")

    for point, pv in r["points"].items():
        add(f"## Operating point `{point}` — {pv['principal']}, page_size={pv['page_size']}")
        add("")
        add("| arm | context_recall | 95% CI | candidate_recall | deterministic | sabotage Δ |")
        add("|---|---|---|---|---|---|")
        for arm, s in pv["summaries"].items():
            c = pv["controls"][arm]
            add(f"| `{arm}` | {s['context_recall']['text']} | {pct(s['context_recall_ci'])} "
                f"| {s['candidate_recall']['text']} | {c['deterministic_over_3_runs']} "
                f"| {c['sabotage_changed_cases']} |")
        add("")

        dead = [a for a, c in pv["controls"].items() if not c["sabotage_proves_arm_is_live"]]
        add("**Sabotage control.** Each arm's sort key was replaced by a constant; the column "
            "above counts cases whose returned order changed. "
            + (f"Arms whose output did not change, and whose result is therefore void: "
               f"{', '.join('`' + a + '`' for a in dead)}."
               if dead else "Every arm changed its output, so every arm was live."))
        add("")

        add("**Distinctness.**")
        add("")
        add("| pair | cases with a different order | status |")
        add("|---|---|---|")
        for pair, d in pv["distinctness"].items():
            add(f"| {pair.replace('__vs__', ' vs ')} | {d['cases_with_different_order']} | {d['status']} |")
        add("")

        add(f"**Paired against `{r['decision']['reference_arm']}`, context_recall.**")
        add("")
        add("| arm | gains | losses | tied 1/1 | tied 0/0 | discordant | McNemar p | Holm p |")
        add("|---|---|---|---|---|---|---|---|")
        for arm, c in pv["paired_vs_production_arm"].items():
            h = pv["holm_bonferroni"].get(arm, {})
            add(f"| `{arm}` | {c['gained']} | {c['lost']} | {c['tied_both_1']} | {c['tied_both_0']} "
                f"| {c['discordant']} | {c['exact_mcnemar_p']} | {h.get('p_holm', '—')} |")
        add("")

    d = r["decision"]
    add("## Decision")
    add("")
    add(f"> {d['rule_text']}")
    add("")
    add(f"Reference arm `{d['reference_arm']}` at operating point `{d['operating_point']}`.")
    add("")
    add("| arm | gains ≥ 8 | losses ≤ 2 | p < 0.05 | meets the rule |")
    add("|---|---|---|---|---|")
    for arm, v in d["per_arm"].items():
        c = v["criteria"]
        add(f"| `{arm}` | {c['gains_ge_8']['actual']} ({c['gains_ge_8']['passed']}) "
            f"| {c['losses_le_2']['actual']} ({c['losses_le_2']['passed']}) "
            f"| {c['mcnemar_p_lt_0_05']['actual']} ({c['mcnemar_p_lt_0_05']['passed']}) "
            f"| **{v['all_passed']}** |")
    add("")
    add(f"**Verdict: {d['verdict']}.**")
    add("")
    if r["void_arms"]:
        add(f"**Void arms** (sabotage did not change their output): {', '.join(r['void_arms'])}.")
        ref = d["reference_arm"]
        if any(v.endswith(":" + ref) for v in r["void_arms"]):
            add("")
            add("## Reading the void label on the reference arm")
            add("")
            add(f"`{ref}` is the production default, and its sort key is inert: `generate_candidates()` "
                "already returns notes in `(-fused_score, id)` order, so sorting by the same key again "
                "cannot move anything. Two consequences, kept apart on purpose:")
            add("")
            add(f"- The **recall figures** for `{ref}` above are valid. They are what production returns, "
                "because production returns the fusion order unchanged.")
            add(f"- The **ranking step** under `{ref}` is redundant. Its gain over `baseline` is the gain of "
                "*not* applying `RelevanceScorer`'s key, not of applying a new one. There is no reranking "
                "step anywhere in the pipeline; the page is the fusion top-k minus whatever the context-pack "
                "builder cannot fit (`07_EVALUATION/reranker_envelope/DEVIATIONS.md`, D-1/D-2).")
            add("")
            add("Pinned by `20_TESTS/test_fused_score_ranking_is_a_noop.py`, including the one known "
                "divergence (ties: generation ascends by id, the arm descends), which no benchmark page exercised.")
    else:
        add("No arm was void: the sabotage control changed every arm's output.")
    add("")

    REPORT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"written {REPORT.relative_to(REPO)} ({len(lines)} lines)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
