"""Closed-loop learning engine: outcome -> evidence -> belief update -> canonical mutation.

Implements Part C of the Antigravity Closure Program:
- C.1 Four transitions:
  1. Outcome (Rezultat): Observable execution outcome (success/failure, cost, metric).
  2. Evidence (Dovada): Binds outcome back to specific memory UUID (not descriptions).
  3. Learning (Invatare): Bayesian belief update with strict monotonic degradation under failures.
  4. Canonical Mutation (Mutatie canonica): Reversible status/confidence change in storage.
- C.2 Honest loop with planted memory tests:
  - Planted false memory: monotonic confidence decay, reaches withdrawal threshold in exactly N steps.
  - Planted true memory: robust under noisy outcomes (85% success / 15% noise), remains active.
- C.3 Anti-self-confirmation guard:
  - Excludes direct-influence feedback loops (only control arm or uninfluenced/unchanged choices).
  - Reports admitted vs filtered evidence volume and retention ratio.
- C.4 Part A ontology concept decay bridge:
  - Concepts promoted from Part A that go unretrieved/unused accumulate idle decay and get demoted.
"""
from __future__ import annotations

import enum
import math
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set, Tuple

from memory_controller.authorizer import Principal, Operation


class InfluenceType(str, enum.Enum):
    """Categorizes how a memory was involved in an agent's decision."""
    DIRECT_CHOICE = "direct_choice"          # Memory directly influenced/dictated the decision
    CONSULTED_UNCHANGED = "consulted_unchanged"  # Memory was retrieved, but decision was identical to control
    CONTROL_ARM = "control_arm"                  # Independent baseline run without memory influence
    UNINFLUENCED = "uninfluenced"                # System outcome measured independently


@dataclass(frozen=True)
class ExecutionOutcome:
    """Transition 1: Observable execution outcome produced by a run."""
    run_id: str
    success: bool
    cost: float = 0.0
    metric_value: float = 1.0
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    error_message: Optional[str] = None
    metadata: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "run_id": self.run_id,
            "success": self.success,
            "cost": self.cost,
            "metric_value": self.metric_value,
            "timestamp": self.timestamp,
            "error_message": self.error_message,
            "metadata": self.metadata,
        }


@dataclass(frozen=True)
class MemoryEvidence:
    """Transition 2: Links an observable outcome back to a specific memory UUID."""
    evidence_id: str
    memory_id: str
    outcome: ExecutionOutcome
    influence_type: InfluenceType
    utility_score: float
    recorded_at: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())

    def to_dict(self) -> Dict[str, Any]:
        return {
            "evidence_id": self.evidence_id,
            "memory_id": self.memory_id,
            "outcome": self.outcome.to_dict(),
            "influence_type": self.influence_type.value,
            "utility_score": self.utility_score,
            "recorded_at": self.recorded_at,
        }


class AntiSelfConfirmationGuard:
    """C.3 Guard: Prevents self-reinforcing echo chambers.

    If a memory directly influenced the agent's choice (DIRECT_CHOICE), using that
    outcome to update the memory's confidence produces self-confirmation bias.
    Confidence is updated ONLY from outcomes observed independently of influence
    (CONTROL_ARM, CONSULTED_UNCHANGED, or UNINFLUENCED).
    """

    def __init__(self, allow_self_confirmation_override: bool = False):
        self.admitted_evidence: List[MemoryEvidence] = []
        self.rejected_evidence: List[Tuple[MemoryEvidence, str]] = []
        self._allow_override = allow_self_confirmation_override

    def filter_evidence(self, evidence: MemoryEvidence) -> Tuple[bool, str]:
        if self._allow_override:
            # Dangerous mode: negative control testing only
            self.admitted_evidence.append(evidence)
            return True, "Admitted: override enabled (negative control only)"

        if evidence.influence_type == InfluenceType.DIRECT_CHOICE:
            reason = (
                f"Self-confirmation hazard: evidence {evidence.evidence_id} directly influenced "
                f"choice for memory {evidence.memory_id}. Excluded from confidence updater."
            )
            self.rejected_evidence.append((evidence, reason))
            return False, reason

        self.admitted_evidence.append(evidence)
        return True, "Admitted: independent observation"

    def get_telemetry(self) -> Dict[str, Any]:
        total = len(self.admitted_evidence) + len(self.rejected_evidence)
        retention = (len(self.admitted_evidence) / total) if total > 0 else 0.0
        return {
            "total_evidence": total,
            "admitted_count": len(self.admitted_evidence),
            "rejected_count": len(self.rejected_evidence),
            "retention_ratio": round(retention, 4),
        }


