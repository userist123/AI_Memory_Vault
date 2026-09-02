"""memory_controller/tests/test_capability_effectiveness.py — Test suite for Capability Effectiveness Matrix and Trend Analysis.

Tests cover all 20 required acceptance criteria:
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
        observed_capabilities={"skills": ["SKILL-ANIMATION"]},
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[])
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
        observed_capabilities={"skills": ["SKILL-API-ROUTER"]},
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[])
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
        observed_capabilities={"skills": ["SKILL-SQLITE-WAL"]},
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[])
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
        observed_capabilities={"skills": ["SKILL-UNIT-TEST"]},
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[])
    cell = res["matrix"]["skills:SKILL-UNIT-TEST:testing"]
    assert cell["total_runs"] == 1
    assert cell["unknown_runs"] == 1
    assert cell["success_runs"] == 0
    assert cell["observed_rate"] == 0.0


def test_6_multiple_categories_for_same_skill():
    """Same skill across different categories produces separate, non-aggregated matrix cells."""
    records = []
    # 10 successes out of 12 for frontend_motion
    for i in range(12):
        outcome = Outcome.SUCCESS.value if i < 10 else Outcome.FAIL.value
        v_method = VerificationMethod.TEST_PASS.value if i < 10 else VerificationMethod.NONE.value
        records.append(
            OutcomeRecord(
                run_id=f"run-motion-{i}",
                outcome=outcome,
                verification_method=v_method,
                task_category="frontend_motion",
                observed_capabilities={"skills": ["frontend-animation"]},
            )
        )
    # 2 successes out of 10 for backend_api
    for i in range(10):
        outcome = Outcome.SUCCESS.value if i < 2 else Outcome.FAIL.value
        v_method = VerificationMethod.TEST_PASS.value if i < 2 else VerificationMethod.NONE.value
        records.append(
            OutcomeRecord(
                run_id=f"run-backend-{i}",
                outcome=outcome,
                verification_method=v_method,
                task_category="backend_api",
                observed_capabilities={"skills": ["frontend-animation"]},
            )
        )

    res = effectiveness_matrix(outcome_records=records, traces=[])
    motion_cell = res["matrix"]["skills:frontend-animation:frontend_motion"]
    backend_cell = res["matrix"]["skills:frontend-animation:backend_api"]

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
        observed_capabilities={
            "skills": ["SKILL-AUDIT-VULN"],
            "agents": ["AGENT-CRITIC"],
            "knowledge_refs": ["00_CORE/Storage_Architecture.md"],
            "procedure_refs": ["03_PROCEDURES/Import_Sanitization.md"],
        },
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[])
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
        observed_capabilities={"skills": ["SKILL-QUANT"]},
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[])
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
            observed_capabilities={"skills": ["SKILL-PYTEST"]},
        )
        for i in range(5)
    ]
    res = effectiveness_matrix(outcome_records=records, traces=[])
    cell = res["matrix"]["skills:SKILL-PYTEST:testing"]
    assert cell["total_runs"] == 5
    assert cell["status"] == "VALID"


def test_11_wilson_reused_from_effectiveness_stats():
    """Wilson lower bound matches the exact formula output for 10/12."""
    records = []
    for i in range(12):
        records.append(
            OutcomeRecord(
                run_id=f"run-w-{i}",
                outcome=Outcome.SUCCESS.value if i < 10 else Outcome.FAIL.value,
                verification_method=VerificationMethod.TEST_PASS.value if i < 10 else VerificationMethod.NONE.value,
                task_category="documentation",
                observed_capabilities={"skills": ["SKILL-DOCS"]},
            )
        )
    res = effectiveness_matrix(outcome_records=records, traces=[])
    cell = res["matrix"]["skills:SKILL-DOCS:documentation"]
    assert abs(cell["wilson_lower_bound"] - 0.552) < 0.005


def test_12_laplace_reused():
    """Smoothed rate matches (successes + 1) / (trials + 2)."""
    rec = OutcomeRecord(
        run_id="run-laplace",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="infra_devops",
        observed_capabilities={"skills": ["SKILL-ANSIBLE"]},
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[])
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
        observed_capabilities={"skills": ["SKILL-ROUTER"]},
    )
    r2 = OutcomeRecord(
        run_id="r2",
        project_id="PROJ-BETA",
        outcome=Outcome.FAIL.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="backend_api",
        observed_capabilities={"skills": ["SKILL-ROUTER"]},
    )

    res_alpha = effectiveness_matrix(outcome_records=[r1, r2], project_id="PROJ-ALPHA", traces=[])
    cell = res_alpha["matrix"]["skills:SKILL-ROUTER:backend_api"]
    assert cell["total_runs"] == 1
    assert cell["success_runs"] == 1

    res_beta = effectiveness_matrix(outcome_records=[r1, r2], project_id="PROJ-BETA", traces=[])
    cell_beta = res_beta["matrix"]["skills:SKILL-ROUTER:backend_api"]
    assert cell_beta["total_runs"] == 1
    assert cell_beta["fail_runs"] == 1


def test_14_task_category_unknown_fallback():
    """Default task category falls back cleanly to 'unknown'."""
    rec = OutcomeRecord(
        run_id="r-unk",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        observed_capabilities={"skills": ["SKILL-GENERAL"]},
    )
    res = effectiveness_matrix(outcome_records=[rec], traces=[])
    assert "skills:SKILL-GENERAL:unknown" in res["matrix"]
    assert res["matrix"]["skills:SKILL-GENERAL:unknown"]["task_category"] == "unknown"




def test_15_trend_improving():
    """Trend analysis flags IMPROVING when recent window success rate is substantially higher."""
    base_time = datetime(2026, 9, 1, 10, 0, 0, tzinfo=timezone.utc)
    records = []
    # Previous window (5 runs): 1 success / 5 (rate = 0.20)
    for i in range(5):
        records.append(
            OutcomeRecord(
                run_id=f"run-prev-{i}",
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                outcome=Outcome.SUCCESS.value if i == 0 else Outcome.FAIL.value,
                verification_method=VerificationMethod.TEST_PASS.value if i == 0 else VerificationMethod.NONE.value,
                task_category="frontend_motion",
                observed_capabilities={"skills": ["SKILL-MOTION"]},
            )
        )
    # Recent window (5 runs): 5 successes / 5 (rate = 1.00)
    for i in range(5):
        records.append(
            OutcomeRecord(
                run_id=f"run-recent-{i}",
                timestamp=(base_time + timedelta(hours=5 + i)).isoformat(),
                outcome=Outcome.SUCCESS.value,
                verification_method=VerificationMethod.TEST_PASS.value,
                task_category="frontend_motion",
                observed_capabilities={"skills": ["SKILL-MOTION"]},
            )
        )

    res = effectiveness_trend(
        capability_type="skills",
        capability_id="SKILL-MOTION",
        task_category="frontend_motion",
        outcome_records=records,
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
    for i in range(10):
        records.append(
            OutcomeRecord(
                run_id=f"run-stable-{i}",
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                outcome=Outcome.SUCCESS.value,
                verification_method=VerificationMethod.TEST_PASS.value,
                task_category="backend_api",
                observed_capabilities={"skills": ["SKILL-STABLE"]},
            )
        )

    res = effectiveness_trend(
        capability_type="skills",
        capability_id="SKILL-STABLE",
        outcome_records=records,
        window_size=5,
    )
    assert res["trend"] == "STABLE"
    assert res["rate_delta"] == 0.0


def test_17_trend_degrading():
    """Trend analysis flags DEGRADING when recent window success rate drops."""
    base_time = datetime(2026, 9, 1, 10, 0, 0, tzinfo=timezone.utc)
    records = []
    # Previous window (5 runs): 5 successes / 5
    for i in range(5):
        records.append(
            OutcomeRecord(
                run_id=f"run-deg-prev-{i}",
                timestamp=(base_time + timedelta(hours=i)).isoformat(),
                outcome=Outcome.SUCCESS.value,
                verification_method=VerificationMethod.TEST_PASS.value,
                task_category="database",
                observed_capabilities={"skills": ["SKILL-DB"]},
            )
        )
    # Recent window (5 runs): 0 successes / 5
    for i in range(5):
        records.append(
            OutcomeRecord(
                run_id=f"run-deg-rec-{i}",
                timestamp=(base_time + timedelta(hours=5 + i)).isoformat(),
                outcome=Outcome.FAIL.value,
                task_category="database",
                observed_capabilities={"skills": ["SKILL-DB"]},
            )
        )

    res = effectiveness_trend(
        capability_type="skills",
        capability_id="SKILL-DB",
        outcome_records=records,
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
            observed_capabilities={"skills": ["SKILL-SMALL"]},
        )
        for i in range(4)
    ]
    res = effectiveness_trend(
        capability_type="skills",
        capability_id="SKILL-SMALL",
        outcome_records=records,
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
            observed_capabilities={"agents": ["AGENT-AUDITOR"]},
        )
        for i in range(8)
    ]
    res1 = effectiveness_matrix(outcome_records=records, traces=[])
    res2 = effectiveness_matrix(outcome_records=records, traces=[])
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
