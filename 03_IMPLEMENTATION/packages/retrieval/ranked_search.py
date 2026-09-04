"""Non-invasive multi-graph + spreading-activation re-ranking layer.

Wraps MemoryController.search() results without modifying memory_controller/.
Opt-in only; the existing search() contract, authorization, and audit logging
are completely untouched — this module only re-orders result lists it receives.
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional

from .multi_graph import MultiGraphMemory
from .spreading_activation import SpreadingActivationEngine


def build_multi_graph(controller) -> MultiGraphMemory:
    all_notes = getattr(controller.storage, "all_notes", None)
    if not callable(all_notes):
        raise TypeError("storage does not expose all_notes() for graph indexing")
    notes = list(all_notes())
    return MultiGraphMemory().build_from_notes(notes)


def ranked_search(controller, principal, query: str, top_k: int = 10,
                   decay: float = 0.6, max_hops: int = 2,
                   diagnostics: Optional[Dict[str, Any]] = None) -> List[Dict[str, Any]]:
    """Call the existing controller.search(), then re-rank with spreading activation.

    Falls back to the original ordering (or empty list) if graph construction,
    activation, or the underlying search() call yields no usable results.
    """
    def mark(status: str, reason: str = "") -> None:
        if diagnostics is not None:
            diagnostics.update({"graph_status": status, "graph_reason": reason})

    mark("UNAVAILABLE", "not attempted")
    try:
        pack = controller.search(principal, query, page_size=max(top_k, 10))
    except Exception as exc:
        mark("FAILED", f"base search failed: {type(exc).__name__}: {exc}")
        return []
    results = pack.get("results", []) if isinstance(pack, dict) else []
    if not results:
        mark("UNAVAILABLE", "base search returned no results")
        return []

    id_to_result = {item.get("id"): item for item in results if item.get("id")}
    if not id_to_result:
        mark("UNAVAILABLE", "base results have no IDs")
        return results[:top_k]

    try:
        graph_memory = build_multi_graph(controller)
        base_scores = {}
        for idx, (note_id, item) in enumerate(id_to_result.items()):
            raw_score = item.get("relevance_score", item.get("score"))
            base_scores[note_id] = float(raw_score) if raw_score is not None else 1.0 / (idx + 1)
        engine = SpreadingActivationEngine(graph_memory, decay=decay, max_hops=max_hops)
        ranked_ids = engine.rank(base_scores, top_k=top_k)
    except Exception as exc:
        mark("FAILED", f"graph rerank failed: {type(exc).__name__}: {exc}")
        return results[:top_k]

    mark("AVAILABLE", "graph rerank completed")

    ranked_id_set = {note_id for note_id, _ in ranked_ids}
    ranked_results = [id_to_result[note_id] for note_id, _ in ranked_ids if note_id in id_to_result]
    for note_id, item in id_to_result.items():
        if note_id not in ranked_id_set:
            ranked_results.append(item)
    return ranked_results[:top_k]
