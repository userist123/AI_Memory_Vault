"""memory_controller/tests/test_observed_memory_trace.py — Test suite for Runtime Observed Memory Trace.

Verifies:
1. Observed trace schema & field invariants
2. Final-context presence only (candidate vs packed exclusion)
3. Real runtime ContextPackBuilder integration
4. Score capture from existing note metadata
5. Declared vs observed reconciliation (fabrication & unacknowledged detection)
6. Telemetry failure safety (zero crash, zero side-effects on context pack)
7. Data minimization (no prompt/content leakage)
8. Run-ID correlation and append-only loading
"""
import json
import tempfile
from pathlib import Path
import pytest

from memory_controller.context.pack_builder import ContextPackBuilder
from memory_controller.memory_trace import (
    ObservedMemoryTrace,
    load_observed_memory_traces,
    reconcile_observed_trace,
    record_observed_memory_trace,
)


def test_observed_trace_schema_and_serialization(tmp_path):
    trace = record_observed_memory_trace(
        run_id="run-test-01",
        results=[
            {"id": "note_01", "score": 0.95},
            {"id": "note_02", "relevance_score": 0.82},
        ],
        context_size_bytes=1024,
        estimated_tokens=256,
        telemetry_dir=tmp_path,
    )
    assert trace is not None
    assert trace.run_id == "run-test-01"
    assert trace.retrieved_memory_ids == ["note_01", "note_02"]
    assert trace.retrieval_scores == {"note_01": 0.95, "note_02": 0.82}
    assert trace.context_size_bytes == 1024
    assert trace.estimated_tokens == 256

    # Verify persistent jsonl
    loaded = load_observed_memory_traces(run_id="run-test-01", telemetry_dir=tmp_path)
    assert len(loaded) == 1
    assert loaded[0].retrieved_memory_ids == ["note_01", "note_02"]


def test_final_context_only_acceptance_test(tmp_path, monkeypatch):
    """Prove that memory excluded by budget degradation does NOT appear in observed trace."""
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path))

    builder = ContextPackBuilder()
    # Provide 3 candidates: M1, M2, M3
    # Craft large note M2 so it gets pruned by degradation or budget
    candidates = [
        {"id": "M1", "content": "Short M1 content", "score": 0.9},
        {"id": "M2", "content": "Large M2 " * 500, "score": 0.5},
        {"id": "M3", "content": "Short M3 content", "score": 0.8},
    ]

    # Set hard byte budget to fit only M1 and M3
    budget = {"soft": 500, "hard": 600, "max_notes": 5}
    pack = builder.build(
        request_id="run-budget-test",
        agent_id="test_agent",
        budget=budget,
        results=candidates,
        disclosure_level="sections",
    )

    packed_ids = [r.get("id") for r in pack["results"]]
    traces = load_observed_memory_traces(run_id="run-budget-test", telemetry_dir=tmp_path)

    assert len(traces) == 1
    observed_ids = traces[0].retrieved_memory_ids
    # Crucial acceptance invariant: observed trace matches final pack exactly
    assert observed_ids == packed_ids


def test_real_runtime_pack_builder_integration(tmp_path, monkeypatch):
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path))

    builder = ContextPackBuilder()
    results = [
        {"id": "00_CORE/Storage_Architecture.md", "score": 0.98, "content": "SQLite WAL"},
        {"id": "01_KNOWLEDGE/Memory_Protocol.md", "score": 0.85, "content": "Protocol"},
    ]

    pack = builder.build(
        request_id="run-real-001",
        agent_id="test_agent",
        budget={"soft": 2000, "hard": 4000},
        results=results,
        disclosure_level="full",
    )

    assert len(pack["results"]) == 2
    traces = load_observed_memory_traces(run_id="run-real-001", telemetry_dir=tmp_path)
    assert len(traces) == 1
    assert traces[0].retrieved_memory_ids == [
        "00_CORE/Storage_Architecture.md",
        "01_KNOWLEDGE/Memory_Protocol.md",
    ]
    assert traces[0].retrieval_scores == {
        "00_CORE/Storage_Architecture.md": 0.98,
        "01_KNOWLEDGE/Memory_Protocol.md": 0.85,
    }


