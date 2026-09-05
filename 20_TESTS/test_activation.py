import time
import pytest
from cognitive_core.activation import (
    base_level_activation,
    ActivationRecord,
    ActivationTracker,
    DORMANT_THRESHOLD
)
from packages.graph.multi_graph import Graph, MultiGraphMemory
from packages.graph.spreading_activation import SpreadingActivationEngine

def test_base_level_activation_decay_monotonicity():
    now = time.time()
    # High frequency access recently
    recent_accesses = [now - 1.0, now - 0.5, now - 0.1]
    act_recent = base_level_activation(recent_accesses, decay=0.5, current_time=now)

    # Access far in the past
    old_accesses = [now - 100.0, now - 200.0, now - 300.0]
    act_old = base_level_activation(old_accesses, decay=0.5, current_time=now)

    assert act_recent > act_old, "Recent access must yield higher activation score than old access"

def test_activation_record_dormancy():
    record = ActivationRecord("test_note_1")
    now = time.time()
    
    # Record access long ago
    record.record_access(now - 10000.0)
    assert record.is_dormant(threshold=DORMANT_THRESHOLD, current_time=now) is True

    # Record recent access
    record.record_access(now)
    assert record.is_dormant(threshold=DORMANT_THRESHOLD, current_time=now) is False

def test_activation_tracker_singleton():
    tracker1 = ActivationTracker.get_instance()
    tracker2 = ActivationTracker.get_instance()
    assert tracker1 is tracker2

    tracker1.record_access("note_abc", time.time())
    assert tracker2.get_activation("note_abc") > DORMANT_THRESHOLD


def _engine_with_single_semantic_edge(weight: float) -> SpreadingActivationEngine:
    graph = Graph("semantic")
    graph.add_edge("seed", "target", weight=weight)
    multi_graph = MultiGraphMemory()
    multi_graph.semantic = graph
    return SpreadingActivationEngine(
        multi_graph,
        decay=0.6,
        max_hops=1,
        graph_weights={"semantic": 1.0, "temporal": 0.0, "causal": 0.0, "entity": 0.0},
    )


def test_spreading_activation_respects_internal_edge_weight():
    weak = _engine_with_single_semantic_edge(0.25).activate({"seed": 1.0})
    strong = _engine_with_single_semantic_edge(0.75).activate({"seed": 1.0})

    assert weak["target"] == pytest.approx(0.15)
    assert strong["target"] == pytest.approx(0.45)
    assert strong["target"] > weak["target"]


def test_spreading_activation_caps_edge_weight_above_one():
    capped = _engine_with_single_semantic_edge(4.0).activate({"seed": 1.0})
    unit = _engine_with_single_semantic_edge(1.0).activate({"seed": 1.0})

    assert capped["target"] == pytest.approx(unit["target"])
