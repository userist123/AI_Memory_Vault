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
from dataclasses import dataclass, field
from typing import Any, Dict, Mapping, Optional

from .actual_usage_telemetry import ActualUsageTelemetry
from .model_provider import ModelRequest, ModelResponse
from .model_tier_router import ModelTierRouter


@dataclass
class CouncilRunWithExecution:
    """Composition wrapper around a Council_Orchestrator CouncilRun.

    Never mutates the wrapped council_run. Adds specialist/synthesis
    execution results and REAL usage tracking alongside the existing,
    untouched estimated telemetry on council_run.telemetry.
    """

    council_run: Any
    model_execution_enabled: bool = False
    specialist_results: Dict[str, ModelResponse] = field(default_factory=dict)
    synthesis_result: Optional[ModelResponse] = None
    actual_usage: ActualUsageTelemetry = field(default_factory=ActualUsageTelemetry)

    @property
    def agent_packs(self) -> Mapping[str, Any]:
        return self.council_run.agent_packs

    @property
    def estimated_telemetry(self) -> Any:
        return self.council_run.telemetry


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

    return wrapped
