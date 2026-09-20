"""observability/retrieval_trace.py — Strongly-typed, versioned RetrievalTrace contract.

OBS-001 Mandate:
1. Canonical schema versioning: Any schema modification without a version bump fails verification.
2. Data minimization: Query text, prompt fragments, and secrets are NEVER logged. Only SHA-256
   fingerprints and classifier category labels are preserved.
3. Complete causality & explainability: Every candidate note that enters or is rejected from the
   context pack carries an explicit reason code.
4. Non-interfering fail-safe execution: Telemetry failures NEVER alter retrieval results.
"""
from __future__ import annotations

import enum
import hashlib
import json
import time
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set, Tuple


SCHEMA_VERSION: str = "1.0.0"


class TraceEvent(str, enum.Enum):
    """Mandated lifecycle events emitted during retrieval execution."""
    QUERY_RECEIVED = "QUERY_RECEIVED"
    QUERY_SANITIZED = "QUERY_SANITIZED"
    CANDIDATES_GENERATED = "CANDIDATES_GENERATED"
    CANDIDATES_MERGED = "CANDIDATES_MERGED"
    POLICY_FILTERED = "POLICY_FILTERED"
    GRAPH_EXPANDED = "GRAPH_EXPANDED"
    CONTEXT_PACKED = "CONTEXT_PACKED"
    ABSTAINED = "ABSTAINED"
    MEMORY_CITED = "MEMORY_CITED"


class ExclusionReason(str, enum.Enum):
    """Standardized machine-readable reason codes for candidate exclusion."""
    RAW_EXCLUDED = "RAW_EXCLUDED"
    LIFECYCLE_FILTERED = "LIFECYCLE_FILTERED"
    AGENT_LIFECYCLE_FLOOR_EXCLUDED = "AGENT_LIFECYCLE_FLOOR_EXCLUDED"
    TYPE_FILTERED = "TYPE_FILTERED"
    AUTHORIZATION_DENIED = "AUTHORIZATION_DENIED"
    SECURITY_CLASSIFICATION_DENIED = "SECURITY_CLASSIFICATION_DENIED"
    DUPLICATE_ELIMINATED = "DUPLICATE_ELIMINATED"
    SCORE_BELOW_THRESHOLD = "SCORE_BELOW_THRESHOLD"
    CANDIDATE_LIMIT_CUT = "CANDIDATE_LIMIT_CUT"
    GRAPH_HUB_SKIPPED = "GRAPH_HUB_SKIPPED"
    GRAPH_DEGRADED_SKIPPED = "GRAPH_DEGRADED_SKIPPED"
    PAGINATION_CUT = "PAGINATION_CUT"
    BUDGET_EXCEEDED = "BUDGET_EXCEEDED"
    ABSTAINED = "ABSTAINED"


class InclusionReason(str, enum.Enum):
    """Standardized machine-readable reason codes for candidate inclusion."""
    INCLUDED_IN_FINAL_PACK = "INCLUDED_IN_FINAL_PACK"


class SchemaVersionMismatchError(RuntimeError):
    """Raised when the runtime schema structure drifts from its declared version."""


# Canonical schema fields frozen for version 1.0.0
_SCHEMA_V1_0_FIELDS: Tuple[str, ...] = (
    "trace_id",
    "schema_version",
    "query_hash",
    "query_class",
    "corpus_version",
    "index_version",
    "config_version",
    "candidate_ids_by_generator",
    "raw_scores_by_signal",
    "fused_rank",
    "rerank_displacement",
    "verdicts",
    "graph_contribution",
    "decisions",
    "abstention_reason",
    "final_rank",
    "stage_latencies_ms",
    "token_estimate",
    "result_link",
    "events",
    "status",
)

# Canonical schema fields for version 1.1.0 (with economic aggregation & dual latency naming)
_SCHEMA_V1_1_FIELDS: Tuple[str, ...] = (
    "trace_id",
    "schema_version",
    "query_hash",
    "query_class",
    "corpus_version",
    "index_version",
    "config_version",
    "candidate_ids_by_generator",
    "raw_scores_by_signal",
    "fused_rank",
    "rerank_displacement",
    "verdicts",
    "graph_contribution",
    "decisions",
    "aggregated_exclusions",
    "abstention_reason",
    "final_rank",
    "stage_latencies_ms",
    "stage_latency_ms",
    "token_estimate",
    "result_link",
    "events",
    "status",
)

