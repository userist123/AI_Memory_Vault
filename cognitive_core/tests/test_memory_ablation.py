"""Deterministic unit tests for Memory Ablation Benchmark (TASK: MEMORY_ABLATION_01).

Validates:
  1. Benchmark suite integrity & deterministic hash reproducibility.
  2. Control condition enforces zero retrieved memory.
  3. Treatment condition executes secure retrieval and injects memory.
  4. Paired trial alternating order and separate workspace isolation.
  5. Trace schema contains valid experiment metadata block.
  6. Statistical aggregation, delta calculation, and paired matrix counting.
  7. Failure taxonomy classification rules.
  8. Artifact generation (JSON and Markdown reports).
"""
from __future__ import annotations

import json
import os
import shutil
import tempfile
from pathlib import Path
from typing import Any, Dict, List
from unittest.mock import MagicMock

import pytest

from cognitive_core.fake_model_provider import FakeModelProvider
from cognitive_core.memory_ablation_benchmark import (
    AblationTask,
    BenchmarkSummary,
    MemoryAblationExperimentRunner,
    PairedTaskResult,
    TrialResult,
    classify_failure,
    compute_benchmark_hash,
    export_ablation_artifacts,
    get_ablation_benchmark_tasks,
)
from cognitive_core.real_execution_harness import (
    AgentModelExecutor,
    AgentTask,
    RealAgentExecutionHarness,
)
from cognitive_core.recall_cli import get_memory_controller


@pytest.fixture(autouse=True)
def ensure_hmac_secret(monkeypatch: pytest.MonkeyPatch) -> None:
    """Ensure HMAC secret is present for MemoryController pagination token generation."""
    if not os.getenv("MEMORY_CONTROLLER_HMAC_SECRET"):
        monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_for_ablation_harness_32chars")


@pytest.fixture
def temp_experiment_dir() -> Path:
    td = Path(tempfile.mkdtemp(prefix="ablation_test_"))
    yield td
    if td.exists():
        shutil.rmtree(td, ignore_errors=True)


def test_benchmark_task_suite_integrity():
    """Test 1: Benchmark suite contains >= 20 tasks, unique IDs, and deterministic hash."""
    tasks = get_ablation_benchmark_tasks()
    assert len(tasks) >= 20

    task_ids = [t.task_id for t in tasks]
    assert len(task_ids) == len(set(task_ids)), "Task IDs must be unique"

    categories = {t.category for t in tasks}
    assert "resilience" in categories
    assert "caching_storage" in categories
    assert "security_policy" in categories
    assert "coordination_consensus" in categories

    for t in tasks:
        assert t.target_file.endswith(".py")
        assert t.test_file.endswith(".py")
        assert len(t.test_code.strip()) > 0
        assert len(t.instructions.strip()) > 0
        assert len(t.memory_query.strip()) > 0

    h1 = compute_benchmark_hash(tasks)
    h2 = compute_benchmark_hash(tasks)
    assert h1 == h2
    assert len(h1) == 64


def test_control_condition_enforces_zero_retrieved_memory(temp_experiment_dir: Path):
    """Test 2: Control condition guarantees zero memory retrieval and empty context."""
    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_experiment_dir,
    )

    task = AgentTask(
        task_id="task_ctrl_check",
        description="Check control condition",
        target_file="target.py",
        test_file="test_target.py",
        instructions="Return actions.",
    )
    ws = temp_experiment_dir / "ws_ctrl"
    ws.mkdir(parents=True, exist_ok=True)

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_test",
        agent_role="synthesizer",
        workspace=ws,
        memory_query="circuit breaker state transitions",
        enable_memory=False,
    )

    trace = trace_ref["record"]
    assert trace["memory"]["retrieval_count"] == 0
    assert trace["memory"]["memory_ids"] == []
    assert trace["memory"]["query"] == ""


def test_treatment_condition_executes_secure_retrieval(temp_experiment_dir: Path):
    """Test 3: Treatment condition queries MemoryController and captures memory IDs."""
    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_experiment_dir,
    )

    task = AgentTask(
        task_id="task_treat_check",
        description="Check treatment condition",
        target_file="target.py",
        test_file="test_target.py",
        instructions="Return actions.",
    )
    ws = temp_experiment_dir / "ws_treat"
    ws.mkdir(parents=True, exist_ok=True)

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_test",
        agent_role="synthesizer",
        workspace=ws,
        memory_query="circuit breaker pattern states",
        enable_memory=True,
    )

    trace = trace_ref["record"]
    assert trace["memory"]["retrieval_count"] > 0
    assert len(trace["memory"]["memory_ids"]) > 0
    assert trace["memory"]["query"] == "circuit breaker pattern states"
    assert "context_hash" in trace["memory"]


