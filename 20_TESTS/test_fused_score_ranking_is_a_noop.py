"""Ranking by `fused_score` cannot reorder anything, and that is pinned here.

`generate_candidates` returns its notes "in deterministic fused order" — sorted
by `(-fused_score, id)`. The production default arm, `RANKING_ARM_FUSED_SCORE`,
then sorts that same list by `(fused_score, id)` with `reverse=True`. The list is
already in that order, so the sort is a no-op: on benchmark v3 the sabotage
control, which replaces the sort key with a constant, changed 0 of 160 returned
pages under this arm while changing 154 under `baseline`.

Two things follow, and both are worth a test rather than a note in a report:

1. r025's gain came from *ceasing to apply* `RelevanceScorer`'s key, not from
   applying a new one. The in-code comment reads as though `fused_score` does
   something; it does not.
2. The returned page is the fusion top-k. There is no reranking step in the
   pipeline at all, which is the premise of any reranker proposal.

The one place theory and measurement part company is ties: `_rank_all` breaks
them ascending by id, `_ranking_key_fn` descending. No returned page in the
benchmark exposed that, and `test_a_tie_is_the_one_case_where_the_two_keys_differ`
below records it as a known, unexercised difference rather than hiding it.
"""
from __future__ import annotations

import os
import sys
import uuid
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "x" * 40)

from memory_controller import controller as controller_mod  # noqa: E402
from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import (  # noqa: E402
    RANKING_ARM_BASELINE,
    RANKING_ARM_FUSED_SCORE,
    MemoryController,
)
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine  # noqa: E402
from retrieval.context.candidate_generation import generate_candidates  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

QUERY = "lifecycle policy for agent retrieval"

BODIES = {
    "strong": "lifecycle policy governs agent retrieval and the lifecycle floor for retrieval",
    "medium": "agent retrieval notes mention policy once",
    "weak": "an unrelated note about trading ledgers",
    "empty": "nothing in common",
}


@pytest.fixture
def controller():
    storage = SQLiteStorageEngine(":memory:")
    ctrl = MemoryController(storage=storage, index=VaultIndex([]), enable_graph_expansion=False)
    for name, body in BODIES.items():
        note_id = str(uuid.uuid4())
        ctrl.propose(Principal.AI_AGENT, {
            "id": note_id, "type": "knowledge", "category": "ranking-noop-test",
            "tags": [name], "created": "2026-10-07", "updated": "2026-10-07",
            "provenance": {"source_type": "execution", "source_ref": "test"},
            "confidence": "high", "verification": "unverified", "relations": [],
            "content": body,
        })
        # RAW -> REVIEW -> attested -> ACTIVE: the policy permits no shortcut,
        # and a note has to be ACTIVE to be ranked at all.
        ctrl.review(Principal.HUMAN, note_id, "approve", "test fixture")
        ctrl.attest(Principal.HUMAN, note_id, "test fixture", "this test")
        ctrl.promote(Principal.HUMAN, note_id)
    return ctrl


def page(ctrl: MemoryController, arm: str, principal=Principal.HUMAN) -> list[str]:
    pack = ctrl.search(principal, QUERY, page_size=4, ranking_arm=arm,
                       enable_graph_expansion=False)
    return [r.get("id") for r in pack.get("results", []) if r.get("id")]


def page_with_a_flattened_key(ctrl: MemoryController, arm: str) -> list[str]:
    """The sabotage control: a constant key, which a stable sort leaves alone."""
    original = controller_mod._ranking_key_fn
    controller_mod._ranking_key_fn = lambda *a, **k: (lambda n: 0)
    try:
        return page(ctrl, arm)
    finally:
        controller_mod._ranking_key_fn = original


def test_candidate_generation_already_returns_fused_order(controller):
    """The premise, checked directly rather than inferred from the arm's behaviour."""
    notes = controller.storage.all_notes()
    assert notes, "the fixture stored nothing, so the premise cannot be checked"
    ranked, trace = generate_candidates(QUERY, notes, 200)
    returned_ids = [n.get("id") for n in ranked]
    fused_ids = [e["id"] for e in trace.to_dict()["fused_ranking"]][:len(returned_ids)]
    assert returned_ids == fused_ids, "candidates are not returned in fusion order"


def test_sorting_by_fused_score_changes_nothing(controller):
    real = page(controller, RANKING_ARM_FUSED_SCORE)
    flattened = page_with_a_flattened_key(controller, RANKING_ARM_FUSED_SCORE)
    assert real, "the page is empty, so the comparison would prove nothing"
    assert real == flattened, (
        "the production arm reordered the page; the no-op finding no longer holds")


def test_the_baseline_arm_does_reorder(controller):
    """The counter-test. Without it, a broken harness would pass the test above."""
    assert page(controller, RANKING_ARM_BASELINE) != \
        page_with_a_flattened_key(controller, RANKING_ARM_BASELINE)


def test_a_tie_is_the_one_case_where_the_two_keys_differ():
    """Recorded as a known difference, unexercised by the benchmark.

    `_rank_all` orders ties ascending by id; `_ranking_key_fn` with
    `reverse=True` orders them descending. Equal fused scores would therefore
    come out reversed. No page in benchmark v3 exposed this, so the no-op
    finding is empirical on 160 cases plus the code argument — not a proof for
    every possible input, and this test says so in executable form.
    """
    key = controller_mod._ranking_key_fn(
        RANKING_ARM_FUSED_SCORE, {}, {"a": 1.0, "b": 1.0}, {})
    tied = [{"id": "a"}, {"id": "b"}]
    assert [n["id"] for n in sorted(tied, key=key, reverse=True)] == ["b", "a"]
    assert [n["id"] for n in tied] == ["a", "b"], "generation would have kept a before b"
