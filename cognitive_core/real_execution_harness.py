"""cognitive_core/real_execution_harness.py — Real Agent Execution Harness.

Provides a deterministic, machine-verifiable runtime harness implementing the contract:
    real agent execution
        ↓
    secure memory retrieval (MemoryController.search)
        ↓
    retrieved memory becomes execution context (Observed)
        ↓
    real command/tool execution (subprocess in workspace)
        ↓
    real workspace modification
        ↓
    real verification/test (subprocess pytest in workspace)
        ↓
    persistent execution evidence (structured JSON trace)

Boundary Invariants:
1. Retrieval must exclusively use MemoryController.search() under Principal.AI_AGENT.
2. Memory observation is recorded as OBSERVED (retrieved memory entered execution context).
3. Causal memory effectiveness, skill promotion, and LLM causal influence are NOT claimed.
4. Execution and verification must use real OS subprocesses, never simulated runners.
5. All executions (success and failure) persist structured immutable execution traces.
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import threading
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Set, Tuple, Union

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.orchestrator import AgentRole, validate_agent_role


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
class ExecutionTrace:
    """Immutable persistent execution trace record."""
    trace_id: str
    agent_id: str
    agent_role: str
    task_id: str
    started_at: str
    finished_at: str
    memory: Dict[str, Any]
    execution: Dict[str, Any]
    workspace: Dict[str, Any]
    verification: Dict[str, Any]

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


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
    ) -> List[CommandExecutionRecord]:
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
    ) -> List[CommandExecutionRecord]:
        records: List[CommandExecutionRecord] = []

        # 1. Modify target file in workspace
        target_path = workspace / task.target_file
        target_path.parent.mkdir(parents=True, exist_ok=True)
        if task.code_patch is not None:
            with open(target_path, 'w', encoding='utf-8') as f:
                f.write(task.code_patch)

        # 2. Modify test file in workspace if specified
        if task.test_file and task.test_patch is not None:
            test_path = workspace / task.test_file
            test_path.parent.mkdir(parents=True, exist_ok=True)
            with open(test_path, 'w', encoding='utf-8') as f:
                f.write(task.test_patch)

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
        return records


class RealAgentExecutionHarness:
    """Smallest reproducible Real Agent Execution Harness.

    Enforces the explicit 12-step contract:
    1. validate agent role
    2. retrieve memory through MemoryController.search()
    3. capture returned memory IDs
    4. construct bounded execution context
    5. execute a REAL command/tool
    6. capture stdout
    7. capture stderr
    8. capture exit code
    9. capture workspace changes
    10. run verification/test
    11. capture verification result
    12. persist execution evidence
    """

    def __init__(
        self,
        memory_controller: Optional[MemoryController] = None,
        trace_dir: Optional[Union[str, Path]] = None,
        authorizer_principal: Principal = Principal.AI_AGENT,
        default_policy: Optional[BaseAgentPolicy] = None,
    ):
        if memory_controller is None:
            from cognitive_core.recall_cli import get_memory_controller
            memory_controller = get_memory_controller()
        self.controller = memory_controller
        self.principal = authorizer_principal
        self.default_policy = default_policy or DeterministicCodeAgentPolicy()

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
        memory_query: str,
        agent_policy: Optional[BaseAgentPolicy] = None,
        verification_command: Optional[List[str]] = None,
    ) -> Tuple[Dict[str, Any], Dict[str, Any]]:
        """Executes task following the 12-step execution contract."""
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
                context_memories.append({
                    'id': str(item['id']),
                    'type': str(item.get('type', 'unknown')),
                    'lifecycle': str(item.get('lifecycle', 'unknown')),
                    'content': str(item.get('content', ''))[:500],
                })

        execution_context: Dict[str, Any] = {
            'task_id': task_obj.task_id,
            'description': task_obj.description,
            'instructions': task_obj.instructions,
            'agent_id': agent_id,
            'agent_role': authorized_role.value,
            'memory_query': memory_query,
            'retrieved_memories': context_memories,
        }
        canonical_context_bytes = json.dumps(execution_context, sort_keys=True, ensure_ascii=False).encode('utf-8')
        context_hash = hashlib.sha256(canonical_context_bytes).hexdigest()

        # 5, 6, 7, 8. Execute a REAL command/tool & capture outputs
        initial_snapshot = _snapshot_directory(ws_path)
        policy = agent_policy or self.default_policy

        command_records: List[CommandExecutionRecord] = []
        policy_error: Optional[str] = None
        try:
            command_records = policy.apply(execution_context, ws_path, task_obj)
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

        v_started = datetime.now(timezone.utc).isoformat()
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

        # 12. Persist execution evidence
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
                'query': memory_query,
                'memory_ids': retrieved_memory_ids,
                'retrieval_count': retrieval_count,
                'relevance_scores': relevance_scores,
                'context_hash': context_hash,
            },
            execution={
                'commands': [f'{rec.command} {" ".join(rec.arguments)}'.strip() for rec in command_records],
                'stdout': all_stdout,
                'stderr': all_stderr,
                'exit_codes': all_exit_codes,
            },
            workspace=workspace_diff.to_dict(),
            verification=v_record.to_dict(),
        )

        trace_dict = trace.to_dict()

        trace_file = self.trace_dir / f'{trace_id}.json'
        traces_jsonl = self.trace_dir / 'execution_traces.jsonl'

        with self._lock:
            with open(trace_file, 'w', encoding='utf-8') as f:
                json.dump(trace_dict, f, indent=2)

            with open(traces_jsonl, 'a', encoding='utf-8') as f:
                f.write(json.dumps(trace_dict) + '\n')

        execution_result = {
            'status': 'success' if (v_record.status == 'passed' and not policy_error) else 'failure',
            'trace_id': trace_id,
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
            'record': trace_dict,
        }

        return execution_result, trace_reference