def test_declared_vs_observed_reconciliation():
    # Case 1: Clean acknowledgement
    res1 = reconcile_observed_trace(
        declared_memory_ids=["M1", "M2"],
        observed_memory_ids=["M1", "M2"],
    )
    assert res1["status"] == "ACKNOWLEDGED_CLEAN"
    assert res1["acknowledged"] == ["M1", "M2"]
    assert res1["fabrications"] == []
    assert res1["unacknowledged"] == []

    # Case 2: Fabrication detection (Declared M9 does not exist in observed context)
    res2 = reconcile_observed_trace(
        declared_memory_ids=["M1", "M9"],
        observed_memory_ids=["M1"],
    )
    assert res2["status"] == "FABRICATION_DETECTED"
    assert res2["acknowledged"] == ["M1"]
    assert res2["fabrications"] == ["M9"]
    assert res2["unacknowledged"] == []

    # Case 3: Unacknowledged retrieval (M2 was in context but agent never declared it)
    res3 = reconcile_observed_trace(
        declared_memory_ids=["M1"],
        observed_memory_ids=["M1", "M2"],
    )
    assert res3["status"] == "UNACKNOWLEDGED_RETRIEVAL"
    assert res3["acknowledged"] == ["M1"]
    assert res3["fabrications"] == []
    assert res3["unacknowledged"] == ["M2"]


def test_telemetry_failure_safety(monkeypatch):
    """Verify that if telemetry write raises an exception, ContextPackBuilder still succeeds."""
    def broken_record(*args, **kwargs):
        raise IOError("Disk full simulation")

    monkeypatch.setattr("memory_controller.context.pack_builder.record_observed_memory_trace", broken_record)

    builder = ContextPackBuilder()
    pack = builder.build(
        request_id="run-fail-safe",
        agent_id="test_agent",
        budget={"soft": 1000, "hard": 2000},
        results=[{"id": "M1", "content": "Safe content"}],
        disclosure_level="metadata",
    )
    # Context pack must build without raising any error
    assert pack is not None
    assert len(pack["results"]) == 1


def test_data_minimization_no_content_leakage(tmp_path):
    """Verify that observed traces never persist prompt or note body content."""
    record_observed_memory_trace(
        run_id="run-privacy-01",
        results=[
            {"id": "note_secret", "content": "TOP_SECRET_PASSWORD_12345", "score": 0.99},
        ],
        context_size_bytes=500,
        estimated_tokens=100,
        telemetry_dir=tmp_path,
    )

    trace_file = tmp_path / "observed_memory_traces.jsonl"
    raw_text = trace_file.read_text(encoding="utf-8")
    assert "TOP_SECRET_PASSWORD_12345" not in raw_text
    assert "note_secret" in raw_text


def test_four_candidate_exclusion_hardening(tmp_path, monkeypatch):
    """Candidate set [M1, M2, M3, M4] where budget drops M2 and M4 -> trace must contain [M1, M3]."""
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path))

    builder = ContextPackBuilder()
    candidates = [
        {"id": "M1", "content": "Small note 1", "score": 0.95},
        {"id": "M2", "content": "Oversized " * 600, "score": 0.40},
        {"id": "M3", "content": "Small note 3", "score": 0.85},
        {"id": "M4", "content": "Oversized " * 600, "score": 0.30},
    ]

    budget = {"soft": 600, "hard": 800, "max_notes": 4}
    pack = builder.build(
        request_id="run-four-cand",
        agent_id="test_agent",
        budget=budget,
        results=candidates,
        disclosure_level="sections",
    )

    packed_ids = [r.get("id") for r in pack["results"]]
    traces = load_observed_memory_traces(run_id="run-four-cand", telemetry_dir=tmp_path)
    assert len(traces) == 1
    assert traces[0].retrieved_memory_ids == packed_ids
    # Verify M2 and M4 are excluded
    assert "M2" not in traces[0].retrieved_memory_ids
    assert "M4" not in traces[0].retrieved_memory_ids


