"""D-04: a cache hit must not change the ranking of a search.

`RetrievalEngine.retrieve()` used to return the cached notes with
`fused_ranking: []`. Under the default `fused_score` ranking arm the controller
sorts by the fused score taken from that trace, so on a hit every note scored
0.0 and the order collapsed to reverse id. The same query, asked twice through
one controller, returned a different top result the second time.
"""
from __future__ import annotations

import pytest

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle, Principal


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_d04_cache_ranking_32bytes")


def _note(note_id, content):
    return {
        "id": note_id, "type": "knowledge", "lifecycle": Lifecycle.ACTIVE.value,
        "category": "c", "tags": ["t"], "created": "2026-09-01", "updated": "2026-09-01",
        "provenance": {"source_type": "user", "source_ref": "unit"},
        "confidence": "high", "verification": "verified", "relations": [],
        "content": content,
    }


def _controller():
    storage = StorageEngine()
    # "a-best" sorts FIRST by id, so the reverse-id collapse puts it last.
    storage.set("a-best", _note("a-best", "zebra zebra zebra zebra quokka"))
    storage.set("b-mid", _note("b-mid", "zebra quokka filler"))
    for i in range(30):
        storage.set(f"m-{i:02d}", _note(f"m-{i:02d}", f"filler words {i} zebra"))
    return MemoryController(storage)


def test_cache_hit_keeps_the_generated_order():
    ctl = _controller()
    first = ctl.search(Principal.AI_AGENT, "zebra quokka", page_size=5)
    second = ctl.search(Principal.AI_AGENT, "zebra quokka", page_size=5)

    assert first["candidate_trace"]["source"] == "generated"
    assert second["candidate_trace"]["source"] == "cache"
    ids1 = [r["id"] for r in first["results"]]
    ids2 = [r["id"] for r in second["results"]]
    # The generated order is relevance-led, not reverse id (which would put m-29 first).
    assert set(ids1[:2]) == {"a-best", "b-mid"}
    assert ids2 == ids1
    assert second["candidate_trace"]["fused_ranking"], "cache hit must carry the fused ranking"


def test_cache_hit_order_matches_for_human_principal_too():
    ctl = _controller()
    ids = [[r["id"] for r in ctl.search(Principal.HUMAN, "zebra quokka", page_size=5)["results"]]
           for _ in range(2)]
    assert ids[0] == ids[1]
