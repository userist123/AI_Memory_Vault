"""WP-11 Phase A (r025) — BLOCKING calibration measurement.

Measures the distribution of top `fused_score` (candidate_trace['fused_ranking'][0])
across three populations, through the real MemoryController.search(), graph
expansion off (matching production default):

  1. answerable held-out queries (heldout.json, abstain == False) -- 27 cases
  2. unanswerable held-out queries (heldout.json, abstain == True) -- 3 cases
  3. 20 generated nonsense queries (fixed list below, not regenerated at
     runtime, so this measurement is reproducible)

If these distributions overlap the way the three ad-hoc examples in the
brief suggested, no fixed threshold on fused_score (or any of D1/D2/D3 in
Phase B) can separate "answerable" from "not" -- the top score is not a
confidence signal at all, and Phase B is then not a tuning problem.

Run: python 07_EVALUATION/r025_wp11_abstention/phase_a_calibration.py
Output: 07_EVALUATION/r025_wp11_abstention/phase_a_calibration_report.json
"""
from __future__ import annotations

import json
import os
import statistics
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
V2 = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(V2))

from freeze import digest, hash_path  # noqa: E402

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

PAGE_SIZE = 10

# Fixed, not regenerated at runtime -- deliberately diverse: pure gibberish,
# word salad drawn from unrelated domains, foreign-language nonsense, and
# syntactically query-shaped but semantically empty questions, so the
# nonsense population isn't just one failure mode.
NONSENSE_QUERIES = [
    "xyzzy plugh frobnicate",
    "purple velocity eleven bicycles whisper",
    "qwzx flerp glomp shazzam",
    "the moon ate seventeen umbrellas quietly",
    "banana thermodynamics of Tuesday",
    "zzzyxwvut abcdefghij klmnop",
    "asdf jkl; qwerty uiop zxcvbn",
    "grondle wimplestitch farnoxical dervish",
    "what colour is the sound of Wednesday",
    "how many syllables does silence weigh",
    "flibbertigibbet moonshadow paperclip syzygy",
    "lorem ipsum dolor sit amet consectetur",
    "the seventeenth invisible elephant of March",
    "wobblefinch quantum marmalade recursion",
    "please describe the taste of the number nine",
    "hjkl asdf poiuy mnbvc",
    "glorbnak fritzendorf krellwhistle",
    "what is the airspeed velocity of an unladen thought",
    "throbbing gristle of the fourth dimension pancake",
    "zibzab morlock fendrifuge",
]


def verify_frozen() -> None:
    p = V2 / "heldout.json"
    recorded = hash_path(p).read_text(encoding="utf-8").strip()
    if digest(p) != recorded:
        raise SystemExit("FROZEN_SET_HASH_MISMATCH:heldout.json")


def query_signals(controller: MemoryController, query: str) -> dict:
    """All three candidate Phase-B signals for one query, computed from the
    real trace, not re-derived: D1 top fused_score, D2 margin (top minus
    median fused_score among candidates), D3 agreement (does the same note
    rank #1 in both the bm25 and entity generators?)."""
    pack = controller.search(Principal.HUMAN, query, page_size=PAGE_SIZE)
    trace = pack.get("candidate_trace", {}) or {}
    ranking = trace.get("fused_ranking") or []
    if not ranking:
        return {"top_fused_score": None, "margin": None, "bm25_entity_agree_top1": None,
                "top_bm25_raw": None, "top_entity_raw": None}

    scores = [float(e.get("fused_score", 0.0)) for e in ranking]
    top = scores[0]
    median = statistics.median(scores)
    margin = top - median

    per_gen = trace.get("per_generator") or {}
    bm25_list = per_gen.get("bm25") or []
    entity_list = per_gen.get("entity") or []
    bm25_top_id = bm25_list[0]["id"] if bm25_list else None
    entity_top_id = entity_list[0]["id"] if entity_list else None
    agree = (bm25_top_id is not None and bm25_top_id == entity_top_id)
    top_bm25_raw = bm25_list[0]["score"] if bm25_list else None
    top_entity_raw = entity_list[0]["score"] if entity_list else None

    return {
        "top_fused_score": round(top, 6),
        "margin": round(margin, 6),
        "bm25_entity_agree_top1": agree,
        "top_bm25_raw": round(float(top_bm25_raw), 6) if top_bm25_raw is not None else None,
        "top_entity_raw": round(float(top_entity_raw), 6) if top_entity_raw is not None else None,
    }


def top_fused_score(controller: MemoryController, query: str) -> float | None:
    return query_signals(controller, query)["top_fused_score"]


def describe(label: str, scores: list[float]) -> dict:
    scores = [s for s in scores if s is not None]
    if not scores:
        return {"label": label, "n": 0}
    return {
        "label": label,
        "n": len(scores),
        "min": round(min(scores), 6),
        "max": round(max(scores), 6),
        "mean": round(statistics.mean(scores), 6),
        "median": round(statistics.median(scores), 6),
        "stdev": round(statistics.stdev(scores), 6) if len(scores) > 1 else 0.0,
        "scores_sorted": sorted(round(s, 6) for s in scores),
    }


