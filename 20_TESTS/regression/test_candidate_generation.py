"""Regression coverage for production candidate generation (r004).

Context: MemoryController.search() used to never read the query text during
candidate selection -- StorageEngine.query() filtered by lifecycle/type/RAW
only, and RetrievalEngine.retrieve() then took the first `candidate_limit`
(hardcoded 20) results in whatever order storage happened to return them
(insertion order for the in-memory/file engines). A relevant note ranked
below that head-20 cut was permanently unreachable by any downstream scorer.

This file covers:
  1. The acceptance criterion: a note relevant to the query but beyond the
     old head-20 insertion-order cut is now retrievable (FAILS on main).
  2. The adversarial criterion: RAW / lifecycle-filtered / type-filtered
     notes never become reachable via candidate ranking, even when they
     would be the best lexical match.
  3. Deterministic total ordering, including the fully-tied (no lexical
     signal at all) case.
  4. Pagination stability: page N+1 neither duplicates nor skips relative
     to page N once ordering depends on query text.
  5. The fail-closed contract: a broken candidate generator raises instead
     of silently degrading to the old unranked behaviour.
  6. candidate_limit as a real, configurable knob.
  7. The structured per-query trace (query -> candidates considered ->
     per-generator scores -> fused score -> what entered the final context).
"""
import pytest
from uuid import uuid4

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle, Principal
from retrieval.context.candidate_generation import generate_candidates, DEFAULT_CANDIDATE_LIMIT


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_r004")


def _note(note_id, content, lifecycle=Lifecycle.ACTIVE.value, note_type="knowledge",
          tags=None, confidence="medium", verification="unverified",
          source_type="user"):
    return {
        "id": note_id,
        "type": note_type,
        "lifecycle": lifecycle,
        "category": "test",
        "tags": tags or [],
        "created": "2026-01-01",
        "updated": "2026-01-01",
        "provenance": {"source_type": source_type, "source_ref": "unit"},
        "confidence": confidence,
        "verification": verification,
        "relations": [],
        "content": content,
    }


def make_controller():
    storage = StorageEngine()
    return storage, MemoryController(storage)


# ---------------------------------------------------------------------------
# 1. Acceptance: a note beyond the old head-20 insertion-order cut is now
#    retrievable. This test FAILS on main.
# ---------------------------------------------------------------------------
def test_relevant_note_beyond_old_head20_insertion_cut_is_retrievable():
    storage, controller = make_controller()
    # 25 filler notes inserted first, none mentioning the query terms.
    for i in range(25):
        note_id = f"filler-{i:03d}"
        storage.set(note_id, _note(note_id, f"unrelated filler document number {i} about gardening"))

    target_id = "target-relevant-note"
    storage.set(target_id, _note(
        target_id,
        "MemoryController candidate generation fuses BM25 lexical scores with entity overlap ranking",
        confidence="high", verification="verified", tags=["MemoryController"],
    ))

    pack = controller.search(Principal.HUMAN, "MemoryController candidate generation BM25", page_size=50)
    ids = [r["id"] for r in pack["results"]]
    assert target_id in ids, (
        "a note relevant to the query but inserted after the old hardcoded "
        "head-20 cut must be retrievable once candidate generation reads "
        "the query text"
    )


# ---------------------------------------------------------------------------
# 2. Adversarial: excluded notes never become reachable via ranking, even
#    when they would dominate lexical/entity scoring.
# ---------------------------------------------------------------------------
def test_raw_note_never_reachable_even_as_best_lexical_match():
    storage, controller = make_controller()
    raw_id = "raw-perfect-match"
    storage.set(raw_id, _note(
        raw_id, "unique_needle_term unique_needle_term unique_needle_term",
        lifecycle=Lifecycle.RAW.value, source_type="inference", tags=["unique_needle_term"],
    ))
    active_id = "active-weak-match"
    storage.set(active_id, _note(active_id, "totally unrelated content"))

    pack = controller.search(Principal.HUMAN, "unique_needle_term", page_size=50)
    ids = [r["id"] for r in pack["results"]]
    assert raw_id not in ids

    # Stronger claim: the RAW note never even enters the fused ranking, i.e.
    # it is excluded BEFORE ranking runs, not merely hidden from the final
    # page after being ranked.
    trace = pack["candidate_trace"]
    fused_ids = {c["id"] for c in trace["fused_ranking"]}
    assert raw_id not in fused_ids
    assert raw_id not in trace["final_context_ids"]
    assert trace["candidates_considered"] == 1  # only the ACTIVE note survived the gate


