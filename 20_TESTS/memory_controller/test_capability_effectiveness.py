"""memory_controller/tests/test_capability_effectiveness.py — Test suite for Capability Effectiveness Matrix and Trend Analysis.

Tests cover all required acceptance criteria under the strict ObservedMemoryTrace evidence boundary:
1. Empty matrix
2. Single skill observed + success
3. Single skill observed + fail
4. Partial outcome handling
5. Unknown outcome handling
6. Multiple categories for same skill
7. Multiple capability types (skills, agents, knowledge_refs, procedure_refs)
8. Run deduplication across multiple traces
9. 1/1 -> INSUFFICIENT_DATA
10. 5+ runs -> VALID
11. Wilson reused from effectiveness_stats
12. Laplace smoothing calculation
13. project_id filtering
14. Task category unknown fallback
15. Trend: IMPROVING
16. Trend: STABLE
17. Trend: DEGRADING
18. Trend: Insufficient sample size
19. Determinism
20. Missing outcome record for run
21. Regression Test: OutcomeRecord.observed_capabilities CANNOT create matrix cells without trace
22. Regression Test: Trace-only capability creates matrix cell
"""
import pytest
from datetime import datetime, timedelta, timezone

from memory_controller.capability_effectiveness import (
    effectiveness_matrix,
    effectiveness_trend,
    normalize_capability_type,
)
from memory_controller.memory_trace import ObservedMemoryTrace
from memory_controller.outcome_tracker import OutcomeRecord, Outcome, VerificationMethod


def test_1_empty_matrix():
    """Empty inputs return empty matrix and zero summaries without error."""
    res = effectiveness_matrix(outcome_records=[], traces=[])
    assert res["matrix"] == {}
    assert res["summary"]["total_cells"] == 0
    assert res["summary"]["valid_cells"] == 0
    assert res["summary"]["total_unique_runs"] == 0


