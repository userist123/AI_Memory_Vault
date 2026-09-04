"""memory_controller/outcome_tracker.py — Append-only outcome tracking for council/agent runs.

Rules:
1. Records are written strictly to telemetry/ (never canonical vault 00_CORE..05_DECISIONS).
2. Schema per run:
   - event_id: unique identifier for the outcome observation
   - run_id: identifier of the council/agent execution
   - outcome: strict enum (success | fail | partial | unknown), default unknown
   - verification_method: strict enum (test_pass | exit_code | human_confirmed | none), default none
   - timestamp: ISO 8601 UTC string
   - task_signature: SHA-256 hash of normalized task pattern
   - evidence: optional factual evidence/log trace
   - recorded_by: optional agent/human identifier
3. Invariant: outcome='success' requires verification_method in {test_pass, exit_code, human_confirmed}.
   Without verifiable proof (verification_method='none'), success is prohibited and fails closed.
4. Append-only provenance: Multiple observations (e.g. automatic followed by human confirmation)
   do not overwrite or delete earlier events; all events are preserved chronologically.
5. Strict isolation: Never writes to canonical memory and has zero coupling to promotion queues.
"""
from __future__ import annotations

import hashlib
import json
import os
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional, Set

from memory_controller.task_categories import (
    TaskCategory,
    VALID_TASK_CATEGORIES,
    validate_task_category,
)

FORBIDDEN_VAULT_DIRS = {
    "00_CORE",
    "01_KNOWLEDGE",
    "02_PROJECTS",
    "02_PROCEDURES",
    "03_PROJECTS",
    "03_PROCEDURES",
    "04_MEMORY",
    "05_DECISIONS",
    "05_RESOURCES",
    "06_RESOURCES",
    "06_INBOX",
    "90_TEMPLATES",
    "99_SYSTEM",
}


class Outcome(str, Enum):
    SUCCESS = "success"
    FAIL = "fail"
    PARTIAL = "partial"
    UNKNOWN = "unknown"


class VerificationMethod(str, Enum):
    TEST_PASS = "test_pass"
    EXIT_CODE = "exit_code"
    HUMAN_CONFIRMED = "human_confirmed"
    NONE = "none"


VALID_OUTCOMES: Set[str] = {o.value for o in Outcome}
VALID_VERIFICATION_METHODS: Set[str] = {v.value for v in VerificationMethod}


def compute_task_signature(task: str) -> str:
    """Compute deterministic SHA-256 signature for normalized task text."""
    normalized = " ".join((task or "").strip().lower().split())
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class ObservedCapabilities:
    """Deterministic capabilities actually observed in runtime execution."""
    skills: List[str] = field(default_factory=list)
    agents: List[str] = field(default_factory=list)
    knowledge_refs: List[str] = field(default_factory=list)
    procedure_refs: List[str] = field(default_factory=list)

    def to_dict(self) -> Dict[str, List[str]]:
        return {
            "skills": list(self.skills),
            "agents": list(self.agents),
            "knowledge_refs": list(self.knowledge_refs),
            "procedure_refs": list(self.procedure_refs),
        }

    @classmethod
    def from_dict(cls, data: Optional[Dict[str, Any]]) -> ObservedCapabilities:
        if not data or not isinstance(data, dict):
            return cls()
        return cls(
            skills=list(data.get("skills", [])),
            agents=list(data.get("agents", [])),
            knowledge_refs=list(data.get("knowledge_refs", [])),
            procedure_refs=list(data.get("procedure_refs", [])),
        )


@dataclass(frozen=True)
class OutcomeRecord:
    """Immutable record representing an outcome observation event."""

    run_id: str
    outcome: str = Outcome.UNKNOWN.value
    verification_method: str = VerificationMethod.NONE.value
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    task_signature: str = ""
    event_id: str = field(default_factory=lambda: f"evt_{uuid.uuid4().hex[:12]}")
    evidence: Optional[str] = None
    recorded_by: Optional[str] = None
    metadata: Optional[Dict[str, Any]] = None
    project_id: Optional[str] = None
    task_category: str = TaskCategory.UNKNOWN.value
    observed_capabilities: Dict[str, List[str]] = field(
        default_factory=lambda: {
            "skills": [],
            "agents": [],
            "knowledge_refs": [],
            "procedure_refs": [],
        }
    )

    def __post_init__(self) -> None:
        if not self.run_id or not str(self.run_id).strip():
            raise ValueError("run_id must be a non-empty string")

        if self.outcome not in VALID_OUTCOMES:
            raise ValueError(
                f"Invalid outcome '{self.outcome}'. Must be one of {sorted(VALID_OUTCOMES)}"
            )

        if self.verification_method not in VALID_VERIFICATION_METHODS:
            raise ValueError(
                f"Invalid verification_method '{self.verification_method}'. "
                f"Must be one of {sorted(VALID_VERIFICATION_METHODS)}"
            )

        # Invariant: No LLM / caller can set outcome=success without verifiable proof
        if self.outcome == Outcome.SUCCESS.value and self.verification_method == VerificationMethod.NONE.value:
            raise ValueError(
                "Fail-closed violation: outcome='success' is prohibited when "
                "verification_method='none'. Verifiable proof required."
            )

        if self.task_category not in VALID_TASK_CATEGORIES:
            raise ValueError(
                f"Invalid task_category '{self.task_category}'. "
                f"Must be one of {sorted(VALID_TASK_CATEGORIES)}"
            )

        if not isinstance(self.observed_capabilities, dict):
            raise ValueError("observed_capabilities must be a dictionary")

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)



