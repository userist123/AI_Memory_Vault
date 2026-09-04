"""
Milestone 3: Verifier Agent (Frontmatter Schema Audit, Invariants P0-P18 Compliance).
"""

import uuid
from typing import Dict, Any, List, Optional
from datetime import datetime, timezone

from jarvis.memory.invariants import (
    Principal,
    Lifecycle,
    NoteType,
    NoteFrontmatter,
    ProvenanceModel,
)
from jarvis.llm.base import BaseLLMProvider, CancellationToken
from jarvis.agents.base import BaseAgent
from jarvis.agents.models import (
    AgentRole,
    ViolationSeverity,
    SchemaViolation,
    VerificationReport,
)


class VerifierAgent(BaseAgent):
    """
    Audits frontmatter schema compliance and trust boundary invariants (P0-P18).
    Strict read-only execution with zero state mutations.
    """

    role: AgentRole = AgentRole.VERIFIER

    def __init__(
        self,
        storage: Optional[Any] = None,
        llm: Optional[BaseLLMProvider] = None,
    ):
        super().__init__(storage=storage, llm=llm)

    def verify_note(
        self,
        note_data: Dict[str, Any],
        principal: Optional[Principal] = None,
        is_proposal: bool = False,
    ) -> VerificationReport:
        """
        Audits note metadata against canonical YAML frontmatter schema and P0-P18 invariants.
        """
        violations: List[SchemaViolation] = []
        missing: List[str] = []

        if not isinstance(note_data, dict):
            violations.append(
                SchemaViolation(
                    field="root",
                    rule="ERR_INVALID_PAYLOAD",
                    message="Note payload must be a dictionary.",
                )
            )
            return VerificationReport(is_valid=False, violations=violations, missing=["all"])

        # 1. Mandatory Fields Check
        required_fields = ["id", "type", "lifecycle", "category", "provenance"]
        for rf in required_fields:
            if rf not in note_data or note_data[rf] is None or note_data[rf] == "":
                missing.append(rf)
                violations.append(
                    SchemaViolation(
                        field=rf,
                        rule="ERR_MANDATORY_FIELD_MISSING",
                        message=f"Mandatory frontmatter field '{rf}' is missing or empty.",
                    )
                )

        note_id = str(note_data.get("id", ""))

        # 2. UUID Syntax Validation
        if "id" in note_data and note_data["id"]:
            try:
                uuid.UUID(str(note_data["id"]))
            except Exception:
                violations.append(
                    SchemaViolation(
                        field="id",
                        rule="ERR_P0_001_INVALID_UUID",
                        message=f"Note id '{note_data['id']}' is not a valid RFC-4122 UUID.",
                    )
                )

        # 3. Enum Values Validation (NoteType & Lifecycle)
        if "type" in note_data and note_data["type"]:
            raw_type = note_data["type"]
            valid_types = {t.value for t in NoteType}
            if raw_type not in valid_types:
                violations.append(
                    SchemaViolation(
                        field="type",
                        rule="ERR_INVALID_NOTE_TYPE",
                        message=f"Note type '{raw_type}' is not in permitted NoteTypes: {valid_types}",
                    )
                )

        if "lifecycle" in note_data and note_data["lifecycle"]:
            raw_lc = note_data["lifecycle"]
            valid_lcs = {lc.value for lc in Lifecycle}
            if raw_lc not in valid_lcs:
                violations.append(
                    SchemaViolation(
                        field="lifecycle",
                        rule="ERR_INVALID_LIFECYCLE",
                        message=f"Lifecycle state '{raw_lc}' is not in permitted states: {valid_lcs}",
                    )
                )

        # 4. Invariant P0-001 / P0-005 (AI Self-Verification Gate)
        verification = note_data.get("verification", "unverified")
        prov = note_data.get("provenance", {})
        source_type = prov.get("source_type") if isinstance(prov, dict) else getattr(prov, "source_type", "unknown")

        if verification == "verified":
            if (principal == Principal.AI_AGENT or is_proposal) and source_type in ["ai", "inference", "unknown"]:
                violations.append(
                    SchemaViolation(
                        field="verification",
                        rule="ERR_P0_001_AI_VERIFIED_GATE",
                        message="AI Agent self-verification is prohibited. Status 'verified' requires human/admin attestation.",
                    )
                )

        # 5. Invariant P0-004 (Creation Lifecycles on Proposal)
        lifecycle = note_data.get("lifecycle")
        if is_proposal and (lifecycle == Lifecycle.ACTIVE.value or lifecycle == "ACTIVE"):
            if principal == Principal.AI_AGENT or principal is None:
                violations.append(
                    SchemaViolation(
                        field="lifecycle",
                        rule="ERR_P0_004_AI_CREATION_LIFECYCLE",
                        message="AI Agent cannot propose directly into 'ACTIVE' lifecycle. Must propose into RAW/REVIEW.",
                    )
                )

        # 6. Invariant P0-002 (Privileged Provenance Types on Proposal)
        if is_proposal and (principal == Principal.AI_AGENT or principal is None) and source_type in {"user", "official", "experience", "import"}:
            violations.append(
                SchemaViolation(
                    field="provenance.source_type",
                    rule="ERR_P0_002_FORBIDDEN_PROVENANCE",
                    message=f"Principal 'ai_agent' cannot claim privileged source_type '{source_type}'.",
                )
            )

        # 7. Invariant P0-012 / P0-013 (Acyclic Supersession)
        supersedes = note_data.get("supersedes")
        if supersedes and note_id:
            if supersedes == note_id:
                violations.append(
                    SchemaViolation(
                        field="supersedes",
                        rule="ERR_P0_012_CYCLIC_SUPERSESSION",
                        message=f"Self-supersession prohibited: note cannot supersede itself ({note_id}).",
                    )
                )
            # If storage is available, verify lineage does not contain a cycle
            elif self.storage:
                try:
                    lineage = self.storage.get_lineage(supersedes)
                    ancestor_ids = {n.get("id") for n in lineage}
                    if note_id in ancestor_ids:
                        violations.append(
                            SchemaViolation(
                                field="supersedes",
                                rule="ERR_P0_012_CYCLIC_SUPERSESSION",
                                message=f"Cyclic supersession detected: note '{note_id}' is already an ancestor of '{supersedes}'.",
                            )
                        )
                except Exception:
                    pass

        is_valid = (len(violations) == 0 and len(missing) == 0)
        return VerificationReport(
            note_id=note_id if note_id else None,
            is_valid=is_valid,
            violations=violations,
            missing=missing,
            audit_timestamp=datetime.now(timezone.utc).isoformat(),
            auditor_role="verifier",
        )

    def verify_proposal(
        self,
        note_data: Dict[str, Any],
        principal: Principal = Principal.AI_AGENT,
    ) -> VerificationReport:
        """Audits a proposed note before persistence against proposal invariants."""
        return self.verify_note(note_data, principal=principal, is_proposal=True)

    def verify_provenance(
        self,
        provenance: Dict[str, Any],
        principal: Principal = Principal.AI_AGENT,
    ) -> VerificationReport:
        """Audit standalone provenance metadata."""
        violations: List[SchemaViolation] = []
        source_type = provenance.get("source_type", "unknown")

        if principal == Principal.AI_AGENT and source_type in {"user", "official", "experience", "import"}:
            violations.append(
                SchemaViolation(
                    field="source_type",
                    rule="ERR_P0_002_FORBIDDEN_PROVENANCE",
                    message=f"Principal 'ai_agent' cannot claim privileged provenance '{source_type}'.",
                )
            )

        if not provenance.get("source_ref"):
            violations.append(
                SchemaViolation(
                    field="source_ref",
                    rule="ERR_PROVENANCE_SOURCE_REF_EMPTY",
                    message="Provenance must include a non-empty source_ref.",
                )
            )

        return VerificationReport(
            is_valid=len(violations) == 0,
            violations=violations,
            missing=[] if provenance.get("source_ref") else ["source_ref"],
            audit_timestamp=datetime.now(timezone.utc).isoformat(),
            auditor_role="verifier",
        )

    async def execute(
        self,
        payload: Dict[str, Any],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> Dict[str, Any]:
        """Audit payload note for schema and invariant compliance."""
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        note = payload.get("note", payload)
        principal_val = payload.get("principal", Principal.AI_AGENT)
        principal = Principal(principal_val) if isinstance(principal_val, str) else principal_val

        report = self.verify_note(note, principal=principal)
        return {
            "valid": report.is_valid,
            "missing": report.missing,
            "violations": [v.rule for v in report.violations],
            "report": report.model_dump(),
        }
