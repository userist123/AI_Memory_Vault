"""Non-invasive diagnostics for graph-backed retrieval.

The probe observes the existing retrieval path; it never changes scoring,
authorization, lifecycle, or storage behavior.
"""
from __future__ import annotations

import time
import traceback
from dataclasses import asdict, dataclass, field
from enum import Enum
from typing import Any, Dict, List, Optional

from packages.retrieval.ranked_search import build_multi_graph, ranked_search


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
        return {"query": self.query, "principal": self.principal,
                "total_scanned": self.total_scanned, "admitted_count": self.admitted_count,
                "rejected_count": self.rejected_count,
                "rejections": [asdict(r) for r in self.rejections],
                "counts_by_reason": self.counts_by_reason}


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
        result = asdict(self)
        result["status"] = self.status.value
        return result


class GraphDiagnosticsProbe:
    """Observe ranked retrieval and expose otherwise silent fallback reasons."""

    @staticmethod
    def probe_ranked_search(controller: Any, principal: Any, query: str,
                            top_k: int = 10) -> GraphDiagnosticReport:
        started = time.perf_counter()
        storage = controller.storage
        engine_name = storage.__class__.__name__
        has_store = hasattr(storage, "store")
        try:
            base_pack = controller.search(principal, query, page_size=max(top_k, 10))
            base_results = base_pack.get("results", []) if isinstance(base_pack, dict) else []
        except Exception as exc:
            return GraphDiagnosticReport(engine_name, query, str(principal),
                GraphExecutionStatus.FALLBACK_NO_RESULTS, has_store,
                type(exc).__name__, str(exc), traceback.format_exc(),
                duration_ms=(time.perf_counter() - started) * 1000)

        pre_ids = [x.get("id") for x in base_results if isinstance(x, dict) and x.get("id")]
        scores_survived = all("relevance_score" in x for x in base_results if isinstance(x, dict))
        if not base_results:
            return GraphDiagnosticReport(engine_name, query, str(principal),
                GraphExecutionStatus.FALLBACK_NO_RESULTS, has_store,
                base_count=0, relevance_scores_survived=scores_survived,
                duration_ms=(time.perf_counter() - started) * 1000)

        graph_exc = None
        graph_tb = None
        try:
            build_multi_graph(controller)
        except Exception as exc:
            graph_exc, graph_tb = exc, traceback.format_exc()

        ranked_results = ranked_search(controller, principal, query, top_k=top_k)
        post_ids = [x.get("id") for x in ranked_results if isinstance(x, dict) and x.get("id")]
        status = (GraphExecutionStatus.FALLBACK_SILENT_EXCEPTION if graph_exc
                  else GraphExecutionStatus.FALLBACK_NO_GRAPH_CHANGES if pre_ids == post_ids
                  else GraphExecutionStatus.APPLIED)
        return GraphDiagnosticReport(
            storage_engine=engine_name, query=query, principal=str(principal), status=status,
            has_store_attribute=has_store,
            exception_type=type(graph_exc).__name__ if graph_exc else None,
            exception_message=str(graph_exc) if graph_exc else None,
            exception_traceback=graph_tb,
            base_count=len(base_results), ranked_count=len(ranked_results),
            pre_ranking=pre_ids, post_ranking=post_ids,
            rank_shifted=pre_ids != post_ids,
            relevance_scores_survived=scores_survived,
            duration_ms=(time.perf_counter() - started) * 1000,
        )

    @staticmethod
    def probe_candidate_rejection(controller: Any, principal: Any, query: str,
                                  target_notes: List[Dict[str, Any]], page_size: int = 5
                                  ) -> CandidateRejectionReport:
        pack = controller.search(principal, query, page_size=page_size)
        admitted = {x.get("id") for x in pack.get("results", []) if x.get("id")}
        rejections: List[RejectionRecord] = []
        counts: Dict[str, int] = {}
        for note in target_notes:
            nid = note.get("id")
            if nid in admitted:
                continue
            lifecycle = str(note.get("lifecycle") or "").upper()
            source = str(note.get("source_type") or "unknown")
            if lifecycle == "RAW":
                reason = RejectionReason.LIFECYCLE_RAW_EXCLUDED
                detail = "RAW lifecycle notes are excluded from active search."
            elif lifecycle in {"SUPERSEDED", "ARCHIVED"}:
                reason = RejectionReason.SUPERSEDED_INACTIVE
                detail = f"Lifecycle {lifecycle} is excluded from standard search."
            else:
                reason = RejectionReason.SCORE_BELOW_THRESHOLD
                detail = "Candidate was not admitted by the observed search page."
            key = reason.value
            counts[key] = counts.get(key, 0) + 1
            rejections.append(RejectionRecord(nid, lifecycle, source, key, detail))
        return CandidateRejectionReport(query, str(principal), len(target_notes),
                                         len(admitted), len(rejections), rejections, counts)
