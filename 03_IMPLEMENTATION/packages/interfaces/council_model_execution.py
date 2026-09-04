"""council_model_execution.py — A7: layers real model execution on top of
Council_Orchestrator.CouncilRun via composition.

Council_Orchestrator.py is NOT modified. CouncilRun (a __slots__ class
with only agent_packs + telemetry) is NOT modified either — this module
wraps it instead, exactly like ActualUsageTelemetry (A5) wraps around
CouncilTokenTelemetry without touching it.

This module never imports 99_SYSTEM.Council_Orchestrator (which is not
a standard-importable package name anyway — it is loaded dynamically
via importlib by its own tests). council_run is accepted duck-typed:
any object exposing .agent_packs (Mapping[str, Any]) and .telemetry
works here.

Four rules enforced by this module:

  Regula 1 — each specialist call receives ONLY its own
              agent_packs[agent_id], never the combined context of all
              agents.
  Regula 2 — synthesis receives task + compact specialist outputs +
              minimal evidence, never the full specialist prompts,
              full memories, or skill definitions.
  Regula 3 — every model call is recorded into ActualUsageTelemetry
              immediately after it returns.
  Regula 4 — model_execution_enabled defaults to False. False preserves
              the exact old Council behavior (retrieval-only, zero
              model calls) — the change is reversible by construction.
"""
from __future__ import annotations

import json
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Dict, List, Mapping, Optional

from .actual_usage_telemetry import ActualUsageTelemetry
from .model_provider import ModelRequest, ModelResponse
from .model_tier_router import ModelTierRouter

VALID_OUTCOMES = {"success", "failure", "partial", "unknown"}
VALID_OUTCOME_SOURCES = {"synthesis_presence", "exit_code", "test_result", "human", "llm_judge"}
VALID_CONFIDENCES = {"low", "medium", "high"}


def append_outcome_event_to_disk(event: OutcomeEvent, path: Optional[str] = None) -> None:
    """Safely append an OutcomeEvent to a JSONL file on disk."""
    from pathlib import Path
    out_path = Path(path or "04_MEMORY/outcome_events.jsonl")
    try:
        out_path.parent.mkdir(parents=True, exist_ok=True)
        with open(out_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(event.to_dict(), ensure_ascii=False) + "\n")
    except Exception:
        # Failsafe: in-memory run remains valid even if disk write is mocked or unwritable
        pass


@dataclass(frozen=True)
class OutcomeEvent:
    """Immutable outcome observation event for a council execution run."""

    event_id: str
    run_id: str
    timestamp: str
    outcome: str
    source: str
    confidence: str
    evidence: str
    labeled_by: Optional[str] = None

    def __post_init__(self) -> None:
        if self.outcome not in VALID_OUTCOMES:
            raise ValueError(f"Invalid outcome '{self.outcome}'. Must be one of {sorted(VALID_OUTCOMES)}")
        if self.source not in VALID_OUTCOME_SOURCES:
            raise ValueError(f"Invalid source '{self.source}'. Must be one of {sorted(VALID_OUTCOME_SOURCES)}")
        if self.confidence not in VALID_CONFIDENCES:
            raise ValueError(f"Invalid confidence '{self.confidence}'. Must be one of {sorted(VALID_CONFIDENCES)}")
        if not isinstance(self.evidence, str):
            raise TypeError("evidence must be a string")

    def to_dict(self) -> Dict[str, Any]:
        return {
            "event_id": self.event_id,
            "run_id": self.run_id,
            "timestamp": self.timestamp,
            "outcome": self.outcome,
            "source": self.source,
            "confidence": self.confidence,
            "evidence": self.evidence,
            "labeled_by": self.labeled_by,
        }


@dataclass
class CouncilRunWithExecution:
    """Composition wrapper around a Council_Orchestrator CouncilRun.

    Never mutates the wrapped council_run. Adds specialist/synthesis
    execution results, REAL usage tracking, and append-only outcome
    events alongside the existing, untouched estimated telemetry on
    council_run.telemetry.
    """

    council_run: Any
    model_execution_enabled: bool = False
    specialist_results: Dict[str, ModelResponse] = field(default_factory=dict)
    synthesis_result: Optional[ModelResponse] = None
    actual_usage: ActualUsageTelemetry = field(default_factory=ActualUsageTelemetry)
    run_id: str = field(default_factory=lambda: f"run_{uuid.uuid4().hex[:12]}")
    _outcome_events: List[OutcomeEvent] = field(default_factory=list)

    @property
    def agent_packs(self) -> Mapping[str, Any]:
        return self.council_run.agent_packs

    @property
    def estimated_telemetry(self) -> Any:
        return self.council_run.telemetry

    @property
    def outcome_events(self) -> List[OutcomeEvent]:
        """Return a copy of recorded outcome events (append-only)."""
        return list(self._outcome_events)

    def add_outcome_event(
        self,
        outcome: str,
        source: str,
        confidence: str = "low",
        evidence: str = "",
        labeled_by: Optional[str] = None,
        event_id: Optional[str] = None,
        timestamp: Optional[str] = None,
        persist: bool = False,
        persist_path: Optional[str] = None,
    ) -> OutcomeEvent:
        """Append an outcome event. Strictly append-only."""
        evt = OutcomeEvent(
            event_id=event_id or f"evt_{uuid.uuid4().hex[:12]}",
            run_id=self.run_id,
            timestamp=timestamp or datetime.now(timezone.utc).isoformat(),
            outcome=outcome,
            source=source,
            confidence=confidence,
            evidence=str(evidence),
            labeled_by=labeled_by,
        )
        self._outcome_events.append(evt)
        if persist:
            append_outcome_event_to_disk(evt, path=persist_path)
        return evt


