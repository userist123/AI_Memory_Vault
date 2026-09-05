"""P1.2 Semantic Synaptogenesis -- synapse_store.py contract tests.

Owner: claude-code. Runs fully offline, no Ollama required. Never touches
memory_controller/**, cognitive_core/tool_router.py, or any canonical
Markdown/frontmatter -- SynapseStore only ever reads/writes its own derived
JSON artifact under a pytest tmp_path.
"""
from __future__ import annotations

import json

import pytest

from cognitive_core.synapse_store import (
    ALLOWED_RELATIONS,
    InvalidSynapseError,
    MAX_WEIGHT,
    MIN_WEIGHT,
    STRONG_RELATIONS,
    Synapse,
    SynapseStore,
    WEAK_RELATIONS,
)


# ---------- src/dst, self-loop, relation enum, weight bounds ----------

def test_self_loop_rejected():
    with pytest.raises(InvalidSynapseError):
        Synapse("a", "a", "related_to", 0.4).validate()


def test_relation_not_in_enum_rejected():
    with pytest.raises(InvalidSynapseError):
        Synapse("a", "b", "made_up_relation", 0.4).validate()


@pytest.mark.parametrize("relation", sorted(ALLOWED_RELATIONS))
def test_every_enum_relation_is_accepted(relation):
    Synapse("a", "b", relation, 0.4).validate()  # must not raise


def test_weight_below_min_rejected():
    with pytest.raises(InvalidSynapseError):
        Synapse("a", "b", "related_to", MIN_WEIGHT - 0.01).validate()


def test_weight_above_max_rejected():
    with pytest.raises(InvalidSynapseError):
        Synapse("a", "b", "related_to", MAX_WEIGHT + 0.01).validate()


def test_weight_at_exact_bounds_accepted():
    Synapse("a", "b", "related_to", MIN_WEIGHT).validate()
    Synapse("a", "b", "related_to", MAX_WEIGHT).validate()


def test_non_numeric_weight_rejected():
    with pytest.raises(InvalidSynapseError):
        Synapse("a", "b", "related_to", "0.4").validate()  # type: ignore[arg-type]


def test_bool_weight_rejected():
    # bool is a subclass of int in Python; must not sneak past the numeric check.
    with pytest.raises(InvalidSynapseError):
        Synapse("a", "b", "related_to", True).validate()  # type: ignore[arg-type]


def test_missing_endpoints_rejected():
    with pytest.raises(InvalidSynapseError):
        Synapse("", "b", "related_to", 0.4).validate()
    with pytest.raises(InvalidSynapseError):
        Synapse("a", "", "related_to", 0.4).validate()


def test_store_add_raises_for_self_loop_not_silently_dropped():
    store = SynapseStore()
    with pytest.raises(InvalidSynapseError):
        store.add(Synapse("x", "x", "related_to", 0.4))


# ---------- duplicate handling ----------

def test_duplicate_triple_merges_not_duplicates():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.3))
    store.add(Synapse("a", "b", "related_to", 0.7))
    all_syn = store.all()
    assert len(all_syn) == 1
    assert all_syn[0].weight == pytest.approx(0.7)  # merge takes the max


def test_same_endpoints_different_relation_are_distinct_edges():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.4))
    store.add(Synapse("a", "b", "depends_on", 0.9))
    assert len(store.all()) == 2


def test_from_index_deduplicates_and_skips_self_loop():
    class FakeNote:
        def __init__(self, note_id, targets):
            self.id = note_id
            self._targets = targets

        def relations(self):
            return [{"target_id": t, "type": "related_to"} for t in self._targets]

    class FakeIndex:
        def __init__(self, notes):
            self.notes = notes
            self.by_id = {n.id: n for n in notes}

    notes = [FakeNote("a", ["b", "a"]), FakeNote("b", [])]  # "a" self-references itself
    idx = FakeIndex(notes)
    store = SynapseStore.from_index(idx, symmetric_weak=False)
    keys = [s.key for s in store.all()]
    assert ("a", "a", "related_to") not in keys
    assert ("a", "b", "related_to") in keys
    assert len(keys) == 1


