"""A/B benchmark harness for agent-to-agent knowledge handoff.

The harness measures raw-context versus KnowledgeCapsule handoff without
providing a silent fallback. The same task and principal must be used for both
arms; only the context representation may differ.
"""

from __future__ import annotations

from dataclasses import dataclass
from time import monotonic
from typing import Callable, Mapping, Any


@dataclass(frozen=True)
class RunObservation:
    input_tokens: int
    output_tokens: int
    tool_calls: int
    repeated_discoveries: int
    completed: bool
    errors: tuple[str, ...] = ()


@dataclass(frozen=True)
class BenchmarkArm:
    name: str
    observation: RunObservation
    elapsed_ms: float


@dataclass(frozen=True)
class HandoffBenchmarkResult:
    baseline: BenchmarkArm
    treatment: BenchmarkArm
    same_task: bool
    same_principal: bool

    @property
    def input_tokens_saved(self) -> int:
        return self.baseline.observation.input_tokens - self.treatment.observation.input_tokens

    @property
    def output_tokens_saved(self) -> int:
        return self.baseline.observation.output_tokens - self.treatment.observation.output_tokens

    @property
    def tool_calls_saved(self) -> int:
        return self.baseline.observation.tool_calls - self.treatment.observation.tool_calls

    @property
    def repeated_discoveries_saved(self) -> int:
        return (
            self.baseline.observation.repeated_discoveries
            - self.treatment.observation.repeated_discoveries
        )


Runner = Callable[[str, Mapping[str, Any], str, str], RunObservation]


class HandoffBenchmark:
    """Run two comparable arms and reject silent treatment fallbacks."""

    def __init__(self, runner: Runner):
        self.runner = runner

    def _run(
        self,
        name: str,
        task: str,
        principal: str,
        context: Mapping[str, Any],
    ) -> BenchmarkArm:
        started = monotonic()
        observation = self.runner(name, context, task, principal)
        elapsed_ms = (monotonic() - started) * 1000.0
        if not isinstance(observation, RunObservation):
            raise TypeError("benchmark_runner_must_return_RunObservation")
        return BenchmarkArm(name=name, observation=observation, elapsed_ms=elapsed_ms)

    def compare(
        self,
        *,
        task: str,
        principal: str,
        raw_context: Mapping[str, Any],
        handoff_context: Mapping[str, Any],
    ) -> HandoffBenchmarkResult:
        if not task.strip():
            raise ValueError("empty_task")
        if not principal.strip():
            raise ValueError("empty_principal")
        if raw_context == handoff_context:
            raise ValueError("arms_must_differ_only_by_context_representation")

        baseline = self._run("baseline_raw_context", task, principal, raw_context)
        treatment = self._run("treatment_knowledge_capsule", task, principal, handoff_context)

        if treatment.observation.errors:
            raise RuntimeError(
                "treatment_arm_failed:" + "|".join(treatment.observation.errors)
            )

        return HandoffBenchmarkResult(
            baseline=baseline,
            treatment=treatment,
            same_task=True,
            same_principal=True,
        )
