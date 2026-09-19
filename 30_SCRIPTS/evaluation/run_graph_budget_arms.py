"""Measure MemoryController graph-expansion budgets on the held-out v2 benchmark.

`GRAPH_EXPANSION_BUDGET = None` (the shipped default) resolves to

    max(0, min(2 * len(notes), 20) - len(notes))

which is 0 whenever lexical search returns 20 or more notes. This script
measures what each budget actually does, through `MemoryController.search()`,
using the same arm construction as
07_EVALUATION/heldout_retrieval_benchmark_v2/run_production_arms.py.

It changes nothing in the controller and recommends no default. It only
records data. Arms:

    graph_off        graph expansion disabled (reference)
    budget_default   graph on, budget None (the shipped behaviour)
    budget_5/10/20   graph on, explicit budget for NEW nodes

Graph arms run non-strict on purpose: strict mode raises when expansion adds
nothing, which would turn exactly the cases this script exists to count into
errors. The per-case `graph_status` is recorded instead.

Usage:
    python 30_SCRIPTS/evaluation/run_graph_budget_arms.py            # run + write JSON + report
    python 30_SCRIPTS/evaluation/run_graph_budget_arms.py --render   # rebuild report from the JSON
"""
from __future__ import annotations

import argparse
import json
import statistics
import sys
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List, Optional

REPO = Path(__file__).resolve().parents[2]
BENCH = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2"
OUT_DIR = REPO / "07_EVALUATION" / "graph_budget"
RESULTS_JSON = OUT_DIR / "graph_budget_results.json"
REPORT_MD = OUT_DIR / "GRAPH_BUDGET_REPORT.md"

UNMEASURABLE = "UNMEASURABLE"
#: Lexical result count at which the default budget is zero.
DEFAULT_BUDGET_ZERO_AT = 20

#: (label, graph_on, budget)
ARMS = (
    ("graph_off", False, None),
    ("budget_default", True, None),
    ("budget_5", True, 5),
    ("budget_10", True, 10),
    ("budget_20", True, 20),
)


# --------------------------------------------------------------------------
# Pure aggregation (no controller, no I/O) — unit-tested in 20_TESTS.
# --------------------------------------------------------------------------

def fraction_text(k: int, n: int) -> str:
    return f"{k}/{n}"


def fraction(k: int, n: int) -> Dict[str, Any]:
    return {"k": k, "n": n, "text": f"{k}/{n}"}


def aggregate_arm(rows: List[Dict[str, Any]]) -> Dict[str, Any]:
    """Summarise per-case rows of one arm with raw fractions."""
    measurable = [r for r in rows if r["candidate_recall"] != UNMEASURABLE]
    expanded_counts = [int(r["expanded"]) for r in rows]
    with_expansion = [c for c in expanded_counts if c > 0]
    return {
        "n_queries": len(rows),
        "n_measurable": len(measurable),
        "candidate_recall": fraction(sum(r["candidate_recall"] for r in measurable), len(measurable)),
        "context_recall": fraction(sum(r["context_recall"] for r in measurable), len(measurable)),
        "queries_with_expansion": fraction(len(with_expansion), len(rows)),
        "new_nodes_total": sum(expanded_counts),
        "mean_new_nodes_per_query": (sum(expanded_counts) / len(rows)) if rows else None,
        "mean_new_nodes_when_expanded": (sum(with_expansion) / len(with_expansion)) if with_expansion else None,
        "graph_status": dict(Counter(r["graph_status"] for r in rows)),
    }


def paired_vs_reference(reference: List[Dict[str, Any]], arm: List[Dict[str, Any]], field: str) -> Dict[str, int]:
    """Cases where `arm` gained/lost the metric relative to the reference arm."""
    by_id = {r["id"]: r for r in reference}
    gained = lost = 0
    for r in arm:
        o = by_id.get(r["id"])
        if o is None or UNMEASURABLE in (r[field], o[field]):
            continue
        if o[field] == 0 and r[field] == 1:
            gained += 1
        elif o[field] == 1 and r[field] == 0:
            lost += 1
    return {"gained": gained, "lost": lost}


def default_budget_control(rows: List[Dict[str, Any]], zero_at: int = DEFAULT_BUDGET_ZERO_AT) -> Dict[str, Any]:
    """Negative control for the default (None) budget.

    Where lexical search returned `zero_at` or more notes the budget formula is
    0, so the number of nodes added must be exactly 0. If it is not, the
    measurement is not seeing the phenomenon the controller documents.
    """
    impossible = [r for r in rows if int(r["seeds"]) >= zero_at]
    added = sum(int(r["expanded"]) for r in impossible)
    possible = [r for r in rows if int(r["seeds"]) < zero_at]
    return {
        "zero_at_seed_count": zero_at,
        "queries_total": len(rows),
        "queries_expansion_impossible": fraction(len(impossible), len(rows)),
        "nodes_added_where_impossible": added,
        "queries_expansion_possible": fraction(len(possible), len(rows)),
        "nodes_added_where_possible": sum(int(r["expanded"]) for r in possible),
        "passed": added == 0,
    }


