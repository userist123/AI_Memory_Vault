"""Non-invasive multi-graph + spreading-activation re-ranking layer.

Wraps MemoryController.search() results without modifying memory_controller/.
Opt-in only; the existing search() contract, authorization, and audit logging
are completely untouched — this module only re-orders result lists it receives.
"""
from __future__ import annotations

from typing import Any, Dict, List

from .multi_graph import MultiGraphMemory
from .spreading_activation import SpreadingActivationEngine


def build_multi_graph(controller) -> MultiGraphMemory:
    notes = list(controller.storage.store.values())
    return MultiGraphMemory().build_from_notes(notes)


def ranked_search(controller, principal, query: str, top_k: int = 10,
                   decay: float = 0.6, max_hops: int = 2) -> List[Dict[str, Any]]:
    """Call the existing controller.search(), then re-rank with spreading activation.

    Falls back to the original ordering (or empty list) if graph construction,
    activation, or the underlying search() call yields no usable results.
    """
    try:
        pack = controller.search(principal, query, page_size=max(top_k, 10))
    except Exception:
        return []
    results = pack.get("results", []) if isinstance(pack, dict) else []
    if not results:
        return []

    id_to_result = {item.get("id"): item for item in results if item.get("id")}
    if not id_to_result:
        return results[:top_k]

    try:
        graph_memory = build_multi_graph(controller)
        base_scores = {note_id: 1.0 / (idx + 1) for idx, note_id in enumerate(id_to_result)}
        engine = SpreadingActivationEngine(graph_memory, decay=decay, max_hops=max_hops)
        ranked_ids = engine.rank(base_scores, top_k=top_k)
    except Exception:
        return results[:top_k]

    ranked_id_set = {note_id for note_id, _ in ranked_ids}
    ranked_results = [id_to_result[note_id] for note_id, _ in ranked_ids if note_id in id_to_result]
    for note_id, item in id_to_result.items():
        if note_id not in ranked_id_set:
            ranked_results.append(item)
    return ranked_results[:top_k]
