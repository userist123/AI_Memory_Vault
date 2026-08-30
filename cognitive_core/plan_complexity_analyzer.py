"""Single source of truth: ActivePlan -> PlanComplexity -> ExecutionMode.

Why this module exists
-----------------------
Executive._estimate_complexity() and CouncilBudgetController.decide() risk
becoming TWO independent opinions about task complexity: decide() accepts a
bare `complexity: int` from whichever caller invokes it, with no guarantee
that value was actually derived from the Planner's real ActivePlan. If a
future caller ever passes an arbitrary int, Planner could say "3 steps"
while the Council says "complexity=1" -- two sources of truth silently
diverging.

This module does not change CouncilBudgetController (it stays a generic,
Planner-agnostic gate on purpose -- coupling it directly to ActivePlan would
be worse architecture). Instead, it is the ONE place that converts a real
ActivePlan into a structured, inspectable PlanComplexity snapshot, so any
caller that wants a plan-derived signal gets it from here, not from
re-implementing the step-counting heuristic independently.

    ActivePlan
       |
    PlanComplexityAnalyzer.analyze()      <- this module
       |
    PlanComplexity (step_count, retrieval_steps, verification_steps,
                    destructive_steps, risk_level, execution_mode)
       |
       +-- .council_complexity  -> int for CouncilBudgetController.decide()
       +-- .require_review      -> bool for CouncilBudgetController.decide()
"""
from __future__ import annotations

import enum
from dataclasses import dataclass
from typing import Any, Dict, List

from .planning import ActivePlan

# Confirmed real signal from cognitive_core/planning.py: Planner.create_plan()
# marks its one destructive/high-risk branch with action == "delete_canonical".
DESTRUCTIVE_ACTIONS = {"delete_canonical"}


class ExecutionMode(str, enum.Enum):
    SIMPLE = "simple"
    MODERATE = "moderate"
    COMPLEX = "complex"
    HIGH_RISK = "high_risk"


@dataclass(frozen=True)
class PlanComplexity:
    step_count: int
    retrieval_steps: int
    verification_steps: int
    destructive_steps: int
    risk_level: str
    execution_mode: ExecutionMode

    @property
    def council_complexity(self) -> int:
        """Maps ExecutionMode to the int scale CouncilBudgetController.decide()
        expects. SIMPLE -> 1 (below the default complexity_threshold of 2, so
        Council is skipped). MODERATE/COMPLEX/HIGH_RISK -> 2, preserving
        exactly the same threshold behavior as the previous
        Executive._estimate_complexity() heuristic (step_count >= 2 -> 2).
        """
        return 1 if self.execution_mode == ExecutionMode.SIMPLE else 2

    @property
    def require_review(self) -> bool:
        """A destructive step (e.g. delete_canonical) forces
        CouncilBudgetController's HIGH_RISK tier via require_review=True,
        sourced from the Planner's OWN step actions -- not from re-guessing
        risk from the query text a second time.
        """
        return self.execution_mode == ExecutionMode.HIGH_RISK


class PlanComplexityAnalyzer:
    """Converts an ActivePlan into a structured PlanComplexity snapshot.

    This is the ONLY place that decides step-count/action-based execution
    mode. CouncilBudgetController remains generic and Planner-agnostic; this
    analyzer is what feeds it a correctly-derived signal instead of leaving
    that derivation scattered or duplicated across callers.
    """

    def analyze(self, plan: ActivePlan) -> PlanComplexity:
        steps = plan.steps
        step_count = len(steps)

        destructive_steps = sum(1 for s in steps if s.get("action") in DESTRUCTIVE_ACTIONS)
        # Confirmed from planning.py: the verification step is a "search"
        # action whose query is literally f"verify {goal}".
        verification_steps = sum(
            1 for s in steps
            if s.get("action") == "search" and str(s.get("query", "")).startswith("verify ")
        )
        retrieval_steps = sum(
            1 for s in steps
            if s.get("action") == "search" and not str(s.get("query", "")).startswith("verify ")
        )

        if destructive_steps > 0:
            mode = ExecutionMode.HIGH_RISK
            risk_level = "high"
        elif step_count >= 3:
            mode = ExecutionMode.COMPLEX
            risk_level = "none"
        elif step_count == 2:
            mode = ExecutionMode.MODERATE
            risk_level = "none"
        else:
            mode = ExecutionMode.SIMPLE
            risk_level = "none"

        return PlanComplexity(
            step_count=step_count,
            retrieval_steps=retrieval_steps,
            verification_steps=verification_steps,
            destructive_steps=destructive_steps,
            risk_level=risk_level,
            execution_mode=mode,
        )