def seed_distribution(rows: List[Dict[str, Any]]) -> Dict[str, Any]:
    seeds = sorted(int(r["seeds"]) for r in rows)
    if not seeds:
        return {"min": None, "median": None, "max": None}
    return {"min": seeds[0], "median": statistics.median(seeds), "max": seeds[-1]}


# --------------------------------------------------------------------------
# Measurement
# --------------------------------------------------------------------------

def _measure() -> Dict[str, Any]:
    sys.path.insert(0, str(BENCH))
    import run_production_arms as rpa  # sets MEMORY_CONTROLLER_HMAC_SECRET default, verifies nothing yet
    from retrieval.vault_index import VaultIndex
    from memory_controller.storage.file_engine import FileStorageEngine

    rpa.verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    cases = rpa.load_cases("heldout.json")
    unresolved = [(c["id"], g) for c in cases for g in c["gold_relevant_notes"] if g not in index.by_id]
    if unresolved:
        raise SystemExit(f"GOLD_UNRESOLVABLE:{unresolved}")

    arms: Dict[str, Any] = {}
    raw_rows: Dict[str, List[Dict[str, Any]]] = {}
    for label, graph_on, budget in ARMS:
        # Same construction as run_production_arms (ranking arm pinned, etc.),
        # then override only what this measurement varies.
        controller = rpa.build(index, storage, graph_on)
        controller.strict_graph_expansion = False
        controller.graph_expansion_budget = budget

        captured: Dict[str, Any] = {}
        original_search = controller.search

        def capturing_search(*args, _orig=original_search, _cap=captured, **kwargs):
            pack = _orig(*args, **kwargs)
            _cap["pack"] = pack
            return pack

        controller.search = capturing_search  # type: ignore[method-assign]

        rows, errors = [], []
        for case in cases:
            try:
                row = rpa.run_case(controller, case, index)
                trace = (captured["pack"].get("candidate_trace") or {})
                row["seeds"] = len(trace.get("graph_seed_ids") or [])
                rows.append(row)
            except Exception as exc:
                errors.append({"id": case["id"], "error": f"{type(exc).__name__}: {exc}"})
        raw_rows[label] = rows
        arms[label] = {"graph_on": graph_on, "budget": budget, "errors": errors, "rows": rows}

    reference = raw_rows["graph_off"]
    out_arms: Dict[str, Any] = {}
    for label, graph_on, budget in ARMS:
        summary = aggregate_arm(raw_rows[label])
        if label != "graph_off":
            summary["candidate_recall_vs_off"] = paired_vs_reference(reference, raw_rows[label], "candidate_recall")
            summary["context_recall_vs_off"] = paired_vs_reference(reference, raw_rows[label], "context_recall")
        out_arms[label] = {
            "graph_on": graph_on,
            "budget": budget,
            "errors": arms[label]["errors"],
            "summary": summary,
            "rows": arms[label]["rows"],
        }

    default_rows = raw_rows["budget_default"]
    return {
        "benchmark": "07_EVALUATION/heldout_retrieval_benchmark_v2/heldout.json",
        "ranking_arm": "RANKING_ARM_BASELINE (pinned, as in run_production_arms.build)",
        "strict_graph_expansion": False,
        "corpus_notes": len(index),
        "n_cases": len(cases),
        "seed_distribution_budget_default": seed_distribution(default_rows),
        "default_budget_negative_control": default_budget_control(default_rows),
        "arms": out_arms,
    }


# --------------------------------------------------------------------------
# Report (generated from the JSON; no hand-written figures)
# --------------------------------------------------------------------------

