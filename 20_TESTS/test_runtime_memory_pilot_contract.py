"""Acceptance tests for the bounded memory -> execution pilot contract.

These tests intentionally exercise the real execution harness seam rather than
mocking the entire pipeline.
"""
from __future__ import annotations

import json
import os
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
            "authority": "TEST_FIXTURE",
            "evidence_level": "TEST_FIXTURE",
        }


class FixtureMemoryController:
    def search(self, principal, query, page_size=5):
        return {
            "results": [{
                "id": "review-fixture-001",
                "type": "knowledge",
                "lifecycle": "REVIEW",
                "content": "unverified review data",
                "verification": {"state": "unverified"},
                "provenance": {"source_type": "ai", "source_ref": "fixture"},
                "score": 0.9,
            }]
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



class SecretEchoModelExecutor(AgentModelExecutor):
    def execute_model(self, context, task):
        secret = os.environ["MEMORY_CONTROLLER_HMAC_SECRET"]
        return ModelExecutionRecord(
            provider_mode="local",
            provider_name="test",
            model_name="test-model",
            request_started_at="2026-10-08T00:00:00+00:00",
            response_finished_at="2026-10-08T00:00:00+00:00",
            latency_ms=0.0,
            response_status="success",
            response_text=json.dumps({"actions": [], "echo": secret}),
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

def test_default_bootstrap_uses_real_vaultaccess_and_real_controller(tmp_path):
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
    )
    result, trace = harness.execute(
        task=_task(),
        agent_id="pilot_real_fixture",
        agent_role="coder",
        workspace=tmp_path / "workspace",
        memory_query="",
        enable_memory=False,
    )
    assert result["status"] == "success"
    sources = trace["record"]["bootstrap"]["sources"]
    assert len(sources) >= 8
    assert all(source["evidence_level"] == "DIRECT" for source in sources)
    assert all(source["sha256"] and source["uri"] for source in sources)
    governance = trace["record"]["bootstrap"]["governance"]
    assert governance["detection"] == "PARTIAL_TEXT_RULES"
    assert not governance["conflicts"]
    assert any(
        item["type"] == "MAIN_ONLY_VS_PILOT_BRANCH"
        and item["status"] == "RESOLVED_BY_OWNER_AUTHORIZATION"
        for item in governance["resolved_conflicts"]
    )


def test_required_evidence_is_enforced_not_declarative(tmp_path):
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=StaticBootstrap(),
    )
    original = harness._contract_for_task

    def broken_contract(task):
        contract = original(task)
        return contract.__class__(
            allowed_files=contract.allowed_files,
            protected_paths=contract.protected_paths,
            allowed_actions=contract.allowed_actions,
            acceptance_criteria=contract.acceptance_criteria,
            evidence_required=contract.evidence_required + ("nonexistent_evidence",),
            stop_conditions=contract.stop_conditions,
            max_memory_results=contract.max_memory_results,
        )

    harness._contract_for_task = broken_contract
    result, trace = harness.execute(
        task=_task(),
        agent_id="pilot_evidence_gate",
        agent_role="coder",
        workspace=tmp_path / "workspace",
        memory_query="",
        enable_memory=False,
    )
    assert result["status"] == "failure"
    assert "nonexistent_evidence" in trace["record"]["execution"]["missing_evidence"]


def test_fixture_bootstrap_is_not_labeled_as_direct_evidence(tmp_path):
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=StaticBootstrap(),
    )
    result, trace = harness.execute(
        task=_task(),
        agent_id="pilot_fixture_evidence",
        agent_role="coder",
        workspace=tmp_path / "workspace",
        memory_query="",
        enable_memory=False,
    )
    assert result["status"] == "success"
    assert all(
        source["evidence_level"] == "TEST_FIXTURE"
        for source in trace["record"]["bootstrap"]["sources"]
    )

def test_bootstrap_sensitive_marker_is_not_persisted_in_trace(tmp_path):
    marker = "SYNTHETIC_SENSITIVE_MARKER_20261008"
    bootstrap = StaticBootstrap()
    payload = bootstrap(_task(), "pilot")
    payload["sources"][0]["text"] = marker
    bootstrap_with_marker = lambda task, principal: payload

    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=bootstrap_with_marker,
    )
    result, trace = harness.execute(
        task=_task(),
        agent_id="pilot_marker",
        agent_role="coder",
        workspace=tmp_path / "workspace",
        memory_query="",
        enable_memory=False,
    )
    assert result["status"] == "success"
    assert marker not in json.dumps(trace["record"], sort_keys=True)

def test_review_memory_remains_data_only_and_unverified(tmp_path):
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=StaticBootstrap(),
        memory_controller=FixtureMemoryController(),
    )
    result, trace = harness.execute(
        task=_task(),
        agent_id="pilot_review_memory",
        agent_role="coder",
        workspace=tmp_path / "workspace",
        memory_query="review data",
        enable_memory=True,
    )
    assert result["status"] == "success"
    memory = trace["record"]["memory"]
    assert memory["memory_ids"] == ["review-fixture-001"]
    context = trace["record"]["model"]
    assert "REVIEW" in json.dumps(trace["record"])
    assert '"authority": "DATA_ONLY"' in json.dumps(trace["record"])
    assert '"verification": {"state": "unverified"}' in json.dumps(trace["record"])

def test_synthetic_secret_is_redacted_from_persisted_trace(tmp_path, monkeypatch):
    secret = "SYNTHETIC_SECRET_20261008"
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", secret)
    harness = RealAgentExecutionHarness(
        trace_dir=tmp_path / "traces",
        bootstrap_provider=StaticBootstrap(),
        model_executor=SecretEchoModelExecutor(provider_mode="local"),
    )
    result, trace = harness.execute(
        task=_task(),
        agent_id="pilot_secret",
        agent_role="coder",
        workspace=tmp_path / "workspace",
        memory_query="",
        enable_memory=False,
    )
    assert result["status"] == "success"
    assert secret not in json.dumps(trace["record"], sort_keys=True)