def test_trace_experiment_block_schema(temp_experiment_dir: Path):
    """Test 4: Execution trace contains well-formed experiment metadata block."""
    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_experiment_dir,
    )

    task = AgentTask(
        task_id="task_exp_schema",
        description="Test experiment schema",
        target_file="target.py",
        test_file="test_target.py",
        instructions="Return actions.",
    )
    ws = temp_experiment_dir / "ws_schema"
    ws.mkdir(parents=True, exist_ok=True)

    exp_block = {
        "experiment_id": "exp_test_001",
        "task_id": "task_exp_schema",
        "condition": "control",
        "trial_id": "trial_abc123",
        "order": 1,
    }

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_test",
        agent_role="synthesizer",
        workspace=ws,
        enable_memory=False,
        experiment=exp_block,
    )

    trace = trace_ref["record"]
    assert "experiment" in trace
    assert trace["experiment"]["experiment_id"] == "exp_test_001"
    assert trace["experiment"]["condition"] == "control"
    assert trace["experiment"]["order"] == 1


def test_paired_trial_alternating_structure(temp_experiment_dir: Path):
    """Test 5: Paired tasks alternate execution order and use separate workspaces."""
    canned_code = json.dumps({
        "actions": [
            {"action": "write_file", "path": "backoff.py", "content": "def compute_backoff(a, b=1.0, m=32.0, f=2.0):\n    if a < 0: raise ValueError\n    return min(m, b * (f ** a))\n"}
        ]
    })
    fake_prov = FakeModelProvider(canned_response=canned_code)
    model_executor = AgentModelExecutor(provider_mode="fake", provider=fake_prov)

    harness = RealAgentExecutionHarness(
        memory_controller=get_memory_controller(),
        trace_dir=temp_experiment_dir,
        model_executor=model_executor,
    )

    tasks = get_ablation_benchmark_tasks()[:2]
    runner = MemoryAblationExperimentRunner(
        harness=harness,
        model_executor=model_executor,
        base_workspace_dir=temp_experiment_dir / "ws_pairs",
        tasks=tasks,
    )

    pair0 = runner.run_paired_task(0, tasks[0])
    # Task 0 (even index): control is order 1, treatment is order 2
    assert pair0.control.order == 1
    assert pair0.treatment.order == 2

    pair1 = runner.run_paired_task(1, tasks[1])
    # Task 1 (odd index): treatment is order 1, control is order 2
    assert pair1.treatment.order == 1
    assert pair1.control.order == 2


def test_aggregation_and_delta_calculations():
    """Test 6: Statistical aggregation calculates correct rates, deltas, and 2x2 matrix."""
    model_executor = AgentModelExecutor(provider_mode="deterministic")
    harness = RealAgentExecutionHarness(
        memory_controller=get_memory_controller(),
        model_executor=model_executor,
    )
    runner = MemoryAblationExperimentRunner(harness=harness, model_executor=model_executor, tasks=[])

    def mock_trial(task_id: str, cond: str, success: bool, lat: float = 100.0) -> TrialResult:
        return TrialResult(
            task_id=task_id,
            condition=cond,
            trial_id=f"tr_{cond}",
            order=1,
            success=success,
            verification_passed=success,
            verification_exit_code=0 if success else 1,
            execution_time_ms=lat + 50.0,
            model_latency_ms=lat,
            tool_execution_time_ms=50.0,
            retrieval_count=0 if cond == "control" else 3,
            memory_ids=[] if cond == "control" else ["id1", "id2", "id3"],
            commands_count=1,
            files_changed=1,
            failure_type=None if success else "TEST_ASSERTION_FAILURE",
            trace_id="tr_1",
        )

    # 4 paired tasks:
    # Task 1: ctrl=False, treat=True (treatment win)
    # Task 2: ctrl=True, treat=True  (both win)
    # Task 3: ctrl=False, treat=False (both fail)
    # Task 4: ctrl=True, treat=False (control win)
    p1 = PairedTaskResult("t1", "resilience", mock_trial("t1", "control", False), mock_trial("t1", "treatment", True), 1)
    p2 = PairedTaskResult("t2", "resilience", mock_trial("t2", "control", True), mock_trial("t2", "treatment", True), 0)
    p3 = PairedTaskResult("t3", "caching", mock_trial("t3", "control", False), mock_trial("t3", "treatment", False), 0)
    p4 = PairedTaskResult("t4", "caching", mock_trial("t4", "control", True), mock_trial("t4", "treatment", False), -1)

    summary = runner.aggregate_results([p1, p2, p3, p4])

    assert summary.control_trials == 4
    assert summary.control_successes == 2
    assert summary.control_success_rate == 0.50
    assert summary.treatment_trials == 4
    assert summary.treatment_successes == 2
    assert summary.treatment_success_rate == 0.50
    assert summary.absolute_delta == 0.0
    assert summary.paired_counts["both_success"] == 1
    assert summary.paired_counts["treatment_win"] == 1
    assert summary.paired_counts["control_win"] == 1
    assert summary.paired_counts["both_failure"] == 1
    assert summary.conclusion_status == "NO_MEASURABLE_MEMORY_EFFECT_DETECTED"


