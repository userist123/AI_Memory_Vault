"""evaluation/retrieval_fusion/adapters.py — Real Multi-Signal Retrieval Adapters.

Encapsulates four discrete candidate generation strategies using REAL repository classes:
  - R1: Semantic token overlap & confidence scoring (memory_controller/context/relevance_scoring.py: RelevanceScorer)
  - R2: Semantic + Lexical Okapi BM25 (memory_controller/financial_search.py: BM25Ranker)
  - R3: Semantic + Lexical + Entity Anchor Boosting (entity tag extraction)
  - R4: Semantic + Lexical + Entity + Graph Neighbor Expansion (cognitive_core/multi_graph.py: MultiGraphMemory)

Adheres strictly to the invariant of using real components without monkeypatching or simulation.
"""
from __future__ import annotations

import os
import re
from typing import Any, Dict, List, Set, Tuple

from cognitive_core.multi_graph import MultiGraphMemory
from memory_controller.context.relevance_scoring import RelevanceScorer
from memory_controller.financial_search import BM25Ranker


class RetrievalSignalStatus:
    SEMANTIC = "AVAILABLE"
    LEXICAL = "AVAILABLE"
    ENTITY = "AVAILABLE"
    GRAPH = "AVAILABLE"


class RetrievalAdapter:
    """Orchestrates R1, R2, R3, R4 retrieval layers over real note objects."""

    def __init__(self, notes: List[Dict[str, Any]]):
        self.notes = notes
        self.notes_by_id: Dict[str, Dict[str, Any]] = {n["id"]: n for n in notes if "id" in n}
        self.semantic_scorer = RelevanceScorer()
        self.bm25_ranker = BM25Ranker()
        self.multi_graph = MultiGraphMemory().build_from_notes(notes)

    def retrieve_r1_semantic(self, query: str, top_k: int = 5) -> List[Dict[str, Any]]:
        """R1: Semantic only via RelevanceScorer (token overlap + confidence)."""
        scored = self.semantic_scorer.score(query, self.notes)
        top_ids = [s["id"] for s in scored[:top_k]]
        return [self.notes_by_id[nid] for nid in top_ids if nid in self.notes_by_id]

    def retrieve_r2_semantic_lexical(self, query: str, top_k: int = 5) -> List[Dict[str, Any]]:
        """R2: Semantic + Okapi BM25 ranking via Reciprocal Rank Fusion (RRF)."""
        # 1. Semantic ranks
        sem_scored = self.semantic_scorer.score(query, self.notes)
        sem_ranks = {s["id"]: rank for rank, s in enumerate(sem_scored, 1)}

        # 2. BM25 ranks
        bm25_scores = self.bm25_ranker.score_corpus(query, self.notes)
        indexed_bm25 = sorted(
            [(self.notes[i]["id"], bm25_scores[i]) for i in range(len(self.notes))],
            key=lambda x: x[1],
            reverse=True,
        )
        bm25_ranks = {nid: rank for rank, (nid, _) in enumerate(indexed_bm25, 1)}

        # 3. Reciprocal Rank Fusion
        rrf_scores: Dict[str, float] = {}
        for nid in self.notes_by_id:
            r_sem = sem_ranks.get(nid, 999)
            r_bm25 = bm25_ranks.get(nid, 999)
            rrf_scores[nid] = (1.0 / (60.0 + r_sem)) + (1.0 / (60.0 + r_bm25))

        sorted_ids = sorted(rrf_scores.keys(), key=lambda k: rrf_scores[k], reverse=True)
        return [self.notes_by_id[nid] for nid in sorted_ids[:top_k] if nid in self.notes_by_id]

    def retrieve_r3_semantic_lexical_entity(
        self, query: str, top_k: int = 5, query_entities: List[str] = None
    ) -> List[Dict[str, Any]]:
        """R3: Semantic + BM25 + Entity Anchor / Tag Matching."""
        # Start with R2 candidate ranking
        sem_scored = self.semantic_scorer.score(query, self.notes)
        sem_ranks = {s["id"]: rank for rank, s in enumerate(sem_scored, 1)}

        bm25_scores = self.bm25_ranker.score_corpus(query, self.notes)
        indexed_bm25 = sorted(
            [(self.notes[i]["id"], bm25_scores[i]) for i in range(len(self.notes))],
            key=lambda x: x[1],
            reverse=True,
        )
        bm25_ranks = {nid: rank for rank, (nid, _) in enumerate(indexed_bm25, 1)}

        # Entity bonus
        q_lower = query.lower()
        query_toks = set(re.findall(r"[a-zA-Z0-9_\-]+", q_lower))
        if query_entities:
            for e in query_entities:
                query_toks.update(re.findall(r"[a-zA-Z0-9_\-]+", e.lower()))

        entity_bonuses: Dict[str, float] = {}
        for nid, note in self.notes_by_id.items():
            tags = [str(t).lower() for t in note.get("tags", [])]
            tag_matches = sum(1 for t in tags if any(qt in t for qt in query_toks))
            entity_bonuses[nid] = tag_matches * 0.005

        # RRF + Entity boost
        r3_scores: Dict[str, float] = {}
        for nid in self.notes_by_id:
            r_sem = sem_ranks.get(nid, 999)
            r_bm25 = bm25_ranks.get(nid, 999)
            rrf = (1.0 / (60.0 + r_sem)) + (1.0 / (60.0 + r_bm25))
            r3_scores[nid] = rrf + entity_bonuses.get(nid, 0.0)

        sorted_ids = sorted(r3_scores.keys(), key=lambda k: r3_scores[k], reverse=True)
        return [self.notes_by_id[nid] for nid in sorted_ids[:top_k] if nid in self.notes_by_id]

    def retrieve_r4_full_fusion_graph(
        self, query: str, top_k: int = 5, query_entities: List[str] = None
    ) -> List[Dict[str, Any]]:
        """R4: R3 + 1-hop Relational Graph Expansion from seed candidates."""
        # 1. Get seed candidates from R3 (top 3)
        seed_candidates = self.retrieve_r3_semantic_lexical_entity(
            query=query, top_k=3, query_entities=query_entities
        )
        candidate_ids = [n["id"] for n in seed_candidates]

        # 2. Expand neighbors across MultiGraph (semantic and entity graphs)
        expanded_ids: Set[str] = set(candidate_ids)
        for nid in candidate_ids:
            # Semantic graph neighbors
            for neighbor_id, _ in self.multi_graph.semantic.neighbors(nid):
                expanded_ids.add(neighbor_id)
            # Entity graph neighbors
            for neighbor_id, _ in self.multi_graph.entity.neighbors(nid):
                expanded_ids.add(neighbor_id)

        # 3. Re-score the expanded set using R3 scoring
        sem_scored = self.semantic_scorer.score(query, self.notes)
        sem_ranks = {s["id"]: rank for rank, s in enumerate(sem_scored, 1)}

        bm25_scores = self.bm25_ranker.score_corpus(query, self.notes)
        indexed_bm25 = sorted(
            [(self.notes[i]["id"], bm25_scores[i]) for i in range(len(self.notes))],
            key=lambda x: x[1],
            reverse=True,
        )
        bm25_ranks = {nid: rank for rank, (nid, _) in enumerate(indexed_bm25, 1)}

        r4_scores: Dict[str, float] = {}
        for nid in expanded_ids:
            if nid not in self.notes_by_id:
                continue
            r_sem = sem_ranks.get(nid, 999)
            r_bm25 = bm25_ranks.get(nid, 999)
            rrf = (1.0 / (60.0 + r_sem)) + (1.0 / (60.0 + r_bm25))
            # Give slight graph discovery weight
            graph_boost = 0.003 if nid not in candidate_ids else 0.006
            r4_scores[nid] = rrf + graph_boost

        sorted_ids = sorted(r4_scores.keys(), key=lambda k: r4_scores[k], reverse=True)
        return [self.notes_by_id[nid] for nid in sorted_ids[:top_k] if nid in self.notes_by_id]
