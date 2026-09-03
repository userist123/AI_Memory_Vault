"""cognitive_core/tests/test_model_agent_execution_harness.py

Deterministic integration test suite for the Model Execution Boundary in RealAgentExecutionHarness:
  Test A: Fake provider -> model execution -> structured action -> validated action -> real command -> verification -> trace
  Test B: Real provider configuration missing fails closed (trace failure, no silent fake fallback)
  Test C: Invalid model action (unknown action type rejected and persisted)
  Test D: Unauthorized action (action unavailable to agent role rejected and persisted)
  Test E: Secret redaction (verifies API keys and HMAC tokens never leak into trace JSON/JSONL)
  Test F: Workspace confinement (path traversal rejected and persisted)
  Integration Test: REAL_PROVIDER_INTEGRATION (explicitly marked for manual/live provider runs)
"""
from __future__ import annotations

import json
import os
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any, Dict, Generator, List

import pytest

from cognitive_core.fake_model_provider import FakeModelProvider
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
        monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_for_real_harness_32chars_min")


@pytest.fixture
def workspace_dir() -> Generator[Path, None, None]:
    """Creates a clean isolated temporary directory for real workspace operations."""
    temp_dir = tempfile.mkdtemp(prefix="agent_model_ws_")
    ws = Path(temp_dir).resolve()
    yield ws
    shutil.rmtree(ws, ignore_errors=True)


@pytest.fixture
def temp_trace_dir() -> Generator[Path, None, None]:
    """Creates a temporary directory for persisted execution traces."""
    temp_dir = tempfile.mkdtemp(prefix="agent_model_traces_")
    t_path = Path(temp_dir).resolve()
    yield t_path
    shutil.rmtree(t_path, ignore_errors=True)


def test_harness_test_a_fake_provider_structured_action(
    workspace_dir: Path, temp_trace_dir: Path
) -> None:
    """Test A: Fake provider executes structured model action, validates, modifies workspace, verifies, traces."""
    canned = json.dumps({
        "actions": [
            {
                "action": "write_file",
                "path": "calc.py",
                "content": "def add(a, b):\n    return a + b\n",
            },
            {
                "action": "write_file",
                "path": "test_calc.py",
                "content": "from calc import add\ndef test_add():\n    assert add(2, 3) == 5\n",
            },
            {
                "action": "run_command",
                "command": [sys.executable, "-m", "py_compile", "calc.py"],
            },
        ]
    })
    fake_provider = FakeModelProvider(
        provider_name="fake",
        model_name="fake-gpt-4o",
        canned_response=canned,
    )
    model_executor = AgentModelExecutor(
        provider_mode="fake",
        provider=fake_provider,
        model_name="fake-gpt-4o",
    )

    task = AgentTask(
        task_id="task_model_test_a",
        description="Write calculator module and verify",
        target_file="calc.py",
        test_file="test_calc.py",
        instructions="Implement addition and verify via pytest.",
    )

    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_trace_dir,
        model_executor=model_executor,
    )

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_coder_01",
        agent_role="synthesizer",
        workspace=workspace_dir,
        memory_query="knowledge",
    )

    assert result["status"] == "success"
    assert result["verification_status"] == "passed"
    assert (workspace_dir / "calc.py").exists()
    assert (workspace_dir / "test_calc.py").exists()

    # Trace assertions
    trace = trace_ref["record"]
    assert trace["model"]["provider_mode"] == "fake"
    assert trace["model"]["provider_name"] == "fake"
    assert trace["model"]["response_status"] == "success"
    assert len(trace["actions"]) == 3
    assert all(act["validated"] for act in trace["actions"])
    assert trace["verification"]["status"] == "passed"
    assert Path(trace_ref["trace_file"]).exists()


