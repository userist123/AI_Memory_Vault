"""memory_controller/tests/test_outcome_tracker.py — Acceptance tests for outcome tracker.

Tests:
A. Verified success run: outcome=success, verification_method=test_pass
B. Failed run: outcome=fail, verification_method=test_pass
C. Unverified run: outcome=unknown, verification_method=none
D. Partial run: outcome=partial
E. Append-only provenance: Multiple observations for same run_id preserve full history
F. Invalid values rejected: Non-enum outcome or verification_method raises ValueError
G. No canonical-memory write: Storing in 00_CORE..05_DECISIONS raises PermissionError
H. No proposal queue coupling: outcome_tracker does not import or call proposal_queue
"""
import ast
import inspect
import pytest
from pathlib import Path

from memory_controller.outcome_tracker import (
    Outcome,
    OutcomeRecord,
    OutcomeTracker,
    VerificationMethod,
    compute_task_signature,
)
import memory_controller.outcome_tracker as outcome_tracker_module


def test_case_a_verified_run_with_success(tmp_path):
    """Test A: Run verified with success (outcome=success, verification_method=test_pass)."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    rec = tracker.record_run(
        run_id="run_success_001",
        task="run test suite for payments",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        evidence="pytest exited with code 0; 45 passed",
    )

    assert rec.run_id == "run_success_001"
    assert rec.outcome == "success"
    assert rec.verification_method == "test_pass"
    assert rec.evidence == "pytest exited with code 0; 45 passed"

    fetched = tracker.get_record("run_success_001")
    assert fetched is not None
    assert fetched.outcome == "success"
    assert fetched.verification_method == "test_pass"


def test_case_b_failed_run(tmp_path):
    """Test B: Run failed (outcome=fail, verification_method=test_pass)."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    rec = tracker.record_run(
        run_id="run_fail_002",
        task="execute integration suite",
        outcome=Outcome.FAIL.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        evidence="AssertionError: expected status 200, got 500",
    )

    assert rec.run_id == "run_fail_002"
    assert rec.outcome == "fail"
    assert rec.verification_method == "test_pass"


def test_case_c_unverified_run_defaults_to_unknown_never_success(tmp_path):
    """Test C: Run without verification (outcome=unknown, verification_method=none)."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    rec = tracker.record_run(
        run_id="run_unverified_003",
        task="generate summary draft",
    )

    assert rec.run_id == "run_unverified_003"
    assert rec.outcome == "unknown"
    assert rec.verification_method == "none"
    assert rec.outcome != "success"

    # Invariant check: outcome=success with none verification is blocked
    with pytest.raises(ValueError, match="Fail-closed violation"):
        tracker.record_run(
            run_id="run_invalid_success",
            task="unverified task",
            outcome=Outcome.SUCCESS.value,
            verification_method=VerificationMethod.NONE.value,
        )


def test_case_d_partial_outcome(tmp_path):
    """Test D: Partial outcome."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    rec = tracker.record_run(
        run_id="run_partial_004",
        task="multi-step data migration",
        outcome=Outcome.PARTIAL.value,
        verification_method=VerificationMethod.EXIT_CODE.value,
        evidence="Exit code 0 but 3 out of 10 tables were skipped",
    )

    assert rec.run_id == "run_partial_004"
    assert rec.outcome == "partial"
    assert rec.verification_method == "exit_code"


