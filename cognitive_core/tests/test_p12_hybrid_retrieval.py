"""P1.2 -- hybrid_retrieval.py contract tests. Fully offline (no Ollama)."""
from __future__ import annotations

from pathlib import Path

from cognitive_core.hybrid_retrieval import HybridRetriever, entities, tokenize
from cognitive_core.vault_index import Note, VaultIndex


def _index():
    notes = [
        Note(id="1", path=Path("1.md"), title="Model Tier Router",
             body="The model tier router resolves a tier to a provider factory. " * 3,
             meta={"type": "knowledge"}),
        Note(id="2", path=Path("2.md"), title="Unrelated Topic",
             body="Completely different subject about gardening and plants.",
             meta={"type": "knowledge"}),
        Note(id="3", path=Path("3.md"), title="Provider Factory Details",
             body="A provider factory builds model provider instances for a tier.",
             meta={"type": "knowledge"}),
    ]
    return VaultIndex(notes)


def test_search_ranks_relevant_note_above_unrelated():
    retr = HybridRetriever(_index())
    hits = retr.search("model tier router provider factory", top_k=3)
    assert hits
    assert hits[0].note.id in ("1", "3")
    ids = [h.note.id for h in hits]
    assert "2" not in ids[:1]


def test_search_is_deterministic_across_reruns():
    retr = HybridRetriever(_index())
    r1 = [(h.note.id, h.score) for h in retr.search("model tier router", top_k=3)]
    r2 = [(h.note.id, h.score) for h in retr.search("model tier router", top_k=3)]
    assert r1 == r2


def test_empty_query_returns_no_crash():
    retr = HybridRetriever(_index())
    hits = retr.search("", top_k=3)
    assert hits == []


def test_bm25_only_and_entity_only_are_isolated_arms():
    retr = HybridRetriever(_index())
    bm25_hits = retr.bm25_only("model tier router", top_k=3)
    entity_hits = retr.entity_only("model tier router", top_k=3)
    assert isinstance(bm25_hits, list)
    assert isinstance(entity_hits, list)


def test_dense_active_false_without_embedder():
    retr = HybridRetriever(_index())
    assert retr.dense_active is False
    assert retr.build_dense_index() is False  # no embedder configured


def test_entity_regex_no_longer_treats_generic_two_part_decimals_as_rare_entities():
    """Regression test for the false-positive found in the earlier audit: two
    documents that merely share generic decimal literals (thresholds,
    probabilities like 0.15, 0.25) must not be treated as sharing a rare
    technical entity. Real three-part version numbers still match."""
    ents = entities("threshold is 0.15, score 0.25, weight 0.6")
    assert "0.15" not in ents
    assert "0.25" not in ents
    ents_version = entities("running python 3.14.2 with pytest 9.0.2")
    assert "3.14.2" in ents_version
    assert "9.0.2" in ents_version


def test_tokenize_strips_stopwords_and_short_tokens():
    toks = tokenize("the model is a tier router and it works")
    assert "the" not in toks
    assert "model" in toks
    assert "router" in toks
