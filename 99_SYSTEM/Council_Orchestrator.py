#!/usr/bin/env python3
"""Council Orchestrator: the actual runtime entry point between a Council
selection and MemoryController calls.

Why this module exists
-----------------------
MemoryController (memory_controller/controller.py) only exposes
single-principal operations: query(), read(), search(), propose(), etc. It
has no concept of "a Council of agents each with a skill selection" and no
method that accepts a Council-wide agent -> skills mapping.
Council_Selection_Boundary.enforce_council_boundary() existed and was tested,
but nothing in the codebase called it before invoking MemoryController -- the
enforcement existed on paper, not in the call graph.

This module closes that gap without touching MemoryController's existing
contract. It is the only place where a Council-wide selection is turned into
a sequence of MemoryController.search() calls, and it guarantees that
enforce_council_boundary() runs to completion, successfully, BEFORE any such
call happens.

    USER TASK -> AGENT ROUTER
       |
    Council_Selection_Boundary.enforce_council_boundary()   <- must pass
       |
    Council_Orchestrator.run_council_retrieval()             <- this module
       |   (loops only over agents approved by the boundary)
       v
    MemoryController.search()  x  N approved agents
       |
    council_token_telemetry.CouncilTokenTelemetry            <- records usage
       v
    per-agent Context Packs -> specialist calls -> synthesis -> LLM

Fail-closed contract: if enforce_council_boundary() raises, this function
raises before touching memory_controller at all. If an approved agent has no
mapped principal, this function resolves ALL principals BEFORE issuing any
retrieval call, so a later agent's missing principal can never leak an
earlier agent's already-completed retrieval (no partial retrieval on
failure).
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any, Dict, Iterable, Mapping, Optional

_THIS_DIR = Path(__file__).resolve().parent


def _load_sibling_module(module_name: str, filename: str):
    spec = importlib.util.spec_from_file_location(module_name, _THIS_DIR / filename)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


_boundary = _load_sibling_module("council_selection_boundary", "Council_Selection_Boundary.py")
_telemetry_mod = _load_sibling_module("council_token_telemetry", "council_token_telemetry.py")

enforce_council_boundary = _boundary.enforce_council_boundary
BoundaryRejectedError = _boundary.BoundaryRejectedError
CouncilTokenTelemetry = _telemetry_mod.CouncilTokenTelemetry


class CouncilRun:
    """Result of one Council retrieval pass: per-agent context packs + telemetry."""

    __slots__ = ("agent_packs", "telemetry")

    def __init__(self, agent_packs: Dict[str, Dict[str, Any]], telemetry: "CouncilTokenTelemetry") -> None:
        self.agent_packs = agent_packs
        self.telemetry = telemetry


def run_council_retrieval(
    memory_controller: Any,
    principal_by_agent: Mapping[str, Any],
    agent_skills: Mapping[str, Iterable[str]],
    agent_queries: Mapping[str, str],
    context_manifest: Dict[str, Any],
    hard_context_bytes: Optional[int] = None,
) -> CouncilRun:
    """Enforce the Council boundary, then retrieve memory for approved agents only.

    Order of operations (do not reorder -- this order IS the enforcement):
      1. enforce_council_boundary(agent_skills, context_manifest, ...). Raises
         BoundaryRejectedError on ANY violation. No MemoryController method is
         called before this succeeds.
      2. Resolve every approved agent's Principal BEFORE issuing any
         retrieval call. A missing principal for one agent fails the whole
         run closed and never lets another agent's retrieval leak out first.
      3. For each approved agent, call memory_controller.search(principal,
         query) and record the returned results into a shared
         CouncilTokenTelemetry via record_context().

    Returns a CouncilRun with one context pack per approved agent and the
    telemetry snapshot. Specialist/synthesis calls happen after this, using
    telemetry.record_specialist()/record_synthesis() on the same object.
    """
    validated = enforce_council_boundary(agent_skills, context_manifest, hard_context_bytes)

    # Resolve every approved agent's principal BEFORE any retrieval call.
    # A missing principal for agent B must not let agent A's retrieval leak
    # out first -- partial retrieval is never an acceptable outcome of a
    # rejected/incomplete Council run.
    resolved_principals: Dict[str, Any] = {}
    for agent_id in validated.agent_skills:
        principal = principal_by_agent.get(agent_id)
        if principal is None:
            raise BoundaryRejectedError(f"no principal mapped for approved agent: {agent_id}")
        resolved_principals[agent_id] = principal

    telemetry = CouncilTokenTelemetry(
        agents_selected=len(validated.agent_skills),
        skills_selected=sum(len(skills) for skills in validated.agent_skills.values()),
    )

    agent_packs: Dict[str, Dict[str, Any]] = {}
    for agent_id in validated.agent_skills:
        principal = resolved_principals[agent_id]
        query = agent_queries.get(agent_id, "")
        pack = memory_controller.search(principal, query)
        agent_packs[agent_id] = pack
        telemetry.record_context(pack.get("results", []))

    return CouncilRun(agent_packs=agent_packs, telemetry=telemetry)