@dataclass
class BeliefState:
    """Transition 3: Bayesian belief representation with strict monotonic degradation."""
    alpha: float = 3.0   # Prior pseudo-successes
    beta: float = 1.0    # Prior pseudo-failures
    observation_count: int = 0
    consecutive_failures: int = 0
    score_history: List[float] = field(default_factory=list)

    @property
    def score(self) -> float:
        """Mean confidence score alpha / (alpha + beta) in [0, 1]."""
        total = self.alpha + self.beta
        return self.alpha / total if total > 0 else 0.0

    def __post_init__(self):
        if not self.score_history:
            self.score_history.append(round(self.score, 6))

    def update(self, success: bool, weight: float = 1.0) -> float:
        """Updates belief state.

        Strict Monotonicity Guarantee under Failures:
        d/d_beta [alpha / (alpha + beta)] = -alpha / (alpha + beta)^2 < 0 for all alpha > 0.
        Thus, every failure strictly and monotonically decreases the score.
        """
        if weight <= 0:
            raise ValueError("Observation weight must be strictly positive")

        self.observation_count += 1
        if success:
            self.alpha += weight
            self.consecutive_failures = 0
        else:
            self.beta += weight
            self.consecutive_failures += 1

        new_score = round(self.score, 6)
        self.score_history.append(new_score)
        return new_score


@dataclass
class CanonicalMutationRecord:
    """Transition 4: Provenance-tracked reversible mutation in storage."""
    mutation_id: str
    memory_id: str
    mutation_type: str  # 'withdraw', 'demote', 'update_confidence', 'stale_decay'
    prior_state: Dict[str, Any]
    new_state: Dict[str, Any]
    reason: str
    evidence_summary: Dict[str, Any]
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    reversible: bool = True

    def to_dict(self) -> Dict[str, Any]:
        return {
            "mutation_id": self.mutation_id,
            "memory_id": self.memory_id,
            "mutation_type": self.mutation_type,
            "prior_state": self.prior_state,
            "new_state": self.new_state,
            "reason": self.reason,
            "evidence_summary": self.evidence_summary,
            "timestamp": self.timestamp,
            "reversible": self.reversible,
        }


class ClosedLoopLearner:
    """Orchestrator for Outcome -> Evidence -> Belief Update -> Canonical Mutation."""

    def __init__(
        self,
        storage: Any,
        controller: Optional[Any] = None,
        withdrawal_threshold: float = 0.35,
        prior_alpha: float = 3.0,
        prior_beta: float = 1.0,
        allow_downward_mutation: bool = True,
        anti_self_confirmation_override: bool = False,
    ):
        self.storage = storage
        self.controller = controller
        self.withdrawal_threshold = withdrawal_threshold
        self.prior_alpha = prior_alpha
        self.prior_beta = prior_beta
        self.allow_downward_mutation = allow_downward_mutation

        self.guard = AntiSelfConfirmationGuard(allow_self_confirmation_override=anti_self_confirmation_override)
        self.beliefs: Dict[str, BeliefState] = {}
        self.mutation_history: List[CanonicalMutationRecord] = []

    def register_memory(self, memory_id: str, initial_score: Optional[float] = None) -> BeliefState:
        """Registers a memory node for closed-loop tracking."""
        if memory_id in self.beliefs:
            return self.beliefs[memory_id]

        if initial_score is not None:
            total = self.prior_alpha + self.prior_beta
            alpha = max(0.1, total * initial_score)
            beta = max(0.1, total * (1.0 - initial_score))
            belief = BeliefState(alpha=alpha, beta=beta)
        else:
            belief = BeliefState(alpha=self.prior_alpha, beta=self.prior_beta)

        self.beliefs[memory_id] = belief
        return belief

    def calculate_steps_to_withdrawal(
        self,
        initial_alpha: Optional[float] = None,
        initial_beta: Optional[float] = None,
        threshold: Optional[float] = None,
    ) -> int:
        """Computes exact number of consecutive failure steps N required to reach withdrawal.

        Formula:
          alpha / (alpha + beta + N) <= threshold
          => N >= alpha / threshold - (alpha + beta)
          => N = ceil(alpha / threshold - (alpha + beta))
        """
        a = initial_alpha if initial_alpha is not None else self.prior_alpha
        b = initial_beta if initial_beta is not None else self.prior_beta
        t = threshold if threshold is not None else self.withdrawal_threshold

        diff = (a / t) - (a + b)
        return max(1, math.ceil(diff))

    def record_outcome(
        self,
        memory_id: str,
        outcome: ExecutionOutcome,
        influence_type: InfluenceType,
        weight: float = 1.0,
        principal: Principal = Principal.AI_AGENT,
    ) -> Tuple[bool, Optional[float], Optional[CanonicalMutationRecord]]:
        """Executes the full 4-step transition pipeline.

        Returns (admitted_by_guard, current_score, mutation_record_if_triggered).
        """
        # Verify memory exists in storage
        note = self.storage.get(memory_id)
        if not note:
            raise ValueError(f"Memory note {memory_id} not found in storage")

        # Calculate utility score
        cost_penalty = min(1.0, outcome.cost / 10.0) if outcome.cost > 0 else 0.0
        if outcome.success:
            utility = max(0.1, outcome.metric_value - cost_penalty)
        else:
            utility = -1.0 * (1.0 + cost_penalty)

        evidence = MemoryEvidence(
            evidence_id=f"ev-{uuid.uuid4().hex[:8]}",
            memory_id=memory_id,
            outcome=outcome,
            influence_type=influence_type,
            utility_score=utility,
        )

        # C.3: Anti-self-confirmation guard check
        admitted, reason = self.guard.filter_evidence(evidence)
        if not admitted:
            belief = self.beliefs.get(memory_id)
            current_score = belief.score if belief else None
            return False, current_score, None

        # C.1 Step 3: Belief Update
        belief = self.register_memory(memory_id)
        new_score = belief.update(success=outcome.success, weight=weight)

        # Check withdrawal threshold
        mutation_record = None
        if new_score <= self.withdrawal_threshold:
            if not self.allow_downward_mutation:
                # Negative control testing: downward mutation blocked
                return True, new_score, None

            # C.1 Step 4: Canonical Mutation (Downward mutation / withdrawal)
            mutation_record = self._execute_withdrawal(
                memory_id=memory_id,
                principal=principal,
                score=new_score,
                reason=f"Confidence {new_score:.4f} dropped below withdrawal threshold {self.withdrawal_threshold:.4f}",
            )

        return True, new_score, mutation_record

    def _execute_withdrawal(
        self,
        memory_id: str,
        principal: Principal,
        score: float,
        reason: str,
    ) -> CanonicalMutationRecord:
        """Applies downward mutation to note in storage with complete reversibility."""
        note = self.storage.get(memory_id)
        prior_state = note.copy()

        # Update note fields for withdrawal
        updated_note = note.copy()
        updated_note["status"] = "withdrawn"
        updated_note["confidence"] = "low"
        updated_note["confidence_score"] = score
        updated_note["verification"] = "disputed"
        updated_note["withdrawn_at"] = datetime.now(timezone.utc).isoformat()
        updated_note["withdrawal_reason"] = reason

        self.storage.set(memory_id, updated_note)

        mutation = CanonicalMutationRecord(
            mutation_id=f"mut-{uuid.uuid4().hex[:8]}",
            memory_id=memory_id,
            mutation_type="withdraw",
            prior_state=prior_state,
            new_state=updated_note,
            reason=reason,
            evidence_summary={
                "final_score": score,
                "observations": self.beliefs[memory_id].observation_count,
                "consecutive_failures": self.beliefs[memory_id].consecutive_failures,
            },
            reversible=True,
        )
        self.mutation_history.append(mutation)
        return mutation

    def rollback_mutation(self, mutation_id: str) -> bool:
        """Reverses a canonical mutation by restoring the prior snapshot."""
        target = next((m for m in self.mutation_history if m.mutation_id == mutation_id), None)
        if not target or not target.reversible:
            return False

        self.storage.set(target.memory_id, target.prior_state.copy())
        if target.memory_id in self.beliefs:
            self.beliefs[target.memory_id].score_history.append(
                target.prior_state.get("confidence_score", self.prior_alpha / (self.prior_alpha + self.prior_beta))
            )
        return True


