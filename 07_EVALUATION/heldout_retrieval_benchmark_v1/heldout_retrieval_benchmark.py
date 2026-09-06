"""R009b frozen held-out retrieval benchmark.

Rules enforced here are part of the experiment contract:
- verify the frozen held-out SHA-256 before doing any retrieval;
- tune thresholds against dev only;
- evaluate held-out at most once per candidate fingerprint (receipt file);
- NEVER inspect held-out failures to tune a later threshold;
- report candidate recall, context recall, and answer correctness separately;
- report every required query class separately;
- default path is deterministic/offline and never calls Ollama/network;
- optional adapters fail closed with an explicit marker.

The default answer score is a deterministic abstention+fact-presence proxy. It
is intentionally not described as LLM answer quality. A future adapter may
supply real answers explicitly.

Paired future comparisons use case IDs and exact McNemar tests. Wilson 95%
intervals are reported for each arm. Minimum effect size is 10 percentage
points; otherwise the interpretation is exactly "no significant difference".
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
from typing import Any, Callable, Dict, List, Sequence, Tuple

ROOT = Path(__file__).resolve().parents[2]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (ROOT, PACKAGES):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))

BENCH = Path(__file__).resolve().parent
HELDOUT = BENCH / "heldout.json"
DEV = BENCH / "dev.json"
HELDOUT_SHA = BENCH / "SET_SHA256.txt"
DEV_SHA = BENCH / "DEV_SHA256.txt"
RECEIPTS = BENCH / "receipts"
DEFAULT_OUT = BENCH / "r009b_baseline.json"
CANDIDATE_LIMITS = (20, 50, 100, 200, 500)
EXPECTED_NOTES = 935
MIN_EFFECT = 0.10
REQUIRED_CLASSES = (
    "exact_identifier_lookup", "paraphrase", "synonym_substitution",
    "lexical_trap", "cross_cluster_multihop", "unanswerable",
)
REGRESSION_BASELINE = {"passed": 1174, "skipped": 3}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def recorded(path: Path) -> str:
    s = path.read_text(encoding="utf-8").strip().split()[0].lower()
    if len(s) != 64 or any(c not in "0123456789abcdef" for c in s):
        raise RuntimeError(f"INVALID_HASH_RECORD:{path}")
    return s


def verify_frozen(data_path: Path, sha_path: Path) -> str:
    expected = recorded(sha_path)
    actual = digest(data_path)
    if actual != expected:
        raise RuntimeError(f"FROZEN_SET_HASH_MISMATCH:{data_path.name}:expected={expected}:actual={actual}")
    return actual


def load_set(path: Path) -> List[Dict[str, Any]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    cases = data.get("cases")
    if not isinstance(cases, list) or not cases:
        raise RuntimeError(f"INVALID_BENCHMARK_SET:{path}")
    ids = [c.get("id") for c in cases]
    if len(ids) != len(set(ids)) or any(not x for x in ids):
        raise RuntimeError("INVALID_BENCHMARK_CASE_IDS")
    required = {"id", "class", "query", "expected_answer", "gold_relevant_notes", "required_facts", "wrong_note_ids", "abstain"}
    if not required.issubset(set().union(*(set(c) for c in cases))):
        raise RuntimeError("INVALID_BENCHMARK_SCHEMA")
    return cases


def validate_heldout(cases: Sequence[Dict[str, Any]]) -> None:
    classes = {c["class"] for c in cases}
    if classes != set(REQUIRED_CLASSES):
        raise RuntimeError(f"INVALID_QUERY_CLASSES:{sorted(classes)}")
    counts = {cls: sum(c["class"] == cls for c in cases) for cls in REQUIRED_CLASSES}
    if any(v < 1 for v in counts.values()):
        raise RuntimeError(f"UNREPRESENTED_QUERY_CLASS:{counts}")


def wilson(k: int, n: int) -> Tuple[float, float]:
    if n == 0:
        return (float("nan"), float("nan"))
    z = 1.959963984540054
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return max(0.0, c - h), min(1.0, c + h)


def exact_binom_two_sided(k: int, n: int) -> float:
    if n == 0:
        return 1.0
    pk = math.comb(n, k) / (2 ** n)
    return min(1.0, sum(math.comb(n, i) / (2 ** n) for i in range(n + 1) if math.comb(n, i) / (2 ** n) <= pk + 1e-15))


def paired_test(a: Sequence[bool], b: Sequence[bool]) -> Dict[str, Any]:
    if len(a) != len(b):
        raise RuntimeError("PAIRED_LENGTH_MISMATCH")
    a_only = sum(x and not y for x, y in zip(a, b))
    b_only = sum((not x) and y for x, y in zip(a, b))
    d = a_only + b_only
    delta = (sum(b) - sum(a)) / len(a) if a else 0.0
    return {"a_only": a_only, "b_only": b_only, "discordant": d, "p_value": exact_binom_two_sided(min(a_only, b_only), d), "paired_delta": delta}


def adapter() -> Callable[..., Dict[str, Any]] | None:
    spec = os.environ.get("R009B_ADAPTER")
    if not spec:
        return None
    if ":" not in spec:
        raise RuntimeError("INVALID_ADAPTER_SPEC")
    mod, name = spec.split(":", 1)
    fn = getattr(importlib.import_module(mod), name, None)
    if not callable(fn):
        raise RuntimeError("INVALID_ADAPTER_ENTRYPOINT")
    return fn


def build_index() -> Any:
    from retrieval.vault_index import VaultIndex  # type: ignore
    idx = VaultIndex.load(
        ROOT,
        roots=("01_ARCHITECTURE", "02_PRODUCT", "10_DOCUMENTATION", "00_GOVERNANCE"),
        lifecycles=("ACTIVE", "REVIEW", "NORMALIZED", "CLASSIFIED", "NONE"),
        include_raw=False,
        include_archived=False,
    )
    if len(idx.notes) != EXPECTED_NOTES:
        raise RuntimeError(f"CORPUS_SIZE_MISMATCH:expected={EXPECTED_NOTES}:actual={len(idx.notes)}")
    return idx


def tune(dev: Sequence[Dict[str, Any]], idx: Any) -> Dict[str, Any]:
    """Tune exclusively from dev. Held-out cases never enter this function."""
    from retrieval.context.candidate_generation import generate_candidates  # type: ignore
    from retrieval.hybrid_retrieval import HybridRetriever  # type: ignore

    notes = [{"id": n.id, "content": n.text, "category": n.category, "tags": n.tags} for n in idx.notes]
    answerable = [c for c in dev if not c["abstain"]]
    limit_scores = []
    for limit in CANDIDATE_LIMITS:
        hits = sum(bool({str(x["id"]) for x in generate_candidates(c["query"], notes, limit)[0]} & {str(g) for g in c["gold_relevant_notes"]}) for c in answerable)
        limit_scores.append({"candidate_limit": limit, "candidate_recall": hits / len(answerable), "n": len(answerable)})
    eligible = [x for x in limit_scores if x["candidate_recall"] >= 0.90]
    selected_limit = min(eligible, key=lambda x: x["candidate_limit"])["candidate_limit"] if eligible else max(limit_scores, key=lambda x: (x["candidate_recall"], -x["candidate_limit"]))["candidate_limit"]

    retriever = HybridRetriever(idx)
    dev_scores = []
    for c in dev:
        hits = retriever.search(c["query"], top_k=1)
        dev_scores.append((float(hits[0].score) if hits else 0.0, bool(c["abstain"])))
    candidates = sorted({0.0, *[s for s, _ in dev_scores], *[(dev_scores[i][0] + dev_scores[j][0]) / 2 for i in range(len(dev_scores)) for j in range(i + 1, len(dev_scores))]})
    best = None
    for threshold in candidates:
        correct = sum((score < threshold) == abstain for score, abstain in dev_scores)
        row = {"threshold": threshold, "abstention_accuracy": correct / len(dev_scores), "n": len(dev_scores)}
        if best is None or (row["abstention_accuracy"], -threshold) > (best["abstention_accuracy"], -best["threshold"]):
            best = row
    return {"selected_candidate_limit": int(selected_limit), "candidate_limit_trials": limit_scores, "selected_abstention_threshold": float(best["threshold"]), "abstention_threshold_trials": candidates and [best] or []}


def run_case(case: Dict[str, Any], idx: Any, limit: int, abstain_threshold: float, fn: Callable[..., Dict[str, Any]] | None) -> Dict[str, Any]:
    from retrieval.context.candidate_generation import generate_candidates  # type: ignore
    from retrieval.hybrid_retrieval import HybridRetriever  # type: ignore

    notes = [{"id": n.id, "content": n.text, "category": n.category, "tags": n.tags} for n in idx.notes]
    candidates, ctrace = generate_candidates(case["query"], notes, limit)
    candidate_ids = [str(n["id"]) for n in candidates]
    if fn is not None:
        out = fn(case=case, index=idx, candidate_limit=limit, abstain_threshold=abstain_threshold)
        if not isinstance(out, dict) or out.get("available") is not True:
            return {"id": case["id"], "class": case["class"], "available": False, "marker": out.get("marker", "OPTIONAL_COMPONENT_UNAVAILABLE") if isinstance(out, dict) else "OPTIONAL_COMPONENT_UNAVAILABLE"}
        candidate_ids = [str(x) for x in out.get("candidate_ids", [])]
        context_ids = [str(x) for x in out.get("context_ids", [])]
        answer_text = str(out.get("answer_text", ""))
        predicted_abstain = bool(out.get("predicted_abstain", not answer_text.strip()))
        ctrace = out.get("trace", {})
        ftrace = out.get("final_trace", {})
    else:
        retriever = HybridRetriever(idx)
        final_hits, ftrace = retriever.search_with_trace(case["query"], top_k=min(20, limit))
        context_ids = [h.note.id for h in final_hits]
        top_score = float(final_hits[0].score) if final_hits else 0.0
        predicted_abstain = (not final_hits) or (top_score < abstain_threshold)
        answer_text = ""  # no model is invoked in the default offline path

    gold = {str(x) for x in case["gold_relevant_notes"]}
    wrong = {str(x) for x in case["wrong_note_ids"]}
    candidate_recall = bool(gold & set(candidate_ids)) if gold else True
    context_recall = bool(gold & set(context_ids)) if gold else True
    blob = " ".join(idx.by_id[n].text for n in context_ids if n in idx.by_id).lower()
    facts = [str(x).lower() for x in case["required_facts"]]
    facts_ok = bool(facts) and all(f in blob for f in facts)
    answer_correct = ((predicted_abstain is False) and facts_ok) if not case["abstain"] else bool(predicted_abstain)
    return {
        "id": case["id"], "class": case["class"], "available": True,
        "candidate_recall": int(candidate_recall), "context_recall": int(context_recall),
        "answer_correctness": int(answer_correct), "abstention_correctness": int(predicted_abstain == bool(case["abstain"])),
        "wrong_note_intrusion": int(bool(wrong & set(context_ids))),
        "candidate_ids": candidate_ids, "context_ids": context_ids,
        "predicted_abstain": predicted_abstain,
        "trace": ctrace.to_dict() if hasattr(ctrace, "to_dict") else ctrace,
        "final_trace": ftrace,
    }


def summarize(results: Sequence[Dict[str, Any]]) -> Dict[str, Any]:
    out = {"overall": {}, "by_class": {}}
    for metric in ("candidate_recall", "context_recall", "answer_correctness"):
        vals = [r[metric] for r in results if r.get("available")]
        k, n = sum(vals), len(vals)
        out["overall"][metric] = {"successes": k, "n": n, "rate": k / n if n else None, "wilson_95": wilson(k, n)}
    for cls in REQUIRED_CLASSES:
        sub = [r for r in results if r.get("class") == cls and r.get("available")]
        out["by_class"][cls] = {}
        for metric in ("candidate_recall", "context_recall", "answer_correctness"):
            vals = [r[metric] for r in sub]; k, n = sum(vals), len(vals)
            out["by_class"][cls][metric] = {"successes": k, "n": n, "rate": k / n if n else None, "wilson_95": wilson(k, n)}
        out["by_class"][cls]["wrong_note_intrusion_rate"] = sum(r["wrong_note_intrusion"] for r in sub) / len(sub) if sub else None
        out["by_class"][cls]["abstention_correctness_rate"] = sum(r["abstention_correctness"] for r in sub) / len(sub) if sub else None
    return out


def compare(base_path: Path, cand_path: Path) -> Dict[str, Any]:
    base = json.loads(base_path.read_text(encoding="utf-8")); cand = json.loads(cand_path.read_text(encoding="utf-8"))
    out = {}
    for metric in ("candidate_recall", "context_recall", "answer_correctness"):
        a = {r["id"]: bool(r[metric]) for r in base["results"] if r.get("available")}
        b = {r["id"]: bool(r[metric]) for r in cand["results"] if r.get("available")}
        ids = sorted(set(a) & set(b))
        if not ids: raise RuntimeError("NO_PAIRED_CASES")
        av, bv = [a[i] for i in ids], [b[i] for i in ids]
        t = paired_test(av, bv)
        ar, br = sum(av) / len(ids), sum(bv) / len(ids)
        out[metric] = {"n": len(ids), "base_rate": ar, "candidate_rate": br, "effect_size": br - ar, "base_wilson_95": wilson(sum(av), len(ids)), "candidate_wilson_95": wilson(sum(bv), len(ids)), "paired_test": t, "interpretation": "significant_difference" if t["p_value"] < 0.05 and abs(t["paired_delta"]) >= MIN_EFFECT else "no significant difference"}
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--candidate-id", default=os.environ.get("GITHUB_SHA", "WORKTREE"))
    ap.add_argument("--compare-base")
    ap.add_argument("--compare-candidate")
    ap.add_argument("--output", default=str(DEFAULT_OUT))
    args = ap.parse_args()

    if args.compare_base or args.compare_candidate:
        if not (args.compare_base and args.compare_candidate): raise SystemExit("BOTH_COMPARE_REPORTS_REQUIRED")
        print(json.dumps(compare(Path(args.compare_base), Path(args.compare_candidate)), indent=2, sort_keys=True))
        return 0

    heldout_hash = verify_frozen(HELDOUT, HELDOUT_SHA)
    dev_hash = verify_frozen(DEV, DEV_SHA)
    heldout = load_set(HELDOUT); dev = load_set(DEV)
    validate_heldout(heldout)

    RECEIPTS.mkdir(parents=True, exist_ok=True)
    receipt = RECEIPTS / f"{args.candidate_id}.json"
    if receipt.exists():
        raise SystemExit(f"HELDOUT_ALREADY_RUN_FOR_CANDIDATE:{args.candidate_id}")

    idx = build_index()
    tuning = tune(dev, idx)  # dev only; held-out is not passed here
    fn = adapter()
    results = [run_case(c, idx, tuning["selected_candidate_limit"], tuning["selected_abstention_threshold"], fn) for c in heldout]
    unavailable = [r for r in results if not r.get("available")]
    if unavailable:
        raise SystemExit(json.dumps({"status": "OPTIONAL_COMPONENT_UNAVAILABLE", "cases": unavailable}, sort_keys=True))

    report = {
        "benchmark": "heldout_retrieval_v1", "candidate_id": args.candidate_id,
        "arm": "explicit_adapter" if fn else "main_offline_hybridretriever",
        "frozen_heldout_sha256": heldout_hash, "frozen_dev_sha256": dev_hash,
        "heldout_case_count": len(heldout), "corpus_note_count": len(idx.notes),
        "expected_corpus_note_count": EXPECTED_NOTES, "minimum_effect_size": MIN_EFFECT,
        "regression_baseline": REGRESSION_BASELINE, "tuning": tuning,
        "results": results, "aggregate": summarize(results),
        "experiment_rules": {
            "heldout_failure_feedback_forbidden": True,
            "heldout_max_runs_per_candidate": 1,
            "default_offline": True, "network": False, "ollama_default": False, "silent_fallback": False,
            "query_classes_separate": True, "stage_metrics_separate": True,
            "corpus_size_power_note": "935 notes is the fixed corpus size, not the number of paired benchmark observations. Power is primarily determined by held-out case count; with 36 cases total and 6 per class, class-specific intervals are necessarily wide. A future positive effect must clear both the paired significance test and the 10-point minimum effect threshold.",
        },
    }
    Path(args.output).write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
    receipt.write_text(json.dumps({"candidate_id": args.candidate_id, "heldout_sha256": heldout_hash, "output": str(args.output)}, indent=2), encoding="utf-8")
    print(f"benchmark={report['benchmark']}")
    print(f"candidate_id={args.candidate_id}")
    print(f"heldout_sha256={heldout_hash}")
    print(f"dev_sha256={dev_hash}")
    print(f"corpus_notes={len(idx.notes)}")
    print(f"selected_candidate_limit={tuning['selected_candidate_limit']} (dev only)")
    print(f"selected_abstention_threshold={tuning['selected_abstention_threshold']} (dev only)")
    print(f"heldout_cases={len(heldout)}")
    for metric, row in report["aggregate"]["overall"].items():
        print(f"{metric}: {row['successes']}/{row['n']}={row['rate']:.6f} Wilson95={row['wilson_95']}")
    for cls in REQUIRED_CLASSES:
        row = report["aggregate"]["by_class"][cls]
        print(f"{cls}: candidate={row['candidate_recall']['rate']:.6f} context={row['context_recall']['rate']:.6f} answer={row['answer_correctness']['rate']:.6f} abstain={row['abstention_correctness_rate']:.6f}")
    print(f"result={args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
