"""Authorized mutation gate for applying verified conflict verdicts.

The gate is the only bridge from an authorized conflict verdict to canonical
memory mutation. It requires a verified evidence bundle and delegates the
actual mutation to MemoryController, which remains the policy/audit authority.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Mapping

from .authorizer import Operation, Principal
from .authorized_verdict import AuthorizedVerdict, Verdict


@dataclass(frozen=True)
class MutationResult:
    verdict_id: str
    action: str
    target_ids: tuple[str, ...]
    changed: bool
    status: str

    def as_dict(self) -> dict[str, Any]:
        return {
            "verdict_id": self.verdict_id,
            "action": self.action,
            "target_ids": list(self.target_ids),
            "changed": self.changed,
            "status": self.status,
        }


class MutationGate:
    """Apply only explicitly authorized, evidence-bound conflict mutations."""

    def __init__(self, controller: Any):
        self.controller = controller

    def _check_principal(self, principal: Principal) -> None:
        if not self.controller.authorizer.is_allowed(principal, Operation.REVIEW):
            raise PermissionError(f"{principal.value} cannot apply conflict verdicts")

    @staticmethod
    def _require_verified_evidence(verdict: AuthorizedVerdict, verification: Mapping[str, Any]) -> None:
        if not verification.get("valid"):
            raise ValueError("Evidence bundle is not valid for mutation")
        if not verification.get("bundle_hash_matches"):
            raise ValueError("Evidence bundle hash does not match")
        if tuple(verification.get("stale_memory_ids", [])):
            raise ValueError("Evidence contains stale memories")
        if tuple(verification.get("missing_memory_ids", [])):
            raise ValueError("Evidence contains missing memories")
        if verification.get("bundle_id") and not str(verification["bundle_id"]).strip():
            raise ValueError("Evidence bundle ID is invalid")

    @staticmethod
    def _winner_loser(verdict: AuthorizedVerdict) -> tuple[str, str]:
        if len(verdict.memory_ids) != 2:
            raise ValueError("Conflict mutation currently requires exactly two memory IDs")
        if verdict.verdict == Verdict.ACCEPT_A:
            return verdict.memory_ids[0], verdict.memory_ids[1]
        if verdict.verdict == Verdict.ACCEPT_B:
            return verdict.memory_ids[1], verdict.memory_ids[0]
        raise ValueError("Verdict does not select a single winner")

    def apply(
        self,
        *,
        principal: Principal,
        verdict: AuthorizedVerdict,
        evidence_verification: Mapping[str, Any],
        action: str,
        reason: str,
    ) -> MutationResult:
        self._check_principal(principal)
        if verdict.reviewer_principal not in {Principal.HUMAN.value, Principal.ADMIN.value}:
            raise PermissionError("Verdict reviewer is not an authorized human/admin principal")
        self._require_verified_evidence(verdict, evidence_verification)
        if not reason.strip():
            raise ValueError("Mutation reason is required")

        verified_hash = str(evidence_verification.get("bundle_hash") or verdict.evidence_bundle_hash)
        if verified_hash != verdict.evidence_bundle_hash:
            raise ValueError("Verified evidence hash does not match verdict")

        if verdict.verdict == Verdict.DEFER:
            if action != "none":
                raise ValueError("DEFER verdict cannot mutate memory")
            return MutationResult(verdict.verdict_id, "none", verdict.memory_ids, False, "deferred")

        if verdict.verdict in {Verdict.ACCEPT_A, Verdict.ACCEPT_B}:
            winner, loser = self._winner_loser(verdict)
            if action == "supersede":
                self.controller.supersede(principal, loser, winner, evidence=verdict.evidence_bundle_hash)
                return MutationResult(verdict.verdict_id, "supersede", (loser, winner), True, "applied")
            if action == "attest":
                self.controller.attest(
                    principal,
                    winner,
                    verification_reason=reason,
                    evidence_reference=verdict.evidence_bundle_hash,
                )
                return MutationResult(verdict.verdict_id, "attest", (winner,), True, "applied")
            raise ValueError("ACCEPT verdict requires action=attest or action=supersede")

        if verdict.verdict == Verdict.REJECT_BOTH:
            if action != "archive":
                raise ValueError("REJECT_BOTH verdict requires action=archive")
            for note_id in verdict.memory_ids:
                self.controller.archive(principal, note_id, reason)
            return MutationResult(verdict.verdict_id, "archive", verdict.memory_ids, True, "applied")

        raise ValueError(f"Unsupported verdict: {verdict.verdict}")