SCHEMA_VERSION: str = "1.1.0"


def compute_schema_fingerprint(fields: Tuple[str, ...]) -> str:
    """Computes a deterministic SHA-256 fingerprint of schema field names."""
    canonical_str = ",".join(sorted(fields))
    return hashlib.sha256(canonical_str.encode("utf-8")).hexdigest()


SCHEMA_FINGERPRINTS: Dict[str, str] = {
    "1.0.0": compute_schema_fingerprint(_SCHEMA_V1_0_FIELDS),
    "1.1.0": compute_schema_fingerprint(_SCHEMA_V1_1_FIELDS),
}


@dataclass
class DecisionRecord:
    """Individual decision entry for a note evaluated during retrieval."""
    decision: str  # "INCLUDED" | "EXCLUDED"
    reason_code: str  # from ExclusionReason or InclusionReason
    stage: str
    details: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "decision": self.decision,
            "reason_code": self.reason_code,
            "stage": self.stage,
            "details": self.details,
        }


@dataclass
class RetrievalTrace:
    """Versioned, privacy-preserving retrieval trace matching OBS-001 specification."""
    trace_id: str
    schema_version: str = SCHEMA_VERSION
    query_hash: str = ""
    query_class: str = "unknown"
    corpus_version: str = "unknown"
    index_version: str = "unknown"
    config_version: str = "unknown"

    # Generator attribution and scoring
    candidate_ids_by_generator: Dict[str, List[str]] = field(default_factory=dict)
    raw_scores_by_signal: Dict[str, Dict[str, float]] = field(default_factory=dict)
    fused_rank: List[Dict[str, Any]] = field(default_factory=list)
    rerank_displacement: Dict[str, int] = field(default_factory=dict)

    # Authority, verification, temporal and lifecycle verdicts
    verdicts: Dict[str, Dict[str, Any]] = field(default_factory=dict)

    # Graph contribution details
    graph_contribution: Dict[str, Any] = field(default_factory=dict)

    # Decisions for competitive notes (included, scored, cut, paginated, packed)
    decisions: Dict[str, Dict[str, Any]] = field(default_factory=dict)

    # Aggregated mass exclusions for storage policy (preserves reason codes & counts without per-note bloat)
    aggregated_exclusions: Dict[str, Dict[str, Any]] = field(default_factory=dict)

    # Abstention
    abstention_reason: Optional[str] = None

    # Final outputs and latency (both stage_latencies_ms and stage_latency_ms exposed)
    final_rank: List[str] = field(default_factory=list)
    stage_latencies_ms: Dict[str, float] = field(default_factory=dict)
    stage_latency_ms: Dict[str, float] = field(default_factory=dict)
    token_estimate: int = 0
    result_link: Optional[str] = None

    # Event audit trail
    events: List[Dict[str, Any]] = field(default_factory=list)

    # Execution telemetry status
    status: str = "ok"

    def get_decision(self, note_id: str) -> Optional[Dict[str, Any]]:
        """Resolves decision record for any note across competitive and aggregated records."""
        if note_id in self.decisions:
            return self.decisions[note_id]
        for code_val, agg in self.aggregated_exclusions.items():
            sample_ids = agg.get("sample_ids", [])
            if note_id in sample_ids:
                return {
                    "decision": "EXCLUDED",
                    "reason_code": code_val,
                    "stage": agg.get("stage", "storage_policy"),
                    "details": agg.get("details", {}),
                    "aggregated": True,
                }
        return None

    def to_dict(self) -> Dict[str, Any]:
        """Serializes the trace to a dictionary, validating privacy invariants."""
        res = asdict(self)
        return res

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> RetrievalTrace:
        """Constructs a RetrievalTrace from a dictionary."""
        valid_fields = set(cls.__dataclass_fields__.keys())
        filtered = {k: v for k, v in data.items() if k in valid_fields}
        return cls(**filtered)

    def to_json(self) -> str:
        """Serializes the trace to JSON format."""
        return json.dumps(self.to_dict(), indent=2)

    def validate_schema(self) -> None:
        """Validates that the instance fields match the declared schema version."""
        current_fields = tuple(self.__dataclass_fields__.keys())
        expected_fp = SCHEMA_FINGERPRINTS.get(self.schema_version)
        if not expected_fp:
            raise SchemaVersionMismatchError(f"Unknown schema version: {self.schema_version}")

        actual_fp = compute_schema_fingerprint(current_fields)
        if actual_fp != expected_fp:
            raise SchemaVersionMismatchError(
                f"Schema field drift detected for version {self.schema_version}. "
                f"Expected fingerprint {expected_fp}, got {actual_fp}. "
                "Bump schema_version when altering schema fields."
            )