def render_report(results: Dict[str, Any]) -> str:
    n = results["n_cases"]
    ctl = results["default_budget_negative_control"]
    dist = results["seed_distribution_budget_default"]
    explicit = [(a["budget"], label) for label, a in results["arms"].items() if a["graph_on"] and a["budget"] is not None]
    max_label = max(explicit)[1] if explicit else "n/a"
    max_rows = results["arms"][max_label]["rows"] if explicit else []
    no_expansion_at_max = sum(1 for r in max_rows if int(r["expanded"]) == 0)
    lines = [
        "# Graph expansion budget — measurement",
        "",
        "Generated by `30_SCRIPTS/evaluation/run_graph_budget_arms.py` from",
        "`graph_budget_results.json`. No figure in this file is hand-written.",
        "",
        f"- Benchmark: `{results['benchmark']}` (frozen by SHA-256, not modified), {n} cases.",
        f"- Corpus notes: {results['corpus_notes']}.",
        f"- Ranking arm: {results['ranking_arm']}.",
        "- Graph arms run **non-strict**: cases where expansion adds nothing are counted, not raised.",
        "",
        "## Read this first: the sample is small",
        "",
        f"The benchmark has {n} cases. A difference of 1–2 cases is noise, in either direction. Nothing below",
        "is a recommendation. It is the data; the default budget is the owner's decision.",
        "",
        "## Arms",
        "",
        "`budget` is the number of NEW nodes a query may gain from expansion. `default` is the shipped `None`,",
        "which resolves to `max(0, min(2*seeds, 20) - seeds)`.",
        "",
        "| arm | budget | candidate recall | context recall | queries with expansion | mean new nodes / query | mean new nodes / expanded query |",
        "|---|---|---:|---:|---:|---:|---:|",
    ]
    for label, arm in results["arms"].items():
        s = arm["summary"]
        budget = "—" if not arm["graph_on"] else ("default" if arm["budget"] is None else str(arm["budget"]))
        mean_all = "—" if s["mean_new_nodes_per_query"] is None else f"{s['mean_new_nodes_per_query']:.2f}"
        mean_exp = "—" if s["mean_new_nodes_when_expanded"] is None else f"{s['mean_new_nodes_when_expanded']:.2f}"
        lines.append(
            f"| {label} | {budget} | {s['candidate_recall']['text']} | {s['context_recall']['text']} | "
            f"{s['queries_with_expansion']['text']} | {mean_all} | {mean_exp} |"
        )
    lines += [
        "",
        "Recall fractions are over measurable cases only (abstention cases are `UNMEASURABLE`, as in R016).",
        "",
        "### Paired against `graph_off`",
        "",
        "Cases where the arm gained / lost the metric relative to the reference arm.",
        "",
        "| arm | candidate recall gained | candidate recall lost | context recall gained | context recall lost |",
        "|---|---:|---:|---:|---:|",
    ]
    for label, arm in results["arms"].items():
        if label == "graph_off":
            continue
        s = arm["summary"]
        c, x = s["candidate_recall_vs_off"], s["context_recall_vs_off"]
        lines.append(f"| {label} | {c['gained']} | {c['lost']} | {x['gained']} | {x['lost']} |")

    lines += [
        "",
        "## Default budget (`None`): where expansion is impossible",
        "",
        f"Lexical results entering expansion (seed notes) across the {n} queries: "
        f"min {dist['min']}, median {dist['median']}, max {dist['max']}.",
        "",
        f"- Queries with {ctl['zero_at_seed_count']} or more seeds (default budget is 0, expansion impossible): "
        f"**{ctl['queries_expansion_impossible']['text']}**.",
        f"- Queries with fewer than {ctl['zero_at_seed_count']} seeds (expansion possible): "
        f"{ctl['queries_expansion_possible']['text']}.",
        f"- Queries where the largest explicit budget (`{max_label}`) still added no node "
        f"(nothing to expand from their seeds): {fraction_text(no_expansion_at_max, n)}.",
        "",
        "### Negative control",
        "",
        f"At budget `None` and {ctl['zero_at_seed_count']}+ seeds the number of nodes added must be exactly 0.",
        "",
        f"- Nodes added on those queries: **{ctl['nodes_added_where_impossible']}**.",
        f"- Nodes added on the remaining queries: {ctl['nodes_added_where_possible']}.",
        f"- Control: **{'PASS' if ctl['passed'] else 'FAIL'}**"
        + (" — the measurement sees the phenomenon documented in `memory/controller.py`."
           if ctl["passed"] else " — the measurement does NOT match the documented behaviour; do not trust the table."),
        "",
        "## Errors",
        "",
    ]
    any_err = False
    for label, arm in results["arms"].items():
        if arm["errors"]:
            any_err = True
            lines.append(f"- {label}: {len(arm['errors'])} case(s) raised: " + "; ".join(
                f"{e['id']} ({e['error']})" for e in arm["errors"]))
    if not any_err:
        lines.append("None. Every arm ran every case.")
    lines.append("")
    return "\n".join(lines)


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--render", action="store_true", help="rebuild the report from the existing JSON only")
    args = parser.parse_args(argv)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    if args.render:
        results = json.loads(RESULTS_JSON.read_text(encoding="utf-8"))
    else:
        results = _measure()
        RESULTS_JSON.write_text(json.dumps(results, indent=2, ensure_ascii=False) + "\n",
                                encoding="utf-8", newline="\n")
    REPORT_MD.write_text(render_report(results), encoding="utf-8", newline="\n")
    ctl = results["default_budget_negative_control"]
    print(f"NEGATIVE_CONTROL={'PASS' if ctl['passed'] else 'FAIL'}")
    print(f"results={RESULTS_JSON}")
    print(f"report={REPORT_MD}")
    return 0 if ctl["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
