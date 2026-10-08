"""B07: check the Book-to-Memory evaluation sets for leakage against the benchmark/held-out sets.

Compares every question/scenario set of the track with every benchmark or held-out set (exact,
normalised-text and n-gram/Jaccard overlap, and shared gold note ids) and writes a JSON report plus a
short Markdown summary. The numbers in both files come from this run; nothing is typed by hand.

Usage:
    python 30_SCRIPTS/evaluation/b2m_leakage_check.py                      # real sets, write the report
    python 30_SCRIPTS/evaluation/b2m_leakage_check.py --check              # exit 1 on an unregistered text overlap
    python 30_SCRIPTS/evaluation/b2m_leakage_check.py --sets-config F.json # use other sets (for tests)

Exit status: 0 when the run completed (and, with --check, every text-level overlap is registered), 1 otherwise.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from lifecycle.validation.book_to_memory_leakage import (  # noqa: E402
    ROLE_HELDOUT,
    ROLE_SCENARIO,
    EvalSet,
    clean_subset,
    load_cases_json,
    load_from_spec,
    load_jsonl,
    run_check,
)

DEFAULT_OUT_DIR = REPO / "07_EVALUATION" / "b2m_leakage"
H1_BASELINE_RESULTS = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "h1_baseline_results.json"

#: Text-level overlaps found on the real data and flagged, not edited: both sets are frozen and
#: hash-pinned (``07_EVALUATION/heldout_retrieval_benchmark_v*/``), and many recorded experiments read
#: them. ``--check`` fails only for an overlap that is NOT listed here. Each entry is (item key a,
#: item key b, why it is kept).
KNOWN_TEXT_OVERLAPS = {
    frozenset({"heldout_v2_dev:D09", "heldout_v2_heldout:H18"}):
        "identical query and gold in the frozen v2 dev and held-out sets; do not tune on D09 and report "
        "v2 held-out results with H18 excluded as a sensitivity check",
    frozenset({"heldout_v1_dev:D08", "heldout_v1_heldout:H21"}):
        "identical query in the v1 sets; v1 is INVALID (gold ids resolve to nothing) and is no longer run",
}
B03_TASKS = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "b03_task_packet" / "scoring" / "tasks.json"


def real_sets() -> List[EvalSet]:
    """The sets of the track and the benchmark/held-out sets it uses or sits next to."""
    ev = REPO / "07_EVALUATION"
    h1 = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "h1_cases.json"
    sets = [
        # The track's own frozen H1 benchmark, one set per split.
        load_cases_json("h1_development", h1, ROLE_SCENARIO, only_split="development"),
        load_cases_json("h1_calibration", h1, ROLE_SCENARIO, only_split="calibration"),
        load_cases_json("h1_held_out", h1, ROLE_HELDOUT, only_split="held_out"),
        # Neighbouring evaluation sets in 07_EVALUATION.
        load_cases_json("heldout_v2_dev", ev / "heldout_retrieval_benchmark_v2" / "dev.json", ROLE_SCENARIO),
        load_cases_json("heldout_v2_heldout", ev / "heldout_retrieval_benchmark_v2" / "heldout.json", ROLE_HELDOUT),
        load_cases_json("heldout_v1_dev", ev / "heldout_retrieval_benchmark_v1" / "dev.json", ROLE_SCENARIO),
        load_cases_json("heldout_v1_heldout", ev / "heldout_retrieval_benchmark_v1" / "heldout.json", ROLE_HELDOUT),
        load_cases_json("retrieval_v3", ev / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json", ROLE_HELDOUT),
        load_jsonl("model_memory_challenge", ev / "model_memory_benchmark_v1" / "retrieval_challenge_cases.jsonl", ROLE_SCENARIO),
        load_cases_json("golden_tasks", ev / "golden_memory_effectiveness_v1" / "golden_tasks.json", ROLE_SCENARIO,
                        text_key="prompt", gold_key="none", id_key="task_id"),
        load_cases_json("curriculum_psychology_ch08", ev / "curriculum" / "openstax_ch08_frozen_test_set.json",
                        ROLE_SCENARIO, text_key="question", list_key="questions", gold_key="none"),
        load_cases_json("curriculum_statistics_ch08", ev / "curriculum" / "openstax_statistics_frozen_test_set.json",
                        ROLE_SCENARIO, text_key="question", list_key="questions", gold_key="none"),
    ]
    if B03_TASKS.exists():
        sets.append(load_cases_json("b03_tasks", B03_TASKS, ROLE_SCENARIO, text_key="question",
                                    list_key="tasks", gold_key="note_ids", id_key="task_id"))
    return sets


def h1_clean_heldout(sets: List[EvalSet]) -> Dict[str, Any]:
    """H1 held-out items that share no text and no gold id with the H1 development/calibration items.

    When the baseline results file is present the recall@10 on the full held-out split and on the clean
    subset are both reported, so the cost of the shared gold ids is a measured number.
    """
    by_name = {s.name: s for s in sets}
    if not {"h1_development", "h1_calibration", "h1_held_out"} <= set(by_name):
        return {}
    sub = clean_subset(by_name["h1_held_out"], [by_name["h1_development"], by_name["h1_calibration"]])
    out: Dict[str, Any] = {"subset": sub}
    if H1_BASELINE_RESULTS.exists():
        res = json.loads(H1_BASELINE_RESULTS.read_text(encoding="utf-8"))
        held = [c for c in res["cases"] if c.get("split") == "held_out"]
        keep = [c for c in held if c["id"] in set(sub["kept"])]

        def recall(cases: List[Dict[str, Any]]) -> Dict[str, Any]:
            hits = sum(1 for c in cases if c.get("hits_at_10"))
            return {"cases": len(cases), "hits_at_10": hits,
                    "recall_at_10": round(hits / len(cases), 4) if cases else None}

        out["baseline_recall_at_10"] = {"held_out_full": recall(held), "held_out_clean": recall(keep)}
    return out


def render_markdown(report: Dict[str, Any]) -> str:
    s = report["summary"]
    lines = [
        "# B07 leakage check (generated)",
        "",
        "Generated by `30_SCRIPTS/evaluation/b2m_leakage_check.py`; the JSON next to this file holds every finding.",
        "",
        f"* sets compared: {len(report['sets'])} ({s['all_pairs']} pairs, {s['relevant_pairs']} leakage-relevant: one side is a held-out/benchmark set)",
        f"* thresholds (fixed before the run): word-3-gram Jaccard >= {report['thresholds']['near_ngram_jaccard']}, "
        f"content-word Jaccard >= {report['thresholds']['near_token_jaccard']}; manual-review floor {report['thresholds']['review_floor']}",
        f"* **text-level overlaps in relevant pairs: {s['text_overlaps']}** "
        f"(exact {s['exact']}, normalised {s['normalised']}, near-duplicate {s['near']})",
        f"* shared gold-note-id pairs in relevant pairs: {s['shared_gold_pairs']}",
        f"* below-threshold pairs listed for manual review: {s['review_pairs']}",
        "",
        "| set | role | items | file sha256 (first 12) |",
        "|---|---|---:|---|",
    ]
    for st in report["sets"]:
        lines.append(f"| {st['name']} | {st['role']} | {st['items']} | `{st['file_sha256'][:12]}` |")
    lines += ["", "## Relevant pairs with any finding", "", "| pair | exact | normalised | near | shared gold | review |", "|---|---:|---:|---:|---:|---:|"]
    for p in report["pairs"]:
        c = p["counts"]
        if p["relevant"] and any(c.values()):
            lines.append(f"| {p['a']} x {p['b']} | {c['exact']} | {c['normalised']} | {c['near']} | {c['gold']} | {c['review']} |")
    flagged = [
        (p["a"], p["b"], f) for p in report["pairs"] if p["relevant"]
        for f in p["findings"] if f["kind"] in ("exact", "normalised", "near")
    ]
    lines += ["", "## Text-level overlaps", ""]
    if not flagged:
        lines.append("None.")
    for a, b, f in flagged:
        why = KNOWN_TEXT_OVERLAPS.get(frozenset({f["a"], f["b"]}), "NOT REGISTERED")
        lines.append(f"* `{f['a']}` ~ `{f['b']}` ({f['kind']}, 3-gram {f['ngram_jaccard']}, content-word {f['token_jaccard']}): "
                     f"\"{f['a_text']}\" / \"{f['b_text']}\" -> flagged, not edited: {why}")
    h1c = report.get("h1_clean_heldout") or {}
    if h1c:
        sub = h1c["subset"]
        lines += ["", "## H1 held-out split versus H1 development/calibration", "",
                  f"* held-out items: {sub['items']}; with no shared text or gold id: {len(sub['kept'])}; "
                  f"sharing a gold id with a development/calibration item: {len(sub['dropped'])}"]
        base = h1c.get("baseline_recall_at_10")
        if base:
            lines.append(f"* production-default baseline recall@10 (from `h1_baseline_results.json`): "
                         f"full held-out {base['held_out_full']['hits_at_10']}/{base['held_out_full']['cases']}, "
                         f"clean subset {base['held_out_clean']['hits_at_10']}/{base['held_out_clean']['cases']}")
    gold = [
        f for p in report["pairs"] if p["relevant"] and p["a"].startswith("h1_") and p["b"].startswith("h1_")
        for f in p["findings"] if f["kind"] == "gold"
    ]
    total_gold = report["summary"]["shared_gold_pairs"]
    lines += ["", f"## Shared gold note ids inside the H1 sets ({len(gold)} of {total_gold} pairs; the rest are in the JSON)", ""]
    for f in gold:
        lines.append(f"* `{f['a']}` / `{f['b']}` share {', '.join('`' + g + '`' for g in f['shared_gold'])}")
    lines.append("")
    return "\n".join(lines)


def main(argv: List[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out-dir", default=str(DEFAULT_OUT_DIR))
    ap.add_argument("--check", action="store_true",
                    help="exit 1 if a text-level overlap is found that is not registered in KNOWN_TEXT_OVERLAPS")
    ap.add_argument("--sets-config", help="JSON list of set specs (name, path, role, ...) to use instead of the real sets")
    ap.add_argument("--no-write", action="store_true", help="print the summary only")
    args = ap.parse_args(argv)

    if args.sets_config:
        specs = json.loads(Path(args.sets_config).read_text(encoding="utf-8"))
        sets = [load_from_spec(spec, base=Path(args.sets_config).parent) for spec in specs]
    else:
        sets = real_sets()
    report = run_check(sets)
    report["h1_clean_heldout"] = h1_clean_heldout(sets)
    summary = report["summary"]
    unregistered = [
        f for p in report["pairs"] if p["relevant"] for f in p["findings"]
        if f["kind"] in ("exact", "normalised", "near") and frozenset({f["a"], f["b"]}) not in KNOWN_TEXT_OVERLAPS
    ]
    summary["unregistered_text_overlaps"] = len(unregistered)
    print(json.dumps(summary, indent=2))
    if not args.no_write:
        out = Path(args.out_dir)
        out.mkdir(parents=True, exist_ok=True)
        (out / "LEAKAGE_REPORT.json").write_text(json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        (out / "LEAKAGE_REPORT.md").write_text(render_markdown(report), encoding="utf-8")
        print(f"wrote {out}")
    if args.check and unregistered:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
