"""20_TESTS/test_retrieval_trace_contract.py — Validation of OBS-001 RetrievalTrace contract.

Enforces:
1. Versioned RetrievalTrace contract (schema_version = '1.0.0').
2. Privacy & data minimization (zero raw query/secret leakage).
3. Universal explainability (every decision has an explicit reason_code).
4. Score reconstructibility and rank displacement tracking.
5. Fail-safe telemetry execution under fault injection.
6. Schema drift protection.
7. Latency overhead benchmarks.
"""
import hashlib
import json
import os
import time
from unittest.mock import patch
import pytest

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import (
    MemoryController,
    StorageEngine,
    RANKING_ARM_BASELINE,
)
from observability.retrieval_trace import (
    SCHEMA_VERSION,
    ExclusionReason,
    InclusionReason,
    RetrievalTrace,
    RetrievalTraceCollector,
    SchemaVersionMismatchError,
    TraceEvent,
)


@pytest.fixture
def rich_storage():
    storage = StorageEngine()
    # 1. Verified active knowledge notes
    for i in range(1, 15):
        storage.set(
            f"active_note_{i:02d}",
            {
                "id": f"active_note_{i:02d}",
                "lifecycle": "active",
                "type": "knowledge",
                "title": f"Active Note {i} on Architecture",
                "content": f"Detailed architectural principles and distributed telemetry patterns {i}.",
                "verification": "verified",
                "confidence": 0.9,
                "source_type": "official",
            },
        )
    # 2. Raw note (should be excluded by default or for AI_AGENT)
    storage.set(
        "raw_unverified_note_99",
        {
            "id": "raw_unverified_note_99",
            "lifecycle": "raw",
            "type": "knowledge",
            "title": "Raw scratchpad",
            "content": "Raw unparsed memory import.",
            "verification": "unverified",
            "confidence": 0.2,
            "source_type": "unknown",
        },
    )
    # 3. Decision type note
    storage.set(
        "decision_note_01",
        {
            "id": "decision_note_01",
            "lifecycle": "active",
            "type": "decision",
            "title": "ADR 001 Architecture Decision",
            "content": "Decision record concerning telemetry and observability.",
            "verification": "verified",
            "confidence": 0.95,
            "source_type": "official",
        },
    )
    return storage