def test_case_e_append_only_preserves_history_and_provenance(tmp_path):
    """Test E: Append-only: Two events for same run_id preserve history without overwrite."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    # Event 1: Automatic execution observation
    rec1 = tracker.record_run(
        run_id="run_dual_005",
        task="deploy microservice",
        outcome=Outcome.PARTIAL.value,
        verification_method=VerificationMethod.EXIT_CODE.value,
        recorded_by="automatic_daemon",
    )

    # Event 2: Subsequent human confirmation correction
    rec2 = tracker.record_run(
        run_id="run_dual_005",
        task="deploy microservice",
        outcome=Outcome.FAIL.value,
        verification_method=VerificationMethod.HUMAN_CONFIRMED.value,
        recorded_by="human_operator",
        evidence="Service failed healthcheck after deployment",
    )

    history = tracker.get_history("run_dual_005")
    assert len(history) == 2
    assert history[0].outcome == "partial"
    assert history[0].recorded_by == "automatic_daemon"
    assert history[1].outcome == "fail"
    assert history[1].recorded_by == "human_operator"

    # Latest record returns the most recent observation
    latest = tracker.get_record("run_dual_005")
    assert latest is not None
    assert latest.outcome == "fail"


def test_case_f_invalid_values_rejected(tmp_path):
    """Test F: Invalid values in outcome or verification_method are rejected."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    with pytest.raises(ValueError, match="Invalid outcome 'random_status'"):
        tracker.record_run(
            run_id="run_bad_006",
            task="some task",
            outcome="random_status",
            verification_method=VerificationMethod.TEST_PASS.value,
        )

    with pytest.raises(ValueError, match="Invalid verification_method 'magical_guess'"):
        tracker.record_run(
            run_id="run_bad_007",
            task="some task",
            outcome=Outcome.FAIL.value,
            verification_method="magical_guess",
        )


def test_case_g_no_canonical_memory_write_isolation(tmp_path):
    """Test G: Structural isolation — writing to canonical vault dirs raises PermissionError."""
    forbidden_dirs = ["00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES", "04_MEMORY", "05_DECISIONS", "06_INBOX"]

    for d in forbidden_dirs:
        forbidden_ledger = tmp_path / d / "telemetry.jsonl"
        with pytest.raises(PermissionError, match="Security Invariant Violation"):
            OutcomeTracker(ledger_path=forbidden_ledger)


def test_case_h_no_proposal_queue_coupling():
    """Test H: outcome_tracker must not import or reference proposal_queue."""
    src = inspect.getsource(outcome_tracker_module)
    parsed = ast.parse(src)

    imports = []
    for node in ast.walk(parsed):
        if isinstance(node, ast.Import):
            for n in node.names:
                imports.append(n.name)
        elif isinstance(node, ast.ImportFrom):
            if node.module:
                imports.append(node.module)

    assert not any("proposal_queue" in imp for imp in imports), (
        f"Coupling violation: outcome_tracker imports proposal_queue: {imports}"
    )
    assert "proposal_queue" not in src.lower(), (
        "Coupling violation: string reference to proposal_queue in outcome_tracker"
    )


def test_legacy_outcome_without_project_id_deserialization(tmp_path):
    """Test backward compatibility: Legacy outcome records without new fields parse cleanly."""
    import json
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    ledger.parent.mkdir(parents=True, exist_ok=True)

    legacy_json = {
        "run_id": "legacy_run_001",
        "outcome": "fail",
        "verification_method": "exit_code",
        "timestamp": "2026-09-01T10:00:00Z",
        "task_signature": "abc123sig",
        "event_id": "evt_legacy001",
        "evidence": "process crashed",
        "recorded_by": "old_agent",
        "metadata": None,
    }
    with open(ledger, "w", encoding="utf-8") as f:
        f.write(json.dumps(legacy_json) + "\n")

    tracker = OutcomeTracker(ledger_path=ledger)
    record = tracker.get_record("legacy_run_001")
    assert record is not None
    assert record.run_id == "legacy_run_001"
    assert record.project_id is None
    assert record.task_category == "unknown"
    assert record.observed_capabilities == {
        "skills": [],
        "agents": [],
        "knowledge_refs": [],
        "procedure_refs": [],
    }


