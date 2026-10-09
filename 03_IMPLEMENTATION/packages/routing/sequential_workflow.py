"""Bounded sequential execution for local-first agent workflows.

The workflow keeps a small, durable task state and makes every step pass through
an executor and an independent reviewer.  It deliberately stores outputs and
evidence, never hidden chain-of-thought.  A caller supplies the runtime adapters
so this module does not start shells, contact networks, or choose a provider on
its own.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import tempfile
import uuid
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Optional


class WorkflowError(RuntimeError):
    """Raised when a workflow cannot satisfy its execution contract."""


@dataclass(frozen=True)
class StepSpec:
    """A bounded unit of work presented to an executor."""

    step_id: str
    objective: str
    acceptance: tuple[str, ...] = ()
    memory_refs: tuple[str, ...] = ()
    evidence: tuple[str, ...] = ()
    max_attempts: int = 2

    def __post_init__(self) -> None:
        if not self.step_id or not self.objective.strip():
            raise ValueError("step_id and objective are required")
        if self.max_attempts < 1:
            raise ValueError("max_attempts must be positive")


@dataclass(frozen=True)
class AgentObservation:
    """Structured result returned by an executor or reviewer."""

    status: str
    summary: str
    evidence: tuple[str, ...] = ()
    failures: tuple[str, ...] = ()
    unknowns: tuple[str, ...] = ()
    metadata: Mapping[str, Any] = field(default_factory=dict)

    def __post_init__(self) -> None:
        if self.status not in {"passed", "failed", "blocked", "unknown"}:
            raise ValueError("invalid observation status")


def observation_from_text(
    text: str,
    *,
    required_terms: Iterable[str] = (),
    evidence: Iterable[str] = (),
) -> AgentObservation:
    """Normalize common small-model status variants without trusting free text blindly."""
    raw = str(text or "")
    normalized = raw.casefold()
    match = re.search(r"(?:\*\*)?status(?:\*\*)?\s*:\s*([a-z_-]+)", normalized)
    token = match.group(1) if match else ""
    pass_tokens = {"pass", "passed", "ok", "complete", "completed", "success"}
    fail_tokens = {"fail", "failed", "failure", "blocked", "unsafe", "incomplete"}
    terms = tuple(str(item).casefold() for item in required_terms if str(item).strip())

    def term_present(term: str) -> bool:
        if term in normalized:
            return True
        # Small models commonly paraphrase these stable contract terms.
        if term == "sha-256":
            return bool(re.search(r"sha\s*[- ]?\s*256", normalized))
        if term == "63 bits":
            return bool(re.search(r"63\s*[- ]?\s*bits?", normalized))
        if term == "fail closed":
            return bool(re.search(r"fail(?:s|ed)?\s*[- ]?\s*closed", normalized))
        if term == "status exactly ok":
            return bool(re.search(r"status.{0,35}(?:exactly|is).{0,12}ok", normalized))
        return False

    terms_ok = all(term_present(term) for term in terms)
    if token in pass_tokens and terms_ok:
        status = "passed"
    elif token in fail_tokens or not terms_ok:
        status = "failed"
    else:
        status = "unknown"
    return AgentObservation(status, raw, tuple(str(item) for item in evidence))


@dataclass
class WorkflowState:
    task_id: str
    goal: str
    current_index: int = 0
    attempts: dict[str, int] = field(default_factory=dict)
    outputs: dict[str, AgentObservation] = field(default_factory=dict)
    reviews: dict[str, AgentObservation] = field(default_factory=dict)
    status: str = "pending"

    def to_dict(self) -> dict[str, Any]:
        return {
            "schema": "agent-workflow.state.v1",
            "task_id": self.task_id,
            "goal_sha256": hashlib.sha256(self.goal.encode("utf-8")).hexdigest(),
            "goal_chars": len(self.goal),
            "current_index": self.current_index,
            "attempts": dict(self.attempts),
            "outputs": {key: asdict(value) for key, value in self.outputs.items()},
            "reviews": {key: asdict(value) for key, value in self.reviews.items()},
            "status": self.status,
        }


@dataclass(frozen=True)
class WorkflowResult:
    task_id: str
    status: str
    completed_steps: tuple[str, ...]
    failed_step: Optional[str]
    state_path: Optional[str]
    outputs: Mapping[str, AgentObservation]
    reviews: Mapping[str, AgentObservation]


Executor = Callable[[StepSpec, str], AgentObservation]
Reviewer = Callable[[StepSpec, AgentObservation, str], AgentObservation]
Escalator = Callable[[StepSpec, AgentObservation, AgentObservation, str], AgentObservation]


class SequentialAgentWorkflow:
    """Execute steps one at a time, review each, and resume from a checkpoint."""

    def __init__(
        self,
        goal: str,
        steps: Iterable[StepSpec],
        *,
        task_id: Optional[str] = None,
        state_path: Optional[str | Path] = None,
        max_context_chars: int = 12000,
    ) -> None:
        if not goal.strip():
            raise ValueError("goal must not be empty")
        self.goal = goal.strip()
        self.steps = tuple(steps)
        if not self.steps:
            raise ValueError("at least one step is required")
        self.task_id = task_id or f"task_{uuid.uuid4().hex[:12]}"
        self.state_path = Path(state_path) if state_path else None
        self.max_context_chars = max(1000, int(max_context_chars))
        self.state = WorkflowState(task_id=self.task_id, goal=self.goal)

    def _context(self, index: int) -> str:
        prior = []
        for step in self.steps[:index]:
            output = self.state.outputs.get(step.step_id)
            review = self.state.reviews.get(step.step_id)
            if output:
                prior.append({"step_id": step.step_id, "output": asdict(output)})
            if review:
                prior.append({"step_id": step.step_id, "review": asdict(review)})
        payload = json.dumps(
            {
                "goal": self.goal,
                "current_step": {
                    "step_id": self.steps[index].step_id,
                    "objective": self.steps[index].objective,
                    "acceptance": list(self.steps[index].acceptance),
                    "memory_refs": list(self.steps[index].memory_refs),
                    "evidence": list(self.steps[index].evidence),
                },
                "completed": prior,
            },
            ensure_ascii=False,
            separators=(",", ":"),
            default=str,
        )
        return payload[-self.max_context_chars :]

    def _save(self) -> None:
        if self.state_path is None:
            return
        self.state_path.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp = tempfile.mkstemp(prefix=".workflow_", dir=str(self.state_path.parent))
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as handle:
                json.dump(self.state.to_dict(), handle, ensure_ascii=False, indent=2)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(tmp, self.state_path)
        except Exception:
            try:
                os.unlink(tmp)
            except OSError:
                pass
            raise

    def run(
        self,
        executor: Executor,
        reviewer: Reviewer,
        *,
        escalator: Optional[Escalator] = None,
    ) -> WorkflowResult:
        """Run or resume the workflow with bounded retries and verification."""
        completed: list[str] = []
        self.state.status = "running"
        self._save()
        for index in range(self.state.current_index, len(self.steps)):
            step = self.steps[index]
            accepted = False
            last_output: Optional[AgentObservation] = None
            last_review: Optional[AgentObservation] = None
            for attempt in range(self.state.attempts.get(step.step_id, 0), step.max_attempts):
                self.state.attempts[step.step_id] = attempt + 1
                context = self._context(index)
                output = executor(step, context)
                review = reviewer(step, output, context)
                self.state.outputs[step.step_id] = output
                self.state.reviews[step.step_id] = review
                self._save()
                last_output, last_review = output, review
                if output.status == "passed" and review.status == "passed":
                    accepted = True
                    break
            if not accepted and escalator and last_output and last_review:
                escalated = escalator(step, last_output, last_review, self._context(index))
                self.state.reviews[step.step_id] = escalated
                self._save()
                accepted = escalated.status == "passed"
            if not accepted:
                self.state.status = "failed"
                self.state.current_index = index
                self._save()
                return WorkflowResult(
                    self.task_id, "failed", tuple(completed), step.step_id,
                    str(self.state_path) if self.state_path else None,
                    dict(self.state.outputs), dict(self.state.reviews),
                )
            completed.append(step.step_id)
            self.state.current_index = index + 1
            self._save()
        self.state.status = "completed"
        self._save()
        return WorkflowResult(
            self.task_id, "completed", tuple(completed), None,
            str(self.state_path) if self.state_path else None,
            dict(self.state.outputs), dict(self.state.reviews),
        )