def test_harness_test_b_real_provider_missing_config_fails_closed(
    workspace_dir: Path, temp_trace_dir: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Test B: Real provider configuration missing fails closed, traces failure, NO silent fake fallback."""
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)

    model_executor = AgentModelExecutor(
        provider_mode="openai",
        model_name="gpt-4o",
        api_key=None,
    )

    task = AgentTask(
        task_id="task_model_test_b",
        description="Fails closed on missing real provider API key",
        target_file="calc.py",
        test_file="test_calc.py",
        instructions="Should not execute because OpenAI API key is missing.",
    )

    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_trace_dir,
        model_executor=model_executor,
    )

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_lead_02",
        agent_role="synthesizer",
        workspace=workspace_dir,
        memory_query="knowledge",
    )

    # Must fail closed
    assert result["status"] == "failure"
    assert result["model_status"] == "failed"

    # Must trace failure accurately with NO silent fallback to fake
    trace = trace_ref["record"]
    assert trace["model"]["provider_mode"] == "openai"
    assert trace["model"]["provider_name"] == "openai"
    assert trace["model"]["response_status"] == "failed"
    assert "OpenAIAuthenticationError" in trace["model"]["response_text"]
    assert Path(trace_ref["trace_file"]).exists()


def test_harness_test_c_invalid_model_action_rejected(
    workspace_dir: Path, temp_trace_dir: Path
) -> None:
    """Test C: Invalid/unknown model action is rejected, does not execute, and is persisted."""
    canned = json.dumps({
        "action": "destroy_database",
        "target": "production_cluster",
    })
    fake_provider = FakeModelProvider(
        provider_name="fake",
        model_name="fake-model",
        canned_response=canned,
    )
    model_executor = AgentModelExecutor(
        provider_mode="fake",
        provider=fake_provider,
    )

    task = AgentTask(
        task_id="task_model_test_c",
        description="Model returns an unknown dangerous action",
        target_file="calc.py",
        test_file="test_calc.py",
        instructions="Produce unknown action type.",
    )

    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_trace_dir,
        model_executor=model_executor,
    )

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_rogue_01",
        agent_role="synthesizer",
        workspace=workspace_dir,
        memory_query="knowledge",
    )

    assert result["status"] == "failure"
    trace = trace_ref["record"]
    assert len(trace["actions"]) == 1
    act = trace["actions"][0]
    assert act["validated"] is False
    assert "unknown action type 'destroy_database'" in act["execution_status"]
    assert Path(trace_ref["trace_file"]).exists()


def test_harness_test_d_unauthorized_action_rejected(
    workspace_dir: Path, temp_trace_dir: Path
) -> None:
    """Test D: Model action unavailable to agent's worker role is rejected, does not execute, and is persisted."""
    # Agent with role 'verifier' attempts to execute 'write_file'
    canned = json.dumps({
        "action": "write_file",
        "path": "malicious.py",
        "content": "print('compromised')\n",
    })
    fake_provider = FakeModelProvider(
        provider_name="fake",
        model_name="fake-model",
        canned_response=canned,
    )
    model_executor = AgentModelExecutor(
        provider_mode="fake",
        provider=fake_provider,
    )

    task = AgentTask(
        task_id="task_model_test_d",
        description="Verifier role attempts unauthorized file write",
        target_file="malicious.py",
        test_file="test_malicious.py",
        instructions="Verifier must not write files.",
    )

    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_trace_dir,
        model_executor=model_executor,
    )

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_auditor_01",
        agent_role="verifier",  # verifier only has 'read_file' and 'run_command', NOT 'write_file'
        workspace=workspace_dir,
        memory_query="knowledge",
    )

    assert result["status"] == "failure"
    trace = trace_ref["record"]
    assert len(trace["actions"]) == 1
    act = trace["actions"][0]
    assert act["validated"] is False
    assert "unauthorized for role 'verifier'" in act["execution_status"]
    assert not (workspace_dir / "malicious.py").exists()
    assert Path(trace_ref["trace_file"]).exists()