class OutcomeTracker:
    """Thread-safe, append-only tracker recording run outcomes exclusively into telemetry/."""

    def __init__(self, ledger_path: Optional[Path | str] = None) -> None:
        if ledger_path is not None:
            self.ledger_path = Path(ledger_path).resolve()
        else:
            self.ledger_path = (Path.cwd() / "telemetry" / "outcomes" / "council_outcomes.jsonl").resolve()

        self._validate_storage_location(self.ledger_path)
        self.ledger_path.parent.mkdir(parents=True, exist_ok=True)

    @staticmethod
    def _validate_storage_location(path: Path) -> None:
        """Enforce strict isolation: outcome records must never reside inside canonical Vault dirs."""
        parts = set(path.parts)
        forbidden_hits = parts.intersection(FORBIDDEN_VAULT_DIRS)
        if forbidden_hits:
            raise PermissionError(
                f"Security Invariant Violation: OutcomeTracker cannot write to canonical "
                f"vault directories ({sorted(forbidden_hits)}). Must write strictly to telemetry/."
            )

    def record_run(
        self,
        run_id: str,
        task: str,
        outcome: str = Outcome.UNKNOWN.value,
        verification_method: str = VerificationMethod.NONE.value,
        evidence: Optional[str] = None,
        recorded_by: Optional[str] = None,
        metadata: Optional[Dict[str, Any]] = None,
        timestamp: Optional[str] = None,
        event_id: Optional[str] = None,
        project_id: Optional[str] = None,
        task_category: Optional[str] = None,
        observed_capabilities: Optional[Dict[str, List[str]] | ObservedCapabilities] = None,
    ) -> OutcomeRecord:
        """Append an outcome observation record. Strictly append-only and immutable."""
        run_id = str(run_id or "").strip()
        if not run_id:
            raise ValueError("run_id must be a non-empty string")

        task_sig = compute_task_signature(task)
        ts = timestamp or datetime.now(timezone.utc).isoformat()
        eid = event_id or f"evt_{uuid.uuid4().hex[:12]}"
        cat = validate_task_category(task_category)

        if isinstance(observed_capabilities, ObservedCapabilities):
            obs_dict = observed_capabilities.to_dict()
        elif isinstance(observed_capabilities, dict):
            obs_dict = {
                "skills": list(observed_capabilities.get("skills", [])),
                "agents": list(observed_capabilities.get("agents", [])),
                "knowledge_refs": list(observed_capabilities.get("knowledge_refs", [])),
                "procedure_refs": list(observed_capabilities.get("procedure_refs", [])),
            }
        else:
            obs_dict = {
                "skills": [],
                "agents": [],
                "knowledge_refs": [],
                "procedure_refs": [],
            }

        record = OutcomeRecord(
            run_id=run_id,
            outcome=outcome,
            verification_method=verification_method,
            timestamp=ts,
            task_signature=task_sig,
            event_id=eid,
            evidence=evidence,
            recorded_by=recorded_by,
            metadata=metadata,
            project_id=str(project_id).strip() if project_id else None,
            task_category=cat,
            observed_capabilities=obs_dict,
        )

        with open(self.ledger_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(record.to_dict(), ensure_ascii=False) + "\n")

        return record

    def get_history(self, run_id: str) -> List[OutcomeRecord]:
        """Fetch all chronological outcome events recorded for run_id."""
        if not self.ledger_path.exists():
            return []

        history: List[OutcomeRecord] = []
        with open(self.ledger_path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    data = json.loads(line)
                    if data.get("run_id") == run_id:
                        if "task_category" not in data or data["task_category"] is None:
                            data["task_category"] = TaskCategory.UNKNOWN.value
                        if "observed_capabilities" not in data or data["observed_capabilities"] is None:
                            data["observed_capabilities"] = {
                                "skills": [],
                                "agents": [],
                                "knowledge_refs": [],
                                "procedure_refs": [],
                            }
                        history.append(OutcomeRecord(**data))
                except (json.JSONDecodeError, TypeError, ValueError):
                    continue
        return history

    def get_record(self, run_id: str) -> Optional[OutcomeRecord]:
        """Fetch latest outcome record for run_id."""
        history = self.get_history(run_id)
        return history[-1] if history else None

    def list_records(
        self,
        outcome: Optional[str] = None,
        task_signature: Optional[str] = None,
        project_id: Optional[str] = None,
        task_category: Optional[str] = None,
    ) -> List[OutcomeRecord]:
        """List all recorded outcomes matching optional filters."""
        if not self.ledger_path.exists():
            return []

        results: List[OutcomeRecord] = []
        with open(self.ledger_path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    data = json.loads(line)
                    if "task_category" not in data or data["task_category"] is None:
                        data["task_category"] = TaskCategory.UNKNOWN.value
                    if "observed_capabilities" not in data or data["observed_capabilities"] is None:
                        data["observed_capabilities"] = {
                            "skills": [],
                            "agents": [],
                            "knowledge_refs": [],
                            "procedure_refs": [],
                        }
                    rec = OutcomeRecord(**data)
                    if outcome and rec.outcome != outcome:
                        continue
                    if task_signature and rec.task_signature != task_signature:
                        continue
                    if project_id and rec.project_id != project_id:
                        continue
                    if task_category and rec.task_category != task_category:
                        continue
                    results.append(rec)
                except (json.JSONDecodeError, TypeError, ValueError):
                    continue
        return results


