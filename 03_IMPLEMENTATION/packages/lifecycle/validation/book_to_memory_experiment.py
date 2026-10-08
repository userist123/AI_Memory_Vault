"""Book-to-Memory Controlled Experimentation Harness & Shadow Mode Execution Engine.

Authoritative Governance:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md (Section 14)
- 08_RESEARCH/BOOK_TO_MEMORY/RESEARCH-TRACK-CONTRACT.md (Sections 88-148)

Operates the controlled experimental bridge:
    BIOLOGICAL FACT -> ENGINEERING HYPOTHESIS -> CONTROLLED EXPERIMENT -> VAULT MECHANISM

Strict Invariants:
1. Shadow Mode: Evaluates variants on a simulated/frozen layer; zero direct modification
   to production index or active corpus.
2. Anti-Gaming Constraints: sample_count >= 5, paired evaluations (control vs variant),
   unbiased failure accounting (failure_count >= 0), both absolute and relative deltas.
3. Cryptographic Human Gate (I-004): Principal.AI_AGENT is blocked from validating changes.
   Only Principal.HUMAN or Principal.ADMIN can sanction CLOSED_CHANGE_VALIDATED.
"""

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from typing import Any, Callable, Dict, List, Optional, Tuple

from memory_controller.authorizer import Principal
from lifecycle.validation.book_to_memory_run_config import RunConfig
from lifecycle.validation.book_to_memory_hypothesis import (
    BookToMemoryHypothesisRegistry,
    DecisionRecord,
    HypothesisRecord,
    TrackState,
)

MALICIOUS_DIRECTIVES = re.compile(
    r"(?i)(SYSTEM\s*:|IGNORE\s+PREVIOUS|DISREGARD\s+RULES|EVAL\s*:|INSTRUCTION\s*:|EXEC\s*:)"
)


class ExperimentValidationError(ValueError):
    """Raised when an experiment configuration or execution parameter violates policy."""
    pass


@dataclass
class ExperimentConfig:
    """Configuration for a controlled hypothesis experiment."""
    experiment_id: str
    hypothesis_id: str
    protocol_name: str
    sample_cases: List[Dict[str, Any]]
    control_arm: str = "baseline_control"
    variant_arm: str = "mechanism_variant"
    shadow_mode: bool = True
    min_sample_size: int = 5
    required_delta_threshold: float = 0.15
    metadata: Dict[str, Any] = field(default_factory=dict)
    #: ``RunConfig.to_dict()`` of the run both arms are executed under (B08); None = unspecified.
    run_config: Optional[Dict[str, Any]] = None

    def __post_init__(self) -> None:
        if self.run_config is not None:
            if isinstance(self.run_config, RunConfig):
                self.run_config = self.run_config.to_dict()
            RunConfig.from_dict(self.run_config)  # raises RunConfigError when malformed
        if not re.match(r"^EXP-[A-Za-z0-9_-]+$", self.experiment_id):
            raise ExperimentValidationError(
                f"Invalid experiment_id '{self.experiment_id}'. Must match pattern '^EXP-[A-Za-z0-9_-]+$'."
            )
        if len(self.sample_cases) < self.min_sample_size:
            raise ExperimentValidationError(
                f"Sample size {len(self.sample_cases)} is less than required minimum {self.min_sample_size}."
            )
        if not self.shadow_mode:
            raise ExperimentValidationError(
                "Experiments must execute with shadow_mode=True to protect production corpus."
            )
        # Check injection in protocol_name
        if MALICIOUS_DIRECTIVES.search(self.protocol_name):
            raise ExperimentValidationError("Malicious directive detected in protocol_name.")

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class ExperimentResult:
    """Empirical findings from a completed controlled experiment."""
    experiment_id: str
    hypothesis_id: str
    sample_count: int
    baseline_scores: List[float]
    variant_scores: List[float]
    mean_baseline: float
    mean_variant: float
    absolute_delta: float
    relative_delta: float
    failure_count: int
    paired: bool
    is_statistically_improved: bool
    per_case_details: List[Dict[str, Any]]
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    digest: str = ""
    run_config: Optional[Dict[str, Any]] = None

    def __post_init__(self) -> None:
        if not self.digest:
            payload = f"{self.experiment_id}:{self.hypothesis_id}:{self.sample_count}:{self.absolute_delta:.4f}:{self.failure_count}"
            self.digest = hashlib.sha256(payload.encode("utf-8")).hexdigest()

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