class OntologyConceptDecayBridge:
    """C.4 Bridge: Connects Part A promoted concepts to Part C closed-loop empirical utility.

    A concept promoted from Part A based on occurrences (e.g. occ >= 3) accumulates
    idle decay if it is never retrieved or never produces positive utility in execution.
    Once its decayed confidence falls below the withdrawal threshold, it is demoted.
    """

    def __init__(
        self,
        learner: ClosedLoopLearner,
        decay_factor: float = 0.90,
        stale_threshold: float = 0.35,
    ):
        self.learner = learner
        self.decay_factor = decay_factor
        self.stale_threshold = stale_threshold
        self.tracked_concepts: Dict[str, Dict[str, Any]] = {}

    def register_ontology_concept(
        self,
        concept_name: str,
        slot_name: str,
        occurrences: int,
        initial_confidence: float = 0.75,
    ) -> None:
        self.tracked_concepts[concept_name] = {
            "slot": slot_name,
            "occurrences": occurrences,
            "initial_confidence": initial_confidence,
            "current_confidence": initial_confidence,
            "idle_epochs": 0,
            "retrieval_count": 0,
            "status": "active",
        }

    def step_epoch(self, retrieved_concepts: Set[str]) -> Dict[str, Any]:
        """Simulates an execution epoch.

        Concepts retrieved and utilized reset their idle epochs.
        Unused concepts suffer exponential decay: C(t) = C_0 * (decay_factor ^ idle_epochs).
        """
        demoted = []
        for name, data in self.tracked_concepts.items():
            if data["status"] != "active":
                continue

            if name in retrieved_concepts:
                data["retrieval_count"] += 1
                data["idle_epochs"] = 0
                data["current_confidence"] = min(0.95, round(data["current_confidence"] + 0.02, 4))
            else:
                data["idle_epochs"] += 1
                decayed = data["initial_confidence"] * (self.decay_factor ** data["idle_epochs"])
                data["current_confidence"] = round(decayed, 4)

                if data["current_confidence"] < self.stale_threshold:
                    data["status"] = "demoted"
                    demoted.append(name)

        return {
            "active_count": sum(1 for d in self.tracked_concepts.values() if d["status"] == "active"),
            "demoted_count": sum(1 for d in self.tracked_concepts.values() if d["status"] == "demoted"),
            "newly_demoted": demoted,
        }