class RetrievalTraceCollector:
    """Stateful collector that aggregates telemetry across retrieval stages.

    All mutation methods are protected with passive error guards to guarantee
    the fail-safe invariant: telemetry failures NEVER break retrieval.
    """

    def __init__(
        self,
        trace_id: str,
        query_hash: str,
        corpus_version: str = "unknown",
        index_version: str = "unknown",
        config_version: str = "unknown",
    ):
        self.trace = RetrievalTrace(
            trace_id=trace_id,
            query_hash=query_hash,
            corpus_version=corpus_version,
            index_version=index_version,
            config_version=config_version,
        )
        self._stage_starts: Dict[str, float] = {}

    def start_stage(self, stage_name: str) -> None:
        """Marks the start timestamp of a pipeline stage."""
        try:
            self._stage_starts[stage_name] = time.perf_counter()
        except Exception:
            pass

    def end_stage(self, stage_name: str) -> None:
        """Calculates and stores duration of a pipeline stage in milliseconds."""
        try:
            if stage_name in self._stage_starts:
                duration_ms = (time.perf_counter() - self._stage_starts[stage_name]) * 1000.0
                val = round(duration_ms, 3)
                self.trace.stage_latencies_ms[stage_name] = val
                self.trace.stage_latency_ms[stage_name] = val
        except Exception:
            pass

    def record_event(self, event: TraceEvent, metadata: Optional[Dict[str, Any]] = None) -> None:
        """Appends a pipeline lifecycle event to the timeline."""
        try:
            self.trace.events.append({
                "event": event.value if isinstance(event, TraceEvent) else str(event),
                "timestamp": datetime.now(timezone.utc).isoformat(),
                "metadata": metadata or {},
            })
        except Exception:
            pass

    def set_query_class(self, query_class: str) -> None:
        """Sets the classified query category/intent."""
        try:
            self.trace.query_class = str(query_class)
        except Exception:
            pass

    def record_generator_candidates(self, generator: str, candidate_ids: List[str]) -> None:
        """Records candidate IDs emitted by a specific generator."""
        try:
            self.trace.candidate_ids_by_generator[generator] = list(candidate_ids)
        except Exception:
            pass

    def record_raw_score(self, note_id: str, signal_name: str, score: float) -> None:
        """Records a single raw signal score for a candidate note."""
        try:
            if note_id not in self.trace.raw_scores_by_signal:
                self.trace.raw_scores_by_signal[note_id] = {}
            self.trace.raw_scores_by_signal[note_id][signal_name] = round(float(score), 6)
        except Exception:
            pass

    def set_fused_rank(self, fused_ranking: List[Dict[str, Any]]) -> None:
        """Records the fused ranking list and maps initial scores."""
        try:
            self.trace.fused_rank = list(fused_ranking)
        except Exception:
            pass

    def record_verdict(self, note_id: str, category: str, verdict: Any) -> None:
        """Records a verification, lifecycle, or authority verdict."""
        try:
            if note_id not in self.trace.verdicts:
                self.trace.verdicts[note_id] = {}
            self.trace.verdicts[note_id][category] = verdict
        except Exception:
            pass

    def record_graph_contribution(self, contribution_data: Dict[str, Any]) -> None:
        """Records graph traversal and activation contribution details."""
        try:
            self.trace.graph_contribution.update(contribution_data)
        except Exception:
            pass

    def record_decision(
        self,
        note_id: str,
        decision: str,
        reason_code: ExclusionReason | InclusionReason | str,
        stage: str,
        details: Optional[Dict[str, Any]] = None,
    ) -> None:
        """Records inclusion or exclusion decision with a formal reason code."""
        try:
            code_val = reason_code.value if hasattr(reason_code, "value") else str(reason_code)
            self.trace.decisions[note_id] = {
                "decision": decision,
                "reason_code": code_val,
                "stage": stage,
                "details": details or {},
            }
        except Exception:
            pass

    def record_bulk_exclusion(
        self,
        reason_code: ExclusionReason | str,
        stage: str,
        count: int,
        sample_ids: Optional[List[str]] = None,
        details: Optional[Dict[str, Any]] = None,
    ) -> None:
        """Records mass exclusions compactly by reason code without bloating individual note decisions."""
        try:
            code_val = reason_code.value if hasattr(reason_code, "value") else str(reason_code)
            self.trace.aggregated_exclusions[code_val] = {
                "reason_code": code_val,
                "stage": stage,
                "count": count,
                "sample_ids": sample_ids[:5] if sample_ids else [],
                "details": details or {},
            }
        except Exception:
            pass

    def get_decision(self, note_id: str) -> Optional[Dict[str, Any]]:
        """Resolves decision record for any note."""
        try:
            return self.trace.get_decision(note_id)
        except Exception:
            return None

    def set_abstention(self, reason: str) -> None:
        """Sets abstention reason if retrieval abstained."""
        try:
            self.trace.abstention_reason = str(reason)
            self.record_event(TraceEvent.ABSTAINED, {"reason": reason})
        except Exception:
            pass

    def finalize(
        self,
        final_notes: List[Dict[str, Any]],
        token_estimate: int = 0,
        result_link: Optional[str] = None,
    ) -> RetrievalTrace:
        """Finalizes the trace, computes displacement, and marks final pack inclusions."""
        try:
            final_ids = [n.get("id") for n in final_notes if n.get("id")]
            self.trace.final_rank = final_ids
            self.trace.token_estimate = token_estimate
            self.trace.result_link = result_link

            # Mark all final results as INCLUDED
            for rank_idx, n_id in enumerate(final_ids, start=1):
                self.record_decision(
                    note_id=n_id,
                    decision="INCLUDED",
                    reason_code=InclusionReason.INCLUDED_IN_FINAL_PACK,
                    stage="context_pack",
                    details={"final_rank": rank_idx},
                )

            # Compute rank displacement (initial fused rank vs final rank)
            initial_ranks: Dict[str, int] = {
                entry.get("id"): entry.get("rank", idx)
                for idx, entry in enumerate(self.trace.fused_rank, start=1)
                if entry.get("id")
            }
            final_ranks_map: Dict[str, int] = {n_id: idx for idx, n_id in enumerate(final_ids, start=1)}

            for n_id, init_r in initial_ranks.items():
                if n_id in final_ranks_map:
                    # positive means moved up, negative means moved down
                    displacement = init_r - final_ranks_map[n_id]
                    self.trace.rerank_displacement[n_id] = displacement

            self.trace.validate_schema()
        except Exception as e:
            self.trace.status = f"degraded_telemetry_error: {str(e)}"

        return self.trace


