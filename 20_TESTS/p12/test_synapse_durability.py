"""r009a — edge durability contract for decay_unused()/prune().

Since r006 the runtime graph is 301 edges: 69 declared, 163 wikilink, 69
inferred mirrors -- none ever activated (r005 found the query path doesn't
wire spreading activation in yet, so nothing has ever traversed them).
Before this change, decay_unused()/prune() treated everything that wasn't
`origin="declared"` as ephemeral, so a wikilink or an inferred mirror decayed
and pruned exactly like a machine's never-reviewed proposal -- purely for
lacking activation, never because anything judged it a bad edge. Nothing in
production calls prune() yet, but the plasticity loop (r010) will run
decay_unused()+prune() repeatedly, and would eventually sever its own input
this way.

This file pins the fix: MACHINE_PROPOSED_ORIGINS / is_durable() as the one
explicit notion of durability, exercised as a full origin x activation truth
table, plus the end-to-end "survives repeated consolidation" property the
whole task exists to guarantee.
"""
from __future__ import annotations

import pytest

from graph.synapse_store import (
    MACHINE_PROPOSED_ORIGINS,
    MIN_WEIGHT,
    PRUNE_THRESHOLD,
    Synapse,
    SynapseStore,
    WIKILINK_WEIGHT,
    is_durable,
)

DURABLE_ORIGINS = ("declared", "inferred", "wikilink")
EPHEMERAL_ORIGINS = ("proposed", "proposed_weak", "proposed_llm")
ALL_KNOWN_ORIGINS = DURABLE_ORIGINS + EPHEMERAL_ORIGINS


# ---------------------------------------------------------------------------
# is_durable(): the single source of truth, and its fail-safe default
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("origin", DURABLE_ORIGINS)
def test_is_durable_true_for_human_adjacent_origins(origin):
    assert is_durable(origin) is True


@pytest.mark.parametrize("origin", EPHEMERAL_ORIGINS)
def test_is_durable_false_for_machine_proposed_origins(origin):
    assert is_durable(origin) is False


def test_machine_proposed_origins_is_the_closed_enumerated_set():
    assert MACHINE_PROPOSED_ORIGINS == {"proposed", "proposed_weak", "proposed_llm"}


def test_is_durable_defaults_true_for_an_unrecognized_future_origin():
    """Blocklist, not allowlist: a new origin this module doesn't know about
    yet must not become silently prunable just by omission."""
    assert is_durable("some_future_origin_nobody_has_invented_yet") is True


# ---------------------------------------------------------------------------
# decay_unused(): full origin x activation truth table
# ---------------------------------------------------------------------------

# (origin, activations, expect_weight_touched_by_decay)
DECAY_MATRIX = [
    ("declared", 0, False),
    ("declared", 5, False),
    ("inferred", 0, False),
    ("inferred", 5, False),
    ("wikilink", 0, False),
    ("wikilink", 5, False),
    ("proposed", 0, True),
    ("proposed", 5, False),
    ("proposed_weak", 0, True),
    ("proposed_weak", 5, False),
    ("proposed_llm", 0, True),
    ("proposed_llm", 5, False),
]


@pytest.mark.parametrize("origin, activations, expect_touched", DECAY_MATRIX)
def test_decay_unused_origin_by_activation_truth_table(origin, activations, expect_touched):
    store = SynapseStore()
    syn = Synapse("a", "b", "related_to", weight=0.4, origin=origin)
    syn.activations = activations
    store.add(syn)

    store.decay_unused(factor=0.5)

    result = store.all()[0]
    if expect_touched:
        assert result.weight == pytest.approx(0.2), (
            f"origin={origin} activations={activations}: expected decay to halve the weight"
        )
    else:
        assert result.weight == pytest.approx(0.4), (
            f"origin={origin} activations={activations}: expected weight untouched by decay"
        )


# ---------------------------------------------------------------------------
# prune(): full origin truth table under default settings
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("origin", DURABLE_ORIGINS)
def test_default_prune_never_removes_durable_origins_even_at_zero_weight_and_reinforcements(origin):
    """The strongest form of the durability claim: even an edge already at
    minimum weight with zero reinforcements -- the worst case a real edge
    could be in -- must survive a default prune if its origin is durable."""
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", weight=MIN_WEIGHT, origin=origin))
    removed = store.prune()
    assert removed == 0
    assert len(store.all()) == 1


@pytest.mark.parametrize("origin", EPHEMERAL_ORIGINS)
def test_default_prune_removes_ephemeral_origins_once_weight_and_reinforcements_qualify(origin):
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", weight=MIN_WEIGHT, origin=origin))
    removed = store.prune()
    assert removed == 1
    assert store.all() == []


def test_keep_durable_false_removes_declared_and_wikilink_edges_too():
    """Requirement: prune() must remain able to remove anything when
    explicitly asked -- only the DEFAULT changed, not the capability."""
    store = SynapseStore()
    for origin in ALL_KNOWN_ORIGINS:
        store.add(Synapse(f"s-{origin}", f"t-{origin}", "related_to", weight=MIN_WEIGHT, origin=origin))
    removed = store.prune(keep_durable=False)
    assert removed == len(ALL_KNOWN_ORIGINS)
    assert store.all() == []


def test_reinforced_ephemeral_edge_still_protected_regardless_of_durability():
    """Pre-existing invariant, unaffected by this change: a reinforcement
    ever having happened protects an edge even if it isn't durable."""
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", weight=0.4, origin="proposed"))
    store.reinforce([("a", "b")], run_id="r1", success=True)
    store.all()[0].weight = MIN_WEIGHT  # force low without touching reinforcements
    removed = store.prune()
    assert removed == 0


# ---------------------------------------------------------------------------
# End-to-end: the property r010's plasticity loop actually depends on.
# ---------------------------------------------------------------------------

def test_never_activated_wikilink_survives_many_consolidation_cycles():
    """Acceptance-shaped regression: a never-activated wikilink edge, at its
    real ingestion weight, must survive repeated decay_unused()+prune()
    cycles indefinitely -- not just once. Before this fix, ~26 cycles were
    enough to cross the prune threshold purely from decay (see
    07_EVALUATION/r009a_prune_before_after.py); this proves that no longer
    happens at any cycle count."""
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", weight=WIKILINK_WEIGHT, origin="wikilink"))
    store.add(Synapse("c", "d", "related_to", weight=0.25, origin="inferred"))

    total_removed = 0
    for _ in range(200):  # far beyond the ~26 cycles that used to matter
        store.decay_unused()
        total_removed += store.prune()

    assert total_removed == 0
    remaining = {s.key: s.weight for s in store.all()}
    assert remaining[("a", "b", "related_to")] == pytest.approx(WIKILINK_WEIGHT)
    assert remaining[("c", "d", "related_to")] == pytest.approx(0.25)


def test_never_activated_proposal_does_not_survive_many_consolidation_cycles():
    """The mirror-image property: this task must not have made pruning
    toothless -- a never-verified machine proposal still gets cut."""
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", weight=0.4, origin="proposed_weak"))

    removed_total = 0
    for _ in range(60):
        store.decay_unused()
        removed_total += store.prune()
        if store.all() == []:
            break

    assert removed_total == 1
    assert store.all() == []
