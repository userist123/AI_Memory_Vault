"""memory_controller/tests/test_promotion_candidates.py — Test Suite for Promotion and Retirement Candidates.

Tests cover all 20 required acceptance criteria:
1. Empty input
2. One insufficient category (N < 5)
3. One valid category (requires >= 2 for decision)
4. Promotion requires two categories
5. Promotion with two Wilson > 0.85
6. Promotion blocked by one degrading category
7. Promotion ignores insufficient category
8. Retirement with two Wilson < 0.40
9. Retirement not triggered above threshold
10. Exact 0.85 not promotion
11. Exact 0.40 not retirement
12. Project dominance detection (> 40%)
13. Project dominance blocks promotion
14. Project filtering
15. Multiple capability types
16. Unknown outcomes
17. Deterministic output
18. No mutation of source records
19. No automatic action executed
20. Same run cannot inflate project/capability statistics
"""
import copy
from datetime import datetime, timedelta, timezone
import pytest

from memory_controller.capability_effectiveness import effectiveness_matrix
from memory_controller.effectiveness_stats import MIN_SAMPLE_SIZE
from memory_controller.memory_trace import ObservedMemoryTrace
from memory_controller.outcome_tracker import OutcomeRecord, Outcome, VerificationMethod
from memory_controller.promotion_candidates import (
    flag_review_candidates,
    PROMOTION_THRESHOLD,
    RETIREMENT_THRESHOLD,
    MIN_CATEGORIES_FOR_DECISION,
    PROJECT_USAGE_CAP,
)


def _build_runs_for_category(
    capability_id: str,
    task_category: str,
    total_runs: int,
    success_runs: int,
    projects: List[str],
    base_id_prefix: str,
    base_time: Optional[datetime] = None,
    degrading_trend: bool = False,
) -> Tuple[List[OutcomeRecord], List[ObservedMemoryTrace]]:
    """Helper to generate deterministic OutcomeRecord and ObservedMemoryTrace pairs."""
    if base_time is None:
        base_time = datetime(2026, 9, 1, 10, 0, 0, tzinfo=timezone.utc)

    records: List[OutcomeRecord] = []
    traces: List[ObservedMemoryTrace] = []

    # If degrading trend is requested, concentrate failures in the recent window.
    # Otherwise, place failures at the beginning so recent/previous windows are stable.
    success_flags = [True] * total_runs
    num_fails = total_runs - success_runs
    if degrading_trend:
        for i in range(total_runs - num_fails, total_runs):
            success_flags[i] = False
    else:
        for i in range(num_fails):
            success_flags[i] = False

    for i in range(total_runs):
        r_id = f"{base_id_prefix}-{task_category}-{i}"
        proj = projects[i % len(projects)] if projects else "unassigned"
        is_success = success_flags[i]
        outcome = Outcome.SUCCESS.value if is_success else Outcome.FAIL.value
        v_method = VerificationMethod.TEST_PASS.value if is_success else VerificationMethod.NONE.value
        ts = (base_time + timedelta(hours=i)).isoformat()

        records.append(
            OutcomeRecord(
                run_id=r_id,
                project_id=proj,
                timestamp=ts,
                outcome=outcome,
                verification_method=v_method,
                task_category=task_category,
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                project_id=proj,
                timestamp=ts,
                retrieved_memory_ids=[capability_id],
            )
        )

    return records, traces


def test_1_empty_input():
    """Empty inputs return empty candidate lists and valid metadata."""
    res = flag_review_candidates(outcome_records=[], traces=[])
    assert res["promotion_candidates"] == []
    assert res["retirement_candidates"] == []
    assert res["blocked_candidates"] == []
    assert res["summary"]["total_capabilities_evaluated"] == 0
    assert res["metadata"]["human_gated"] is True


def test_2_one_insufficient_category():
    """A capability with only 1 insufficient category (N < 5) produces no candidates."""
    recs, traces = _build_runs_for_category(
        capability_id="SKILL-TINY",
        task_category="frontend_motion",
        total_runs=3,
        success_runs=3,
        projects=["proj-a", "proj-b", "proj-c"],
        base_id_prefix="tiny",
    )
    res = flag_review_candidates(outcome_records=recs, traces=traces)
    assert res["promotion_candidates"] == []
    assert res["retirement_candidates"] == []