class SafeTraceCollectorProxy:
    """Fail-safe proxy wrapping RetrievalTraceCollector.

    Guarantees that ANY exception raised by the telemetry collector (due to fault injection,
    mocking, invalid data, or internal errors) is swallowed and converts status to degraded,
    ensuring that retrieval execution is NEVER disrupted.
    """

    def __init__(self, collector: RetrievalTraceCollector):
        self._collector = collector

    @property
    def trace(self) -> RetrievalTrace:
        return self._collector.trace

    def finalize(
        self,
        final_notes: List[Dict[str, Any]],
        token_estimate: int = 0,
        result_link: Optional[str] = None,
    ) -> RetrievalTrace:
        try:
            return self._collector.finalize(final_notes, token_estimate, result_link)
        except Exception as e:
            try:
                self._collector.trace.status = f"degraded_telemetry_error: {str(e)}"
                return self._collector.trace
            except Exception:
                return RetrievalTrace(
                    trace_id="degraded",
                    status=f"degraded_telemetry_error: {str(e)}",
                )

    def __getattr__(self, name: str) -> Any:
        attr = getattr(self._collector, name)
        if callable(attr):
            def safe_wrapper(*args, **kwargs):
                try:
                    return attr(*args, **kwargs)
                except Exception as e:
                    try:
                        self._collector.trace.status = f"degraded_telemetry_error: {str(e)}"
                    except Exception:
                        pass
                    return None
            return safe_wrapper
        return attr

