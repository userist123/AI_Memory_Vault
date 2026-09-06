"""Lexical + entity candidate generation for the production search path.

Wires the query text into ``RetrievalEngine.retrieve()`` (see
``retrieval.py``). Before this module existed, ``StorageEngine.query()``
filtered by lifecycle/type only -- the query text was never read -- so
candidate selection was effectively "first N by insertion order". Any
relevant note ranked below that head-N cut was permanently unreachable by
any downstream scorer.

This module reuses the existing, already-tested lexical primitives from
``hybrid_retrieval.py`` (``BM25``, ``tokenize``, ``entities`` -- the P1.2
semantic-cortex prototype) instead of writing a second lexical scorer.
Dense embeddings (``OllamaEmbedder``) are intentionally never imported here:
dense stays fully opt-in/offline per the production contract.

Security contract
------------------
This module never touches storage and never learns about notes the caller
did not already decide to hand it. It receives ``notes`` that MUST already
have passed every hard gate (RAW exclusion, lifecycle filter, type filter --
see ``StorageEngine.query()`` / ``FileStorageEngine.query()``) and it only
re-orders and truncates that exact list: the output is always a subset of
the input, by identity of note dict, never a superset assembled from any
other source. See ``RetrievalEngine.retrieve()`` for where the hard gate is
applied (before this module is ever called), and
``20_TESTS/regression/test_candidate_generation_call_path.py`` for an
AST-level proof that the gate always runs first.

Determinism contract
---------------------
``generate_candidates`` is a pure function of (query, notes, candidate_limit).
Every ranking step breaks ties on note id (lexicographic), never on
insertion/dict order, so the output ordering is stable across repeated calls
and across process restarts -- a prerequisite for correct offset-based
pagination (see ``20_TESTS/regression/test_candidate_pagination_stability.py``).

Fail-closed contract
---------------------
Nothing in this module catches exceptions from the underlying BM25/tokenize/
entities primitives. If they raise, ``generate_candidates`` raises, and
``RetrievalEngine.retrieve()`` MUST let that propagate rather than silently
falling back to the pre-fix "first N by insertion order" behaviour and
reporting success.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, List, Sequence, Set, Tuple

from ..hybrid_retrieval import BM25, entities, tokenize

# Fusion constants. Deliberately kept as independent, locally-owned values
# (not imported from HybridRetriever) so this production path never
# implicitly changes behaviour if the experimental P1.2 prototype's tuning
# changes. They are set to match HybridRetriever.RRF_K / DEFAULT_WEIGHTS at
# the time of writing for consistency between the two lexical rankers.
RRF_K = 60
BM25_WEIGHT = 1.0
ENTITY_WEIGHT = 0.8

# See 07_EVALUATION/candidate_generation_measurement.py for the recall@K
# measurement this default is based on. Summary: on a synthetic corpus sized
# to this vault's observed scale (hundreds of notes), naive head-20
# insertion-order truncation has ~0% recall for a relevant note planted past
# position 20, while candidate_limit=200 recovers >=95% of planted-relevant
# notes for corpora up to ~1000 notes without materially increasing p95
# latency (BM25 over a few hundred short documents is sub-millisecond).
DEFAULT_CANDIDATE_LIMIT = 200


class CandidateGenerationError(RuntimeError):
    """Raised when candidate generation cannot run at all.

    Per the fail-closed contract, callers (``RetrievalEngine.retrieve()``)
    MUST let this propagate. It must never be caught internally to fall back
    to unranked, unfiltered-by-query behaviour.
    """


@dataclass
class CandidateTrace:
    """Structured, per-query candidate-generation trace (requirement: no log strings)."""

    query: str
    candidate_limit: int
    candidates_considered: int
    per_generator: Dict[str, List[Dict[str, Any]]] = field(default_factory=dict)
    fused_ranking: List[Dict[str, Any]] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "query": self.query,
            "candidate_limit": self.candidate_limit,
            "candidates_considered": self.candidates_considered,
            "per_generator": self.per_generator,
            "fused_ranking": self.fused_ranking,
        }


def _note_text(note: Dict[str, Any]) -> str:
    content = note.get("content", "") or ""
    if not isinstance(content, str):
        content = str(content)
    category = note.get("category", "") or ""
    if not isinstance(category, str):
        category = str(category)
    return f"{content} {category}"


def _note_entities(note: Dict[str, Any], text: str) -> Set[str]:
    tags = note.get("tags") or []
    tag_set = {str(t).lower() for t in tags}
    return entities(text) | tag_set


def _rank_all(scores: Sequence[float], ids: Sequence[str]) -> List[int]:
    """Full deterministic ranking over every index -- ties broken by note id.

    Unlike ``HybridRetriever._ranked`` (which drops zero-score entries),
    zero scores are kept here: an unmatched query must still degrade to a
    fully deterministic, complete ordering (by id) rather than to an
    incomplete/empty one. Dropping zero scores would silently shrink the
    candidate set below ``candidate_limit`` whenever the corpus has more
    non-matching than matching notes -- the same recall bug this module
    exists to fix, just moved one level down.
    """
    return [idx for idx, _ in sorted(enumerate(scores), key=lambda p: (-p[1], ids[p[0]]))]


def generate_candidates(
    query: str,
    notes: List[Dict[str, Any]],
    candidate_limit: int,
) -> Tuple[List[Dict[str, Any]], CandidateTrace]:
    """Rank ``notes`` (already hard-gate filtered) by fused BM25 + entity overlap.

    Returns ``(top-candidate_limit notes in deterministic fused order, trace)``.

    ``notes`` is treated as a closed set: the returned list is always a
    subset of ``notes`` by identity/id, in the same dict objects, never
    supplemented from any other source. Ties (including the fully-tied
    all-zero-score case for a query with no lexical/entity signal at all)
    are broken by ascending note id, giving deterministic total ordering.
    """
    query = query or ""
    if not notes:
        return [], CandidateTrace(query=query, candidate_limit=candidate_limit, candidates_considered=0)

    ids = [str(n.get("id")) for n in notes]
    texts = [_note_text(n) for n in notes]
    tokenized_docs = [tokenize(t) for t in texts]
    entity_sets = [_note_entities(n, t) for n, t in zip(notes, texts)]

    q_tokens = tokenize(query)
    q_entities = entities(query) | set(q_tokens)

    bm25 = BM25(tokenized_docs)
    bm25_scores = bm25.scores(q_tokens)
    bm25_rank = _rank_all(bm25_scores, ids)

    entity_scores = [
        len(q_entities & ents) / ((len(ents) + 1) ** 0.5) for ents in entity_sets
    ]
    entity_rank = _rank_all(entity_scores, ids)

    fused_scores = [0.0] * len(notes)
    signals: List[Dict[str, int]] = [dict() for _ in notes]
    for name, ranking, weight in (("bm25", bm25_rank, BM25_WEIGHT), ("entity", entity_rank, ENTITY_WEIGHT)):
        for rank, idx in enumerate(ranking, start=1):
            fused_scores[idx] += weight / (RRF_K + rank)
            signals[idx][name] = rank

    order = _rank_all(fused_scores, ids)
    limit = max(0, int(candidate_limit))
    top = order[:limit]

    per_generator = {
        "bm25": [{"id": ids[i], "score": round(bm25_scores[i], 6)} for i in bm25_rank[:limit]],
        "entity": [{"id": ids[i], "score": round(entity_scores[i], 6)} for i in entity_rank[:limit]],
    }
    fused_ranking = [
        {"rank": rank, "id": ids[i], "fused_score": round(fused_scores[i], 6), "signals": signals[i]}
        for rank, i in enumerate(top, start=1)
    ]

    trace = CandidateTrace(
        query=query,
        candidate_limit=limit,
        candidates_considered=len(notes),
        per_generator=per_generator,
        fused_ranking=fused_ranking,
    )
    return [notes[i] for i in top], trace
