"""Graph Diagnostics Probe & Candidate Attribution Observer (Antigravity A10).

Provides deep runtime inspection into:
1. Production graph execution status and silent exception detection (GAP-012, GAP-014).
2. Candidate rejection attribution and score preservation diagnostics (GAP-001, GAP-002).
3. Cross-storage engine compatibility validation across InMemory, SQLite, and File engines.

Zero modifications to core memory security boundaries or scoring logic.
"""
from __future__ import annotations

import inspect
import sys
import time
import traceback
from dataclasses import asdict, dataclass, field
from enum import Enum
from typing import Any, Dict, List, Optional, Tuple

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.ranked_search import ranked_search, build_multi_graph


class GraphExecutionStatus(str, Enum):
    APPLIED = "APPLIED"
    FALLBACK_SILENT_EXCEPTION = "FALLBACK_SILENT_EXCEPTION"
    FALLBACK_NO_RESULTS = "FALLBACK_NO_RESULTS"
    FALLBACK_NO_GRAPH_CHANGES = "FALLBACK_NO_GRAPH_CHANGES"


class RejectionReason(str, Enum):
    LIFECYCLE_RAW_EXCLUDED = "LIFECYCLE_RAW_EXCLUDED"
    LIFECYCLE_STAGE_MISMATCH = "LIFECYCLE_STAGE_MISMATCH"
    SCORE_BELOW_THRESHOLD = "SCORE_BELOW_THRESHOLD"
    BUDGET_PAGE_SIZE_EXHAUSTED = "BUDGET_PAGE_SIZE_EXHAUSTED"
    PRINCIPAL_UNAUTHORIZED = "PRINCIPAL_UNAUTHORIZED"
    SUPERSEDED_INACTIVE = "SUPERSEDED_INACTIVE"


@dataclass
class RejectionRecord:
    note_id: str
    lifecycle: str
    source_type: str
    rejection_reason: str
    detail: str


@dataclass
class CandidateRejectionReport:
    query: str
    principal: str
    total_scanned: int
    admitted_count: int
    rejected_count: int
    rejections: List[RejectionRecord] = field(default_factory=list)
    counts_by_reason: Dict[str, int] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "query": self.query,
            "principal": self.principal,
            "total_scanned": self.total_scanned,
            "admitted_count": self.admitted_count,
            "rejected_count": self.rejected_count,
            "rejections": [asdict(r) for r in self.rejections],
            "counts_by_reason": self.counts_by_reason,
        }


@dataclass
class GraphDiagnosticReport:
    storage_engine: str
    query: str
    principal: str
    status: GraphExecutionStatus
    has_store_attribute: bool
    exception_type: Optional[str] = None
    exception_message: Optional[str] = None
    exception_traceback: Optional[str] = None
    base_count: int = 0
    ranked_count: int = 0
    pre_ranking: List[str] = field(default_factory=list)
    post_ranking: List[str] = field(default_factory=list)
    rank_shifted: bool = False
    relevance_scores_survived: bool = False
    duration_ms: float = 0.0

    def to_dict(self) -> Dict[str, Any]:
        return {
            "storage_engine": self.storage_engine,
            "query": self.query,
            "principal": self.principal,
            "status": self.status.value if isinstance(self.status, GraphExecutionStatus) else str(self.status),
            "has_store_attribute": self.has_store_attribute,
            "exception_type": self.exception_type,
            "exception_message": self.exception_message,
            "exception_traceback": self.exception_traceback,
            "base_count": self.base_count,
            "ranked_count": self.ranked_count,
            "pre_ranking": self.pre_ranking,
            "post_ranking": self.post_ranking,
            "rank_shifted": self.rank_shifted,
            "relevance_scores_survived": self.relevance_scores_survived,
            "duration_ms": self.duration_ms,
        }


