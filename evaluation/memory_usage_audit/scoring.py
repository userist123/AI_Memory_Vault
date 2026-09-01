"""evaluation/memory_usage_audit/scoring.py — Memory Usage Audit Scoring Engine.

Calculates multi-dimensional memory utilization metrics based on verified evidence:
  - Memory Access Score
  - Memory Retrieval Score
  - Skill Usage Score
  - Decision Influence Score
  - Verification Score
  - Outcome Learning Score
  - Overall Memory Utilization Score
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional


LEVEL_WEIGHTS = {
    "VERIFIED": 1.0,
    "SUPPORTED": 0.6,
    "UNVERIFIED": 0.0,
    "MISSING": 0.0,
    "CONTRADICTED": -0.5,
}


@dataclass
class StageEvaluation:
    stage_id: str
    level: str  # VERIFIED | SUPPORTED | UNVERIFIED | MISSING | CONTRADICTED
    evidence_found: List[str] = field(default_factory=list)
    unverified_claims: List[str] = field(default_factory=list)
    missing_elements: List[str] = field(default_factory=list)
    contradictions: List[str] = field(default_factory=list)
    notes: str = ""

    @property
    def score(self) -> float:
        return LEVEL_WEIGHTS.get(self.level, 0.0)


@dataclass
class AuditScorecard:
    case_id: str
    stage_evaluations: Dict[str, StageEvaluation]

    @property
    def memory_access_score(self) -> float:
        s1 = self.stage_evaluations.get("A_MEMORY_DISCOVERY", StageEvaluation("A", "MISSING")).score
        s2 = self.stage_evaluations.get("D_SKILL_DISCOVERY", StageEvaluation("D", "MISSING")).score
        return round(max(0.0, (s1 + s2) / 2.0) * 100.0, 1)

    @property
    def memory_retrieval_score(self) -> float:
        s1 = self.stage_evaluations.get("B_MEMORY_RETRIEVAL", StageEvaluation("B", "MISSING")).score
        s2 = self.stage_evaluations.get("C_MEMORY_LOADING", StageEvaluation("C", "MISSING")).score
        return round(max(0.0, (s1 + s2) / 2.0) * 100.0, 1)

    @property
    def skill_usage_score(self) -> float:
        s1 = self.stage_evaluations.get("E_SKILL_ACTIVATION", StageEvaluation("E", "MISSING")).score
        s2 = self.stage_evaluations.get("F_SUBAGENT_ROUTING", StageEvaluation("F", "MISSING")).score
        return round(max(0.0, (s1 + s2) / 2.0) * 100.0, 1)

    @property
    def decision_influence_score(self) -> float:
        s = self.stage_evaluations.get("G_DECISION_INFLUENCE", StageEvaluation("G", "MISSING")).score
        return round(max(0.0, s) * 100.0, 1)

    @property
    def verification_score(self) -> float:
        s = self.stage_evaluations.get("I_VERIFICATION", StageEvaluation("I", "MISSING")).score
        return round(max(0.0, s) * 100.0, 1)

    @property
    def outcome_learning_score(self) -> float:
        s1 = self.stage_evaluations.get("J_OUTCOME_CAPTURE", StageEvaluation("J", "MISSING")).score
        s2 = self.stage_evaluations.get("K_CONSOLIDATION", StageEvaluation("K", "MISSING")).score
        return round(max(0.0, (s1 + s2) / 2.0) * 100.0, 1)

    @property
    def overall_utilization_score(self) -> float:
        # Weighted composite: Access (10%), Retrieval (20%), Skill (15%), Influence (25%), Verification (20%), Learning (10%)
        composite = (
            (self.memory_access_score * 0.10) +
            (self.memory_retrieval_score * 0.20) +
            (self.skill_usage_score * 0.15) +
            (self.decision_influence_score * 0.25) +
            (self.verification_score * 0.20) +
            (self.outcome_learning_score * 0.10)
        )
        return round(max(0.0, composite), 1)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "case_id": self.case_id,
            "memory_access_score": self.memory_access_score,
            "memory_retrieval_score": self.memory_retrieval_score,
            "skill_usage_score": self.skill_usage_score,
            "decision_influence_score": self.decision_influence_score,
            "verification_score": self.verification_score,
            "outcome_learning_score": self.outcome_learning_score,
            "overall_utilization_score": self.overall_utilization_score,
            "stages": {
                k: {
                    "level": v.level,
                    "score": v.score,
                    "evidence_found": v.evidence_found,
                    "unverified_claims": v.unverified_claims,
                    "missing_elements": v.missing_elements,
                    "contradictions": v.contradictions,
                    "notes": v.notes,
                }
                for k, v in self.stage_evaluations.items()
            }
        }