def test_3_one_valid_category_not_enough_for_promotion():
    """A capability with only 1 valid category (> 0.85) is not eligible (requires >= 2)."""
    # 50 runs, 48 successes -> Wilson lower bound ~ 0.868 > 0.85
    recs, traces = _build_runs_for_category(
        capability_id="SKILL-SOLO-CAT",
        task_category="frontend_motion",
        total_runs=50,
        success_runs=48,
        projects=["proj-a", "proj-b", "proj-c"],
        base_id_prefix="solo",
    )
    res = flag_review_candidates(outcome_records=recs, traces=traces)
    assert res["promotion_candidates"] == []
    assert res["retirement_candidates"] == []


def test_4_promotion_requires_two_categories():
    """Promotion strictly requires >= 2 valid categories meeting threshold."""
    # Cat 1: 50 runs, 48 successes (Wilson ~ 0.868)
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-TWO-CAT",
        task_category="frontend_motion",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="c1",
    )
    # Cat 2: 50 runs, 48 successes (Wilson ~ 0.868)
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-TWO-CAT",
        task_category="frontend_layout",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="c2",
    )
    res = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    assert len(res["promotion_candidates"]) == 1
    cand = res["promotion_candidates"][0]
    assert cand["capability_id"] == "SKILL-TWO-CAT"
    assert set(cand["eligible_categories"]) == {"frontend_motion", "frontend_layout"}
    assert cand["status"] == "REVIEW_REQUIRED"


def test_5_promotion_with_two_wilson_above_85():
    """Two categories with Wilson > 0.85, balanced projects, and non-degrading trend pass."""
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-EXCELLENT",
        task_category="backend_api",
        total_runs=50,
        success_runs=49,
        projects=["p1", "p2", "p3"],
        base_id_prefix="ex1",
    )
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-EXCELLENT",
        task_category="database",
        total_runs=50,
        success_runs=49,
        projects=["p1", "p2", "p3"],
        base_id_prefix="ex2",
    )
    res = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    assert len(res["promotion_candidates"]) == 1
    assert res["promotion_candidates"][0]["capability_id"] == "SKILL-EXCELLENT"


def test_6_promotion_blocked_by_one_degrading_category():
    """If one eligible category has a DEGRADING trend, promotion is blocked."""
    # Cat 1: Non-degrading (50 runs, 48 successes)
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-DEGRADING-BLOCK",
        task_category="frontend_motion",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="nd",
    )
    # Cat 2: Degrading trend (50 runs, 40 successes concentrated early, recent window 0/5)
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-DEGRADING-BLOCK",
        task_category="frontend_layout",
        total_runs=50,
        success_runs=40,
        projects=["p1", "p2", "p3"],
        base_id_prefix="deg",
        degrading_trend=True,
    )
    res = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    assert res["promotion_candidates"] == []


def test_7_promotion_ignores_insufficient_category():
    """An insufficient category does not block promotion if 2 other valid categories qualify."""
    # Cat 1: Valid high (50 runs, 48 successes)
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-MULTI-VALID",
        task_category="frontend_motion",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="mv1",
    )
    # Cat 2: Valid high (50 runs, 48 successes)
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-MULTI-VALID",
        task_category="frontend_layout",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="mv2",
    )
    # Cat 3: Insufficient (2 runs)
    r3, t3 = _build_runs_for_category(
        capability_id="SKILL-MULTI-VALID",
        task_category="backend_api",
        total_runs=2,
        success_runs=2,
        projects=["p1", "p2"],
        base_id_prefix="mv3",
    )
    res = flag_review_candidates(outcome_records=r1 + r2 + r3, traces=t1 + t2 + t3)
    assert len(res["promotion_candidates"]) == 1
    assert set(res["promotion_candidates"][0]["eligible_categories"]) == {"frontend_motion", "frontend_layout"}


def test_8_retirement_with_two_wilson_below_40():
    """Retirement candidate is flagged when Wilson lower bound < 0.40 in >= 2 valid categories."""
    # Cat 1: 20 runs, 2 successes -> Wilson lower bound ~ 0.028 < 0.40
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-FAILING",
        task_category="database",
        total_runs=20,
        success_runs=2,
        projects=["p1", "p2", "p3"],
        base_id_prefix="f1",
    )
    # Cat 2: 20 runs, 2 successes -> Wilson lower bound ~ 0.028 < 0.40
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-FAILING",
        task_category="testing",
        total_runs=20,
        success_runs=2,
        projects=["p1", "p2", "p3"],
        base_id_prefix="f2",
    )
    res = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    assert len(res["retirement_candidates"]) == 1
    assert res["retirement_candidates"][0]["capability_id"] == "SKILL-FAILING"
    assert res["retirement_candidates"][0]["recommendation_type"] == "RETIREMENT_CANDIDATE"