def test_2_single_skill_observed_success():
    """Single observed skill with success produces 1 total run and 1 success."""
    rec = OutcomeRecord(
        run_id="run-01",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="frontend_motion",
    )
    t = ObservedMemoryTrace(
        run_id="run-01",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-ANIMATION"],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])
    assert "skills:SKILL-ANIMATION:frontend_motion" in res["matrix"]
    cell = res["matrix"]["skills:SKILL-ANIMATION:frontend_motion"]
    assert cell["total_runs"] == 1
    assert cell["success_runs"] == 1
    assert cell["observed_rate"] == 1.0
    assert cell["status"] == "INSUFFICIENT_DATA"


def test_3_single_skill_observed_fail():
    """Single observed skill with fail produces 1 fail run and 0 observed_rate."""
    rec = OutcomeRecord(
        run_id="run-02",
        outcome=Outcome.FAIL.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="backend_api",
    )
    t = ObservedMemoryTrace(
        run_id="run-02",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-API-ROUTER"],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])
    cell = res["matrix"]["skills:SKILL-API-ROUTER:backend_api"]
    assert cell["total_runs"] == 1
    assert cell["fail_runs"] == 1
    assert cell["success_runs"] == 0
    assert cell["observed_rate"] == 0.0


def test_4_partial_outcome():
    """Partial outcome is counted in partial_runs and does not count as success."""
    rec = OutcomeRecord(
        run_id="run-03",
        outcome=Outcome.PARTIAL.value,
        task_category="database",
    )
    t = ObservedMemoryTrace(
        run_id="run-03",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-SQLITE-WAL"],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])
    cell = res["matrix"]["skills:SKILL-SQLITE-WAL:database"]
    assert cell["total_runs"] == 1
    assert cell["partial_runs"] == 1
    assert cell["success_runs"] == 0
    assert cell["observed_rate"] == 0.0


def test_5_unknown_outcome():
    """Unknown outcome is counted in unknown_runs and does not count as success."""
    rec = OutcomeRecord(
        run_id="run-04",
        outcome=Outcome.UNKNOWN.value,
        task_category="testing",
    )
    t = ObservedMemoryTrace(
        run_id="run-04",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-UNIT-TEST"],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])
    cell = res["matrix"]["skills:SKILL-UNIT-TEST:testing"]
    assert cell["total_runs"] == 1
    assert cell["unknown_runs"] == 1
    assert cell["success_runs"] == 0
    assert cell["observed_rate"] == 0.0


def test_6_multiple_categories_for_same_skill():
    """Same skill across different categories produces separate, non-aggregated matrix cells."""
    records = []
    traces = []
    # 10 successes out of 12 for frontend_motion
    for i in range(12):
        outcome = Outcome.SUCCESS.value if i < 10 else Outcome.FAIL.value
        v_method = VerificationMethod.TEST_PASS.value if i < 10 else VerificationMethod.NONE.value
        r_id = f"run-motion-{i}"
        records.append(
            OutcomeRecord(
                run_id=r_id,
                outcome=outcome,
                verification_method=v_method,
                task_category="frontend_motion",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp="2026-09-02T10:00:00Z",
                retrieved_memory_ids=["SKILL-FRONTEND-ANIMATION"],
            )
        )
    # 2 successes out of 10 for backend_api
    for i in range(10):
        outcome = Outcome.SUCCESS.value if i < 2 else Outcome.FAIL.value
        v_method = VerificationMethod.TEST_PASS.value if i < 2 else VerificationMethod.NONE.value
        r_id = f"run-backend-{i}"
        records.append(
            OutcomeRecord(
                run_id=r_id,
                outcome=outcome,
                verification_method=v_method,
                task_category="backend_api",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp="2026-09-02T10:00:00Z",
                retrieved_memory_ids=["SKILL-FRONTEND-ANIMATION"],
            )
        )

    res = effectiveness_matrix(outcome_records=records, traces=traces)
    motion_cell = res["matrix"]["skills:SKILL-FRONTEND-ANIMATION:frontend_motion"]
    backend_cell = res["matrix"]["skills:SKILL-FRONTEND-ANIMATION:backend_api"]

    assert motion_cell["total_runs"] == 12
    assert motion_cell["success_runs"] == 10
    assert abs(motion_cell["observed_rate"] - (10.0 / 12.0)) < 0.001
    assert motion_cell["status"] == "VALID"

    assert backend_cell["total_runs"] == 10
    assert backend_cell["success_runs"] == 2
    assert abs(backend_cell["observed_rate"] - 0.20) < 0.001
    assert backend_cell["status"] == "VALID"


def test_7_multiple_capability_types():
    """Matrix supports skills, agents, knowledge_refs, and procedure_refs independently."""
    rec = OutcomeRecord(
        run_id="run-multi-type",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="security_audit",
    )
    t = ObservedMemoryTrace(
        run_id="run-multi-type",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=[
            "SKILL-AUDIT-VULN",
            "AGENT-CRITIC",
            "00_CORE/Storage_Architecture.md",
            "03_PROCEDURES/Import_Sanitization.md",
        ],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])
    assert "skills:SKILL-AUDIT-VULN:security_audit" in res["matrix"]
    assert "agents:AGENT-CRITIC:security_audit" in res["matrix"]
    assert "knowledge_refs:00_CORE/Storage_Architecture.md:security_audit" in res["matrix"]
    assert "procedure_refs:03_PROCEDURES/Import_Sanitization.md:security_audit" in res["matrix"]


def test_8_run_deduplication_across_traces():
    """Two traces for the same run_id referencing the same skill produce exactly 1 total run."""
    rec = OutcomeRecord(
        run_id="run-dup-01",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="frontend_layout",
    )
    t1 = ObservedMemoryTrace(
        run_id="run-dup-01",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-GRID-LAYOUT"],
    )
    t2 = ObservedMemoryTrace(
        run_id="run-dup-01",
        timestamp="2026-09-02T10:00:01Z",
        retrieved_memory_ids=["SKILL-GRID-LAYOUT", "SKILL-GRID-LAYOUT"],
    )

    res = effectiveness_matrix(outcome_records=[rec], traces=[t1, t2])
    cell = res["matrix"]["skills:SKILL-GRID-LAYOUT:frontend_layout"]
    assert cell["total_runs"] == 1
    assert cell["success_runs"] == 1


def test_9_one_out_of_one_is_insufficient_data():
    """1/1 success rate must be flagged INSUFFICIENT_DATA."""
    rec = OutcomeRecord(
        run_id="run-one",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="trading_logic",
    )
    t = ObservedMemoryTrace(
        run_id="run-one",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-QUANT"],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])
    cell = res["matrix"]["skills:SKILL-QUANT:trading_logic"]
    assert cell["total_runs"] == 1
    assert cell["observed_rate"] == 1.0
    assert cell["status"] == "INSUFFICIENT_DATA"


def test_10_five_runs_is_valid():
    """5 runs (MIN_SAMPLE_SIZE) reaches VALID status."""
    records = [
        OutcomeRecord(
            run_id=f"run-five-{i}",
            outcome=Outcome.SUCCESS.value,
            verification_method=VerificationMethod.TEST_PASS.value,
            task_category="testing",
        )
        for i in range(5)
    ]
    traces = [
        ObservedMemoryTrace(
            run_id=f"run-five-{i}",
            timestamp="2026-09-02T10:00:00Z",
            retrieved_memory_ids=["SKILL-PYTEST"],
        )
        for i in range(5)
    ]
    res = effectiveness_matrix(outcome_records=records, traces=traces)
    cell = res["matrix"]["skills:SKILL-PYTEST:testing"]
    assert cell["total_runs"] == 5
    assert cell["status"] == "VALID"


def test_11_wilson_reused_from_effectiveness_stats():
    """Wilson lower bound matches the exact formula output for 10/12."""
    records = []
    traces = []
    for i in range(12):
        r_id = f"run-w-{i}"
        records.append(
            OutcomeRecord(
                run_id=r_id,
                outcome=Outcome.SUCCESS.value if i < 10 else Outcome.FAIL.value,
                verification_method=VerificationMethod.TEST_PASS.value if i < 10 else VerificationMethod.NONE.value,
                task_category="documentation",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp="2026-09-02T10:00:00Z",
                retrieved_memory_ids=["SKILL-DOCS"],
            )
        )
    res = effectiveness_matrix(outcome_records=records, traces=traces)
    cell = res["matrix"]["skills:SKILL-DOCS:documentation"]
    assert abs(cell["wilson_lower_bound"] - 0.552) < 0.005


def test_12_laplace_reused():
    """Smoothed rate matches (successes + 1) / (trials + 2)."""
    rec = OutcomeRecord(
        run_id="run-laplace",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="infra_devops",
    )
    t = ObservedMemoryTrace(
        run_id="run-laplace",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-ANSIBLE"],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])
    cell = res["matrix"]["skills:SKILL-ANSIBLE:infra_devops"]
    # 1 success / 1 trial -> (1+1)/(1+2) = 2/3 ~ 0.6667
    assert abs(cell["smoothed_rate"] - (2.0 / 3.0)) < 0.001


def test_13_project_id_filtering():
    """Matrix limits aggregation strictly to the requested project_id."""
    r1 = OutcomeRecord(
        run_id="r1",
        project_id="PROJ-ALPHA",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="backend_api",
    )
    t1 = ObservedMemoryTrace(
        run_id="r1",
        project_id="PROJ-ALPHA",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-ROUTER"],
    )
    r2 = OutcomeRecord(
        run_id="r2",
        project_id="PROJ-BETA",
        outcome=Outcome.FAIL.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="backend_api",
    )
    t2 = ObservedMemoryTrace(
        run_id="r2",
        project_id="PROJ-BETA",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-ROUTER"],
    )

    res_alpha = effectiveness_matrix(outcome_records=[r1, r2], traces=[t1, t2], project_id="PROJ-ALPHA")
    cell = res_alpha["matrix"]["skills:SKILL-ROUTER:backend_api"]
    assert cell["total_runs"] == 1
    assert cell["success_runs"] == 1

    res_beta = effectiveness_matrix(outcome_records=[r1, r2], traces=[t1, t2], project_id="PROJ-BETA")
    cell_beta = res_beta["matrix"]["skills:SKILL-ROUTER:backend_api"]
    assert cell_beta["total_runs"] == 1
    assert cell_beta["fail_runs"] == 1


def test_14_task_category_unknown_fallback():
    """Default task category falls back cleanly to 'unknown'."""
    rec = OutcomeRecord(
        run_id="r-unk",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
    )
    t = ObservedMemoryTrace(
        run_id="r-unk",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-GENERAL"],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])
    assert "skills:SKILL-GENERAL:unknown" in res["matrix"]
    assert res["matrix"]["skills:SKILL-GENERAL:unknown"]["task_category"] == "unknown"


def test_15_trend_improving():
    """Trend analysis flags IMPROVING when recent window success rate is substantially higher."""
    base_time = datetime(2026, 9, 1, 10, 0, 0, tzinfo=timezone.utc)
    records = []
    traces = []
    # Previous window (5 runs): 1 success / 5 (rate = 0.20)
    for i in range(5):
        r_id = f"run-prev-{i}"
        records.append(
            OutcomeRecord(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                outcome=Outcome.SUCCESS.value if i == 0 else Outcome.FAIL.value,
                verification_method=VerificationMethod.TEST_PASS.value if i == 0 else VerificationMethod.NONE.value,
                task_category="frontend_motion",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                retrieved_memory_ids=["SKILL-MOTION"],
            )
        )
    # Recent window (5 runs): 5 successes / 5 (rate = 1.00)
    for i in range(5):
        r_id = f"run-recent-{i}"
        records.append(
            OutcomeRecord(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=5 + i)).isoformat(),
                outcome=Outcome.SUCCESS.value,
                verification_method=VerificationMethod.TEST_PASS.value,
                task_category="frontend_motion",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=5 + i)).isoformat(),
                retrieved_memory_ids=["SKILL-MOTION"],
            )
        )

    res = effectiveness_trend(
        capability_type="skills",
        capability_id="SKILL-MOTION",
        task_category="frontend_motion",
        outcome_records=records,
        traces=traces,
        window_size=5,
    )
    assert res["trend"] == "IMPROVING"
    assert res["status"] == "VALID"
    assert res["previous_rate"] == 0.20
    assert res["recent_rate"] == 1.00
    assert res["rate_delta"] == 0.80


def test_16_trend_stable():
    """Trend analysis flags STABLE when delta is within tolerance."""
    base_time = datetime(2026, 9, 1, 10, 0, 0, tzinfo=timezone.utc)
    records = []
    traces = []
    for i in range(10):
        r_id = f"run-stable-{i}"
        records.append(
            OutcomeRecord(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                outcome=Outcome.SUCCESS.value,
                verification_method=VerificationMethod.TEST_PASS.value,
                task_category="backend_api",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                retrieved_memory_ids=["SKILL-STABLE"],
            )
        )

    res = effectiveness_trend(
        capability_type="skills",
        capability_id="SKILL-STABLE",
        outcome_records=records,
        traces=traces,
        window_size=5,
    )
    assert res["trend"] == "STABLE"
    assert res["rate_delta"] == 0.0


def test_17_trend_degrading():
    """Trend analysis flags DEGRADING when recent window success rate drops."""
    base_time = datetime(2026, 9, 1, 10, 0, 0, tzinfo=timezone.utc)
    records = []
    traces = []
    # Previous window (5 runs): 5 successes / 5
    for i in range(5):
        r_id = f"run-deg-prev-{i}"
        records.append(
            OutcomeRecord(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                outcome=Outcome.SUCCESS.value,
                verification_method=VerificationMethod.TEST_PASS.value,
                task_category="database",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                retrieved_memory_ids=["SKILL-DB"],
            )
        )
    # Recent window (5 runs): 0 successes / 5
    for i in range(5):
        r_id = f"run-deg-rec-{i}"
        records.append(
            OutcomeRecord(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=5 + i)).isoformat(),
                outcome=Outcome.FAIL.value,
                task_category="database",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp=(base_time + timedelta(hours=5 + i)).isoformat(),
                retrieved_memory_ids=["SKILL-DB"],
            )
        )

    res = effectiveness_trend(
        capability_type="skills",
        capability_id="SKILL-DB",
        outcome_records=records,
        traces=traces,
        window_size=5,
    )
    assert res["trend"] == "DEGRADING"
    assert res["previous_rate"] == 1.00
    assert res["recent_rate"] == 0.00
    assert res["rate_delta"] == -1.00


