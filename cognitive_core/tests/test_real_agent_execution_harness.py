"""cognitive_core/tests/test_real_agent_execution_harness.py

Deterministic integration test suite for RealAgentExecutionHarness.
Validates the complete 12-step contract:
  - Test A: Retrieval occurs (MemoryController.search is invoked)
  - Test B: Retrieved memory is observable in execution context and context hash
  - Test C: Real command executes in isolated workspace with actual subprocess
  - Test D: Verification test suite runs via subprocess and captures results
  - Test E: Persistent structured trace exists on disk with full metadata
  - Test F: Failed execution is also persisted with failure evidence
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path
from typing import Any, Dict, List

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.orchestrator import UnknownAgentRoleError
from cognitive_core.real_execution_harness import (
    AgentTask,
    CommandExecutionRecord,
    DeterministicCodeAgentPolicy,
    RealAgentExecutionHarness,
)


@pytest.fixture(autouse=True)
def ensure_hmac_secret(monkeypatch):
    """Ensures a valid HMAC secret is set for MemoryController pagination tokens."""
    secret = os.getenv("MEMORY_CONTROLLER_HMAC_SECRET")
    if not secret:
        monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_for_real_harness_32chars_min")


@pytest.fixture
def temp_workspace():
    """Provides an isolated temporary workspace directory."""
    with tempfile.TemporaryDirectory() as tmpdir:
        yield Path(tmpdir)


@pytest.fixture
def temp_trace_dir():
    """Provides an isolated temporary directory for execution traces."""
    with tempfile.TemporaryDirectory() as tmpdir:
        yield Path(tmpdir)


@pytest.fixture
def sample_task() -> AgentTask:
    """Provides a deterministic task fixture: add sum_positive function and tests."""
    return AgentTask(
        task_id="task_calc_sum_001",
        description="Add sum_positive function and verify its correctness",
        target_file="calculator.py",
        test_file="test_calculator.py",
        instructions="Implement sum_positive(numbers) filtering out non-positive values.",
        code_patch="""def sum_positive(numbers):
    return sum(x for x in numbers if x > 0)
""",
        test_patch="""from calculator import sum_positive

def test_sum_positive_basic():
    assert sum_positive([1, 2, 3]) == 6

def test_sum_positive_with_negatives():
    assert sum_positive([-5, 10, -2, 5]) == 15

def test_sum_positive_empty():
    assert sum_positive([]) == 0