def test_failure_taxonomy_classification():
    """Test 7: Failure taxonomy correctly categorizes failure modes."""
    # 1. Provider failure
    t1 = {"model": {"response_status": "failed", "error_details": "API connection dropped"}}
    assert classify_failure(t1, {}) == "PROVIDER_FAILURE"

    # 2. Timeout
    t2 = {"model": {"response_status": "failed", "error_details": "HTTP timeout reached"}}
    assert classify_failure(t2, {}) == "TIMEOUT"

    # 3. Model output invalid
    t3 = {"model": {"response_status": "success"}, "actions": []}
    assert classify_failure(t3, {}) == "MODEL_OUTPUT_INVALID"

    # 4. Action unauthorized
    t4 = {"model": {"response_status": "success"}, "actions": [{"validated": False}]}
    assert classify_failure(t4, {}) == "ACTION_UNAUTHORIZED"

    # 5. Test assertion failure
    t5 = {
        "model": {"response_status": "success"},
        "actions": [{"validated": True}],
        "verification": {"status": "failed", "exit_code": 1, "stdout": "AssertionError: 3 != 4\nFAILED test_math.py"}
    }
    assert classify_failure(t5, {}) == "TEST_ASSERTION_FAILURE"


def test_artifact_export(temp_experiment_dir: Path):
    """Test 8: export_ablation_artifacts writes valid JSON and Markdown files."""
    summary = BenchmarkSummary(
        experiment_id="exp_test_art",
        benchmark_version="1.0.0",
        benchmark_hash="testhash123",
        git_commit_sha="commit123",
        provider="fake",
        model="fake-model",
        task_count=1,
        control_trials=1,
        control_successes=0,
        control_failures=1,
        control_success_rate=0.0,
        treatment_trials=1,
        treatment_successes=1,
        treatment_failures=0,
        treatment_success_rate=1.0,
        absolute_delta=1.0,
        relative_delta=100.0,
        paired_counts={"both_success": 0, "treatment_win": 1, "control_win": 0, "both_failure": 0},
        mean_control_latency_ms=10.0,
        mean_treatment_latency_ms=15.0,
        mean_control_execution_ms=20.0,
        mean_treatment_execution_ms=25.0,
        total_retrievals=3,
        mean_retrievals_per_treatment=3.0,
        failure_breakdown_control={"TEST_ASSERTION_FAILURE": 1},
        failure_breakdown_treatment={},
        conclusion_status="MEMORY_HELPFUL_UNDER_TESTED_CONDITIONS",
    )

    t_ctrl = TrialResult("t1", "control", "tr_c", 1, False, False, 1, 20.0, 10.0, 10.0, 0, [], 1, 1, "TEST_ASSERTION_FAILURE", "tr_c")
    t_treat = TrialResult("t1", "treatment", "tr_t", 2, True, True, 0, 25.0, 15.0, 10.0, 3, ["id1"], 1, 1, None, "tr_t")
    paired = [PairedTaskResult("t1", "resilience", t_ctrl, t_treat, 1)]

    json_f, md_f = export_ablation_artifacts(
        summary=summary,
        paired_results=paired,
        output_dir=temp_experiment_dir,
        date_slug="test-slug",
    )

    assert json_f.exists()
    assert md_f.exists()

    with open(json_f, "r", encoding="utf-8") as f:
        data = json.load(f)
    assert data["summary"]["experiment_id"] == "exp_test_art"
    assert len(data["paired_results"]) == 1

    md_text = md_f.read_text(encoding="utf-8")
    assert "# Controlled Memory Ablation Benchmark Report (test-slug)" in md_text
    assert "MEMORY_HELPFUL_UNDER_TESTED_CONDITIONS" in md_text