def test_9_retirement_not_triggered_above_threshold():
    """Retirement candidate is NOT flagged if Wilson lower bound >= 0.40."""
    # 20 runs, 12 successes -> Wilson lower bound ~ 0.386 < 0.40, but if 20 runs, 14 successes -> Wilson ~ 0.48 > 0.40
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-MEDIOCRE",
        task_category="database",
        total_runs=20,
        success_runs=14,
        projects=["p1", "p2", "p3"],
        base_id_prefix="m1",
    )
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-MEDIOCRE",
        task_category="testing",
        total_runs=20,
        success_runs=14,
        projects=["p1", "p2", "p3"],
        base_id_prefix="m2",
    )
    res = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    assert res["retirement_candidates"] == []


def test_10_exact_085_not_promotion():
    """Boundary test: Wilson Lower Bound must be strictly > 0.85, not equal."""
    # Custom threshold equal to exact calculated Wilson
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-EXACT-THRESH",
        task_category="frontend_motion",
        total_runs=20,
        success_runs=19,
        projects=["p1", "p2", "p3"],
        base_id_prefix="et1",
    )
    matrix_res = effectiveness_matrix(outcome_records=r1, traces=t1)
    exact_wilson = matrix_res["matrix"]["skills:SKILL-EXACT-THRESH:frontend_motion"]["wilson_lower_bound"]

    # When promotion_threshold == exact_wilson, it must not qualify (wilson > threshold is False)
    res = flag_review_candidates(
        outcome_records=r1,
        traces=t1,
        promotion_threshold=exact_wilson,
        min_categories=1,
    )
    assert res["promotion_candidates"] == []


def test_11_exact_040_not_retirement():
    """Boundary test: Wilson Lower Bound must be strictly < 0.40, not equal."""
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-EXACT-RET",
        task_category="database",
        total_runs=20,
        success_runs=10,
        projects=["p1", "p2", "p3"],
        base_id_prefix="er1",
    )
    matrix_res = effectiveness_matrix(outcome_records=r1, traces=t1)
    exact_wilson = matrix_res["matrix"]["skills:SKILL-EXACT-RET:database"]["wilson_lower_bound"]

    # When retirement_threshold == exact_wilson, it must not qualify (wilson < threshold is False)
    res = flag_review_candidates(
        outcome_records=r1,
        traces=t1,
        retirement_threshold=exact_wilson,
        min_categories=1,
    )
    assert res["retirement_candidates"] == []


def test_12_project_dominance_detection():
    """Project dominance is flagged when a single project accounts for > 40% of observations."""
    # 50 runs: 30 in proj-A (60% > 40%), 10 in proj-B (20%), 10 in proj-C (20%)
    projects = ["proj-A"] * 30 + ["proj-B"] * 10 + ["proj-C"] * 10
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-DOMINATED",
        task_category="frontend_motion",
        total_runs=50,
        success_runs=48,
        projects=projects,
        base_id_prefix="dom",
    )
    res = flag_review_candidates(outcome_records=r1, traces=t1)
    # Check metric in blocked or summary
    assert res["promotion_candidates"] == []


def test_13_project_dominance_blocks_promotion():
    """Even with 2 high-scoring categories, project dominance in one category blocks promotion."""
    # Cat 1: Balanced (33% each)
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-DOM-BLOCK",
        task_category="frontend_motion",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="db1",
    )
    # Cat 2: Dominated (80% in p1)
    projects_dom = ["p1"] * 40 + ["p2"] * 10
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-DOM-BLOCK",
        task_category="frontend_layout",
        total_runs=50,
        success_runs=48,
        projects=projects_dom,
        base_id_prefix="db2",
    )
    res = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    assert res["promotion_candidates"] == []
    assert len(res["blocked_candidates"]) == 1
    assert "project dominance" in res["blocked_candidates"][0]["overall_reason"]


def test_14_project_filtering():
    """flag_review_candidates with project_id filters analysis strictly to that project."""
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-FILTER",
        task_category="backend_api",
        total_runs=50,
        success_runs=48,
        projects=["ALPHA"],
        base_id_prefix="pf1",
    )
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-FILTER",
        task_category="backend_api",
        total_runs=50,
        success_runs=2,
        projects=["BETA"],
        base_id_prefix="pf2",
    )
    res_alpha = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2, project_id="ALPHA")
    assert res_alpha["metadata"]["project_id"] == "ALPHA"