def test_18_trend_insufficient_sample_size():
    """Trend returns INSUFFICIENT_DATA when total runs < 2 * window_size."""
    records = [
        OutcomeRecord(
            run_id=f"r-trend-small-{i}",
            outcome=Outcome.SUCCESS.value,
            verification_method=VerificationMethod.TEST_PASS.value,
        )
        for i in range(4)
    ]
    traces = [
        ObservedMemoryTrace(
            run_id=f"r-trend-small-{i}",
            timestamp="2026-09-02T10:00:00Z",
            retrieved_memory_ids=["SKILL-SMALL"],
        )
        for i in range(4)
    ]
    res = effectiveness_trend(
        capability_type="skills",
        capability_id="SKILL-SMALL",
        outcome_records=records,
        traces=traces,
        window_size=5,
    )
    assert res["trend"] == "INSUFFICIENT_DATA"
    assert res["status"] == "INSUFFICIENT_DATA"


def test_19_determinism():
    """Repeated calls produce identical dictionary structures."""
    records = [
        OutcomeRecord(
            run_id=f"run-det-{i}",
            outcome=Outcome.SUCCESS.value if i % 2 == 0 else Outcome.FAIL.value,
            verification_method=VerificationMethod.TEST_PASS.value if i % 2 == 0 else VerificationMethod.NONE.value,
            task_category="security_audit",
        )
        for i in range(8)
    ]
    traces = [
        ObservedMemoryTrace(
            run_id=f"run-det-{i}",
            timestamp="2026-09-02T10:00:00Z",
            retrieved_memory_ids=["AGENT-AUDITOR"],
        )
        for i in range(8)
    ]
    res1 = effectiveness_matrix(outcome_records=records, traces=traces)
    res2 = effectiveness_matrix(outcome_records=records, traces=traces)
    assert res1 == res2


