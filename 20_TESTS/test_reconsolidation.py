"""Reconsolidation lifecycle tests.

Reconsolidation rewrites *settled* memory: it takes an ACTIVE/VERIFIED note
back to RECONSOLIDATING and then re-publishes it. Before the canonical
lifecycle authority existed, `Consolidator.challenge()` and
`resolve_challenge()` wrote straight to storage with no policy evaluation and
no principal constraint, which let an AI agent silently rewrite canonical
knowledge -- the documented reconsolidation bypass.

`lifecycle.policy` now gates both halves and restricts them to HUMAN/ADMIN.
The tests below pin that closure and the surviving legitimate behavior.
"""
import pytest

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.consolidation import Consolidator
from cognitive_core.tool_router import ToolRouter
from lifecycle import policy as lifecycle_policy


@pytest.fixture
def setup_consolidator():
    storage = StorageEngine()
    mc = MemoryController(storage=storage)
    tr = ToolRouter(memory_controller=mc)
    consolidator = Consolidator(memory_controller=mc, tool_router=tr)
    return mc, consolidator, storage


def _seed_active(storage, note_id="canonical_note_123"):
    storage.set(note_id, {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "content": "Original canonical fact",
        "updated": "2026-08-20T00:00:00Z",
    })
    return note_id


def test_memory_reconsolidation_challenge_and_resolution(setup_consolidator):
    """An authorized principal can still complete the full cycle."""
    mc, consolidator, storage = setup_consolidator
    note_id = _seed_active(storage)

    conflicting_evidence = {"claim": "New contradictory finding", "source": "execution_test"}
    challenged_note = consolidator.challenge(
        note_id, conflicting_evidence, principal=Principal.HUMAN
    )

    assert challenged_note is not None
    assert challenged_note["lifecycle"] == Lifecycle.RECONSOLIDATING.value
    assert "previous_version" in challenged_note
    assert challenged_note["previous_version"]["content"] == "Original canonical fact"
    assert challenged_note["conflicting_evidence"] == conflicting_evidence

    resolved_data = {"content": "Updated canonical fact with new findings", "relations": []}
    final_note = consolidator.resolve_challenge(
        note_id, resolved_node=resolved_data, principal=Principal.HUMAN
    )

    assert final_note["lifecycle"] == Lifecycle.ACTIVE.value
    assert final_note["content"] == "Updated canonical fact with new findings"
    assert final_note["conflicting_evidence"] is None


def test_unresolved_challenge_demotes_to_review(setup_consolidator):
    """Resolving with no replacement node demotes the memory to REVIEW."""
    mc, consolidator, storage = setup_consolidator
    note_id = _seed_active(storage)

    consolidator.challenge(note_id, {"claim": "conflict"}, principal=Principal.HUMAN)
    final_note = consolidator.resolve_challenge(
        note_id, resolved_node=None, principal=Principal.HUMAN
    )

    assert final_note["lifecycle"] == Lifecycle.REVIEW.value


# ---------------------------------------------------------------------------
# Bypass closure -- these are the regression tests for the policy gate.
# ---------------------------------------------------------------------------


def test_ai_agent_cannot_challenge_canonical_memory(setup_consolidator):
    """REGRESSION: an AI agent must not be able to destabilize settled memory.

    This previously succeeded and was the reconsolidation bypass.
    """
    mc, consolidator, storage = setup_consolidator
    note_id = _seed_active(storage)

    with pytest.raises(lifecycle_policy.LifecycleViolation):
        consolidator.challenge(
            note_id, {"claim": "attacker-supplied"}, principal=Principal.AI_AGENT
        )


def test_denied_challenge_leaves_memory_untouched(setup_consolidator):
    """Fail closed: a denied challenge performs no partial write."""
    mc, consolidator, storage = setup_consolidator
    note_id = _seed_active(storage)

    with pytest.raises(lifecycle_policy.LifecycleViolation):
        consolidator.challenge(
            note_id, {"claim": "attacker-supplied"}, principal=Principal.AI_AGENT
        )

    stored = storage.get(note_id)
    assert stored["lifecycle"] == Lifecycle.ACTIVE.value
    assert stored["content"] == "Original canonical fact"
    assert "previous_version" not in stored
    assert "conflicting_evidence" not in stored


def test_ai_agent_cannot_resolve_reconsolidation(setup_consolidator):
    """REGRESSION: the resolve half is gated independently of the challenge.

    An AI agent must not be able to re-publish a note that a human took into
    RECONSOLIDATING, otherwise the bypass simply moves one step later.
    """
    mc, consolidator, storage = setup_consolidator
    note_id = _seed_active(storage)
    consolidator.challenge(note_id, {"claim": "conflict"}, principal=Principal.HUMAN)

    with pytest.raises(lifecycle_policy.LifecycleViolation):
        consolidator.resolve_challenge(
            note_id,
            resolved_node={"content": "attacker rewrite", "relations": []},
            principal=Principal.AI_AGENT,
        )

    stored = storage.get(note_id)
    assert stored["lifecycle"] == Lifecycle.RECONSOLIDATING.value
    assert stored["content"] == "Original canonical fact"


def test_default_principal_is_not_privileged(setup_consolidator):
    """Omitting the principal defaults to AI_AGENT and must therefore deny.

    The old signature defaulted to AI_AGENT and wrote anyway; the default must
    not be a way around the gate.
    """
    mc, consolidator, storage = setup_consolidator
    note_id = _seed_active(storage)

    with pytest.raises(lifecycle_policy.LifecycleViolation):
        consolidator.challenge(note_id, {"claim": "conflict"})

    assert storage.get(note_id)["lifecycle"] == Lifecycle.ACTIVE.value


def test_admin_may_drive_reconsolidation(setup_consolidator):
    """ADMIN retains the capability -- the gate narrows, it does not remove."""
    mc, consolidator, storage = setup_consolidator
    note_id = _seed_active(storage)

    challenged = consolidator.challenge(
        note_id, {"claim": "conflict"}, principal=Principal.ADMIN
    )
    assert challenged["lifecycle"] == Lifecycle.RECONSOLIDATING.value

    final = consolidator.resolve_challenge(
        note_id,
        resolved_node={"content": "admin-reviewed fact", "relations": []},
        principal=Principal.ADMIN,
    )
    assert final["lifecycle"] == Lifecycle.ACTIVE.value


def test_non_settled_states_are_not_challengeable(setup_consolidator):
    """Only ACTIVE/VERIFIED are settled enough to be reconsolidated."""
    mc, consolidator, storage = setup_consolidator
    note_id = "review_note"
    storage.set(note_id, {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.REVIEW.value,
        "content": "not settled yet",
        "updated": "2026-08-20T00:00:00Z",
    })

    # Guarded before the policy is consulted, so this returns None rather than
    # raising -- the note is simply not a reconsolidation candidate.
    assert consolidator.challenge(note_id, {"claim": "c"}, principal=Principal.HUMAN) is None
    assert storage.get(note_id)["lifecycle"] == Lifecycle.REVIEW.value