def test_concurrency_multi_thread_traces(tmp_path, monkeypatch):
    """Concurrent builds with different run_ids must not corrupt or mix trace records."""
    import concurrent.futures
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path))

    builder = ContextPackBuilder()

    def run_worker(worker_id: int):
        run_id = f"run-thread-{worker_id}"
        notes = [{"id": f"note_{worker_id}_{i}", "score": 0.5 + (i * 0.1)} for i in range(3)]
        builder.build(
            request_id=run_id,
            agent_id=f"agent_{worker_id}",
            budget={"soft": 2000, "hard": 4000},
            results=notes,
            disclosure_level="metadata",
        )
        return run_id

    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
        futures = [executor.submit(run_worker, i) for i in range(5)]
        results = [f.result() for f in concurrent.futures.as_completed(futures)]

    # Verify each thread's trace is isolated and correctly logged
    for i in range(5):
        run_id = f"run-thread-{i}"
        traces = load_observed_memory_traces(run_id=run_id, telemetry_dir=tmp_path)
        assert len(traces) == 1
        expected_ids = [f"note_{i}_{j}" for j in range(3)]
        assert traces[0].retrieved_memory_ids == expected_ids


def test_score_integrity_no_recalculation(tmp_path, monkeypatch):
    """Verify scores are directly preserved without recalculation, and un-scored notes have no invented scores."""
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path))

    builder = ContextPackBuilder()
    candidates = [
        {"id": "note_scored_a", "score": 0.7712, "content": "Note A"},
        {"id": "note_scored_b", "relevance_score": 0.9123, "content": "Note B"},
        {"id": "note_unscored", "content": "Note C without score"},
    ]

    builder.build(
        request_id="run-score-test",
        agent_id="test_agent",
        budget={"soft": 2000, "hard": 4000},
        results=candidates,
        disclosure_level="full",
    )

    traces = load_observed_memory_traces(run_id="run-score-test", telemetry_dir=tmp_path)
    assert len(traces) == 1
    scores = traces[0].retrieval_scores
    assert scores["note_scored_a"] == 0.7712
    assert scores["note_scored_b"] == 0.9123
    assert "note_unscored" not in scores  # Score must NOT be invented


def test_telemetry_failure_status_reconciliation():
    """Verify reconciliation behavior when observation is missing or failed."""
    # When observation failed (observed=None), status is OBSERVATION_FAILED
    rec = reconcile_observed_trace(declared_memory_ids=["M1", "M2"], observed_memory_ids=None)
    assert rec["status"] == "OBSERVATION_FAILED"
    assert rec["acknowledged"] == []
    assert rec["fabrications"] == ["M1", "M2"]


def test_legacy_trace_deserialization_and_new_trace_with_project_id(tmp_path):
    """Test backward compatibility: Legacy trace records without project_id parse cleanly with project_id=None."""
    trace_path = tmp_path / "observed_memory_traces.jsonl"

    legacy_json = {
        "run_id": "legacy-trace-001",
        "timestamp": "2026-09-01T12:00:00Z",
        "retrieved_memory_ids": ["note_x", "note_y"],
        "retrieval_scores": {"note_x": 0.99},
        "context_size_bytes": 512,
        "estimated_tokens": 128,
    }
    with open(trace_path, "w", encoding="utf-8") as f:
        f.write(json.dumps(legacy_json) + "\n")

    loaded = load_observed_memory_traces(run_id="legacy-trace-001", telemetry_dir=tmp_path)
    assert len(loaded) == 1
    assert loaded[0].run_id == "legacy-trace-001"
    assert loaded[0].project_id is None

    # Now append a new trace with project_id
    new_trace = record_observed_memory_trace(
        run_id="new-trace-002",
        results=[{"id": "note_z", "score": 0.88}],
        context_size_bytes=256,
        estimated_tokens=64,
        telemetry_dir=tmp_path,
        project_id="PROJ-BETA",
    )
    assert new_trace is not None
    assert new_trace.project_id == "PROJ-BETA"

    # Filter by project_id
    beta_traces = load_observed_memory_traces(project_id="PROJ-BETA", telemetry_dir=tmp_path)
    assert len(beta_traces) == 1
    assert beta_traces[0].run_id == "new-trace-002"


