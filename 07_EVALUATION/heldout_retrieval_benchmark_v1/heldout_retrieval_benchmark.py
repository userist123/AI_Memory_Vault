"""R009b frozen held-out retrieval benchmark.

This runner is deliberately conservative. The held-out set is frozen and hash
checked before every execution. Thresholds may be tuned against ``dev.json``
only. The held-out set is evaluated at most once per candidate change, and its
failures MUST NOT be inspected or used to tune thresholds. The runner enforces
that operational rule with a candidate receipt: a second held-out execution
for the same candidate fingerprint is refused. CI must archive/commit that
receipt; local reruns are likewise refused once the receipt exists.

Default mode is deterministic/offline and uses the repository's lexical
HybridRetriever only; no Ollama connection is attempted. Optional adapters are
fail-closed and must explicitly declare their availability. There is no
silent fallback to another retrieval path.

The benchmark reports three separate stages:
  1. candidate_recall  - a gold note entered the candidate set;
  2. context_recall   - a gold note reached the final context pack;
  3. answer_correctness - all required facts were present, or an unanswerable
     case was explicitly treated as abstention.

The default answer correctness is a deterministic retrieval-backed proxy, not
an LLM answer-quality claim. An external answer adapter may be supplied later;
it must emit an explicit availability marker when unavailable.

Minimum effect size: 10 percentage points. Future arm comparisons use paired
case IDs and an exact McNemar test plus Wilson confidence intervals; a result
below the minimum effect size or a non-significant paired test is reported as
"no significant difference" rather than as a win.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib
import json
import math
import os
import sys
from pathlib import Path
from typing import Any, Callable, Dict, Iterable, List, Sequence, Tuple

ROOT = Path(__file__).resolve().parents[2]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
if str(PACKAGES) not in sys.path:
    sys.path.insert(0, str(PACKAGES))

BENCH = Path(__file__).resolve().parent
HELDOUT_PATH = BENCH / "heldout.json"
DEV_PATH = BENCH / "dev.json"
HELDOUT_HASH_PATH = BENCH / "SET_SHA256.txt"
DEV_HASH_PATH = BENCH / "DEV_SHA256.txt"
RECEIPT_DIR = BENCH / "receipts"
RESULT_PATH = BENCH / "r009b_baseline.json"
DEFAULT_CANDIDATE_LIMITS = (20, 50, 100, 200, 500)
MIN_EFFECT_SIZE = 0.10
EXPECTED_CORPUS_NOTES = 935
EXPECTED_REGRESSION = {"passed": 1174, "skipped": 3}
REQUIRED_CLASSES = (
    "exact_identifier_lookup",
    "paraphrase",
    "synonym_substitution",
    "lexical_trap",
    "cross_cluster_multihop",
    "unanswerable",
)


def sha256_bytes(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_recorded_hash(path: Path) -> str:
    value = path.read_text(encoding="utf-8").strip().split()[0]
    if len(value) != 64 or any(c not in "0123456789abcdef" for c in value.lower()):
        raise RuntimeError(f"INVALID_HASH_RECORD:{path}")
    return value.lower()


def verify_frozen(path: Path, hash_path: Path) -> str:
    recorded = read_recorded_hash(hash_path)
    actual = sha256_bytes(path)
    if actual != recorded:
        raise RuntimeError(
            f"FROZEN_SET_HASH_MISMATCH:{path.name}:expected={recorded}:actual={actual}"
        )
    return actual


def load_cases(path: Path) -> List[Dict[str, Any]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    cases = data.get("cases")
    if not isinstance(cases, list) or not cases:
        raise RuntimeError(f"INVALID_BENCHMARK_SET:{path}")
    ids = [c.get("id") for c in cases]
    if len(ids) != len(set(ids)) or any(not x for x in ids):
        raise RuntimeError("INVALID_BENCHMARK_CASE_IDS")
    return cases


def validate_schema(cases: Sequence[Dict[str, Any]], heldout: bool) -> None:
    required = {"id", "class", "query", "expected_answer", "gold_relevant_notes", "required_facts", "wrong_note_ids", "abstain"}
    classes = {c["class"] for c in cases}
    missing = required - set().union(*(c.keys() for c in cases))
    if missing:
        raise RuntimeError(f"INVALID_BENCHMARK_SCHEMA:{sorted(missing)}")
    if heldout and classes != set(REQUIRED_CLASSES):
        raise RuntimeError(f"INVALID_QUERY_CLASSES:{sorted(classes)}")
    if heldout:
        counts = {name: sum(1 for c in cases if c["class"] == name) for name in REQUIRED_CLASSES}
        if any(v == 0 for v in counts.values()):
            raise RuntimeError(f"UNREPRESENTED_QUERY_CLASS:{counts}")


def wilson(successes: int, n: int, z: float = 1.959963984540054) -> Tuple[float, float]:
    if n <= 0:
        return (float("nan"), float("nan"))
    phat = successes / n
    denom = 1 + z * z / n
    center = (phat + z * z / (2 * n)) / denom
    half = z * math.sqrt(phat * (1 - phat) / n + z * z / (4 * n * n)) / denom
    return max(0.0, center - half), min(1.0, center + half)


def exact_binom_two_sided(k: int, n: int) -> float:
    if n == 0:
        return 1.0
    p = 0.5
    pmf = [math.comb(n, i) * (p ** n) for i in range(n + 1)]
    pk = pmf[k]
    return min(1.0, sum(x for x in pmf if x <= pk + 1e-15))


def mcnemar_exact(a: Sequence[bool], b: Sequence[bool]) -> Dict[str, Any]:
    if len(a) != len(b):
        raise ValueError("PAIRED_LENGTH_MISMATCH")
    b01 = sum(1 for x, y in zip(a, b) if not x and y)
    b10 = sum(1 for x, y in zip(a, b) if x and not y)
    discordant = b01 + b10
    p = exact_binom_two_sided(min(b01, b10), discordant) if discordant else 1.0
    delta = (sum(b) - sum(a)) / len(a) if a else 0.0
    return {"a_only": b10, "b_only": b01, "discordant": discordant, "p_value": p, "paired_delta": delta}


def locate_graph_aware_adapter() -> Callable[..., Dict[str, Any]] | None:
    spec = os.environ.get("R009B_ADAPTER")
    if not spec:
        return None
    if ":" not in spec:
        raise RuntimeError("INVALID_ADAPTER_SPEC")
    module_name, func_name = spec.split(":", 1)
    module = importlib.import_module(module_name)
    fn = getattr(module, func_name, None)
    if not callable(fn):
        raise RuntimeError("INVALID_ADAPTER_ENTRYPOINT")
    return fn


def build_index() -> Any:
    from retrieval.vault_index import VaultIndex  # type: ignore

    roots = ("01_ARCHITECTURE", "02_PRODUCT", "10_DOCUMENTATION", "00_GOVERNANCE")
    index = VaultIndex.load(ROOT, roots=roots, lifecycles=("ACTIVE", "REVIEW", "NORMALIZED", "CLASSIFIED", "NONE"), include_raw=False, include_archived=False)
    if len(index.notes) != EXPECTED_CORPUS_NOTES:
        raise RuntimeError(f"CORPUS_SIZE_MISMATCH:expected={EXPECTED_CORPUS_NOTES}:actual={len(index.notes)}")
    return index


def tune_candidate_limit(dev_cases: Sequence[Dict[str, Any]], index: Any) -> Dict[str, Any]:
    from retrieval.context.candidate_generation import generate_candidates  # type: ignore

    notes = [
        {"id": n.id, "content": n.text, "category": n.category, "tags": n.tags}
        for n in index.notes
    ]
    scores = []
    for limit in DEFAULT_CANDIDATE_LIMITS:
        hits = 0
        for case in dev_cases:
            candidates, _ = generate_candidates(case["query"], notes, limit)
            ids = {str(n["id"]) for n in candidates}
            if any(str(g) in ids for g in case["gold_relevant_notes"]):
                hits += 1
        recall = hits / len(dev_cases)
        scores.append({"candidate_limit": limit, "candidate_recall": recall, "n": len(dev_cases)})
    target = max((s for s in scores if s["candidate_recall"] >= 0.90), key=lambda s: -s["candidate_limit"], default=None)
    if target is None:
        target = max(scores, key=lambda s: (s["candidate_recall"], -s["candidate_limit"]))
    return {"selected_candidate_limit": target["candidate_limit"], "tested": scores}


def run_case(case: Dict[str, Any], index: Any, candidate_limit: int, adapter: Callable[..., Dict[str, Any]] | None) -> Dict[str, Any]:
    from retrieval.context.candidate_generation import generate_candidates  # type: ignore
    from retrieval.hybrid_retrieval import HybridRetriever  # type: ignore

    notes = [
        {"id": n.id, "content": n.text, "category": n.category, "tags": n.tags}
        for n in index.notes
    ]
    candidates, trace = generate_candidates(case["query"], notes, candidate_limit)
    candidate_ids = [str(n["id"]) for n in candidates]

    # Final-context stage is intentionally separated from candidate generation.
    # The default offline arm uses HybridRetriever.search() to represent the
    # existing final retrieval pack. A graph-aware arm can replace both stages
    # only by explicitly supplying R009B_ADAPTER; no implicit fallback occurs.
    if adapter is None:
        retriever = HybridRetriever(index)
        final_hits, final_trace = retriever.search_with_trace(case["query"], top_k=min(20, candidate_limit))
        context_ids = [h.note.id for h in final_hits]
        answer_text = ""
    else:
        out = adapter(case=case, index=index, candidate_limit=candidate_limit)
        if not isinstance(out, dict) or out.get("available") is not True:
            marker = (out or {}).get("marker", "OPTIONAL_COMPONENT_UNAVAILABLE") if isinstance(out, dict) else "OPTIONAL_COMPONENT_UNAVAILABLE"
            return {"id": case["id"], "available": False, "marker": marker}
        candidate_ids = [str(x) for x in out.get("candidate_ids", [])]
        context_ids = [str(x) for x in out.get("context_ids", [])]
        answer_text = str(out.get("answer_text", ""))
        trace = out.get("trace", {})
        final_trace = out.get("final_trace", {})

    gold = {str(x) for x in case["gold_relevant_notes"]}
    wrong = {str(x) for x in case["wrong_note_ids"]}
    candidate_recall = bool(gold & set(candidate_ids)) if gold else True
    context_recall = bool(gold & set(context_ids)) if gold else True

    context_blob = " ".join(index.by_id[n].text for n in context_ids if n in index.by_id).lower()
    required = [str(x).lower() for x in case["required_facts"]]
    facts_ok = all(f in context_blob for f in required) if required else False
    if case["abstain"]:
        answer_correct = (answer_text.strip() == "" and not gold)
        abstention_correct = answer_correct
    else:
        answer_correct = facts_ok if adapter is None else all(f in answer_text.lower() for f in required)
        abstention_correct = False

    return {
        "id": case["id"],
        "class": case["class"],
        "available": True,
        "candidate_recall": int(candidate_recall),
        "context_recall": int(context_recall),
        "answer_correctness": int(answer_correct),
        "abstention_correctness": int(abstention_correct),
        "wrong_note_intrusion": int(bool(wrong & set(context_ids))),
        "candidate_ids": candidate_ids,
        "context_ids": context_ids,
        "trace": trace.to_dict() if hasattr(trace, "to_dict") else trace,
        "final_trace": final_trace,
    }


def aggregate(results: Sequence[Dict[str, Any]]) -> Dict[str, Any]:
    out: Dict[str, Any] = {"overall": {}, "by_class": {}}
    for metric in ("candidate_recall", "context_recall", "answer_correctness"):
        vals = [r[metric] for r in results if r.get("available")]
        n = len(vals)
        s = sum(vals)
        lo, hi = wilson(s, n)
        out["overall"][metric] = {"successes": s, "n": n, "rate": s / n if n else None, "wilson_95": [lo, hi]}
    for cls in REQUIRED_CLASSES:
        subset = [r for r in results if r.get("class") == cls and r.get("available")]
        out["by_class"][cls] = {}
        for metric in ("candidate_recall", "context_recall", "answer_correctness"):
            vals = [r[metric] for r in subset]
            n = len(vals)
            s = sum(vals)
            lo, hi = wilson(s, n)
            out["by_class"][cls][metric] = {"successes": s, "n": n, "rate": s / n if n else None, "wilson_95": [lo, hi]}
        wrong = sum(r["wrong_note_intrusion"] for r in subset)
        out["by_class"][cls]["wrong_note_intrusion_rate"] = wrong / len(subset) if subset else None
    return out


def compare_reports(base: Dict[str, Any], candidate: Dict[str, Any]) -> Dict[str, Any]:
    a = {r["id"]: r["answer_correctness"] for r in base["results"] if r.get("available")}
    b = {r["id"]: r["answer_correctness"] for r in candidate["results"] if r.get("available")}
    ids = sorted(set(a) & set(b))
    if not ids:
        raise RuntimeError("NO_PAIRED_CASES")
    paired = mcnemar_exact([bool(a[i]) for i in ids], [bool(b[i]) for i in ids])
    base_rate = sum(a[i] for i in ids) / len(ids)
    cand_rate = sum(b[i] for i in ids) / len(ids)
    return {
        "paired_cases": len(ids),
        "base_answer_rate": base_rate,
        "candidate_answer_rate": cand_rate,
        "effect_size": cand_rate - base_rate,
        "minimum_effect_size": MIN_EFFECT_SIZE,
        "mcnemar_exact": paired,
        "interpretation": (
            "significant_difference"
            if paired["p_value"] < 0.05 and abs(paired["paired_delta"]) >= MIN_EFFECT_SIZE
            else "no significant difference"
        ),
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--candidate-id", default=os.environ.get("GITHUB_SHA", "WORKTREE"))
    ap.add_argument("--baseline", action="store_true")
    ap.add_argument("--compare-base")
    ap.add_argument("--compare-candidate")
    ap.add_argument("--output", default=str(RESULT_PATH))
    args = ap.parse_args()

    if args.compare_base or args.compare_candidate:
        if not (args.compare_base and args.compare_candidate):
            raise SystemExit("BOTH_COMPARE_REPORTS_REQUIRED")
        base = json.loads(Path(args.compare_base).read_text(encoding="utf-8"))
        cand = json.loads(Path(args.compare_candidate).read_text(encoding="utf-8"))
        print(json.dumps(compare_reports(base, cand), indent=2))
        return 0

    heldout_hash = verify_frozen(HELDOUT_PATH, HELDOUT_HASH_PATH)
    dev_hash = verify_frozen(DEV_PATH, DEV_HASH_PATH)
    heldout = load_cases(HELDOUT_PATH)
    dev = load_cases(DEV_PATH)
    validate_schema(heldout, heldout=True)
    validate_schema(dev, heldout=False)

    receipt = RECEIPT_DIR / f"{args.candidate_id}.json"
    if receipt.exists():
        raise SystemExit(f"HELDOUT_ALREADY_RUN_FOR_CANDIDATE:{args.candidate_id}")

    index = build_index()
    tuning = tune_candidate_limit(dev, index)
    selected_limit = int(tuning["selected_candidate_limit"])

    adapter = locate_graph_aware_adapter()
    if adapter is None:
        arm = "main_offline_hybridretriever"
    else:
        arm = "explicit_adapter"

    results = [run_case(c, index, selected_limit, adapter) for c in heldout]
    unavailable = [r for r in results if not r.get("available")]
    if unavailable:
        marker = {"status": "OPTIONAL_COMPONENT_UNAVAILABLE", "cases": unavailable}
        raise SystemExit(json.dumps(marker, sort_keys=True))

    report = {
        "benchmark": "heldout_retrieval_v1",
        "candidate_id": args.candidate_id,
        "arm": arm,
        "frozen_heldout_sha256": heldout_hash,
        "frozen_dev_sha256": dev_hash,
        "heldout_case_count": len(heldout),
        "corpus_note_count": len(index.notes),
        "expected_corpus_note_count": EXPECTED_CORPUS_NOTES,
        "tuning": tuning,
        "minimum_effect_size": MIN_EFFECT_SIZE,
        "regression_baseline": EXPECTED_REGRESSION,
        "results": results,
        "aggregate": aggregate(results),
        "rules": {
            "heldout_failure_feedback_forbidden": True,
            "heldout_max_runs_per_candidate": 1,
            "no_network": True,
            "ollama_default": False,
            "silent_fallback": False,
        },
    }
    out = Path(args.output)
    out.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
    RECEIPT_DIR.mkdir(parents=True, exist_ok=True)
    receipt.write_text(
        json.dumps({"candidate_id": args.candidate_id, "heldout_sha256": heldout_hash, "output": str(out)}, indent=2),
        encoding="utf-8",
    )

    print(f"benchmark={report['benchmark']}")
    print(f"candidate_id={args.candidate_id}")
    print(f"heldout_sha256={heldout_hash}")
    print(f"corpus_notes={len(index.notes)}")
    print(f"selected_candidate_limit={selected_limit} (tuned from dev only)")
    print(f"heldout_cases={len(heldout)}")
    for metric, row in report["aggregate"]["overall"].items():
        print(f"{metric}: {row['successes']}/{row['n']}={row['rate']:.6f} Wilson95={row['wilson_95']}")
    print("QUERY_CLASSES")
    for cls in REQUIRED_CLASSES:
        rows = report["aggregate"]["by_class"][cls]
        print(f"  {cls}: candidate={rows['candidate_recall']['rate']:.6f} context={rows['context_recall']['rate']:.6f} answer={rows['answer_correctness']['rate']:.6f}")
    print(f"result={out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