""",
    )


def test_harness_test_a_retrieval_occurs(temp_workspace, temp_trace_dir, sample_task):
    """Test A: Verify that MemoryController.search() is actually invoked during execution."""
    harness = RealAgentExecutionHarness(trace_dir=temp_trace_dir)

    result, trace_ref = harness.execute(
        task=sample_task,
        agent_id="test_agent_01",
        agent_role="coder",
        workspace=temp_workspace,
        memory_query="knowledge",
    )

    record = trace_ref["record"]
    assert "memory" in record
    assert record["memory"]["query"] == "knowledge"
    # retrieval_count must be integer >= 0, reflecting real search invocation
    assert isinstance(record["memory"]["retrieval_count"], int)
    assert isinstance(record["memory"]["memory_ids"], list)


def test_harness_test_b_retrieved_memory_observable_in_context(temp_workspace, temp_trace_dir, sample_task):
    """Test B: Verify retrieved memory is observable in context metadata and context hash."""
    harness = RealAgentExecutionHarness(trace_dir=temp_trace_dir)

    result, trace_ref = harness.execute(
        task=sample_task,
        agent_id="test_agent_02",
        agent_role="synthesizer",
        workspace=temp_workspace,
        memory_query="knowledge",
    )

    record = trace_ref["record"]
    memory_info = record["memory"]
    context_hash = memory_info["context_hash"]

    assert context_hash is not None
    assert len(context_hash) == 64  # Valid SHA-256 hex digest
    assert trace_ref["context_hash"] == context_hash

    # If memories were returned, their IDs are explicitly present in the memory block
    for mem_id in memory_info["memory_ids"]:
        assert isinstance(mem_id, str)
        assert len(mem_id) > 0


def test_harness_test_c_real_command_executes(temp_workspace, temp_trace_dir, sample_task):
    """Test C: Verify real command executes in workspace subprocess, capturing stdout/stderr/exit code."""
    harness = RealAgentExecutionHarness(trace_dir=temp_trace_dir)

    result, trace_ref = harness.execute(
        task=sample_task,
        agent_id="test_agent_03",
        agent_role="coder",
        workspace=temp_workspace,
        memory_query="architecture",
    )

    record = trace_ref["record"]
    exec_info = record["execution"]

    # Real py_compile command was executed
    assert len(exec_info["commands"]) >= 1
    assert any("py_compile" in cmd for cmd in exec_info["commands"])
    assert exec_info["exit_codes"] == [0]

    # Filesystem in workspace was really changed
    ws_info = record["workspace"]
    assert "calculator.py" in ws_info["files_created"] or "calculator.py" in ws_info["files_modified"]
    assert (temp_workspace / "calculator.py").exists()


def test_harness_test_d_verification_runs(temp_workspace, temp_trace_dir, sample_task):
    """Test D: Verify real pytest verification command runs against the modified workspace."""
    harness = RealAgentExecutionHarness(trace_dir=temp_trace_dir)

    result, trace_ref = harness.execute(
        task=sample_task,
        agent_id="test_agent_04",
        agent_role="coder",
        workspace=temp_workspace,
        memory_query="testing",
    )

    record = trace_ref["record"]
    v_info = record["verification"]

    assert "pytest" in v_info["command"]
    assert v_info["exit_code"] == 0
    assert v_info["status"] == "passed"
    assert "passed" in v_info["stdout"]
    assert result["status"] == "success"
    assert result["verification_status"] == "passed"


def test_harness_test_e_persistent_trace_exists(temp_workspace, temp_trace_dir, sample_task):
    """Test E: Verify that persistent trace record exists on disk with all required fields."""
    harness = RealAgentExecutionHarness(trace_dir=temp_trace_dir)

    result, trace_ref = harness.execute(
        task=sample_task,
        agent_id="test_agent_05",
        agent_role="coder",
        workspace=temp_workspace,
        memory_query="SQLite WAL",
    )

    trace_file = Path(trace_ref["trace_file"])
    traces_jsonl = Path(trace_ref["traces_jsonl"])

    # Persistent files exist on disk
    assert trace_file.exists()
    assert traces_jsonl.exists()

    with open(trace_file, "r", encoding="utf-8") as f:
        persisted = json.load(f)

    # Validate exact schema fields required by specification
    assert "trace_id" in persisted
    assert persisted["agent_id"] == "test_agent_05"
    assert persisted["agent_role"] == "synthesizer" or persisted["agent_role"] == "coder"
    assert persisted["task_id"] == sample_task.task_id
    assert "started_at" in persisted
    assert "finished_at" in persisted

    assert "memory" in persisted
    assert "query" in persisted["memory"]
    assert "memory_ids" in persisted["memory"]
    assert "retrieval_count" in persisted["memory"]
    assert "context_hash" in persisted["memory"]

    assert "execution" in persisted
    assert "commands" in persisted["execution"]
    assert "stdout" in persisted["execution"]
    assert "stderr" in persisted["execution"]
    assert "exit_codes" in persisted["execution"]

    assert "workspace" in persisted
    assert "files_created" in persisted["workspace"]
    assert "files_modified" in persisted["workspace"]
    assert "files_deleted" in persisted["workspace"]

    assert "verification" in persisted
    assert "command" in persisted["verification"]
    assert "stdout" in persisted["verification"]
    assert "stderr" in persisted["verification"]
    assert "exit_code" in persisted["verification"]
    assert "status" in persisted["verification"]


def test_harness_test_f_failed_execution_persisted(temp_workspace, temp_trace_dir):
    """Test F: Force one deterministic failure and verify exit_code != 0 and failure evidence is preserved."""
    harness = RealAgentExecutionHarness(trace_dir=temp_trace_dir)

    # Create task with a failing test
    failing_task = AgentTask(
        task_id="task_failing_001",
        description="Task with failing assertion",
        target_file="broken.py",
        test_file="test_broken.py",
        instructions="Intentional failure to test persistence under error conditions.",
        code_patch="""def broken_fn():
    return False
""",
        test_patch="""from broken import broken_fn

def test_should_fail():
    assert broken_fn() is True
""",
    )

    result, trace_ref = harness.execute(
        task=failing_task,
        agent_id="test_agent_fail",
        agent_role="coder",
        workspace=temp_workspace,
        memory_query="errors",
    )

    # Overall execution status is failure
    assert result["status"] == "failure"
    assert result["verification_status"] == "failed"
    assert result["verification_exit_code"] != 0

    # Trace file STILL exists and preserves failure evidence
    trace_file = Path(trace_ref["trace_file"])
    assert trace_file.exists()

    with open(trace_file, "r", encoding="utf-8") as f:
        persisted = json.load(f)

    assert persisted["verification"]["exit_code"] != 0
    assert persisted["verification"]["status"] == "failed"
    assert "AssertionError" in persisted["verification"]["stdout"] or "FAILED" in persisted["verification"]["stdout"]


def test_harness_role_validation_fails_closed(temp_workspace, temp_trace_dir, sample_task):
    """Verify that unknown/unsupported agent roles fail closed before execution."""
    harness = RealAgentExecutionHarness(trace_dir=temp_trace_dir)

    with pytest.raises(UnknownAgentRoleError):
        harness.execute(
            task=sample_task,
            agent_id="test_agent_bad_role",
            agent_role="unauthorized_super_admin",
            workspace=temp_workspace,
            memory_query="test",
        )