def test_lifecycle_filtered_note_never_reachable_even_as_best_lexical_match():
    storage, controller = make_controller()
    superseded_id = "superseded-perfect-match"
    storage.set(superseded_id, _note(
        superseded_id, "unique_needle_term unique_needle_term unique_needle_term",
        lifecycle=Lifecycle.SUPERSEDED.value, verification="verified", tags=["unique_needle_term"],
    ))
    active_id = "active-weak-match"
    storage.set(active_id, _note(active_id, "totally unrelated content"))

    pack = controller.search(
        Principal.HUMAN, "unique_needle_term", page_size=50, lifecycles=[Lifecycle.ACTIVE]
    )
    ids = [r["id"] for r in pack["results"]]
    assert superseded_id not in ids
    trace = pack["candidate_trace"]
    assert superseded_id not in {c["id"] for c in trace["fused_ranking"]}
    assert trace["candidates_considered"] == 1


def test_type_filtered_note_never_reachable_even_as_best_lexical_match():
    storage, controller = make_controller()
    wrong_type_id = "wrong-type-perfect-match"
    storage.set(wrong_type_id, _note(
        wrong_type_id, "unique_needle_term unique_needle_term unique_needle_term",
        note_type="procedure", verification="verified", tags=["unique_needle_term"],
    ))
    right_type_id = "right-type-weak-match"
    storage.set(right_type_id, _note(right_type_id, "totally unrelated content", note_type="knowledge"))

    pack = controller.search(
        Principal.HUMAN, "unique_needle_term", page_size=50, types=["knowledge"]
    )
    ids = [r["id"] for r in pack["results"]]
    assert wrong_type_id not in ids
    trace = pack["candidate_trace"]
    assert wrong_type_id not in {c["id"] for c in trace["fused_ranking"]}
    assert trace["candidates_considered"] == 1


# ---------------------------------------------------------------------------
# 3. Deterministic total ordering, including the fully-tied case.
# ---------------------------------------------------------------------------
def test_generate_candidates_is_pure_and_repeatable():
    notes = [
        {"id": "b-note", "content": "alpha beta gamma"},
        {"id": "a-note", "content": "alpha beta gamma"},
    ]
    result1, _ = generate_candidates("alpha beta", notes, candidate_limit=10)
    result2, _ = generate_candidates("alpha beta", notes, candidate_limit=10)
    assert [n["id"] for n in result1] == [n["id"] for n in result2]
    # Identical content -> identical score -> tie broken by ascending id.
    assert [n["id"] for n in result1] == ["a-note", "b-note"]


def test_generate_candidates_all_zero_score_still_fully_ordered_by_id():
    """A query with no lexical/entity signal at all must not silently drop
    notes from the candidate set -- it must degrade to a complete,
    deterministic ordering (by id), never to an arbitrary/partial one."""
    notes = [{"id": "z-note"}, {"id": "a-note"}, {"id": "m-note"}]
    result, trace = generate_candidates("completely unrelated gibberish query", notes, candidate_limit=10)
    assert [n["id"] for n in result] == ["a-note", "m-note", "z-note"]
    assert trace.candidates_considered == 3


def test_search_ranking_is_deterministic_across_repeated_calls_bypassing_cache():
    """Exercise the full search() path twice with a fresh controller/cache
    each time (so the second call can't just be a cache hit) and confirm
    the resulting order is identical."""
    def run():
        storage, controller = make_controller()
        for i in range(12):
            note_id = f"n{i:02d}"
            storage.set(note_id, _note(note_id, f"shared term note number {i}"))
        pack = controller.search(Principal.HUMAN, "shared term", page_size=12)
        return [r["id"] for r in pack["results"]]

    assert run() == run()