# ---------- persistence round-trip + malformed persistence ----------

def test_persistence_round_trip(tmp_path):
    store = SynapseStore()
    store.add(Synapse("a", "b", "depends_on", 0.9, origin="declared", evidence=["run-1"]))
    path = store.save(tmp_path / "synapses.json")
    reloaded = SynapseStore.load(path)
    assert reloaded.degree_stats() == store.degree_stats()
    syn = reloaded.all()[0]
    assert syn.source_id == "a" and syn.target_id == "b" and syn.relation == "depends_on"
    assert syn.evidence == ["run-1"]


def test_load_missing_file_returns_empty_store(tmp_path):
    store = SynapseStore.load(tmp_path / "does_not_exist.json")
    assert store.all() == []


def test_load_unreadable_json_does_not_raise(tmp_path):
    path = tmp_path / "corrupt.json"
    path.write_text("{not valid json::", encoding="utf-8")
    store = SynapseStore.load(path)
    assert store.all() == []
    assert store.rejected_on_load()


def test_load_skips_malformed_records_without_crashing(tmp_path):
    path = tmp_path / "synapses.json"
    payload = {
        "version": 1,
        "synapses": [
            {"source_id": "a", "target_id": "b", "relation": "depends_on", "weight": 0.9},
            {"source_id": "a", "target_id": "a", "relation": "depends_on", "weight": 0.9},  # self-loop
            {"source_id": "c", "target_id": "d", "relation": "not_a_relation", "weight": 0.4},  # bad relation
            {"source_id": "e", "target_id": "f", "relation": "related_to", "weight": 999.0},  # out of bounds
            {"source_id": "g", "target_id": "h"},  # missing relation/weight -> uses dataclass defaults, valid
        ],
    }
    path.write_text(json.dumps(payload), encoding="utf-8")
    store = SynapseStore.load(path)
    ok_keys = {s.key for s in store.all()}
    assert ("a", "b", "depends_on") in ok_keys
    assert ("g", "h", "related_to") in ok_keys
    assert len(store.all()) == 2
    assert len(store.rejected_on_load()) == 3


def test_load_ignores_unknown_extra_fields(tmp_path):
    path = tmp_path / "synapses.json"
    payload = {"version": 1, "synapses": [
        {"source_id": "a", "target_id": "b", "relation": "related_to", "weight": 0.4,
         "future_field_from_a_newer_schema": "should be ignored, not crash"},
    ]}
    path.write_text(json.dumps(payload), encoding="utf-8")
    store = SynapseStore.load(path)
    assert len(store.all()) == 1


# ---------- reinforce / decay / prune / weighted spreading / hop decay ----------

def test_reinforce_only_touches_edges_in_observed_pairs():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.4))
    store.add(Synapse("x", "y", "related_to", 0.4))
    touched = store.reinforce([("a", "b")], run_id="r1", success=True)
    assert touched == 1
    a_b = next(s for s in store.all() if s.key == ("a", "b", "related_to"))
    x_y = next(s for s in store.all() if s.key == ("x", "y", "related_to"))
    assert a_b.weight > 0.4
    assert x_y.weight == pytest.approx(0.4)


def test_reinforce_success_increases_and_failure_decreases():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.4))
    store.reinforce([("a", "b")], run_id="r1", success=True)
    w_after_success = store.all()[0].weight
    assert w_after_success > 0.4

    store2 = SynapseStore()
    store2.add(Synapse("a", "b", "related_to", 0.4))
    store2.reinforce([("a", "b")], run_id="r1", success=False)
    w_after_failure = store2.all()[0].weight
    assert w_after_failure < 0.4


def test_reinforce_never_exceeds_max_weight():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.4))
    for i in range(200):
        store.reinforce([("a", "b")], run_id=f"r{i}", success=True, rate=0.5)
    assert store.all()[0].weight <= MAX_WEIGHT


def test_reinforce_never_drops_below_min_weight():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.4))
    for i in range(200):
        store.reinforce([("a", "b")], run_id=f"r{i}", success=False, rate=0.9)
    assert store.all()[0].weight >= MIN_WEIGHT