def _serialize_pack_for_prompt(pack: Any) -> str:
    """Render a single agent's context pack as a compact prompt string.

    Only this agent's own pack is ever serialized here — never the
    combined context of all agents (Regula 1).
    """
    try:
        return json.dumps(pack, ensure_ascii=False, default=str)
    except (TypeError, ValueError):
        return str(pack)


def _serialize_specialist_summary(agent_id: str, response: ModelResponse) -> Dict[str, Any]:
    """Compact representation of one specialist's output for synthesis input.

    Deliberately excludes the specialist's full prompt, its full memory
    pack, and skill definitions (Regula 2) — synthesis only ever sees
    this compact summary, never the specialist's raw inputs.
    """
    return {"agent_id": agent_id, "output": response.content}


def run_council_with_model_execution(
    council_run: Any,
    model_tier_router: ModelTierRouter,
    task: str,
    agent_model_tiers: Mapping[str, str],
    synthesis_model_tier: str,
    model_execution_enabled: bool = False,
    synthesis_system_prompt: Optional[str] = None,
    persist_events: bool = False,
) -> CouncilRunWithExecution:
    """Execute specialist + synthesis model calls on top of an existing CouncilRun.

    When model_execution_enabled is False (the default), this is a
    no-op wrapper: it returns the council_run wrapped untouched, with
    empty specialist_results/synthesis_result and empty actual_usage.
    This is the exact old Council behavior — reversible by construction
    (Regula 4).

    When True, calls exactly one specialist ModelProvider.generate() per
    agent in council_run.agent_packs (Regula 1), then exactly one
    synthesis ModelProvider.generate() over the compact specialist
    summaries (Regula 2), recording real usage after each call
    (Regula 3).
    """
    wrapped = CouncilRunWithExecution(
        council_run=council_run,
        model_execution_enabled=model_execution_enabled,
    )

    if not model_execution_enabled:
        return wrapped

    for agent_id, pack in council_run.agent_packs.items():
        tier = agent_model_tiers.get(agent_id)
        if tier is None:
            raise ValueError(
                f"No model_tier configured for agent '{agent_id}'. "
                f"Configured agents: {sorted(agent_model_tiers)}"
            )
        provider = model_tier_router.resolve(tier)
        request = ModelRequest(
            prompt=_serialize_pack_for_prompt(pack),
            model_tier=tier,
        )
        response = provider.generate(request)
        wrapped.specialist_results[agent_id] = response
        wrapped.actual_usage.record_specialist_actual(
            response.usage,
            provider=response.provider,
            model=response.model,
            model_tier=response.model_tier,
        )

    specialist_summaries = [
        _serialize_specialist_summary(agent_id, response)
        for agent_id, response in wrapped.specialist_results.items()
    ]
    synthesis_prompt = json.dumps(
        {"task": task, "specialist_outputs": specialist_summaries},
        ensure_ascii=False,
        default=str,
    )
    synthesis_provider = model_tier_router.resolve(synthesis_model_tier)
    synthesis_request = ModelRequest(
        prompt=synthesis_prompt,
        model_tier=synthesis_model_tier,
        system_prompt=synthesis_system_prompt,
    )
    synthesis_response = synthesis_provider.generate(synthesis_request)
    wrapped.synthesis_result = synthesis_response
    wrapped.actual_usage.record_synthesis_actual(
        synthesis_response.usage,
        provider=synthesis_response.provider,
        model=synthesis_response.model,
        model_tier=synthesis_response.model_tier,
    )

    # Automatic minimal telemetry population (synthesis_presence)
    outcome_status = "success" if (wrapped.synthesis_result and wrapped.synthesis_result.content) else "partial"
    wrapped.add_outcome_event(
        outcome=outcome_status,
        source="synthesis_presence",
        confidence="low",
        evidence=f"Council model execution completed ({len(wrapped.specialist_results)} specialists, 1 synthesis).",
        persist=persist_events,
    )

    return wrapped