# ---------------------------------------------------------------------------
# 4. Pagination stability under query-driven ordering.
# ---------------------------------------------------------------------------
def test_pagination_neither_duplicates_nor_skips_across_pages():
    storage, controller = make_controller()
    total = 37
    for i in range(total):
        note_id = f"n{i:03d}"
        storage.set(note_id, _note(note_id, f"shared query term appears in note {i}"))

    seen = []
    token = None
    page_size = 8
    for _ in range(total // page_size + 2):
        pack = controller.search(Principal.HUMAN, "shared query term", page_size=page_size, page_token=token)
        page_ids = [r["id"] for r in pack["results"]]
        seen.extend(page_ids)
        token = pack.get("next_page_token")
        if not token:
            break

    assert len(seen) == len(set(seen)), "pagination must not return the same note on two different pages"
    assert set(seen) == {f"n{i:03d}" for i in range(total)}, "pagination must not skip any note"


# ---------------------------------------------------------------------------
# 5. Fail closed: a broken candidate generator raises; it is never caught
#    and papered over with the old unranked head-N behaviour.
# ---------------------------------------------------------------------------
def test_candidate_generation_failure_propagates_instead_of_silent_fallback(monkeypatch):
    storage, controller = make_controller()
    storage.set("n1", _note("n1", "hello world"))

    import memory_controller.context.retrieval as retrieval_module

    def _boom(*args, **kwargs):
        raise RuntimeError("simulated candidate generator outage")

    monkeypatch.setattr(retrieval_module, "generate_candidates", _boom)

    with pytest.raises(RuntimeError, match="simulated candidate generator outage"):
        controller.search(Principal.HUMAN, "hello")


# ---------------------------------------------------------------------------
# 6. candidate_limit is a real, configurable recall knob.
# ---------------------------------------------------------------------------
def test_generate_candidates_respects_candidate_limit():
    notes = [{"id": f"n{i:03d}", "content": f"term{i}"} for i in range(50)]
    result, trace = generate_candidates("term", notes, candidate_limit=5)
    assert len(result) == 5
    assert trace.candidate_limit == 5


class _FixedStorage:
    """Minimal storage stub returning a fixed note list regardless of filters,
    for isolating RetrievalEngine.retrieve()'s candidate_limit handling from
    MemoryController.search()'s cache/budget machinery."""

    def __init__(self, notes):
        self._notes = notes

    def query(self, intent=None, lifecycle=None, types=None):
        return list(self._notes)


def test_retrieval_engine_honors_explicit_candidate_limit_override():
    from retrieval.context.retrieval import RetrievalEngine

    notes = [{"id": f"n{i:03d}", "content": "x"} for i in range(30)]
    engine = RetrievalEngine(_FixedStorage(notes))
    classified = {"lifecycle_filters": [], "target_types": [], "candidate_limit": 7}
    result = engine.retrieve(classified, query="x")
    assert len(result) == 7


def test_default_candidate_limit_is_no_longer_hardcoded_twenty():
    # The old hardcoded default silently capped recall at 20 regardless of
    # corpus size or budget. The new default is a named, measured constant
    # (see 07_EVALUATION/candidate_generation_measurement.py).
    assert DEFAULT_CANDIDATE_LIMIT != 20
    assert DEFAULT_CANDIDATE_LIMIT == 200


# ---------------------------------------------------------------------------
# 7. Structured per-query trace.
# ---------------------------------------------------------------------------
def test_search_pack_includes_structured_candidate_trace():
    storage, controller = make_controller()
    storage.set("n1", _note("n1", "alpha beta"))
    storage.set("n2", _note("n2", "alpha gamma"))

    pack = controller.search(Principal.HUMAN, "alpha beta", page_size=10)
    trace = pack["candidate_trace"]

    assert trace["source"] == "generated"
    assert trace["query"] == "alpha beta"
    assert trace["candidates_considered"] == 2
    assert set(trace["per_generator"].keys()) == {"bm25", "entity"}
    assert isinstance(trace["fused_ranking"], list) and trace["fused_ranking"]
    for item in trace["fused_ranking"]:
        assert {"rank", "id", "fused_score", "signals"} <= set(item.keys())
    assert trace["final_context_ids"] == [r["id"] for r in pack["results"]]


def test_candidate_trace_present_even_when_served_from_cache():
    storage, controller = make_controller()
    storage.set("n1", _note("n1", "alpha beta"))

    controller.search(Principal.HUMAN, "alpha beta", page_size=10)  # populate cache
    pack = controller.search(Principal.HUMAN, "alpha beta", page_size=10)  # cache hit

    trace = pack["candidate_trace"]
    assert trace["source"] == "cache"
    assert trace["final_context_ids"] == [r["id"] for r in pack["results"]]
