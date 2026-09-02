"""memory_controller/tests/test_project_ledger.py — Tests for Project Session Ledger & Skill Effectiveness."""
import json
import pytest
from pathlib import Path

from memory_controller.memory_trace import record_observed_memory_trace, load_observed_memory_traces
from memory_controller.outcome_tracker import OutcomeTracker, VerificationMethod, Outcome
from memory_controller.project_ledger import (
    record_project_session,
    load_project_sessions,
    project_report,
    skill_effectiveness_report,
)


def test_record_project_session_validation(tmp_path: Path):
    with pytest.raises(ValueError, match="project_id must be a non-empty string"):
        record_project_session("", "run_123", telemetry_dir=tmp_path)

    with pytest.raises(ValueError, match="run_id must be a non-empty string"):
        record_project_session("proj_abc", "", telemetry_dir=tmp_path)


def test_record_and_load_project_sessions(tmp_path: Path):
    tel_dir = tmp_path / "telemetry"
    s1 = record_project_session("proj_alpha", "run_1", telemetry_dir=tel_dir)
    s2 = record_project_session("proj_alpha", "run_2", telemetry_dir=tel_dir)
    s3 = record_project_session("proj_beta", "run_3", telemetry_dir=tel_dir)

    all_sessions = load_project_sessions(telemetry_dir=tel_dir)
    assert len(all_sessions) == 3

    alpha_sessions = load_project_sessions(project_id="proj_alpha", telemetry_dir=tel_dir)
    assert len(alpha_sessions) == 2
    assert {s.run_id for s in alpha_sessions} == {"run_1", "run_2"}


def test_acceptance_skill_effectiveness_66_7_percent(tmp_path: Path):
    """TASK B Point 5 Acceptance Test:

    Simulate 3 runs on the same fake project_id, with 2 success and 1 fail,
    all using the same fake skill in OBSERVED.
    skill_effectiveness_report() must report exactly 66.7% for that skill.
    """
    tel_dir = tmp_path / "telemetry"
    fake_project = "PROJ-QUANTUM-X"
    fake_skill = "skill_quantum_solver"
    outcomes_file = tel_dir / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=outcomes_file)

    # Run 1: success, observed skill
    record_observed_memory_trace(
        run_id="run_101",
        results=[{"id": fake_skill, "score": 0.95}],
        telemetry_dir=tel_dir,
        project_id=fake_project,
    )
    tracker.record_run(
        run_id="run_101",
        task="Quantum optimization run 1",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        project_id=fake_project,
    )
    record_project_session(fake_project, "run_101", telemetry_dir=tel_dir)

    # Run 2: success, observed skill
    record_observed_memory_trace(
        run_id="run_102",
        results=[{"id": fake_skill, "score": 0.92}],
        telemetry_dir=tel_dir,
        project_id=fake_project,
    )
    tracker.record_run(
        run_id="run_102",
        task="Quantum optimization run 2",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        project_id=fake_project,
    )
    record_project_session(fake_project, "run_102", telemetry_dir=tel_dir)

    # Run 3: fail, observed skill
    record_observed_memory_trace(
        run_id="run_103",
        results=[{"id": fake_skill, "score": 0.88}],
        telemetry_dir=tel_dir,
        project_id=fake_project,
    )
    tracker.record_run(
        run_id="run_103",
        task="Quantum optimization run 3",
        outcome=Outcome.FAIL.value,
        verification_method=VerificationMethod.EXIT_CODE.value,
        project_id=fake_project,
    )
    record_project_session(fake_project, "run_103", telemetry_dir=tel_dir)

    # Compute skill effectiveness
    report = skill_effectiveness_report(telemetry_dir=tel_dir, outcomes_path=outcomes_file)

    assert fake_skill in report["skills"]
    skill_stat = report["skills"][fake_skill]
    assert skill_stat["total_observed_runs"] == 3
    assert skill_stat["success_runs"] == 2
    assert skill_stat["fail_runs"] == 1
    assert skill_stat["success_rate"] == 0.667
    assert skill_stat["success_percentage"] == 66.7


def test_declared_only_skill_excluded_from_effectiveness(tmp_path: Path):
    """Hard rule: A skill only declared by an agent without confirmed OBSERVED trace

    must never be counted in skill_effectiveness_report.
    """
    tel_dir = tmp_path / "telemetry"
    outcomes_file = tel_dir / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=outcomes_file)

    # Run without observing fake_declared_skill in final context
    record_observed_memory_trace(
        run_id="run_wob_art",
        results=[{"id": "KNOW-SomeDoc", "score": 0.5}],
        telemetry_dir=tel_dir,
    )
    tracker.record_run(
        run_id="run_wob_art",
        task="Autonomous design task",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.HUMAN_CONFIRMED.value,
        metadata={"declared_skills": ["skill_hallucinated_photoshop"]},
    )

    report = skill_effectiveness_report(telemetry_dir=tel_dir, outcomes_path=outcomes_file)
    assert "skill_hallucinated_photoshop" not in report["skills"]


def test_project_report_aggregation(tmp_path: Path):
    tel_dir = tmp_path / "telemetry"
    outcomes_file = tel_dir / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=outcomes_file)
    project_id = "PROJ-FINSCOPE"

    record_observed_memory_trace(
        run_id="run_f1",
        results=[
            {"id": "AGENT-ROUTER", "score": 1.0},
            {"id": "SKILL-API-DESIGN", "score": 0.9},
            {"id": "KNOW-FinScope-Architecture", "score": 0.8},
        ],
        telemetry_dir=tel_dir,
        project_id=project_id,
    )
    tracker.record_run(
        run_id="run_f1",
        task="Build API endpoint",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        project_id=project_id,
    )
    record_project_session(project_id, "run_f1", telemetry_dir=tel_dir)

    record_observed_memory_trace(
        run_id="run_f2",
        results=[
            {"id": "AGENT-RETRIEVAL", "score": 0.95},
            {"id": "SKILL-SQLITE-WAL", "score": 0.85},
            {"id": "KNOW-FinScope-Architecture", "score": 0.8},
        ],
        telemetry_dir=tel_dir,
        project_id=project_id,
    )
    tracker.record_run(
        run_id="run_f2",
        task="Run database migration",
        outcome=Outcome.FAIL.value,
        verification_method=VerificationMethod.EXIT_CODE.value,
        project_id=project_id,
    )
    record_project_session(project_id, "run_f2", telemetry_dir=tel_dir)

    rep = project_report(project_id, telemetry_dir=tel_dir, outcomes_path=outcomes_file)
    assert rep["project_id"] == project_id
    assert rep["outcomes"]["total"] == 2
    assert rep["outcomes"]["success"] == 1
    assert rep["outcomes"]["fail"] == 1
    assert "AGENT-ROUTER" in rep["agents"]
    assert "AGENT-RETRIEVAL" in rep["agents"]
    assert "SKILL-API-DESIGN" in rep["skills"]
    assert "SKILL-SQLITE-WAL" in rep["skills"]
    assert "KNOW-FinScope-Architecture" in rep["knowledge"]

    expected_prefix = f"Proiect {project_id} a folosit agenții"
    assert rep["summary_text"].startswith(expected_prefix)
    assert "Din 2 runde: 1 success, 1 fail, restul 0 unknown." in rep["summary_text"]
