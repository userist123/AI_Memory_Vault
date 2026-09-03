"""cognitive_core/real_execution_harness.py — Real Agent Execution Harness.

Provides a deterministic, machine-verifiable runtime harness implementing the contract:
    real agent execution
        ↓
    secure memory retrieval (MemoryController.search)
        ↓
    retrieved memory becomes execution context (Observed)
        ↓
    REAL model inference (Fake, Local, or OpenAI provider)
        ↓
    model-produced structured action validation (role + workspace isolation)
        ↓
    real command/tool execution (subprocess in workspace)
        ↓
    real workspace modification
        ↓
    real verification/test (subprocess pytest in workspace)
        ↓
    persistent execution evidence (structured JSON trace with secret redaction)

Boundary Invariants:
1. Retrieval must exclusively use MemoryController.search() under Principal.AI_AGENT.
2. Memory observation is recorded as OBSERVED (retrieved memory entered execution context).
3. Causal memory effectiveness, skill promotion, and LLM causal influence are NOT claimed.
4. Execution and verification must use real OS subprocesses, never simulated runners.
5. All executions persist structured immutable execution traces with credentials redacted.
6. Real provider execution fails closed if misconfigured; never silently falls back to fake.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import sys
import threading
import time
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Set, Tuple, Union

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.orchestrator import AgentRole, validate_agent_role
from cognitive_core.model_provider import ModelProvider, ModelRequest, ModelResponse
from cognitive_core.fake_model_provider import FakeModelProvider
from cognitive_core.local_provider import LocalProvider, LocalProviderError
from cognitive_core.openai_provider import OpenAIProvider, OpenAIProviderError, OpenAIAuthenticationError


# Strict action permissions per agent role
ROLE_ALLOWED_ACTIONS: Dict[AgentRole, Set[str]] = {
    AgentRole.SYNTHESIZER: {"write_file", "run_command", "read_file"},
    AgentRole.VERIFIER: {"read_file", "run_command"},
    AgentRole.RETRIEVAL: {"read_file"},
    AgentRole.ROUTER: set(),
    AgentRole.CONSOLIDATOR: {"write_file", "read_file"},
    AgentRole.CRITIC: {"read_file"},
}


@dataclass
class AgentTask:
    """Explicit task definition for the execution harness."""
    task_id: str
    description: str
    target_file: str
    test_file: str
    instructions: str
    code_patch: Optional[str] = None
    test_patch: Optional[str] = None
    verification_command: Optional[List[str]] = None


@dataclass
class CommandExecutionRecord:
    """Record of a real command executed in the workspace subprocess."""
    command: str
    arguments: List[str]
    started_at: str
    finished_at: str
    stdout: str
    stderr: str
    exit_code: int

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class WorkspaceDiff:
    """Filesystem changes observed in the workspace."""
    files_created: List[str] = field(default_factory=list)
    files_modified: List[str] = field(default_factory=list)
    files_deleted: List[str] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class VerificationResult:
    """Outcome of the real verification/test subprocess command."""
    command: str
    stdout: str
    stderr: str
    exit_code: int
    status: str  # 'passed' | 'failed'

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class ActionExecutionRecord:
    """Record of a model-requested action and its validation/execution outcome."""
    action_type: str
    validated: bool
    execution_status: str
    details: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "action_type": self.action_type,
            "validated": self.validated,
            "execution_status": self.execution_status,
        }


@dataclass
class ModelExecutionRecord:
    """Structured record of the model execution boundary."""
    provider_mode: str          # 'deterministic' | 'fake' | 'local' | 'openai'
    provider_name: str          # 'none' | 'fake' | 'local' | 'openai'
    model_name: str
    request_started_at: str
    response_finished_at: str
    latency_ms: float
    response_status: str        # 'success' | 'failed' | 'skipped'
    response_text: str
    error_details: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class ExecutionTrace:
    """Immutable persistent execution trace record."""
    trace_id: str
    agent_id: str
    agent_role: str
    task_id: str
    started_at: str
    finished_at: str
    memory: Dict[str, Any]
    model: Dict[str, Any]
    actions: List[Dict[str, Any]]
    execution: Dict[str, Any]
    workspace: Dict[str, Any]
    verification: Dict[str, Any]
    experiment: Optional[Dict[str, Any]] = None

    def to_dict(self) -> Dict[str, Any]:
        data = asdict(self)
        if self.experiment is None:
            data.pop("experiment", None)
        return data


def _redact_secrets(data: Any, secrets: List[str]) -> Any:
    """Recursively redacts configured credentials from trace structures."""
    active_secrets = [s for s in secrets if isinstance(s, str) and len(s) >= 4]
    if not active_secrets:
        return data

    if isinstance(data, str):
        redacted = data
        for s in active_secrets:
            redacted = redacted.replace(s, "[REDACTED_SECRET]")
        return redacted
    elif isinstance(data, dict):
        return {k: _redact_secrets(v, active_secrets) for k, v in data.items()}
    elif isinstance(data, list):
        return [_redact_secrets(item, active_secrets) for item in data]
    return data


def _snapshot_directory(directory: Path) -> Dict[str, str]:
    """Snapshots non-cached files in a workspace directory to SHA-256 hashes."""
    snapshot: Dict[str, str] = {}
    if not directory.exists():
        return snapshot

    for root, dirs, files in os.walk(directory):
        dirs[:] = [d for d in dirs if d not in ('.pytest_cache', '__pycache__', '.git', 'telemetry')]
        for filename in files:
            filepath = Path(root) / filename
            rel_path = filepath.relative_to(directory).as_posix()
            try:
                with open(filepath, 'rb') as f:
                    content_bytes = f.read()
                snapshot[rel_path] = hashlib.sha256(content_bytes).hexdigest()
            except Exception:
                pass
    return snapshot


def _calculate_workspace_diff(
    initial_snapshot: Dict[str, str], current_snapshot: Dict[str, str]
) -> WorkspaceDiff:
    """Compares initial and current snapshots to identify created, modified, and deleted files."""
    created: List[str] = []
    modified: List[str] = []
    deleted: List[str] = []

    for rel_path, file_hash in current_snapshot.items():
        if rel_path not in initial_snapshot:
            created.append(rel_path)
        elif initial_snapshot[rel_path] != file_hash:
            modified.append(rel_path)

    for rel_path in initial_snapshot:
        if rel_path not in current_snapshot:
            deleted.append(rel_path)

    return WorkspaceDiff(
        files_created=sorted(created),
        files_modified=sorted(modified),
        files_deleted=sorted(deleted),
    )


class AgentModelExecutor:
    """Explicit boundary for model inference.

    Connects the harness to configured ModelProvider implementations:
      - 'deterministic': no model call (baseline)
      - 'fake': FakeModelProvider for deterministic testing
      - 'local': LocalProvider (Ollama HTTP endpoint)
      - 'openai': OpenAIProvider (OpenAI Responses API)

    Enforces:
      1. No silent fallback from real provider to fake.
      2. Missing provider configuration fails closed.
      3. Secrets are never stored or returned.
      4. Network/API errors are captured and recorded.
    """

    def __init__(
        self,
        provider_mode: str = "deterministic",
        provider: Optional[ModelProvider] = None,
        model_name: Optional[str] = None,
        base_url: Optional[str] = None,
        api_key: Optional[str] = None,
        model_tier: str = "standard",
    ) -> None:
        self.provider_mode = provider_mode.strip().lower()
        self.custom_provider = provider
        self.model_name = model_name
        self.base_url = base_url
        self.api_key = api_key
        self.model_tier = model_tier

        valid_modes = {"deterministic", "fake", "local", "openai"}
        if self.provider_mode not in valid_modes:
            raise ValueError(
                f"Unsupported provider_mode: '{self.provider_mode}'. "
                f"Supported modes: {sorted(list(valid_modes))}"
            )

    def execute_model(
        self,
        context: Dict[str, Any],
        task: AgentTask,
    ) -> ModelExecutionRecord:
        """Invokes the configured provider and captures structured telemetry."""
        now_iso = datetime.now(timezone.utc).isoformat()

        if self.provider_mode == "deterministic":
            return ModelExecutionRecord(
                provider_mode="deterministic",
                provider_name="none",
                model_name="deterministic_policy",
                request_started_at=now_iso,
                response_finished_at=now_iso,
                latency_ms=0.0,
                response_status="skipped",
                response_text="Model execution skipped (deterministic mode).",
            )

        # Assemble prompt from bounded execution context
        prompt = (
            f"TASK ID: {task.task_id}\n"
            f"DESCRIPTION: {task.description}\n"
            f"INSTRUCTIONS: {task.instructions}\n\n"
            f"CONTEXT MEMORIES:\n{json.dumps(context.get('retrieved_memories', []), indent=2)}\n\n"
            "Produce structured JSON with actions to solve the task."
        )
        system_prompt = (
            f"You are an AI Agent with role: {context.get('agent_role', 'synthesizer')}.\n"
            "You must respond ONLY with a single valid JSON object containing an 'actions' list, e.g.:\n"
            '{"actions": [{"action": "write_file", "path": "...", "content": "..."}]}'
        )
        model_req = ModelRequest(
            prompt=prompt,
            model_tier=self.model_tier,
            system_prompt=system_prompt,
            metadata={"task_id": task.task_id, "format": "json"},
        )

        req_start = datetime.now(timezone.utc)
        req_start_iso = req_start.isoformat()
        t0 = time.perf_counter()

        # 1. Fake Provider
        if self.provider_mode == "fake":
            provider = self.custom_provider or FakeModelProvider(
                provider_name="fake",
                model_name=self.model_name or "fake-model",
            )
            resp = provider.generate(model_req)
            t1 = time.perf_counter()
            req_end_iso = datetime.now(timezone.utc).isoformat()
            return ModelExecutionRecord(
                provider_mode="fake",
                provider_name=resp.provider,
                model_name=resp.model,
                request_started_at=req_start_iso,
                response_finished_at=req_end_iso,
                latency_ms=round((t1 - t0) * 1000.0, 2),
                response_status="success",
                response_text=resp.content,
            )

        # 2. Local Provider (Ollama)
        elif self.provider_mode == "local":
            try:
                provider = self.custom_provider or LocalProvider(
                    model_name=self.model_name or "qwen2.5-coder",
                    base_url=self.base_url or "http://localhost:11434",
                )
                resp = provider.generate(model_req)
                t1 = time.perf_counter()
                req_end_iso = datetime.now(timezone.utc).isoformat()
                return ModelExecutionRecord(
                    provider_mode="local",
                    provider_name=resp.provider,
                    model_name=resp.model,
                    request_started_at=req_start_iso,
                    response_finished_at=req_end_iso,
                    latency_ms=round((t1 - t0) * 1000.0, 2),
                    response_status="success",
                    response_text=resp.content,
                )
            except Exception as e:
                # Fails closed, records provider error, NEVER falls back to fake
                t1 = time.perf_counter()
                req_end_iso = datetime.now(timezone.utc).isoformat()
                return ModelExecutionRecord(
                    provider_mode="local",
                    provider_name="local",
                    model_name=self.model_name or "qwen2.5-coder",
                    request_started_at=req_start_iso,
                    response_finished_at=req_end_iso,
                    latency_ms=round((t1 - t0) * 1000.0, 2),
                    response_status="failed",
                    response_text=f"LocalProviderError: {str(e)}",
                    error_details=f"Provider endpoint connection failure: {str(e)}",
                )

        # 3. OpenAI Provider
        elif self.provider_mode == "openai":
            key = self.api_key or os.getenv("OPENAI_API_KEY")
            if not key:
                t1 = time.perf_counter()
                req_end_iso = datetime.now(timezone.utc).isoformat()
                return ModelExecutionRecord(
                    provider_mode="openai",
                    provider_name="openai",
                    model_name=self.model_name or "gpt-4o",
                    request_started_at=req_start_iso,
                    response_finished_at=req_end_iso,
                    latency_ms=round((t1 - t0) * 1000.0, 2),
                    response_status="failed",
                    response_text="OpenAIAuthenticationError: OPENAI_API_KEY is not configured",
                    error_details="Missing OpenAI API key in environment or arguments",
                )

            try:
                provider = self.custom_provider or OpenAIProvider(
                    model_name=self.model_name or "gpt-4o",
                    api_key=key,
                    base_url=self.base_url or "https://api.openai.com/v1",
                )
                resp = provider.generate(model_req)
                t1 = time.perf_counter()
                req_end_iso = datetime.now(timezone.utc).isoformat()
                return ModelExecutionRecord(
                    provider_mode="openai",
                    provider_name=resp.provider,
                    model_name=resp.model,
                    request_started_at=req_start_iso,
                    response_finished_at=req_end_iso,
                    latency_ms=round((t1 - t0) * 1000.0, 2),
                    response_status="success",
                    response_text=resp.content,
                )
            except Exception as e:
                # Fails closed, records API error, NEVER falls back to fake
                t1 = time.perf_counter()
                req_end_iso = datetime.now(timezone.utc).isoformat()
                return ModelExecutionRecord(
                    provider_mode="openai",
                    provider_name="openai",
                    model_name=self.model_name or "gpt-4o",
                    request_started_at=req_start_iso,
                    response_finished_at=req_end_iso,
                    latency_ms=round((t1 - t0) * 1000.0, 2),
                    response_status="failed",
                    response_text=f"OpenAIProviderError: {str(e)}",
                    error_details=f"API request failure: {str(e)}",
                )

        raise RuntimeError(f"Unhandled provider_mode: {self.provider_mode}")


def _extract_json_payload(text: str) -> Optional[Any]:
    """Extracts JSON payload from model text, markdown blocks, or raw delimiters."""
    cleaned = text.strip()
    # 1. Direct JSON parse
    try:
        return json.loads(cleaned)
    except Exception:
        pass

    # 2. Markdown code blocks
    code_match = re.search(r"```(?:json)?\s*([\s\S]*?)\s*```", cleaned)
    if code_match:
        try:
            return json.loads(code_match.group(1).strip())
        except Exception:
            pass

    # 3. Outer brace pair
    b_start = cleaned.find("{")
    b_end = cleaned.rfind("}")
    if b_start != -1 and b_end != -1 and b_end > b_start:
        try:
            return json.loads(cleaned[b_start : b_end + 1])
        except Exception:
            pass

    # 4. Outer bracket pair
    a_start = cleaned.find("[")
    a_end = cleaned.rfind("]")
    if a_start != -1 and a_end != -1 and a_end > a_start:
        try:
            return json.loads(cleaned[a_start : a_end + 1])
        except Exception:
            pass

    return None


def _extract_and_validate_actions(
    model_text: str,
    role: AgentRole,
    workspace: Path,
) -> Tuple[List[Dict[str, Any]], List[ActionExecutionRecord]]:
    """Extracts, validates, and scopes model-produced actions."""
    records: List[ActionExecutionRecord] = []
    valid_actions: List[Dict[str, Any]] = []

    parsed = _extract_json_payload(model_text)

    if parsed is None:
        records.append(
            ActionExecutionRecord(
                action_type="unknown",
                validated=False,
                execution_status="rejected: no structured action found in response",
            )
        )
        return valid_actions, records

    # Normalize to list of action objects
    candidates: List[Dict[str, Any]] = []
    if isinstance(parsed, dict):
        if "actions" in parsed and isinstance(parsed["actions"], list):
            candidates = parsed["actions"]
        elif "action" in parsed:
            candidates = [parsed]
    elif isinstance(parsed, list):
        candidates = parsed

    allowed_actions = ROLE_ALLOWED_ACTIONS.get(role, set())

    for item in candidates:
        if not isinstance(item, dict):
            records.append(
                ActionExecutionRecord(
                    action_type="unknown",
                    validated=False,
                    execution_status="rejected: action specification must be a dictionary",
                )
            )
            continue

        act_type = str(item.get("action", "")).strip().lower()
        if not act_type:
            records.append(
                ActionExecutionRecord(
                    action_type="unknown",
                    validated=False,
                    execution_status="rejected: missing action field",
                )
            )
            continue

        # 1. Action type validation
        if act_type not in ("write_file", "run_command", "read_file"):
            records.append(
                ActionExecutionRecord(
                    action_type=act_type,
                    validated=False,
                    execution_status=f"rejected: unknown action type '{act_type}'",
                )
            )
            continue

        # 2. Role authorization validation
        if act_type not in allowed_actions:
            records.append(
                ActionExecutionRecord(
                    action_type=act_type,
                    validated=False,
                    execution_status=f"rejected: action '{act_type}' unauthorized for role '{role.value}'",
                )
            )
            continue

        # 3. Workspace containment validation
        if act_type in ("write_file", "read_file"):
            target_rel = item.get("path")
            if not target_rel or not isinstance(target_rel, str):
                records.append(
                    ActionExecutionRecord(
                        action_type=act_type,
                        validated=False,
                        execution_status="rejected: missing or invalid file path",
                    )
                )
                continue

            target_path = (workspace / target_rel).resolve()
            ws_resolved = workspace.resolve()
            if not (target_path == ws_resolved or ws_resolved in target_path.parents):
                records.append(
                    ActionExecutionRecord(
                        action_type=act_type,
                        validated=False,
                        execution_status="rejected: path traversal outside workspace",
                    )
                )
                continue

        # Validated successfully
        valid_actions.append(item)
        records.append(
            ActionExecutionRecord(
                action_type=act_type,
                validated=True,
                execution_status="validated",
                details=item,
            )
        )

    return valid_actions, records


class BaseAgentPolicy:
    """Abstract agent execution policy boundary.

    Distinguishes:
      - agent orchestration (harness pipeline, role validation, context assembly)
      - tool execution (real subprocess command and file manipulation)
      - LLM inference (external or local neural generation when configured)
    """

    def apply(
        self,
        context: Dict[str, Any],
        workspace: Path,
        task: AgentTask,
    ) -> Tuple[List[CommandExecutionRecord], List[ActionExecutionRecord]]:
        raise NotImplementedError


class DeterministicCodeAgentPolicy(BaseAgentPolicy):
    """Deterministic agent policy for local reproducible execution.

    Applies task code and test patches to the workspace and executes
    real tool commands without requiring external paid LLM APIs.
    """

    def apply(
        self,
        context: Dict[str, Any],
        workspace: Path,
        task: AgentTask,
    ) -> Tuple[List[CommandExecutionRecord], List[ActionExecutionRecord]]:
        records: List[CommandExecutionRecord] = []
        action_records: List[ActionExecutionRecord] = []

        # 1. Modify target file in workspace
        target_path = workspace / task.target_file
        target_path.parent.mkdir(parents=True, exist_ok=True)
        if task.code_patch is not None:
            with open(target_path, 'w', encoding='utf-8') as f:
                f.write(task.code_patch)
            action_records.append(
                ActionExecutionRecord(
                    action_type="write_file",
                    validated=True,
                    execution_status="executed",
                )
            )

        # 2. Modify test file in workspace if specified
        if task.test_file and task.test_patch is not None:
            test_path = workspace / task.test_file
            test_path.parent.mkdir(parents=True, exist_ok=True)
            with open(test_path, 'w', encoding='utf-8') as f:
                f.write(task.test_patch)
            action_records.append(
                ActionExecutionRecord(
                    action_type="write_file",
                    validated=True,
                    execution_status="executed",
                )
            )

        # 3. Real command execution (e.g. check python syntax on target file)
        started_at = datetime.now(timezone.utc).isoformat()
        cmd = [sys.executable, '-m', 'py_compile', str(target_path)]
        res = subprocess.run(cmd, cwd=str(workspace), capture_output=True, text=True)
        finished_at = datetime.now(timezone.utc).isoformat()

        records.append(
            CommandExecutionRecord(
                command=sys.executable,
                arguments=['-m', 'py_compile', str(target_path)],
                started_at=started_at,
                finished_at=finished_at,
                stdout=res.stdout,
                stderr=res.stderr,
                exit_code=res.returncode,
            )
        )
        action_records.append(
            ActionExecutionRecord(
                action_type="run_command",
                validated=True,
                execution_status="executed" if res.returncode == 0 else "failed",
            )
        )
        return records, action_records


class RealAgentExecutionHarness:
    """Smallest reproducible Real Agent Execution Harness with Model Inference.

    Enforces the explicit execution contract:
    1. validate agent role
    2. retrieve memory through MemoryController.search()
    3. capture returned memory IDs
    4. construct bounded execution context
    5. execute REAL model inference (Fake, Local, or OpenAI)
    6. validate and scope model-produced actions
    7. execute a REAL command/tool
    8. capture stdout/stderr/exit code
    9. capture workspace changes
    10. run verification/test
    11. capture verification result
    12. persist execution evidence with secret redaction
    """

    def __init__(
        self,
        memory_controller: Optional[MemoryController] = None,
        trace_dir: Optional[Union[str, Path]] = None,
        authorizer_principal: Principal = Principal.AI_AGENT,
        default_policy: Optional[BaseAgentPolicy] = None,
        model_executor: Optional[AgentModelExecutor] = None,
    ):
        if memory_controller is None:
            from cognitive_core.recall_cli import get_memory_controller
            memory_controller = get_memory_controller()
        self.controller = memory_controller
        self.principal = authorizer_principal
        self.default_policy = default_policy or DeterministicCodeAgentPolicy()
        self.model_executor = model_executor or AgentModelExecutor(provider_mode="deterministic")

        base_trace = Path(trace_dir or os.getenv('ANTIGRAVITY_TELEMETRY_DIR', 'telemetry'))
        self.trace_dir = base_trace / 'execution_traces'
        self.trace_dir.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()

    def execute(
        self,
        task: Union[AgentTask, Dict[str, Any]],
        agent_id: str,
        agent_role: Union[str, AgentRole],
        workspace: Union[str, Path],
        memory_query: Optional[str] = None,
        agent_policy: Optional[BaseAgentPolicy] = None,
        verification_command: Optional[List[str]] = None,
        model_executor: Optional[AgentModelExecutor] = None,
        enable_memory: bool = True,
        experiment: Optional[Dict[str, Any]] = None,
    ) -> Tuple[Dict[str, Any], Dict[str, Any]]:
        """Executes task following the full agent execution contract."""
        started_at = datetime.now(timezone.utc).isoformat()
        trace_id = f'trace_{uuid.uuid4().hex[:12]}'
        ws_path = Path(workspace).resolve()
        ws_path.mkdir(parents=True, exist_ok=True)

        # Normalize task object
        if isinstance(task, dict):
            task_obj = AgentTask(
                task_id=task.get('task_id', f'task_{uuid.uuid4().hex[:8]}'),
                description=task.get('description', 'Agent execution task'),
                target_file=task.get('target_file', 'target.py'),
                test_file=task.get('test_file', 'test_target.py'),
                instructions=task.get('instructions', ''),
                code_patch=task.get('code_patch'),
                test_patch=task.get('test_patch'),
                verification_command=task.get('verification_command'),
            )
        else:
            task_obj = task

        # 1. Validate agent role
        authorized_role = validate_agent_role(agent_role)

        # 2 & 3. Retrieve memory through MemoryController.search() and capture IDs
        raw_results: List[Dict[str, Any]] = []
        effective_query = memory_query if (enable_memory and memory_query) else ""
        if enable_memory and memory_query:
            pack = self.controller.search(
                principal=self.principal,
                query=memory_query,
                page_size=5,
            )
            raw_results = pack.get('results', []) if isinstance(pack, dict) else []

        retrieved_memory_ids: List[str] = []
        relevance_scores: Dict[str, float] = {}

        for item in raw_results:
            if isinstance(item, dict):
                m_id = item.get('id') or item.get('note_id')
                if m_id and m_id not in retrieved_memory_ids:
                    retrieved_memory_ids.append(str(m_id))
                    score = item.get('score')
                    if score is not None:
                        try:
                            relevance_scores[str(m_id)] = round(float(score), 4)
                        except (ValueError, TypeError):
                            pass

        retrieval_count = len(retrieved_memory_ids)

        # 4. Construct bounded execution context & calculate context hash
        context_memories: List[Dict[str, Any]] = []
        for item in raw_results:
            if isinstance(item, dict) and item.get('id'):
                content = item.get('content') or item.get('snippet') or ''
                context_memories.append({
                    'id': str(item['id']),
                    'type': str(item.get('type', 'unknown')),
                    'lifecycle': str(item.get('lifecycle', 'unknown')),
                    'content': str(content)[:500],
                })

        execution_context: Dict[str, Any] = {
            'task_id': task_obj.task_id,
            'description': task_obj.description,
            'instructions': task_obj.instructions,
            'agent_id': agent_id,
            'agent_role': authorized_role.value,
            'memory_query': effective_query,
            'retrieved_memories': context_memories,
        }
        canonical_context_bytes = json.dumps(execution_context, sort_keys=True, ensure_ascii=False).encode('utf-8')
        context_hash = hashlib.sha256(canonical_context_bytes).hexdigest()

        # 5. Execute REAL model inference boundary
        m_executor = model_executor or self.model_executor
        model_record = m_executor.execute_model(execution_context, task_obj)

        # 6, 7, 8. Action validation and execution
        initial_snapshot = _snapshot_directory(ws_path)
        command_records: List[CommandExecutionRecord] = []
        action_records: List[ActionExecutionRecord] = []
        policy_error: Optional[str] = None

        if model_record.provider_mode != "deterministic":
            # If model execution failed, record failure without fallback
            if model_record.response_status == "failed":
                policy_error = f"Model execution failed: {model_record.response_text}"
            else:
                # Parse and validate actions from model output
                valid_actions, action_recs = _extract_and_validate_actions(
                    model_record.response_text, authorized_role, ws_path
                )
                action_records.extend(action_recs)

                # Execute validated actions
                for act in valid_actions:
                    act_type = act.get("action")
                    if act_type == "write_file":
                        target_file_rel = act.get("path")
                        target_file_path = ws_path / target_file_rel
                        target_file_path.parent.mkdir(parents=True, exist_ok=True)
                        with open(target_file_path, "w", encoding="utf-8") as f:
                            f.write(act.get("content", ""))

                    elif act_type == "run_command":
                        cmd = act.get("command")
                        if isinstance(cmd, str):
                            cmd_list = cmd.split()
                        elif isinstance(cmd, list):
                            cmd_list = [str(c) for c in cmd]
                        else:
                            continue

                        cmd_started = datetime.now(timezone.utc).isoformat()
                        res = subprocess.run(cmd_list, cwd=str(ws_path), capture_output=True, text=True)
                        cmd_finished = datetime.now(timezone.utc).isoformat()
                        command_records.append(
                            CommandExecutionRecord(
                                command=cmd_list[0] if cmd_list else "",
                                arguments=cmd_list[1:] if len(cmd_list) > 1 else [],
                                started_at=cmd_started,
                                finished_at=cmd_finished,
                                stdout=res.stdout,
                                stderr=res.stderr,
                                exit_code=res.returncode,
                            )
                        )
        else:
            # Fallback to deterministic policy when in deterministic provider mode
            policy = agent_policy or self.default_policy
            try:
                cmd_recs, act_recs = policy.apply(execution_context, ws_path, task_obj)
                command_records.extend(cmd_recs)
                action_records.extend(act_recs)
            except Exception as e:
                policy_error = str(e)
                command_records.append(
                    CommandExecutionRecord(
                        command='policy_execution',
                        arguments=[],
                        started_at=datetime.now(timezone.utc).isoformat(),
                        finished_at=datetime.now(timezone.utc).isoformat(),
                        stdout='',
                        stderr=policy_error,
                        exit_code=1,
                    )
                )

        # 9. Capture workspace changes
        current_snapshot = _snapshot_directory(ws_path)
        workspace_diff = _calculate_workspace_diff(initial_snapshot, current_snapshot)

        # 10 & 11. Run verification/test & capture result
        v_cmd = verification_command or task_obj.verification_command
        if not v_cmd:
            if task_obj.test_file and (ws_path / task_obj.test_file).exists():
                v_cmd = [sys.executable, '-m', 'pytest', task_obj.test_file, '-v']
            else:
                v_cmd = [sys.executable, '-m', 'pytest', '-v']

        try:
            v_res = subprocess.run(v_cmd, cwd=str(ws_path), capture_output=True, text=True)
            v_record = VerificationResult(
                command=' '.join(v_cmd),
                stdout=v_res.stdout,
                stderr=v_res.stderr,
                exit_code=v_res.returncode,
                status='passed' if v_res.returncode == 0 else 'failed',
            )
        except Exception as e:
            v_record = VerificationResult(
                command=' '.join(v_cmd),
                stdout='',
                stderr=str(e),
                exit_code=127,
                status='failed',
            )

        finished_at = datetime.now(timezone.utc).isoformat()

        # 12. Persist execution evidence with secret redaction
        all_stdout = '\n'.join(rec.stdout for rec in command_records if rec.stdout)
        all_stderr = '\n'.join(rec.stderr for rec in command_records if rec.stderr)
        all_exit_codes = [rec.exit_code for rec in command_records]

        trace = ExecutionTrace(
            trace_id=trace_id,
            agent_id=agent_id,
            agent_role=authorized_role.value,
            task_id=task_obj.task_id,
            started_at=started_at,
            finished_at=finished_at,
            memory={
                'query': effective_query,
                'memory_ids': retrieved_memory_ids,
                'retrieval_count': retrieval_count,
                'relevance_scores': relevance_scores,
                'context_hash': context_hash,
            },
            model=model_record.to_dict(),
            actions=[act.to_dict() for act in action_records],
            execution={
                'commands': [f'{rec.command} {" ".join(rec.arguments)}'.strip() for rec in command_records],
                'stdout': all_stdout,
                'stderr': all_stderr,
                'exit_codes': all_exit_codes,
            },
            workspace=workspace_diff.to_dict(),
            verification=v_record.to_dict(),
            experiment=experiment,
        )

        trace_dict = trace.to_dict()

        # Redact secrets before writing to disk
        secrets_to_redact = [
            os.getenv("OPENAI_API_KEY", ""),
            os.getenv("MEMORY_CONTROLLER_HMAC_SECRET", ""),
            getattr(m_executor, "api_key", "") or "",
        ]
        redacted_trace_dict = _redact_secrets(trace_dict, secrets_to_redact)

        trace_file = self.trace_dir / f'{trace_id}.json'
        traces_jsonl = self.trace_dir / 'execution_traces.jsonl'

        with self._lock:
            with open(trace_file, 'w', encoding='utf-8') as f:
                json.dump(redacted_trace_dict, f, indent=2)

            with open(traces_jsonl, 'a', encoding='utf-8') as f:
                f.write(json.dumps(redacted_trace_dict) + '\n')

        has_action_rejections = any(not a.validated for a in action_records)
        is_success = (
            v_record.status == 'passed'
            and not policy_error
            and model_record.response_status != 'failed'
            and not has_action_rejections
        )

        execution_result = {
            'status': 'success' if is_success else 'failure',
            'trace_id': trace_id,
            'model_status': model_record.response_status,
            'verification_status': v_record.status,
            'verification_exit_code': v_record.exit_code,
            'files_modified': workspace_diff.files_modified,
            'files_created': workspace_diff.files_created,
            'files_deleted': workspace_diff.files_deleted,
        }
        trace_reference = {
            'trace_id': trace_id,
            'trace_file': str(trace_file),
            'traces_jsonl': str(traces_jsonl),
            'context_hash': context_hash,
            'record': redacted_trace_dict,
        }

        return execution_result, trace_reference