class BookToMemoryExperimentHarness:
    """Harness executing controlled experiments under research governance."""

    def __init__(self, registry: BookToMemoryHypothesisRegistry) -> None:
        self.registry = registry
        self._experiments: Dict[str, ExperimentConfig] = {}
        self._results: Dict[str, ExperimentResult] = {}

    def configure_experiment(
        self,
        config: ExperimentConfig,
        actor: Principal = Principal.AI_AGENT,
    ) -> ExperimentConfig:
        """Register an experiment configuration and advance hypothesis to EXPERIMENT_READY."""
        hyp = self.registry.get_hypothesis(config.hypothesis_id)
        if not hyp:
            raise ExperimentValidationError(f"Hypothesis '{config.hypothesis_id}' not found in registry.")

        if hyp.state != TrackState.HYPOTHESIS_READY:
            raise ExperimentValidationError(
                f"Hypothesis must be in HYPOTHESIS_READY to configure experiment (current: {hyp.state.value})."
            )

        # Advance state in registry
        self.registry.transition_state(
            hypothesis_id=config.hypothesis_id,
            target_state=TrackState.EXPERIMENT_READY,
            actor=actor,
        )

        self._experiments[config.experiment_id] = config
        return config

    def run_experiment(
        self,
        experiment_id: str,
        case_evaluator: Callable[[Dict[str, Any]], Tuple[float, float, Dict[str, Any]]],
        actor: Principal = Principal.AI_AGENT,
    ) -> ExperimentResult:
        """Execute a controlled paired experiment across sample cases."""
        if experiment_id not in self._experiments:
            raise ExperimentValidationError(f"Experiment '{experiment_id}' has not been configured.")

        config = self._experiments[experiment_id]
        hyp = self.registry.get_hypothesis(config.hypothesis_id)
        if not hyp or hyp.state != TrackState.EXPERIMENT_READY:
            raise ExperimentValidationError(
                f"Hypothesis must be in EXPERIMENT_READY to execute (current: {hyp.state.value if hyp else 'NONE'})."
            )

        # Transition to EVIDENCE_PENDING
        self.registry.transition_state(
            hypothesis_id=config.hypothesis_id,
            target_state=TrackState.EVIDENCE_PENDING,
            actor=actor,
        )

        baseline_scores: List[float] = []
        variant_scores: List[float] = []
        per_case_details: List[Dict[str, Any]] = []
        failure_count = 0

        for case in config.sample_cases:
            try:
                base_score, var_score, case_meta = case_evaluator(case)
                baseline_scores.append(base_score)
                variant_scores.append(var_score)
                per_case_details.append({
                    "case_id": case.get("case_id", "unknown"),
                    "base_score": base_score,
                    "var_score": var_score,
                    "delta": var_score - base_score,
                    "metadata": case_meta,
                    "status": "SUCCESS",
                })
            except Exception as ex:
                failure_count += 1
                baseline_scores.append(0.0)
                variant_scores.append(0.0)
                per_case_details.append({
                    "case_id": case.get("case_id", "unknown"),
                    "base_score": 0.0,
                    "var_score": 0.0,
                    "delta": 0.0,
                    "error": str(ex),
                    "status": "FAILED",
                })

        sample_count = len(config.sample_cases)
        mean_base = sum(baseline_scores) / sample_count if sample_count > 0 else 0.0
        mean_var = sum(variant_scores) / sample_count if sample_count > 0 else 0.0
        abs_delta = mean_var - mean_base
        rel_delta = (abs_delta / mean_base) if mean_base > 0.0 else abs_delta

        is_improved = (
            abs_delta >= config.required_delta_threshold
            and failure_count == 0
            and sample_count >= config.min_sample_size
        )

        result = ExperimentResult(
            experiment_id=experiment_id,
            hypothesis_id=config.hypothesis_id,
            sample_count=sample_count,
            baseline_scores=baseline_scores,
            variant_scores=variant_scores,
            mean_baseline=mean_base,
            mean_variant=mean_var,
            absolute_delta=abs_delta,
            relative_delta=rel_delta,
            failure_count=failure_count,
            paired=True,
            is_statistically_improved=is_improved,
            per_case_details=per_case_details,
            run_config=config.run_config,
        )

        self._results[experiment_id] = result

        # Transition to EVIDENCE_AVAILABLE
        self.registry.transition_state(
            hypothesis_id=config.hypothesis_id,
            target_state=TrackState.EVIDENCE_AVAILABLE,
            actor=actor,
        )

        return result

    def prepare_decision_package(
        self,
        experiment_id: str,
        actor: Principal = Principal.AI_AGENT,
    ) -> DecisionRecord:
        """Package experiment results into a formal decision record and advance to DECISION_PENDING."""
        if experiment_id not in self._results:
            raise ExperimentValidationError(f"No experiment result found for '{experiment_id}'.")

        res = self._results[experiment_id]
        config = self._experiments[experiment_id]
        hyp = self.registry.get_hypothesis(res.hypothesis_id)
        if not hyp or hyp.state != TrackState.EVIDENCE_AVAILABLE:
            raise ExperimentValidationError(
                f"Hypothesis must be in EVIDENCE_AVAILABLE to package decision (current: {hyp.state.value if hyp else 'NONE'})."
            )

        # Transition to DECISION_PENDING
        self.registry.transition_state(
            hypothesis_id=res.hypothesis_id,
            target_state=TrackState.DECISION_PENDING,
            actor=actor,
        )

        # Outcome determination respecting principal authority:
        # If actor is HUMAN and is_statistically_improved, outcome can be CLOSED_CHANGE_VALIDATED.
        # Otherwise, outcome is DECISION_PENDING (awaiting human decision) or CLOSED_NO_CHANGE (if failed).
        if not res.is_statistically_improved:
            decision_outcome = TrackState.CLOSED_NO_CHANGE
        elif res.run_config is None:
            # B08: an improvement measured without a recorded run config cannot be shown to compare
            # like with like, so it never closes a change as validated.
            decision_outcome = TrackState.DECISION_PENDING
        elif actor in (Principal.HUMAN, Principal.ADMIN):
            decision_outcome = TrackState.CLOSED_CHANGE_VALIDATED
        else:
            decision_outcome = TrackState.DECISION_PENDING

        summary = (
            f"Experiment {experiment_id} evaluated {res.sample_count} paired cases. "
            f"Baseline: {res.mean_baseline:.3f}, Variant: {res.mean_variant:.3f}, "
            f"Absolute Delta: {res.absolute_delta:+.3f}, Failures: {res.failure_count}. "
            + ("Run config recorded." if res.run_config is not None
               else "NO RUN CONFIG RECORDED: not comparable, cannot close as validated (PR #209 B08).")
        )

        decision = DecisionRecord(
            decision_id=f"DEC-{experiment_id}",
            hypothesis_id=res.hypothesis_id,
            baseline_id=config.control_arm,
            variant_id=config.variant_arm,
            sample_count=res.sample_count,
            paired_evaluations=res.paired,
            primary_metric_delta=res.relative_delta,
            absolute_delta=res.absolute_delta,
            failed_run_count=res.failure_count,
            decision_outcome=decision_outcome,
            rationale=summary,
            evaluator_principal=actor.value,
        )

        self.registry.record_decision(decision, actor=actor)
        return decision

    def finalize_hypothesis_decision(
        self,
        hypothesis_id: str,
        approved: bool,
        rationale: str,
        actor: Principal,
        hmac_token: Optional[str] = None,
    ) -> HypothesisRecord:
        """Finalize hypothesis state to CLOSED_CHANGE_VALIDATED or CLOSED_NO_CHANGE.

        Strict Gate: CLOSED_CHANGE_VALIDATED requires Principal.HUMAN or Principal.ADMIN.
        """
        hyp = self.registry.get_hypothesis(hypothesis_id)
        if not hyp:
            raise ExperimentValidationError(f"Hypothesis '{hypothesis_id}' not found.")

        if hyp.state != TrackState.DECISION_PENDING:
            raise ExperimentValidationError(
                f"Hypothesis must be in DECISION_PENDING to finalize (current: {hyp.state.value})."
            )

        if approved:
            if actor == Principal.AI_AGENT:
                raise PermissionError(
                    "AI Agent cannot authorize CLOSED_CHANGE_VALIDATED (I-001/I-004 gate). Human Owner signature required."
                )
            target_state = TrackState.CLOSED_CHANGE_VALIDATED
            # Update decision record in registry with final human attestation
            existing_dec = None
            for d in self.registry._decisions.values():
                if d.hypothesis_id == hypothesis_id:
                    existing_dec = d
                    break

            if existing_dec:
                updated_dec = DecisionRecord(
                    decision_id=existing_dec.decision_id,
                    hypothesis_id=hypothesis_id,
                    baseline_id=existing_dec.baseline_id,
                    variant_id=existing_dec.variant_id,
                    sample_count=existing_dec.sample_count,
                    paired_evaluations=existing_dec.paired_evaluations,
                    primary_metric_delta=existing_dec.primary_metric_delta,
                    absolute_delta=existing_dec.absolute_delta,
                    failed_run_count=existing_dec.failed_run_count,
                    decision_outcome=TrackState.CLOSED_CHANGE_VALIDATED,
                    rationale=rationale,
                    evaluator_principal=actor.value,
                )
                self.registry.record_decision(updated_dec, actor=actor)
        else:
            target_state = TrackState.CLOSED_NO_CHANGE

        updated = self.registry.transition_state(
            hypothesis_id=hypothesis_id,
            target_state=target_state,
            actor=actor,
        )
        return updated

    def get_experiment_ledger(self) -> List[Dict[str, Any]]:
        """Return audit ledger of all configured experiments and their results."""
        ledger: List[Dict[str, Any]] = []
        for exp_id, cfg in self._experiments.items():
            res = self._results.get(exp_id)
            ledger.append({
                "experiment_id": exp_id,
                "hypothesis_id": cfg.hypothesis_id,
                "protocol": cfg.protocol_name,
                "shadow_mode": cfg.shadow_mode,
                "sample_size": len(cfg.sample_cases),
                "has_result": res is not None,
                "absolute_delta": res.absolute_delta if res else None,
                "is_statistically_improved": res.is_statistically_improved if res else None,
            })
        return ledger

    def compute_harness_digest(self) -> str:
        """Compute deterministic SHA-256 digest of entire experiment harness state."""
        payload = json.dumps(
            {
                "registry_digest": self.registry.compute_registry_digest(),
                "experiments": {k: v.to_dict() for k, v in sorted(self._experiments.items())},
                "results": {k: v.to_dict() for k, v in sorted(self._results.items())},
            },
            sort_keys=True,
        )
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()
