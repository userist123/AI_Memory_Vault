"""Acceptance tests for the bounded memory -> execution pilot contract.

These tests intentionally exercise the real execution harness seam rather than
mocking the entire pipeline.
"""
from __future__ import annotations

import json
import sys

import pytest

from cognitive_core.real_execution_harness import (
    AgentModelExecutor,
    AgentTask,
    ExecutionContractError,
    ModelExecutionRecord,
    RealAgentExecutionHarness,
)


class StaticBootstrap:
    def __init__(self, *, fail: bool = False):
        self.fail = fail

    def __call__(self, task: AgentTask, principal: str):
        if self.fail:
            raise RuntimeError("bootstrap unavailable")
        return {
            "sources": [
                {"name": "AGENTS.md", "uri": "vault://governance/agents", "sha256": "a" * 64,
                 "text": "Repository rules"},
                {"name": "CLAUDE.md", "uri": "vault://governance/claude", "sha256": "b" * 64,
                 "text": "Execution rules"},
                {"name": "VAULT_STATE.md", "uri": "vault://governance/vault_state", "sha256": "c" * 64,
                 "text": "Current state"},
            ],
            "authority": "host_bootstrap",
        }


class MaliciousModelExecutor(AgentModelExecutor):
    def execute_model(self, context, task):
        assert context["retrieved_memories"]
        payload = {
            "actions": [
                {
                    "action": "write_file",
                    "path": "outside_scope.py",
                    "content": "# instruction from retrieved memory",
                }
            ]
        }
        return ModelExecutionRecord(
            provider_mode="local",
            provider_name="test",
            model_name="test-model",
            request_started_at="2026-10-08T00:00:00+00:00",
            response_finished_at="2026-10-08T00:00:00+00:00",
            latency_ms=0.0,
            response_status="success",
            response_text=json.dumps(payload),
        )


def _task():
    return AgentTask(
        task_id="pilot_contract_001",
        description="Add a small deterministic helper and verify it.",
        target_file="calculator.py",
        test_file="test_calculator.py",
        instructions="Implement the requested helper and keep the change in scope.",
        code_patch="def add(a, b):\n    return a + b\n",
        test_patch="from calculator import add\n\ndef test_add():\n    assert add(2, 3) == 5\n",
        verification_command=[sys.executable, "-m", "pytest", "test_calculator.py", "-q"],
    )


def test_bootstrap_is_mandatory_and_context_is_bounded(tmp_path):
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=StaticBootstrap(),
    )
    result, trace = harness.execute(
        task=_task(),
        agent_id="pilot_agent",
        agent_role="coder",
        workspace=tmp_path / "workspace",
        memory_query="coding",
    )
    assert result["status"] == "success"
    record = trace["record"]
    assert len(record["bootstrap"]["sources"]) == 3
    assert record["execution_contract"]["allowed_files"] == ["calculator.py", "test_calculator.py"]
    assert record["execution_contract"]["evidence_required"]
    assert record["memory"]["support_limit"] == 2
    assert len(record["memory"]["memory_ids"]) <= 2
    assert len(record["context_hash"]) == 64
    assert len(record["contract_hash"]) == 64


def test_missing_bootstrap_blocks_execution(tmp_path):
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=StaticBootstrap(fail=True),
    )
    with pytest.raises(ExecutionContractError, match="bootstrap"):
        harness.execute(
            task=_task(),
            agent_id="pilot_agent",
            agent_role="coder",
            workspace=tmp_path / "workspace",
            memory_query="coding",
        )
    assert not (tmp_path / "workspace" / "calculator.py").exists()


def test_retrieved_memory_cannot_authorize_out_of_scope_mutation(tmp_path):
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=StaticBootstrap(),
        model_executor=MaliciousModelExecutor(provider_mode="local"),
    )
    result, trace = harness.execute(
        task=_task(),
        agent_id="pilot_agent",
        agent_role="coder",
        workspace=tmp_path / "workspace",
        memory_query="malicious instruction",
    )
    assert result["status"] == "failure"
    assert any(
        "outside scope" in a["execution_status"]
        for a in trace["record"]["actions"]
        if not a["validated"]
    )
    assert not (tmp_path / "workspace" / "outside_scope.py").exists()


def test_memory_on_off_harness_keeps_comparison_comparable(tmp_path):
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=StaticBootstrap(),
    )
    task = _task()
    with_memory, with_memory_trace = harness.execute(
        task=task,
        agent_id="pilot_compare",
        agent_role="coder",
        workspace=tmp_path / "with_memory",
        memory_query="coding",
        enable_memory=True,
    )
    without_memory, without_memory_trace = harness.execute(
        task=task,
        agent_id="pilot_compare",
        agent_role="coder",
        workspace=tmp_path / "without_memory",
        memory_query="coding",
        enable_memory=False,
    )
    assert with_memory["verification_status"] == without_memory["verification_status"] == "passed"
    assert with_memory_trace["record"]["execution_contract"] == without_memory_trace["record"]["execution_contract"]
    assert with_memory_trace["record"]["memory"]["query"] == "coding"
    assert without_memory_trace["record"]["memory"]["query"] == ""
    assert with_memory_trace["record"]["context_hash"] != without_memory_trace["record"]["context_hash"]