def test_harness_test_e_secret_redaction(
    workspace_dir: Path, temp_trace_dir: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Test E: Configured credentials and API secrets are never persisted into traces."""
    secret_key = "sk-super-secret-openai-api-key-998877"
    secret_hmac = "hmac-secret-super-confidential-token-112233"
    monkeypatch.setenv("OPENAI_API_KEY", secret_key)
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", secret_hmac)

    # Model generates text containing the credentials
    canned = json.dumps({
        "actions": [
            {
                "action": "write_file",
                "path": "credentials.txt",
                "content": f"Leaked key: {secret_key} and hmac: {secret_hmac}",
            }
        ]
    })
    fake_provider = FakeModelProvider(
        provider_name="fake",
        model_name="fake-model",
        canned_response=canned,
    )
    model_executor = AgentModelExecutor(
        provider_mode="fake",
        provider=fake_provider,
        api_key=secret_key,
    )

    task = AgentTask(
        task_id="task_model_test_e",
        description="Verify redaction of credentials from trace output",
        target_file="credentials.txt",
        test_file="test_dummy.py",
        instructions="Ensure trace never contains secrets.",
        verification_command=[sys.executable, "-c", "print('ok')"],
    )

    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_trace_dir,
        model_executor=model_executor,
    )

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_synth_01",
        agent_role="synthesizer",
        workspace=workspace_dir,
        memory_query="knowledge",
    )

    trace_file_path = Path(trace_ref["trace_file"])
    with open(trace_file_path, "r", encoding="utf-8") as f:
        trace_file_raw = f.read()

    traces_jsonl_path = Path(trace_ref["traces_jsonl"])
    with open(traces_jsonl_path, "r", encoding="utf-8") as f:
        traces_jsonl_raw = f.read()

    # Secrets must NEVER appear in trace files
    assert secret_key not in trace_file_raw
    assert secret_hmac not in trace_file_raw
    assert secret_key not in traces_jsonl_raw
    assert secret_hmac not in traces_jsonl_raw

    # Redaction marker must be present
    assert "[REDACTED_SECRET]" in trace_file_raw


def test_harness_test_f_workspace_escape_rejected(
    workspace_dir: Path, temp_trace_dir: Path
) -> None:
    """Test F: Path traversal outside workspace is rejected, not written, and persisted."""
    canned = json.dumps({
        "action": "write_file",
        "path": "../../outside_workspace_escape.txt",
        "content": "malicious content outside workspace\n",
    })
    fake_provider = FakeModelProvider(
        provider_name="fake",
        model_name="fake-model",
        canned_response=canned,
    )
    model_executor = AgentModelExecutor(
        provider_mode="fake",
        provider=fake_provider,
    )

    task = AgentTask(
        task_id="task_model_test_f",
        description="Attempt path traversal outside workspace",
        target_file="outside.txt",
        test_file="test_dummy.py",
        instructions="Should be rejected by path resolution check.",
    )

    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_trace_dir,
        model_executor=model_executor,
    )

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_coder_02",
        agent_role="synthesizer",
        workspace=workspace_dir,
        memory_query="knowledge",
    )

    assert result["status"] == "failure"
    trace = trace_ref["record"]
    assert len(trace["actions"]) == 1
    assert trace["actions"][0]["validated"] is False
    assert "path traversal outside workspace" in trace["actions"][0]["execution_status"]
    assert not (workspace_dir.parent / "outside_workspace_escape.txt").exists()


@pytest.mark.skipif(
    not os.getenv("RUN_REAL_PROVIDER_INTEGRATION"),
    reason="Requires live provider environment (RUN_REAL_PROVIDER_INTEGRATION=1)",
)
def test_real_provider_integration(workspace_dir: Path, temp_trace_dir: Path) -> None:
    """REAL_PROVIDER_INTEGRATION: Manual/live integration test with real external/local provider."""
    provider_mode = os.getenv("REAL_PROVIDER_MODE", "local")
    model_name = os.getenv("REAL_PROVIDER_MODEL", "qwen2.5-coder")
    base_url = os.getenv("REAL_PROVIDER_BASE_URL", "http://localhost:11434")
    api_key = os.getenv("OPENAI_API_KEY")

    model_executor = AgentModelExecutor(
        provider_mode=provider_mode,
        model_name=model_name,
        base_url=base_url,
        api_key=api_key,
    )

    task = AgentTask(
        task_id="task_live_real_provider",
        description="Live model execution with real provider",
        target_file="math_ops.py",
        test_file="test_math_ops.py",
        instructions="Implement multiply(a, b) and test_multiply() in pytest format.",
    )

    controller = get_memory_controller()
    harness = RealAgentExecutionHarness(
        memory_controller=controller,
        trace_dir=temp_trace_dir,
        model_executor=model_executor,
    )

    result, trace_ref = harness.execute(
        task=task,
        agent_id="agent_live_01",
        agent_role="synthesizer",
        workspace=workspace_dir,
        memory_query="knowledge",
    )

    assert result["status"] == "success"
    trace = trace_ref["record"]
    assert trace["model"]["provider_mode"] == provider_mode
    assert trace["model"]["response_status"] == "success"
    assert trace["model"]["latency_ms"] > 0
