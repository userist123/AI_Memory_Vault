#!/usr/bin/env python3
"""Fail-closed boundary between Council selection and model execution.

This module is the single required checkpoint between "agents, skills and
context have been selected" and "a model call is allowed to happen". It does
not duplicate business rules from the underlying validators; it composes
them and converts every violation into a hard failure.

Contract: enforce_council_boundary() either returns a validated selection or
raises BoundaryRejectedError. There is no "continue anyway" path and no
soft-report mode here on purpose -- callers that want a non-fatal report
should call Skill_Runtime_Gate.validate_council_selection() or
Council_Context_Validator.validate() directly. This module exists so that a
runtime caller cannot accidentally skip enforcement by forgetting to check a
return value.

    USER TASK
       |
    AGENT ROUTER
       |
    Council Selection Boundary   <- this module
       |  agents <= MAX_AGENTS_PER_COUNCIL
       |  skills/agent <= MAX_SKILLS_PER_AGENT (Skill_Runtime_Gate)
       |  unique skills <= MAX_TOTAL_SKILLS (Skill_Runtime_Gate)
       |  context manifest structural limits (Council_Context_Validator)
       v
    Skill Runtime Gate (loads only what was approved)
       v
    Memory Controller / Context Pack Builder
       v
    LLM
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional

_THIS_DIR = Path(__file__).resolve().parent


def _load_sibling_module(module_name: str, filename: str):
    spec = importlib.util.spec_from_file_location(module_name, _THIS_DIR / filename)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[module_name] = module  # required so dataclasses can resolve __module__
    spec.loader.exec_module(module)
    return module


_skill_gate = _load_sibling_module("skill_runtime_gate", "Skill_Runtime_Gate.py")
_context_validator = _load_sibling_module("council_context_validator", "Council_Context_Validator.py")

SkillBudgetError = _skill_gate.SkillBudgetError
validate_council_selection = _skill_gate.validate_council_selection
validate_context_manifest = _context_validator.validate

MAX_AGENTS_PER_COUNCIL = 3


class BoundaryRejectedError(RuntimeError):
    """Raised when a Council selection or context manifest fails validation.

    No model call may follow the raising of this exception. Callers must not
    catch it and proceed with a degraded/partial selection; the only valid
    responses are: fix the selection and retry, or abort the Council run.
    """


class BoundaryResult:
    """The only object a caller may use to proceed to Skill_Runtime_Gate/LLM.

    Plain class (not a dataclass) on purpose: this module is loaded via
    importlib.util.spec_from_file_location by callers/tests that do not
    register it in sys.modules first, and @dataclass fails in that scenario
    because it looks up __module__ in sys.modules while building __repr__/eq.
    """

    __slots__ = ("agent_skills", "context_manifest")

    def __init__(self, agent_skills: Dict[str, List[str]], context_manifest: Dict[str, Any]) -> None:
        self.agent_skills = agent_skills
        self.context_manifest = context_manifest

    def as_dict(self) -> Dict[str, Any]:
        return {"agent_skills": self.agent_skills, "context_manifest": self.context_manifest}

    def __eq__(self, other: object) -> bool:
        if not isinstance(other, BoundaryResult):
            return NotImplemented
        return self.agent_skills == other.agent_skills and self.context_manifest == other.context_manifest

    def __repr__(self) -> str:
        return f"BoundaryResult(agent_skills={self.agent_skills!r}, context_manifest={self.context_manifest!r})"


def enforce_council_boundary(
    agent_skills: Mapping[str, Iterable[str]],
    context_manifest: Dict[str, Any],
    hard_context_bytes: Optional[int] = None,
) -> BoundaryResult:
    """Validate a full Council selection before any model call is permitted.

    Raises BoundaryRejectedError on ANY violation:
      - too many agents in the Council
      - a single agent selecting more than MAX_SKILLS_PER_AGENT skills
      - more than MAX_TOTAL_SKILLS unique skills across the whole Council
      - a context manifest that fails Council_Context_Validator.validate()
        (duplicate memory/evidence payloads, forbidden broad-context flags,
        oversized serialized manifest, malformed structure, etc.)

    On success, returns a BoundaryResult carrying exactly the validated
    agent -> skills mapping and the manifest that was checked, so a caller
    cannot accidentally act on unvalidated input by using a different object.
    """
    if not isinstance(agent_skills, Mapping):
        raise BoundaryRejectedError("agent_skills must be a mapping of agent id to skill ids")

    if len(agent_skills) > MAX_AGENTS_PER_COUNCIL:
        raise BoundaryRejectedError(
            f"too many agents in Council: {len(agent_skills)} > {MAX_AGENTS_PER_COUNCIL}"
        )

    try:
        normalised_skills = validate_council_selection(agent_skills)
    except SkillBudgetError as exc:
        raise BoundaryRejectedError(f"skill budget rejected: {exc}") from exc

    if not isinstance(context_manifest, dict):
        raise BoundaryRejectedError("context_manifest must be a dict")

    validator_kwargs: Dict[str, Any] = {}
    if hard_context_bytes is not None:
        validator_kwargs["hard_context_bytes"] = hard_context_bytes

    errors = validate_context_manifest(context_manifest, **validator_kwargs)
    if errors:
        raise BoundaryRejectedError("context manifest rejected: " + "; ".join(errors))

    return BoundaryResult(normalised_skills, context_manifest)
