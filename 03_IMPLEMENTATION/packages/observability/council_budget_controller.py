"""Decides whether and how much Council dispatch a task actually needs.

Why this module exists
-----------------------
Without this controller, Executive._dispatch_via_orchestrator() called
MultiAgentOrchestrator.route_and_dispatch() unconditionally for every single
task, always running Router-triage, (conditional) Retrieval, and the
Verifier tally -- regardless of whether the task was trivial or complex.
Combined with process_intent()'s own Activation + RecallEngine + Reasoning +
Planning + Reflection chain, this is exactly the token-multiplication risk:
"Executive + full Council + Reasoning + Planner + Reflection all at once"
for every task, however simple.

This mirrors the complexity/risk gating already used by Jarvis's
AgentCouncil.plan() (jarvis/agents/agent_council.py: mode=single/council,
complexity_threshold, risky_capabilities), adapted to this vault's
MultiAgentOrchestrator worker roles instead of named agent profiles.

    ONE TASK
       |
    CouncilBudgetController.decide()   <- this module
       |
       +-- NONE      -> Council skipped entirely
       +-- LIGHT     -> Verifier only, no Retrieval
       +-- STANDARD  -> Retrieval + Verifier
       +-- HIGH_RISK -> Retrieval + Verifier, forced review

Never: Executive + full Council + Reasoning + Planner + Reflection running
unconditionally for a task that did not need any of that.
"""
from __future__ import annotations

import enum
from dataclasses import dataclass
from typing import Iterable


class CouncilTier(str, enum.Enum):
    NONE = "none"
    LIGHT = "light"
    STANDARD = "standard"
    HIGH_RISK = "high_risk"


@dataclass(frozen=True)
class CouncilBudgetDecision:
    tier: CouncilTier
    run_retrieval: bool
    run_verifier: bool
    reason: str

    @property
    def should_dispatch(self) -> bool:
        return self.tier != CouncilTier.NONE


class CouncilBudgetController:
    """Gate that decides Council involvement BEFORE any dispatch happens.

    complexity_threshold: minimum task complexity (caller-supplied integer,
        default 1 = trivial) at which Council involvement becomes
        justified even without a risky keyword match.
    risky_keywords: keywords that, when present in the query, force at
        least STANDARD tier (Retrieval + Verifier) regardless of
        complexity, because risk -- not just complexity -- justifies
        review.
    """

    def __init__(
        self,
        *,
        complexity_threshold: int = 2,
        risky_keywords: Iterable[str] = (
            "delete", "archive", "supersede", "propose", "verify",
            "credential", "security", "production", "deploy",
        ),
    ) -> None:
        self.complexity_threshold = max(1, complexity_threshold)
        self.risky_keywords = {str(k).casefold() for k in risky_keywords}

    def decide(
        self,
        query: str,
        *,
        complexity: int = 1,
        require_review: bool = False,
    ) -> CouncilBudgetDecision:
        lowered = str(query).casefold()
        risky = any(k in lowered for k in self.risky_keywords)

        if require_review or (risky and complexity >= self.complexity_threshold):
            return CouncilBudgetDecision(
                CouncilTier.HIGH_RISK, run_retrieval=True, run_verifier=True,
                reason="risky capability at or above complexity threshold, or review explicitly required",
            )
        if risky:
            return CouncilBudgetDecision(
                CouncilTier.STANDARD, run_retrieval=True, run_verifier=True,
                reason="risky capability detected in query",
            )
        if complexity >= self.complexity_threshold:
            return CouncilBudgetDecision(
                CouncilTier.LIGHT, run_retrieval=False, run_verifier=True,
                reason="complexity at or above threshold, but no risk signal",
            )
        return CouncilBudgetDecision(
            CouncilTier.NONE, run_retrieval=False, run_verifier=False,
            reason="task below complexity threshold and no risk signal: Council skipped",
        )