def test_reinforce_distinguishes_2_vs_20_confirmations_until_saturation():
    """Required by spec: a 2x-confirmed edge and a 20x-confirmed edge must
    remain numerically distinguishable, until they saturate near the ceiling."""
    store2 = SynapseStore()
    store2.add(Synapse("a", "b", "related_to", 0.4))
    for i in range(2):
        store2.reinforce([("a", "b")], run_id=f"r{i}", success=True, rate=0.15)
    w2 = store2.all()[0].weight

    store20 = SynapseStore()
    store20.add(Synapse("a", "b", "related_to", 0.4))
    for i in range(20):
        store20.reinforce([("a", "b")], run_id=f"r{i}", success=True, rate=0.15)
    w20 = store20.all()[0].weight

    assert w20 > w2  # strictly more reinforced
    assert w2 < MAX_WEIGHT * 0.9   # 2 confirmations: nowhere near saturated
    assert w20 > MAX_WEIGHT * 0.9  # 20 confirmations: essentially saturated
    assert w20 - w2 > 0.01  # not collapsed into indistinguishable floats


def test_decay_unused_only_affects_non_declared_edges_with_zero_activations():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.4, origin="declared"))
    store.add(Synapse("c", "d", "related_to", 0.4, origin="proposed"))
    store.decay_unused(factor=0.5)
    declared = next(s for s in store.all() if s.key == ("a", "b", "related_to"))
    proposed = next(s for s in store.all() if s.key == ("c", "d", "related_to"))
    assert declared.weight == pytest.approx(0.4)  # untouched
    assert proposed.weight == pytest.approx(0.2)  # halved


def test_prune_never_removes_declared_edges():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.01, origin="declared"))
    removed = store.prune(threshold=0.5)
    assert removed == 0
    assert len(store.all()) == 1


def test_prune_removes_weak_unreinforced_proposed_edges():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.01, origin="proposed"))
    removed = store.prune(threshold=0.5)
    assert removed == 1
    assert store.all() == []


def test_prune_keeps_reinforced_edges_even_if_below_threshold():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.4, origin="proposed"))
    store.reinforce([("a", "b")], run_id="r1", success=True)
    store.all()[0].weight = 0.01  # force it low without going through reinforcements=0 path
    removed = store.prune(threshold=0.5)
    assert removed == 0  # reinforcements > 0 protects it


def test_spread_hop_decay_reduces_activation_with_distance():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 1.0))
    store.add(Synapse("b", "c", "related_to", 1.0))
    activation = store.spread({"a": 1.0}, decay=0.5, max_hops=2)
    assert activation["a"] == 1.0
    assert 0 < activation["b"] < activation["a"]
    assert "c" in activation
    assert activation["c"] < activation["b"]


def test_spread_respects_max_hops():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 1.0))
    store.add(Synapse("b", "c", "related_to", 1.0))
    store.add(Synapse("c", "d", "related_to", 1.0))
    activation = store.spread({"a": 1.0}, decay=0.9, max_hops=1)
    assert "b" in activation
    assert "c" not in activation  # 2 hops away, max_hops=1
    assert "d" not in activation


def test_spread_is_deterministic_across_reruns():
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.9))
    store.add(Synapse("a", "c", "related_to", 0.9))
    store.add(Synapse("b", "d", "related_to", 0.9))
    store.add(Synapse("c", "d", "related_to", 0.9))
    r1 = store.spread({"a": 1.0}, max_hops=3)
    r2 = store.spread({"a": 1.0}, max_hops=3)
    assert r1 == r2


def test_weight_contract_is_single_and_coherent():
    """0 <= weight <= MAX_WEIGHT, not [0, 1] -- guards against reintroducing a
    stray probability assumption anywhere downstream."""
    assert MIN_WEIGHT == 0.0
    assert MAX_WEIGHT > 1.0
    assert STRONG_RELATIONS.isdisjoint(WEAK_RELATIONS)
    assert STRONG_RELATIONS | WEAK_RELATIONS == ALLOWED_RELATIONS
