"""AKE Loop B: retrieval quality of a frozen benchmark through the production agent path.

Each case runs ``MemoryController.search(Principal.AI_AGENT, query, page_size=k)``: the call
``interfaces/memory_access.search`` makes for ``memory_search``, at the agent page size (5).
A miss is classified from the controller's own ``candidate_trace.fused_ranking``:

- ``PAGINATION_CUT``      gold was a candidate, ranked below k;
- ``FILTERED``            gold ranked within k but was not served (lifecycle floor, quarantine, ...);
- ``NEVER_CANDIDATE``     gold is in storage but never became a candidate;
- ``GOLD_NOT_IN_STORAGE`` the storage engine cannot see the gold note at all: a structural zero,
  reported apart so it is not mistaken for a retrieval failure (VaultIndex and FileStorageEngine
  scan different populations, VAULT_STATE.md section 4).

Abstain cases are ``ABSTAIN_UNMEASURABLE``: there is no abstention signal to score yet.
The output carries case ids, classes and ranks; never the query text or note content.
"""
from __future__ import annotations

import hashlib
import json
import math
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from memory_controller.authorizer import Principal

AGENT_PAGE_SIZE = 5


class BenchmarkIntegrityError(ValueError):
    """The benchmark file does not match its frozen SHA-256 sidecar."""


def canonical_digest(path: Path) -> str:
    """SHA-256 over the file with CRLF/CR normalised to LF (07_EVALUATION/heldout_retrieval_benchmark_v2/CONTRACT.md)."""
    data = Path(path).read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    return hashlib.sha256(data).hexdigest()


def load_frozen_benchmark(path) -> Dict[str, Any]:
    path = Path(path)
    sidecar = path.with_suffix(path.suffix + ".sha256")
    if not path.is_file() or not sidecar.is_file():
        raise BenchmarkIntegrityError(f"benchmark or its .sha256 sidecar is missing: {path.name}")
    recorded = sidecar.read_text(encoding="utf-8").strip()
    actual = canonical_digest(path)
    if actual != recorded:
        raise BenchmarkIntegrityError(f"FROZEN_SET_HASH_MISMATCH: {path.name}")
    data = json.loads(path.read_text(encoding="utf-8"))
    cases = data.get("cases")
    if not isinstance(cases, list) or not cases:
        raise BenchmarkIntegrityError(f"no cases in {path.name}")
    return {"name": data.get("set_name") or path.stem, "path": path.name, "digest": actual, "cases": cases}


def wilson_interval(successes: int, n: int, z: float = 1.959963984540054) -> Tuple[Optional[float], Optional[float]]:
    if n <= 0:
        return (None, None)
    p = successes / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return (max(0.0, centre - half), min(1.0, centre + half))


def _in_storage(controller, note_id: str) -> bool:
    storage = getattr(controller, "storage", None)
    index = getattr(storage, "id_to_path", None)
    if isinstance(index, dict):
        return note_id in index
    store = getattr(storage, "store", None)
    if isinstance(store, dict):
        return note_id in store
    try:
        return storage.get(note_id) is not None
    except Exception:
        return False


def _run_case(controller, case: Dict[str, Any], k: int) -> Dict[str, Any]:
    row: Dict[str, Any] = {"id": str(case.get("id")), "class": str(case.get("class"))}
    if case.get("abstain"):
        return {**row, "hit": None, "rank_in_page": None, "fused_rank": None, "miss_class": "ABSTAIN_UNMEASURABLE"}
    gold = [str(g) for g in case.get("gold_relevant_notes") or []]
    pack = controller.search(Principal.AI_AGENT, case["query"], page_size=k)
    served = [r.get("id") for r in (pack.get("results") or [])[:k] if isinstance(r, dict)]
    fused = (pack.get("candidate_trace") or {}).get("fused_ranking") or []
    fused_rank = {}
    for pos, entry in enumerate(fused, start=1):
        if isinstance(entry, dict) and entry.get("id") is not None:
            fused_rank.setdefault(str(entry["id"]), int(entry.get("rank") or pos))
    page_ranks = [served.index(g) + 1 for g in gold if g in served]
    gold_ranks = [fused_rank[g] for g in gold if g in fused_rank]
    best_fused = min(gold_ranks) if gold_ranks else None
    if page_ranks:
        return {**row, "hit": 1, "rank_in_page": min(page_ranks), "fused_rank": best_fused, "miss_class": None}
    if not any(_in_storage(controller, g) for g in gold):
        miss = "GOLD_NOT_IN_STORAGE"
    elif best_fused is None:
        miss = "NEVER_CANDIDATE"
    elif best_fused > k:
        miss = "PAGINATION_CUT"
    else:
        miss = "FILTERED"
    return {**row, "hit": 0, "rank_in_page": None, "fused_rank": best_fused, "miss_class": miss}


def evaluate(controller, cases: List[Dict[str, Any]], k: int = AGENT_PAGE_SIZE) -> Dict[str, Any]:
    rows = [_run_case(controller, case, k) for case in cases]
    measurable = [r for r in rows if r["hit"] is not None]
    scorable = [r for r in measurable if r["miss_class"] != "GOLD_NOT_IN_STORAGE"]
    hits = sum(r["hit"] for r in measurable)
    mrr = (sum(1.0 / r["rank_in_page"] for r in measurable if r["rank_in_page"]) / len(measurable)) if measurable else None
    by_class: Dict[str, Dict[str, Any]] = {}
    for klass in sorted({r["class"] for r in measurable}):
        sub = [r for r in measurable if r["class"] == klass]
        by_class[klass] = {"n": len(sub), "hits": sum(r["hit"] for r in sub)}
    summary = {
        "principal": Principal.AI_AGENT.name,
        "k": k,
        "n": len(rows),
        "n_measurable": len(measurable),
        "n_scorable": len(scorable),
        "hits": hits,
        "hit_rate": hits / len(measurable) if measurable else None,
        "hit_rate_ci95": list(wilson_interval(hits, len(measurable))),
        "hit_rate_scorable": (sum(r["hit"] for r in scorable) / len(scorable)) if scorable else None,
        "mrr_at_k": mrr,
        "miss_classes": dict(Counter(r["miss_class"] for r in rows if r["miss_class"])),
        "by_class": by_class,
    }
    return {"cases": rows, "summary": summary}