def overlap_summary(a: dict, b: dict) -> dict:
    """How much do two populations' score ranges overlap?"""
    if a["n"] == 0 or b["n"] == 0:
        return {"overlap": "unmeasurable (empty population)"}
    lo = max(a["min"], b["min"])
    hi = min(a["max"], b["max"])
    overlap_width = max(0.0, hi - lo)
    span = max(a["max"], b["max"]) - min(a["min"], b["min"])
    return {
        "overlap_range": [round(lo, 6), round(hi, 6)] if overlap_width > 0 else None,
        "overlap_fraction_of_combined_span": round(overlap_width / span, 4) if span > 0 else None,
    }


def main() -> int:
    verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    controller = MemoryController(storage=storage, index=index, enable_graph_expansion=False)

    cases = json.loads((V2 / "heldout.json").read_text(encoding="utf-8"))["cases"]
    answerable = [c for c in cases if not c["abstain"]]
    unanswerable = [c for c in cases if c["abstain"]]

    answerable_sig = [query_signals(controller, c["query"]) for c in answerable]
    unanswerable_sig = [query_signals(controller, c["query"]) for c in unanswerable]
    nonsense_sig = [query_signals(controller, q) for q in NONSENSE_QUERIES]

    answerable_scores = [s["top_fused_score"] for s in answerable_sig]
    unanswerable_scores = [s["top_fused_score"] for s in unanswerable_sig]
    nonsense_scores = [s["top_fused_score"] for s in nonsense_sig]

    d_answerable = describe("answerable_heldout", answerable_scores)
    d_unanswerable = describe("unanswerable_heldout", unanswerable_scores)
    d_nonsense = describe("nonsense_generated", nonsense_scores)

    def signal_stats(sigs: list[dict], key: str) -> dict:
        vals = [s[key] for s in sigs if s.get(key) is not None]
        if not vals:
            return {"n": 0}
        return {"n": len(vals), "min": round(min(vals), 6), "max": round(max(vals), 6),
                "mean": round(statistics.mean(vals), 6)}

    def separable(pos: list[float], neg: list[float]) -> dict:
        pos = [p for p in pos if p is not None]
        neg = [n for n in neg if n is not None]
        if not pos or not neg:
            return {"separable": None}
        return {"separable": min(pos) > max(neg), "min_pos": round(min(pos), 6), "max_neg": round(max(neg), 6)}

    report = {
        "page_size": PAGE_SIZE,
        "populations": {
            "answerable_heldout": d_answerable,
            "unanswerable_heldout": d_unanswerable,
            "nonsense_generated": d_nonsense,
        },
        "per_case_answerable": [
            {"id": c["id"], "query": c["query"], **s}
            for c, s in zip(answerable, answerable_sig)
        ],
        "per_case_unanswerable": [
            {"id": c["id"], "query": c["query"], **s}
            for c, s in zip(unanswerable, unanswerable_sig)
        ],
        "per_case_nonsense": [
            {"query": q, **s}
            for q, s in zip(NONSENSE_QUERIES, nonsense_sig)
        ],
        "overlap_answerable_vs_unanswerable": overlap_summary(d_answerable, d_unanswerable),
        "overlap_answerable_vs_nonsense": overlap_summary(d_answerable, d_nonsense),
        "overlap_unanswerable_vs_nonsense": overlap_summary(d_unanswerable, d_nonsense),
    }

    # D1: does ANY threshold on top fused_score separate answerable from
    # (unanswerable UNION nonsense) with zero errors?
    neg_scores = [s for s in (unanswerable_scores + nonsense_scores) if s is not None]
    pos_scores = [s for s in answerable_scores if s is not None]
    report["D1_top_fused_score"] = separable(pos_scores, neg_scores)

    # D2: margin (top - median candidate fused_score).
    pos_margin = [s["margin"] for s in answerable_sig if s.get("margin") is not None]
    neg_margin = [s["margin"] for s in (unanswerable_sig + nonsense_sig) if s.get("margin") is not None]
    report["D2_margin"] = separable(pos_margin, neg_margin)
    report["D2_margin_stats"] = {
        "answerable": signal_stats(answerable_sig, "margin"),
        "non_answerable": signal_stats(unanswerable_sig + nonsense_sig, "margin"),
    }

    # D3: bm25/entity top-1 agreement rate per population (a boolean signal,
    # not a threshold -- reported as agreement RATE per population, and
    # whether disagreement alone would flag any non-answerable case).
    def agreement_rate(sigs: list[dict]) -> float | None:
        vals = [s["bm25_entity_agree_top1"] for s in sigs if s.get("bm25_entity_agree_top1") is not None]
        return round(sum(vals) / len(vals), 4) if vals else None

    report["D3_bm25_entity_agreement_rate"] = {
        "answerable": agreement_rate(answerable_sig),
        "unanswerable": agreement_rate(unanswerable_sig),
        "nonsense": agreement_rate(nonsense_sig),
    }

    out_path = HERE / "phase_a_calibration_report.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in report.items() if not k.startswith("per_case")}, indent=2))
    print(f"\nFull report -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