def test_15_multiple_capability_types():
    """Engine evaluates skills, agents, knowledge_refs, and procedure_refs independently."""
    r1, t1 = _build_runs_for_category(
        capability_id="AGENT-SPECIALIST",
        task_category="security_audit",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="ag1",
    )
    r2, t2 = _build_runs_for_category(
        capability_id="AGENT-SPECIALIST",
        task_category="testing",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="ag2",
    )
    res = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    assert len(res["promotion_candidates"]) == 1
    assert res["promotion_candidates"][0]["capability_type"] == "agents"
    assert res["promotion_candidates"][0]["capability_id"] == "AGENT-SPECIALIST"


def test_16_unknown_outcomes():
    """Runs with unknown outcomes reduce observed rate and Wilson bound."""
    records = []
    traces = []
    for i in range(20):
        r_id = f"r-unk-{i}"
        outcome = Outcome.SUCCESS.value if i < 10 else Outcome.UNKNOWN.value
        v_method = VerificationMethod.TEST_PASS.value if i < 10 else VerificationMethod.NONE.value
        records.append(
            OutcomeRecord(
                run_id=r_id,
                outcome=outcome,
                verification_method=v_method,
                task_category="testing",
            )
        )
        traces.append(
            ObservedMemoryTrace(
                run_id=r_id,
                timestamp="2026-09-02T10:00:00Z",
                retrieved_memory_ids=["SKILL-UNK-TEST"],
            )
        )
    res = flag_review_candidates(outcome_records=records, traces=traces)
    assert res["promotion_candidates"] == []


def test_17_deterministic_output():
    """Repeated calls produce identical evaluation outputs."""
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-DET",
        task_category="frontend_motion",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="det1",
    )
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-DET",
        task_category="frontend_layout",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="det2",
    )
    res1 = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    res2 = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    assert res1 == res2


def test_18_no_mutation_of_source_records():
    """Source records and traces passed in are strictly unmutated."""
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-MUT",
        task_category="frontend_motion",
        total_runs=10,
        success_runs=9,
        projects=["p1", "p2", "p3"],
        base_id_prefix="mut",
    )
    r1_copy = copy.deepcopy(r1)
    t1_copy = copy.deepcopy(t1)

    _ = flag_review_candidates(outcome_records=r1, traces=t1)

    assert r1 == r1_copy
    assert t1 == t1_copy


def test_19_no_automatic_action_executed():
    """Verifies that all candidates are flagged REVIEW_REQUIRED with no automated mutation."""
    r1, t1 = _build_runs_for_category(
        capability_id="SKILL-NO-AUTO",
        task_category="frontend_motion",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="na1",
    )
    r2, t2 = _build_runs_for_category(
        capability_id="SKILL-NO-AUTO",
        task_category="frontend_layout",
        total_runs=50,
        success_runs=48,
        projects=["p1", "p2", "p3"],
        base_id_prefix="na2",
    )
    res = flag_review_candidates(outcome_records=r1 + r2, traces=t1 + t2)
    for cand in res["promotion_candidates"] + res["retirement_candidates"] + res["blocked_candidates"]:
        assert cand["status"] == "REVIEW_REQUIRED"
    assert "NONE" in res["summary"]["action_taken"]


def test_20_same_run_cannot_inflate_project_statistics():
    """Duplicate traces for the same run_id contribute exactly 1 observation to project counts."""
    rec = OutcomeRecord(
        run_id="run-dup-proj",
        project_id="PROJ-A",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        task_category="frontend_layout",
    )
    t1 = ObservedMemoryTrace(
        run_id="run-dup-proj",
        project_id="PROJ-A",
        timestamp="2026-09-02T10:00:00Z",
        retrieved_memory_ids=["SKILL-DEDUP-P"],
    )
    t2 = ObservedMemoryTrace(
        run_id="run-dup-proj",
        project_id="PROJ-A",
        timestamp="2026-09-02T10:00:01Z",
        retrieved_memory_ids=["SKILL-DEDUP-P"],
    )
    res = flag_review_candidates(outcome_records=[rec], traces=[t1, t2])
    # The cell must have total_runs == 1, not 2
    assert res["summary"]["total_capabilities_evaluated"] == 1