def test_new_outcome_with_project_id_and_task_category(tmp_path):
    """Test new outcome records with project_id, controlled task_category, and observed_capabilities."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    rec = tracker.record_run(
        run_id="run_new_002",
        task="Build API router endpoint",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        project_id="PROJ-VAULT-CORE",
        task_category="backend_api",
        observed_capabilities={
            "skills": ["SKILL-API-DESIGN"],
            "agents": ["AGENT-ROUTER"],
            "knowledge_refs": ["KNOW-Backend-Spec"],
            "procedure_refs": ["PROC-API-Standard"],
        },
    )

    assert rec.project_id == "PROJ-VAULT-CORE"
    assert rec.task_category == "backend_api"
    assert rec.observed_capabilities["skills"] == ["SKILL-API-DESIGN"]
    assert rec.observed_capabilities["agents"] == ["AGENT-ROUTER"]
    assert rec.observed_capabilities["knowledge_refs"] == ["KNOW-Backend-Spec"]
    assert rec.observed_capabilities["procedure_refs"] == ["PROC-API-Standard"]

    fetched = tracker.get_record("run_new_002")
    assert fetched is not None
    assert fetched.project_id == "PROJ-VAULT-CORE"
    assert fetched.task_category == "backend_api"


def test_controlled_task_category_validation_and_rejection(tmp_path):
    """Test strict controlled vocabulary for task_category."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    # Valid categories pass
    for cat in [
        "frontend_motion", "frontend_layout", "backend_api", "database",
        "security_audit", "trading_logic", "documentation", "testing",
        "infra_devops", "unknown"
    ]:
        rec = tracker.record_run(
            run_id=f"run_cat_{cat}",
            task="task text",
            outcome=Outcome.UNKNOWN.value,
            task_category=cat,
        )
        assert rec.task_category == cat

    # None or empty defaults to 'unknown'
    rec_none = tracker.record_run(
        run_id="run_cat_none",
        task="task text",
        outcome=Outcome.UNKNOWN.value,
        task_category=None,
    )
    assert rec_none.task_category == "unknown"

    # Arbitrary strings rejected
    with pytest.raises(ValueError, match="Invalid task_category 'magic_ai_marketing'"):
        tracker.record_run(
            run_id="run_cat_invalid",
            task="task text",
            outcome=Outcome.UNKNOWN.value,
            task_category="magic_ai_marketing",
        )


def test_declared_capability_does_not_populate_observed_capabilities(tmp_path):
    """Hard invariant: declared capabilities in metadata never populate observed_capabilities."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    rec = tracker.record_run(
        run_id="run_anti_fab_001",
        task="task with declared skills in prose",
        outcome=Outcome.UNKNOWN.value,
        metadata={"declared_skills": ["SKILL-FABRICATED-VOICE", "SKILL-MAGIC"]},
        observed_capabilities=None,
    )

    assert rec.observed_capabilities["skills"] == []
    assert "SKILL-FABRICATED-VOICE" not in rec.observed_capabilities["skills"]


def test_project_id_remains_unchanged_across_lifecycle(tmp_path):
    """Test that project_id remains consistent across append-only observations."""
    ledger = tmp_path / "telemetry" / "outcomes" / "council_outcomes.jsonl"
    tracker = OutcomeTracker(ledger_path=ledger)

    rec1 = tracker.record_run(
        run_id="run_proj_lifecycle",
        task="execute migration",
        outcome=Outcome.PARTIAL.value,
        verification_method=VerificationMethod.EXIT_CODE.value,
        project_id="PROJ-LIFECYCLE-1",
    )
    rec2 = tracker.record_run(
        run_id="run_proj_lifecycle",
        task="execute migration",
        outcome=Outcome.SUCCESS.value,
        verification_method=VerificationMethod.TEST_PASS.value,
        project_id="PROJ-LIFECYCLE-1",
    )

    history = tracker.get_history("run_proj_lifecycle")
    assert len(history) == 2
    assert history[0].project_id == "PROJ-LIFECYCLE-1"
    assert history[1].project_id == "PROJ-LIFECYCLE-1"

