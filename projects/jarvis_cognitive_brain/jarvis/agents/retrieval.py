"""
Milestone 3: Retrieval Agent (Associative Recall, CTE Lineage Traversal, Read-Only Scoping).
"""

import time
import math
import re
from typing import Dict, Any, List, Optional, Union
from jarvis.llm.base import BaseLLMProvider, CancellationToken
from jarvis.agents.base import BaseAgent
from jarvis.agents.models import (
    AgentRole,
    RetrievalQuery,
    ScoredMemoryNote,
    RetrievalResult,
)


class RetrievalAgent(BaseAgent):
    """
    Retrieval Agent executing multi-signal lexical/semantic recall,
    synapse traversal, and recursive CTE supersession lineage resolution.
    Operates under strict READ / SEARCH least-privilege scoping.
    """

    role: AgentRole = AgentRole.RETRIEVAL

    def __init__(
        self,
        storage: Optional[Any] = None,
        llm: Optional[BaseLLMProvider] = None,
    ):
        super().__init__(storage=storage, llm=llm)

    def resolve_lineage(self, note_id: str) -> Optional[Dict[str, Any]]:
        """Find the active successor note at the head of a supersession chain."""
        if not self.storage:
            return None
        return self.storage.resolve_active_lineage(note_id)

    async def retrieve(
        self,
        request: Union[RetrievalQuery, Dict[str, Any]],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> RetrievalResult:
        """Execute scoped associative recall and CTE lineage resolution."""
        t0 = time.time()
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if isinstance(request, dict):
            req = RetrievalQuery(**request)
        else:
            req = request

        if not self.storage:
            return RetrievalResult(
                query=req.query,
                notes=[],
                matches=[],
                total_candidates=0,
                retrieval_time_ms=(time.time() - t0) * 1000.0,
            )

        # 1. Fetch initial candidates via BM25 search
        raw_candidates = self.storage.search_bm25(req.query, limit=req.limit * 3)

        # Also apply category or tag filters if provided
        if req.category:
            cat_candidates = self.storage.query(category=req.category, limit=req.limit * 2)
            existing_ids = {c["id"] for c in raw_candidates}
            for c in cat_candidates:
                if c["id"] not in existing_ids:
                    raw_candidates.append(c)

        if not raw_candidates and not req.query.strip():
            raw_candidates = self.storage.query(limit=req.limit)

        # 2. Lineage Resolution & Supersession Filtering
        processed_candidates: List[Dict[str, Any]] = []
        seen_ids = set()

        for note in raw_candidates:
            note_id = note.get("id")
            lifecycle = note.get("lifecycle", "ACTIVE")

            if not req.include_superseded and (lifecycle == "SUPERSEDED" or note.get("superseded_by")):
                # Resolve to active head of chain
                active_successor = self.storage.resolve_active_lineage(note_id)
                if active_successor and active_successor.get("id") not in seen_ids:
                    seen_ids.add(active_successor["id"])
                    note_copy = active_successor.copy()
                    note_copy["_lineage_resolved_from"] = note_id
                    processed_candidates.append(note_copy)
            else:
                if note_id and note_id not in seen_ids:
                    seen_ids.add(note_id)
                    processed_candidates.append(note)

        # 3. Wikilink / Graph Synapse Expansion (if max_depth >= 2)
        if req.max_depth >= 2:
            expanded_ids = set(seen_ids)
            for note in list(processed_candidates):
                relations = note.get("relations", [])
                for rel in relations:
                    if isinstance(rel, dict):
                        target_id = rel.get("target_id")
                        if target_id and target_id not in expanded_ids:
                            target_note = self.storage.get(target_id)
                            if target_note:
                                expanded_ids.add(target_id)
                                processed_candidates.append(target_note)

        # 4. Multi-Signal Scoring
        scored_notes: List[ScoredMemoryNote] = []
        q_tokens = set(re.findall(r"\w+", req.query.lower()))

        confidence_weights = {
            "very_high": 1.0,
            "high": 0.8,
            "medium": 0.5,
            "low": 0.2,
            "unknown": 0.1,
        }

        for note in processed_candidates:
            content = (note.get("content") or "").lower()
            category = (note.get("category") or "").lower()
            tags = [t.lower() for t in note.get("tags") or []]

            # Lexical score
            match_count = sum(1 for tok in q_tokens if tok in content or tok in category or any(tok in t for t in tags))
            lexical_score = (match_count / max(1, len(q_tokens))) if q_tokens else 0.5

            # Confidence score
            conf_val = note.get("confidence", "medium")
            conf_score = confidence_weights.get(conf_val, 0.5)
            if note.get("verification") == "verified":
                conf_score = min(1.0, conf_score + 0.2)

            # Activation / Recency score
            activation_score = 0.5

            # Composite weighted score
            composite = 0.5 * lexical_score + 0.3 * conf_score + 0.2 * activation_score

            scored_notes.append(
                ScoredMemoryNote(
                    note=note,
                    composite_score=round(composite, 4),
                    lexical_score=round(lexical_score, 4),
                    activation_score=round(activation_score, 4),
                    confidence_score=round(conf_score, 4),
                    lineage_active_successor_id=note.get("superseded_by"),
                )
            )

        # Sort descending by composite score
        scored_notes.sort(key=lambda s: s.composite_score, reverse=True)
        top_notes = scored_notes[: req.limit]
        top_matches = [n.note for n in top_notes]
        top_id = top_notes[0].note.get("id") if top_notes else None

        elapsed_ms = (time.time() - t0) * 1000.0

        return RetrievalResult(
            query=req.query,
            notes=top_notes,
            matches=top_matches,
            total_candidates=len(processed_candidates),
            top_id=top_id,
            retrieval_time_ms=elapsed_ms,
        )

    async def execute(
        self,
        payload: Dict[str, Any],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> Dict[str, Any]:
        """Execute retrieval task against storage."""
        result = await self.retrieve(payload, cancellation_token=cancellation_token)
        return {
            "query": result.query,
            "matches": result.matches,
            "notes": [n.model_dump() for n in result.notes],
            "count": len(result.matches),
            "total_candidates": result.total_candidates,
            "top_id": result.top_id,
            "retrieval_time_ms": result.retrieval_time_ms,
        }
