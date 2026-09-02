"""evaluation/memory_trace/trace_validator.py — Agent Memory Trace Validator.

Implements deterministic schema verification, declared vs observed event reconciliation,
causal link verification, skill lifecycle tracking, and trace completeness classification.
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Set, Tuple


@dataclass
class ReconciliationReport:
    trace_id: str
    memory_status: str          # VERIFIED | DECLARED_ONLY | MISSING
    skill_status: str           # VERIFIED | DECLARED_ONLY | MISSING
    subagent_status: str        # VERIFIED | DECLARED_ONLY | MISSING
    decision_influence: str     # MEMORY_INFLUENCE_VERIFIED | MEMORY_INFLUENCE_UNVERIFIED
    verification_status: str    # VERIFIED | DECLARED_ONLY | MISSING
    outcome_status: str         # VERIFIED | DECLARED_ONLY | MISSING
    trust_level: str            # T0_DECLARED_ONLY | T1_TOOL_OBSERVED | T2_EXECUTION_VERIFIED | T3_OUTCOME_VERIFIED
    completeness: str           # COMPLETE | PARTIAL | BROKEN
    first_missing_link: Optional[str] = None
    declared_only_claims: List[str] = field(default_factory=list)
    verified_elements: List[str] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "trace_id": self.trace_id,
            "memory_status": self.memory_status,
            "skill_status": self.skill_status,
            "subagent_status": self.subagent_status,
            "decision_influence": self.decision_influence,
            "verification_status": self.verification_status,
            "outcome_status": self.outcome_status,
            "trust_level": self.trust_level,
            "completeness": self.completeness,
            "first_missing_link": self.first_missing_link,
            "declared_only_claims": self.declared_only_claims,
            "verified_elements": self.verified_elements,
        }


class TraceValidator:
    """Validates and reconciles Agent Memory Traces against observed evidence."""

    REQUIRED_TOP_FIELDS = [
        "trace_id", "task_id", "agent_id", "query", "declared", "observed"
    ]

    @classmethod
    def validate_schema(cls, trace: Dict[str, Any]) -> Tuple[bool, List[str]]:
        errors = []
        for f in cls.REQUIRED_TOP_FIELDS:
            if f not in trace:
                errors.append(f"Missing required top-level field: '{f}'")

        if "declared" in trace and not isinstance(trace["declared"], dict):
            errors.append("'declared' field must be a dictionary")
        if "observed" in trace and not isinstance(trace["observed"], dict):
            errors.append("'observed' field must be a dictionary")

        return (len(errors) == 0, errors)

    @classmethod
    def reconcile(cls, trace: Dict[str, Any]) -> ReconciliationReport:
        is_valid, errors = cls.validate_schema(trace)
        if not is_valid:
            raise ValueError(f"Invalid Memory Trace Schema: {errors}")

        trace_id = trace.get("trace_id", "unknown_trace")
        declared = trace.get("declared", {})
        observed = trace.get("observed", {})

        declared_memories = declared.get("retrieved_memories", [])
        observed_retrievals = observed.get("retrieval_events", [])
        observed_loads = observed.get("memory_load_events", [])

        declared_skills = declared.get("activated_skills", [])
        observed_skills = observed.get("skill_load_events", [])

        declared_subagents = declared.get("activated_subagents", [])
        observed_subagents = observed.get("subagent_events", [])

        declared_decisions = declared.get("decisions_influenced", [])
        observed_decisions = observed.get("decision_events", [])

        declared_verifications = declared.get("verification_claims", [])
        observed_verifications = observed.get("verification_events", [])

        declared_outcomes = declared.get("outcome_claims", [])
        observed_outcomes = observed.get("outcome_events", [])

        declared_only: List[str] = []
        verified_elements: List[str] = []

        # 1. Memory Retrieval Status
        if observed_retrievals and all(e.get("evidence_ref") for e in observed_retrievals):
            memory_status = "VERIFIED"
            verified_elements.append(f"Observed {len(observed_retrievals)} memory retrievals with evidence")
        elif declared_memories:
            memory_status = "DECLARED_ONLY"
            declared_only.append(f"Declared memories {declared_memories} without observed retrieval events")
        else:
            memory_status = "MISSING"

        # 2. Skill Activation Status
        if observed_skills and any(e.get("state") in ("ACTIVATED", "APPLIED", "VERIFIED") and e.get("evidence_ref") for e in observed_skills):
            skill_status = "VERIFIED"
            verified_elements.append(f"Observed {len(observed_skills)} activated skills")
        elif declared_skills:
            skill_status = "DECLARED_ONLY"
            declared_only.append(f"Declared skills {declared_skills} without observed skill load events")
        else:
            skill_status = "MISSING"

        # 3. Subagent Routing Status
        if observed_subagents and all(e.get("dispatch_event") or e.get("evidence_ref") for e in observed_subagents):
            subagent_status = "VERIFIED"
            verified_elements.append(f"Observed {len(observed_subagents)} subagent dispatches")
        elif declared_subagents:
            subagent_status = "DECLARED_ONLY"
            declared_only.append(f"Declared subagents {declared_subagents} without observed dispatch events")
        else:
            subagent_status = "MISSING"

        # 4. Decision Influence Status
        if memory_status == "VERIFIED" and observed_decisions and any(d.get("governing_memory_id") for d in observed_decisions):
            decision_influence = "MEMORY_INFLUENCE_VERIFIED"
            verified_elements.append("Causal link established from retrieved memory to decision")
        elif declared_decisions:
            decision_influence = "MEMORY_INFLUENCE_UNVERIFIED"
            declared_only.append(f"Declared decision influence {declared_decisions} without verified memory linkage")
        else:
            decision_influence = "MISSING"

        # 5. Verification Status
        if observed_verifications and all(v.get("evidence_ref") for v in observed_verifications):
            verification_status = "VERIFIED"
            verified_elements.append(f"Observed {len(observed_verifications)} empirical verifications")
        elif declared_verifications:
            verification_status = "DECLARED_ONLY"
            declared_only.append(f"Declared verifications {declared_verifications} without empirical test evidence")
        else:
            verification_status = "MISSING"

        # 6. Outcome Status
        if observed_outcomes and any(o.get("verification_method") and o.get("evidence_ref") for o in observed_outcomes):
            outcome_status = "VERIFIED"
            verified_elements.append("Outcome logged with verifiable evidence reference")
        elif declared_outcomes:
            outcome_status = "DECLARED_ONLY"
            declared_only.append(f"Declared outcome {declared_outcomes} without empirical proof")
        else:
            outcome_status = "MISSING"

        # 7. Trust Level Evaluation
        if outcome_status == "VERIFIED" and verification_status == "VERIFIED" and memory_status == "VERIFIED":
            trust_level = "T3_OUTCOME_VERIFIED"
        elif verification_status == "VERIFIED":
            trust_level = "T2_EXECUTION_VERIFIED"
        elif memory_status == "VERIFIED" or skill_status == "VERIFIED":
            trust_level = "T1_TOOL_OBSERVED"
        else:
            trust_level = "T0_DECLARED_ONLY"

        # 8. Trace Completeness & Missing Link Identification
        first_missing = None
        if not trace.get("query"):
            completeness = "BROKEN"
            first_missing = "QUERY"
        elif memory_status != "VERIFIED":
            completeness = "BROKEN" if declared_memories else "PARTIAL"
            first_missing = "RETRIEVE"
        elif not observed_loads and memory_status == "VERIFIED":
            completeness = "BROKEN" if (declared_decisions or observed_decisions) else "PARTIAL"
            first_missing = "LOAD"
        elif decision_influence != "MEMORY_INFLUENCE_VERIFIED":
            completeness = "BROKEN"
            first_missing = "DECIDE"
        elif verification_status != "VERIFIED":
            completeness = "BROKEN" if declared_verifications else "PARTIAL"
            first_missing = "VERIFY"
        elif outcome_status != "VERIFIED":
            completeness = "BROKEN" if declared_outcomes else "PARTIAL"
            first_missing = "OUTCOME"
        else:
            completeness = "COMPLETE"

        return ReconciliationReport(
            trace_id=trace_id,
            memory_status=memory_status,
            skill_status=skill_status,
            subagent_status=subagent_status,
            decision_influence=decision_influence,
            verification_status=verification_status,
            outcome_status=outcome_status,
            trust_level=trust_level,
            completeness=completeness,
            first_missing_link=first_missing,
            declared_only_claims=declared_only,
            verified_elements=verified_elements,
        )