def test_20_missing_outcome_record_for_run():
    """If trace exists for a run_id without an OutcomeRecord, outcome defaults to unknown."""
    t = ObservedMemoryTrace(
        run_id="run-trace-only",
        timestamp="2026-09-02T12:00:00Z",
        retrieved_memory_ids=["SKILL-LONE-TRACE"],
    )
    res = effectiveness_matrix(outcome_records=[], traces=[t])
    cell = res["matrix"]["skills:SKILL-LONE-TRACE:unknown"]
    assert cell["total_runs"] == 1
    assert cell["unknown_runs"] == 1
    assert cell["success_runs"] == 0
    assert cell["observed_rate"] == 0.0
    assert cell["status"] == "INSUFFICIENT_DATA"


def test_21_regression_declared_in_outcome_cannot_create_matrix_cell():
    """Task 3.1 Hard Invariant: OutcomeRecord.observed_capabilities CANNOT create matrix cells without trace."""
    rec = OutcomeRecord(
        run_id="run-X",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="backend_api",
        observed_capabilities={"skills": ["SKILL-NOT-IN-TRACE"]},
    )
    t = ObservedMemoryTrace(
        run_id="run-X",
        timestamp="2026-09-02T12:00:00Z",
        retrieved_memory_ids=[],  # Empty trace
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])

    # SKILL-NOT-IN-TRACE must NOT appear in matrix
    assert "skills:SKILL-NOT-IN-TRACE:backend_api" not in res["matrix"]
    assert res["matrix"] == {}


def test_22_regression_trace_only_creates_matrix_cell():
    """Task 3.1 Invariant: ObservedMemoryTrace.retrieved_memory_ids creates matrix cell even if OutcomeRecord has empty capabilities."""
    rec = OutcomeRecord(
        run_id="run-Y",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="frontend_motion",
        observed_capabilities={},  # Empty declared
    )
    t = ObservedMemoryTrace(
        run_id="run-Y",
        timestamp="2026-09-02T12:00:00Z",
        retrieved_memory_ids=["SKILL-IN-TRACE"],
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[t])

    # SKILL-IN-TRACE MUST appear in matrix
    assert "skills:SKILL-IN-TRACE:frontend_motion" in res["matrix"]
    cell = res["matrix"]["skills:SKILL-IN-TRACE:frontend_motion"]
    assert cell["total_runs"] == 1
    assert cell["success_runs"] == 1
    assert cell["observed_rate"] == 1.0
