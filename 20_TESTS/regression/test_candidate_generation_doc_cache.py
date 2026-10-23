"""The per-note token cache in candidate generation changes speed, never results.

`generate_candidates` memoizes `tokenize` / `entities` of each note text by
(function, text). These tests pin the three ways such a cache could lie:
returning results that differ from an uncached run, serving an edited note's
old tokens, and serving one tokenizer's results to another (the tokenizer
experiment swaps `tokenize` at module level).
"""
from __future__ import annotations

import pytest

import retrieval.context.candidate_generation as cg
from retrieval.hybrid_retrieval import entities, tokenize


def _notes():
    return [
        {"id": "n-a", "content": "Științific: regăsire BM25 pentru notele vaultului", "category": "knowledge", "tags": ["RAG"]},
        {"id": "n-b", "content": "Deploy pipeline v1.2.3 with Docker and RFC 7", "category": "procedure", "tags": []},
        {"id": "n-c", "content": "Casa3D planșe și cote tastate", "category": "project", "tags": ["casa3d"]},
        {"id": "n-d", "content": "unrelated text about gardening", "category": "misc", "tags": None},
    ]


def _uncached(query, notes, limit, monkeypatch):
    monkeypatch.setattr(cg, "_doc_tokens", lambda fn, text: tuple(fn(text)))
    monkeypatch.setattr(cg, "_doc_entities", lambda fn, text: frozenset(fn(text)))
    return cg.generate_candidates(query, notes, limit)


@pytest.fixture(autouse=True)
def _clear_cache():
    cg._doc_tokens.cache_clear()
    cg._doc_entities.cache_clear()
    yield
    cg._doc_tokens.cache_clear()
    cg._doc_entities.cache_clear()


@pytest.mark.parametrize("query", ["regasire stiintifica", "Docker RFC 7", "casa3d cote", "nothing matches xyz", ""])
def test_cached_and_uncached_runs_are_identical(query, monkeypatch):
    cached_top, cached_trace = cg.generate_candidates(query, _notes(), 3)
    # second call is served from the cache
    again_top, again_trace = cg.generate_candidates(query, _notes(), 3)
    with monkeypatch.context() as m:
        plain_top, plain_trace = _uncached(query, _notes(), 3, m)
    ids = lambda notes: [n["id"] for n in notes]
    assert ids(cached_top) == ids(again_top) == ids(plain_top)
    assert cached_trace.to_dict() == again_trace.to_dict() == plain_trace.to_dict()


def test_the_cache_is_actually_used():
    cg.generate_candidates("docker", _notes(), 3)
    misses = cg._doc_tokens.cache_info().misses
    cg.generate_candidates("casa3d", _notes(), 3)
    info = cg._doc_tokens.cache_info()
    assert info.misses == misses  # same note texts: no new tokenization
    assert info.hits >= len(_notes())


def test_an_edited_note_is_retokenized():
    notes = _notes()
    _, trace = cg.generate_candidates("gardening", notes, 4)
    bm25 = {i: s["bm25"] for i, s in trace.raw_scores_by_signal.items()}
    assert bm25["n-d"] > 0 and bm25["n-b"] == 0
    # move the word from n-d to n-b: a stale cache would keep the old scores
    notes[3] = dict(notes[3], content="kubernetes operators")
    notes[1] = dict(notes[1], content="gardening tips for spring")
    _, trace = cg.generate_candidates("gardening", notes, 4)
    bm25 = {i: s["bm25"] for i, s in trace.raw_scores_by_signal.items()}
    assert bm25["n-b"] > 0 and bm25["n-d"] == 0


def test_a_tokenizer_patched_at_module_level_is_honoured(monkeypatch):
    cg.generate_candidates("docker", _notes(), 4)  # warm the cache with the production tokenizer
    seen = []

    def spy(text):
        seen.append(text)
        return ["spy"]

    monkeypatch.setattr(cg, "tokenize", spy)
    _, trace = cg.generate_candidates("docker", _notes(), 4)
    assert len(seen) == len(_notes()) + 1  # every note and the query went through the patched function
    assert all(v["bm25"] == trace.raw_scores_by_signal["n-a"]["bm25"] for v in trace.raw_scores_by_signal.values())


def test_cached_values_are_immutable():
    text = cg._note_text(_notes()[1])
    assert isinstance(cg._doc_tokens(tokenize, text), tuple)
    assert isinstance(cg._doc_entities(entities, text), frozenset)
    assert list(cg._doc_tokens(tokenize, text)) == tokenize(text)
    assert set(cg._doc_entities(entities, text)) == entities(text)


def test_exceptions_propagate_and_are_not_cached(monkeypatch):
    calls = {"n": 0}

    def boom(text):
        calls["n"] += 1
        raise RuntimeError("tokenizer failed")

    monkeypatch.setattr(cg, "tokenize", boom)
    with pytest.raises(RuntimeError):
        cg.generate_candidates("q", _notes(), 2)
    with pytest.raises(RuntimeError):
        cg.generate_candidates("q", _notes(), 2)
    assert calls["n"] == 2  # the failure was recomputed, not served from a cache


def test_an_entities_function_patched_at_module_level_is_honoured(monkeypatch):
    # review follow-up: a cache keyed by text only would serve the production entity sets here
    cg.generate_candidates("docker", _notes(), 4)  # warm the cache with the production `entities`
    monkeypatch.setattr(cg, "entities", lambda text: {"only-this"})
    _, trace = cg.generate_candidates("only-this", _notes(), 4)
    entity_scores = {i: s["entity"] for i, s in trace.raw_scores_by_signal.items()}
    assert all(v > 0 for v in entity_scores.values()), entity_scores


def test_the_memory_controller_copy_has_its_own_honoured_cache(monkeypatch):
    # MemoryController calls the shim's second copy of this module; the tokenizer experiment patches both
    import memory_controller.context.candidate_generation as mc_cg

    assert mc_cg is not cg
    plain_top, _ = mc_cg.generate_candidates("docker", _notes(), 4)
    monkeypatch.setattr(mc_cg, "tokenize", lambda text: ["same"])
    _, trace = mc_cg.generate_candidates("same", _notes(), 4)
    bm25 = {s["bm25"] for s in trace.raw_scores_by_signal.values()}
    assert len(bm25) == 1  # every note tokenized by the patched function scores identically
    monkeypatch.undo()
    again_top, _ = mc_cg.generate_candidates("docker", _notes(), 4)
    assert [n["id"] for n in again_top] == [n["id"] for n in plain_top]
