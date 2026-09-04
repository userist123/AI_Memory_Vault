from cognitive_core.multi_graph import MultiGraphMemory
from cognitive_core.spreading_activation import SpreadingActivationEngine


def _note(note_id, category="architecture", tags=None, content="", created="2026-01-01",
          relations=None):
    return {
        "id": note_id, "category": category, "tags": tags or [], "content": content,
        "created": created, "relations": relations or [],
    }


def test_semantic_graph_links_shared_tags_and_category():
    notes = [
        _note("n1", tags=["sqlite", "storage"]),
        _note("n2", tags=["sqlite", "wal"]),
        _note("n3", category="unrelated", tags=["gaming"]),
    ]
    graph_memory = MultiGraphMemory().build_from_notes(notes)
    neighbors_n1 = {n for n, _ in graph_memory.semantic.neighbors("n1")}
    assert "n2" in neighbors_n1
    assert "n3" not in neighbors_n1


def test_temporal_graph_chains_by_created_date():
    notes = [
        _note("early", created="2026-01-01"),
        _note("late", created="2026-02-01"),
    ]
    graph_memory = MultiGraphMemory().build_from_notes(notes)
    neighbors = {n for n, _ in graph_memory.temporal.neighbors("early")}
    assert "late" in neighbors


def test_causal_graph_uses_explicit_relations():
    notes = [
        _note("old", relations=[{"relation": "replaced_by", "target_id": "new"}]),
        _note("new"),
    ]
    graph_memory = MultiGraphMemory().build_from_notes(notes)
    neighbors = {n for n, _ in graph_memory.causal.neighbors("old")}
    assert "new" in neighbors


def test_entity_graph_links_shared_capitalized_entities():
    notes = [
        _note("a", content="Folosim SQLite pentru VaultEngine."),
        _note("b", content="VaultEngine ruleaza pe SQLite WAL."),
        _note("c", content="altceva complet neconectat"),
    ]
    graph_memory = MultiGraphMemory().build_from_notes(notes)
    neighbors_a = {n for n, _ in graph_memory.entity.neighbors("a")}
    assert "b" in neighbors_a
    assert "c" not in neighbors_a


def test_spreading_activation_decays_with_distance():
    notes = [_note("seed", tags=["x"]), _note("mid", tags=["x"]), _note("far", category="other")]
    graph_memory = MultiGraphMemory().build_from_notes(notes)
    graph_memory.temporal.add_edge("mid", "far", relation="precedes", weight=1.0)
    engine = SpreadingActivationEngine(graph_memory, decay=0.5, max_hops=2)
    result = engine.activate({"seed": 1.0})
    assert result.get("mid", 0) > result.get("far", 0)


def test_rank_fuses_base_scores_with_activation():
    notes = [_note("a", tags=["x"]), _note("b", tags=["x"])]
    graph_memory = MultiGraphMemory().build_from_notes(notes)
    engine = SpreadingActivationEngine(graph_memory, decay=0.5, max_hops=1)
    ranked = engine.rank({"a": 1.0})
    ranked_ids = [node_id for node_id, _ in ranked]
    assert "a" in ranked_ids and "b" in ranked_ids
    assert ranked[0][0] == "a"


def test_spreading_activation_respects_edge_weight():
    graph_memory = MultiGraphMemory()
    graph_memory.semantic.add_edge("seed", "weak", weight=0.25)
    graph_memory.semantic.add_edge("seed", "strong", weight=1.0)
    engine = SpreadingActivationEngine(graph_memory, decay=0.5, max_hops=1)

    activated = engine.activate({"seed": 1.0})

    assert activated["strong"] > activated["weak"]


def test_graph_node_types_explicit_and_default_resolution():
    notes = [
        {"id": "t1", "category": "session", "node_type": "task", "content": "run audit"},
        {"id": "f1", "category": "session", "node_type": "failure", "content": "timeout error"},
        {"id": "c1", "category": "session", "node_type": "correction", "content": "increase timeout"},
        {"id": "o1", "category": "session", "node_type": "outcome", "content": "passed"},
        {"id": "legacy_dec", "category": "architecture", "content": "use sqlite"},
        {"id": "legacy_proc", "category": "procedure", "content": "deployment guide"},
        {"id": "legacy_fact", "category": "session", "content": "generic note"},
    ]
    graph_memory = MultiGraphMemory().build_from_notes(notes)

    assert graph_memory.semantic.get_node_type("t1") == "task"
    assert graph_memory.semantic.get_node_type("f1") == "failure"
    assert graph_memory.semantic.get_node_type("c1") == "correction"
    assert graph_memory.semantic.get_node_type("o1") == "outcome"
    assert graph_memory.semantic.get_node_type("legacy_dec") == "decision"
    assert graph_memory.semantic.get_node_type("legacy_proc") == "procedure"
    assert graph_memory.semantic.get_node_type("legacy_fact") == "fact"

    # Exported dict contains node_types
    exported = graph_memory.semantic.to_dict()
    assert "node_types" in exported
    assert exported["node_types"]["t1"] == "task"


def test_invalid_node_type_raises_value_error():
    import pytest
    from cognitive_core.multi_graph import validate_node_type

    with pytest.raises(ValueError, match="Invalid node_type"):
        validate_node_type("INVALID TYPE WITH SPACES & $")
    with pytest.raises(ValueError, match="node_type cannot be empty"):
        validate_node_type("")


def test_real_vault_categories_resolve_to_valid_controlled_node_types():
    from cognitive_core.multi_graph import CONTROLLED_NODE_TYPES, resolve_node_type

    real_vault_sample_categories = [
        "architecture", "decisions", "procedures", "policy-lesson", "lessons",
        "errors", "experiences", "task", "goals", "intent", "tool", "soc-tooling",
        "knowledge", "session", "consolidated-knowledge", "audit", "security", "unknown_category"
    ]
    for cat in real_vault_sample_categories:
        node_type = resolve_node_type({"category": cat})
        assert node_type in CONTROLLED_NODE_TYPES