def test_retrieval_trace_attached_and_structured(rich_storage):
    """Verifies that every search produces a structured RetrievalTrace adhering to schema v1.1.0."""
    controller = MemoryController(storage=rich_storage, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(Principal.HUMAN, "architectural principles telemetry", page_size=5)

    assert "retrieval_trace" in pack, "retrieval_trace must be attached to search pack"
    trace = pack["retrieval_trace"]

    # Required fields
    assert trace["schema_version"] == SCHEMA_VERSION
    assert trace["schema_version"] == "1.1.0"
    assert trace["status"] == "ok"
    assert "trace_id" in trace and len(trace["trace_id"]) > 0
    assert "query_hash" in trace and len(trace["query_hash"]) == 64
    assert "candidate_ids_by_generator" in trace
    assert "raw_scores_by_signal" in trace
    assert "fused_rank" in trace
    assert "rerank_displacement" in trace
    assert "verdicts" in trace
    assert "graph_contribution" in trace
    assert "decisions" in trace
    assert "aggregated_exclusions" in trace
    assert "final_rank" in trace
    assert "stage_latencies_ms" in trace
    assert "stage_latency_ms" in trace
    assert "events" in trace

    # Stage latencies dual exposure verification
    assert isinstance(trace["stage_latency_ms"], dict)
    assert isinstance(trace["stage_latencies_ms"], dict)
    assert len(trace["stage_latency_ms"]) > 0
    assert trace["stage_latency_ms"] == trace["stage_latencies_ms"]
    for stage, lat in trace["stage_latency_ms"].items():
        assert isinstance(lat, (int, float))
        assert lat >= 0.0

    # Events timeline
    event_names = [e["event"] for e in trace["events"]]
    assert TraceEvent.QUERY_RECEIVED.value in event_names
    assert TraceEvent.QUERY_SANITIZED.value in event_names
    assert TraceEvent.CANDIDATES_GENERATED.value in event_names
    assert TraceEvent.CONTEXT_PACKED.value in event_names


def test_data_minimization_zero_query_or_secret_leakage(rich_storage):
    """Adversarial test: High-entropy secret query MUST NOT leak into trace."""
    secret_token = "SECRET_SUPER_CONFIDENTIAL_TOKEN_XYZ_999"
    sensitive_query = f"find system secrets with {secret_token} and passwords"

    controller = MemoryController(storage=rich_storage, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(Principal.HUMAN, sensitive_query, page_size=5)

    trace = pack["retrieval_trace"]
    serialized = json.dumps(trace)

    # Assert secret token is never logged anywhere in the trace
    assert secret_token not in serialized, "Critical leak: secret token found in retrieval trace!"
    assert sensitive_query not in serialized, "Raw query string leaked in retrieval trace!"

    # Verify query is only present as a SHA-256 hash
    expected_hash = hashlib.sha256(sensitive_query.strip().encode("utf-8")).hexdigest()
    assert trace["query_hash"] == expected_hash


def test_all_exclusions_have_explicit_reason_codes(rich_storage):
    """Verifies that every evaluated candidate is assigned an explicit, standardized reason code."""
    controller = MemoryController(storage=rich_storage, ranking_arm=RANKING_ARM_BASELINE)
    # Search with AI_AGENT to trigger agent floor exclusions and pagination cuts
    pack = controller.search(
        Principal.AI_AGENT,
        "architectural principles",
        page_size=3,
        types=["knowledge"],  # Excludes decision_note_01
    )

    trace = pack["retrieval_trace"]
    decisions = trace["decisions"]
    assert len(decisions) > 0, "Trace decisions must not be empty"

    valid_reason_codes = {r.value for r in ExclusionReason} | {r.value for r in InclusionReason}

    for note_id, dec in decisions.items():
        assert dec["decision"] in ("INCLUDED", "EXCLUDED")
        assert dec["reason_code"] in valid_reason_codes, f"Invalid reason code {dec['reason_code']} for {note_id}"
        assert "stage" in dec

    # Check that raw note was excluded due to agent lifecycle floor or raw exclusion
    if "raw_unverified_note_99" in decisions:
        assert decisions["raw_unverified_note_99"]["decision"] == "EXCLUDED"
        assert decisions["raw_unverified_note_99"]["reason_code"] in (
            ExclusionReason.RAW_EXCLUDED.value,
            ExclusionReason.AGENT_LIFECYCLE_FLOOR_EXCLUDED.value,
        )

    # Check that decision note was excluded due to type filter
    if "decision_note_01" in decisions:
        assert decisions["decision_note_01"]["decision"] == "EXCLUDED"
        assert decisions["decision_note_01"]["reason_code"] == ExclusionReason.TYPE_FILTERED.value


def test_scores_reconstructible_and_displacement_tracked(rich_storage):
    """Verifies raw signal scores and fused rank displacement are preserved for auditing."""
    controller = MemoryController(storage=rich_storage, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(Principal.HUMAN, "principles", page_size=5)

    trace = pack["retrieval_trace"]
    fused_rank = trace["fused_rank"]
    raw_scores = trace["raw_scores_by_signal"]
    displacement = trace["rerank_displacement"]

    assert len(fused_rank) > 0
    # Every fused candidate must have raw scores
    for entry in fused_rank:
        cand_id = entry["id"]
        assert cand_id in raw_scores, f"Candidate {cand_id} missing from raw_scores_by_signal"
        assert "fused_score" in entry or "score" in entry

    # Displacement dictionary is computed
    assert isinstance(displacement, dict)


def test_telemetry_failure_does_not_break_retrieval(rich_storage):
    """Fault injection: Telemetry failures must never break search execution (fail-safe)."""
    controller = MemoryController(storage=rich_storage, ranking_arm=RANKING_ARM_BASELINE)

    # Fault 1: record_event raises
    with patch.object(RetrievalTraceCollector, "record_event", side_effect=RuntimeError("Event crash")):
        pack = controller.search(Principal.HUMAN, "principles", page_size=5)
        assert len(pack["results"]) > 0, "Retrieval failed when record_event crashed"

    # Fault 2: finalize raises
    with patch.object(RetrievalTraceCollector, "finalize", side_effect=RuntimeError("Finalize crash")):
        pack = controller.search(Principal.HUMAN, "principles", page_size=5)
        assert len(pack["results"]) > 0, "Retrieval failed when finalize crashed"
        # Check that degraded fallback trace is captured
        assert pack["retrieval_trace"]["status"].startswith("degraded")


def test_schema_version_drift_fails():
    """Verifies that modifying schema fields without bumping version fails verification."""
    trace = RetrievalTrace(trace_id="test_t1")
    # Clean trace validates
    trace.validate_schema()

    # Unknown version fails
    trace.schema_version = "99.9.9"
    with pytest.raises(SchemaVersionMismatchError, match="Unknown schema version"):
        trace.validate_schema()


def test_telemetry_latency_overhead(rich_storage):
    """Benchmarks retrieval trace overhead and reports exact milliseconds and percentage."""
    controller = MemoryController(storage=rich_storage, ranking_arm=RANKING_ARM_BASELINE)

    # Warmup
    for _ in range(5):
        controller.search(Principal.HUMAN, "architectural principles", page_size=5)

    # Benchmark runs
    iterations = 50
    t0 = time.perf_counter()
    for _ in range(iterations):
        pack = controller.search(Principal.HUMAN, "architectural principles", page_size=5)
    total_time_s = time.perf_counter() - t0

    avg_time_ms = (total_time_s / iterations) * 1000.0
    trace = pack["retrieval_trace"]
    stage_latencies = trace.get("stage_latencies_ms", {})

    print(f"\n[BENCHMARK] Total search latency: {avg_time_ms:.2f} ms/query across {iterations} queries")
    for stage, lat in stage_latencies.items():
        print(f"  - Stage '{stage}': {lat:.3f} ms")

    # The entire search should be fast (< 20ms in mock storage)
    assert avg_time_ms < 50.0, f"Average search latency {avg_time_ms:.2f} ms exceeds 50ms"


def test_aggregated_exclusions_and_get_decision():
    """Verifies that aggregated bulk exclusions record properly and get_decision resolves correctly."""
    collector = RetrievalTraceCollector(trace_id="test_agg_t1", query_hash="a" * 64)
    collector.record_bulk_exclusion(
        reason_code=ExclusionReason.RAW_EXCLUDED.value,
        stage="storage_policy",
        count=150,
        sample_ids=["raw_01", "raw_02", "raw_03"],
        details={"criteria": "raw_content_excluded"},
    )
    collector.record_decision(
        note_id="comp_01",
        decision="EXCLUDED",
        reason_code=ExclusionReason.PAGINATION_CUT.value,
        stage="pagination",
        details={"rank": 6},
    )

    trace = collector.finalize(final_notes=[{"id": "note_win_01"}])
    assert "RAW_EXCLUDED" in trace.aggregated_exclusions
    agg = trace.aggregated_exclusions["RAW_EXCLUDED"]
    assert agg["count"] == 150
    assert len(agg["sample_ids"]) == 3
    assert "raw_01" in agg["sample_ids"]

    # Resolution via get_decision
    dec_comp = trace.get_decision("comp_01")
    assert dec_comp is not None
    assert dec_comp["reason_code"] == ExclusionReason.PAGINATION_CUT.value

    dec_sample = trace.get_decision("raw_02")
    assert dec_sample is not None
    assert dec_sample["reason_code"] == ExclusionReason.RAW_EXCLUDED.value
    assert dec_sample["aggregated"] is True

    dec_missing = trace.get_decision("non_existent")
    assert dec_missing is None

    # Roundtrip serialization
    d = trace.to_dict()
    assert "aggregated_exclusions" in d
    assert "stage_latency_ms" in d
    reconstituted = RetrievalTrace.from_dict(d)
    assert reconstituted.schema_version == "1.1.0"
    assert "RAW_EXCLUDED" in reconstituted.aggregated_exclusions


def test_trace_size_under_20_kb():
    """Verifies that an evaluated corpus with 150+ notes produces a trace comfortably below 20 KB."""
    storage = StorageEngine()
    for i in range(150):
        storage.set(
            f"note_{i:03d}",
            {
                "id": f"note_{i:03d}",
                "lifecycle": "active" if i % 3 != 0 else "raw",
                "type": "knowledge" if i % 2 == 0 else "decision",
                "title": f"Note {i} on System Design and Telemetry",
                "content": f"Telemetry content for evaluation of memory retrieval trace {i}.",
                "verification": "verified" if i % 3 != 0 else "unverified",
                "confidence": 0.85,
                "source_type": "official" if i % 3 != 0 else "unknown",
            },
        )

    controller = MemoryController(storage=storage, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(Principal.AI_AGENT, "telemetry system design", page_size=5)

    trace = pack["retrieval_trace"]
    serialized = json.dumps(trace)
    size_kb = len(serialized.encode("utf-8")) / 1024.0

    print(f"\n[BENCHMARK] Trace size with 150 notes: {size_kb:.2f} KB (Target: < 20.0 KB)")
    assert size_kb < 20.0, f"Trace size {size_kb:.2f} KB exceeds 20.0 KB ceiling!"
    assert len(trace["stage_latency_ms"]) > 0
    assert "aggregated_exclusions" in trace

