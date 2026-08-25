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