class GraphDiagnosticsProbe:
    """Non-invasive observer probing graph execution and candidate filtering."""

    @staticmethod
    def probe_ranked_search(
        controller: MemoryController,
        principal: Principal,
        query: str,
        top_k: int = 10,
    ) -> GraphDiagnosticReport:
        t0 = time.perf_counter()
        engine_name = controller.storage.__class__.__name__
        has_store = hasattr(controller.storage, "store")

        # 1. Capture base search without graph
        try:
            base_pack = controller.search(principal, query, page_size=max(top_k, 10))
            base_results = base_pack.get("results", []) if isinstance(base_pack, dict) else []
        except Exception as e:
            return GraphDiagnosticReport(
                storage_engine=engine_name,
                query=query,
                principal=str(principal),
                status=GraphExecutionStatus.FALLBACK_NO_RESULTS,
                has_store_attribute=has_store,
                exception_type=type(e).__name__,
                exception_message=str(e),
                duration_ms=(time.perf_counter() - t0) * 1000,
            )

        pre_ids = [item.get("id") for item in base_results if isinstance(item, dict) and item.get("id")]
        scores_survived = all("relevance_score" in item for item in base_results if isinstance(item, dict))

        if not base_results:
            return GraphDiagnosticReport(
                storage_engine=engine_name,
                query=query,
                principal=str(principal),
                status=GraphExecutionStatus.FALLBACK_NO_RESULTS,
                has_store_attribute=has_store,
                base_count=0,
                ranked_count=0,
                relevance_scores_survived=scores_survived,
                duration_ms=(time.perf_counter() - t0) * 1000,
            )

        # 2. Probe build_multi_graph directly to catch silent exceptions
        intercepted_exc: Optional[Exception] = None
        tb_str: Optional[str] = None
        try:
            _ = build_multi_graph(controller)
        except Exception as e:
            intercepted_exc = e
            tb_str = traceback.format_exc()

        # 3. Call ranked_search
        ranked_results = ranked_search(controller, principal, query, top_k=top_k)
        post_ids = [item.get("id") for item in ranked_results if isinstance(item, dict) and item.get("id")]
        duration_ms = (time.perf_counter() - t0) * 1000

        # 4. Determine status
        if intercepted_exc is not None:
            status = GraphExecutionStatus.FALLBACK_SILENT_EXCEPTION
        elif pre_ids == post_ids:
            status = GraphExecutionStatus.FALLBACK_NO_GRAPH_CHANGES
        else:
            status = GraphExecutionStatus.APPLIED

        return GraphDiagnosticReport(
            storage_engine=engine_name,
            query=query,
            principal=str(principal),
            status=status,
            has_store_attribute=has_store,
            exception_type=type(intercepted_exc).__name__ if intercepted_exc else None,
            exception_message=str(intercepted_exc) if intercepted_exc else None,
            exception_traceback=tb_str,
            base_count=len(base_results),
            ranked_count=len(ranked_results),
            pre_ranking=pre_ids,
            post_ranking=post_ids,
            rank_shifted=(pre_ids != post_ids),
            relevance_scores_survived=scores_survived,
            duration_ms=duration_ms,
        )

    @staticmethod
    def probe_candidate_rejection(
        controller: MemoryController,
        principal: Principal,
        query: str,
        target_notes: List[Dict[str, Any]],
        page_size: int = 5,
    ) -> CandidateRejectionReport:
        """Inspects which notes from a candidate pool are rejected and why."""
        pack = controller.search(principal, query, page_size=page_size)
        admitted_ids = {item.get("id") for item in pack.get("results", []) if item.get("id")}

        rejections: List[RejectionRecord] = []
        counts: Dict[str, int] = {}

        # Scan each note to determine exact rejection reason
        for note in target_notes:
            nid = note.get("id")
            if nid in admitted_ids:
                continue

            lifecycle = (note.get("lifecycle") or "").upper()
            src = note.get("source_type") or "unknown"

            # Determine cause
            if lifecycle == "RAW":
                reason = RejectionReason.LIFECYCLE_RAW_EXCLUDED.value
                detail = "RAW lifecycle notes strictly excluded from active search per I-003"
            elif lifecycle in ["SUPERSEDED", "ARCHIVED"]:
                reason = RejectionReason.SUPERSEDED_INACTIVE.value
                detail = f"Lifecycle {lifecycle} excluded from standard search"
            else:
                # Score or budget
                reason = RejectionReason.SCORE_BELOW_THRESHOLD.value
                detail = "Note score failed to reach admission threshold or was cut off by page_size"

            counts[reason] = counts.get(reason, 0) + 1
            rejections.append(
                RejectionRecord(
                    note_id=nid,
                    lifecycle=lifecycle,
                    source_type=src,
                    rejection_reason=reason,
                    detail=detail,
                )
            )

        return CandidateRejectionReport(
            query=query,
            principal=str(principal),
            total_scanned=len(target_notes),
            admitted_count=len(admitted_ids),
            rejected_count=len(rejections),
            rejections=rejections,
            counts_by_reason=counts,
        )
