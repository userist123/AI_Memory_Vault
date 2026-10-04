"""Secure update orchestration for the AI security boundary."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Callable, Protocol

from .audit_trail import AuditTrail
from .security_update_policy import (
    SecurityUpdate,
    SecurityUpdatePolicy,
    verify_package_sha256,
)
from .catalog_provenance_policy import CatalogDisposition, CatalogProvenance, CatalogProvenancePolicy


class SignatureVerifier(Protocol):
    def __call__(self, update: SecurityUpdate) -> bool: ...


class PackageInstaller(Protocol):
    def __call__(self, update: SecurityUpdate, package: bytes) -> None: ...


@dataclass(frozen=True)
class UpdateCandidate:
    update: SecurityUpdate
    package: bytes


class SecurityUpdateManager:
    """Check, verify, stage and install trusted security updates.

    Network access and installation are injected so this layer never invents a
    transport or executes downloaded code itself.
    """

    def __init__(
        self,
        policy: SecurityUpdatePolicy,
        *,
        verify_signature: SignatureVerifier,
        install: PackageInstaller,
        audit_trail: AuditTrail | None = None,
        provenance_policy: CatalogProvenancePolicy | None = None,
        production_mode: bool = False,
    ) -> None:
        if production_mode and provenance_policy is None:
            raise PermissionError("production runtime requires mandatory provenance_policy")
        self.policy = policy
        self._verify_signature = verify_signature
        self._install = install
        self._audit = audit_trail
        self._provenance_policy = provenance_policy
        self._production_mode = production_mode

    def evaluate(
        self,
        update: SecurityUpdate,
        package: bytes,
        *,
        provenance: CatalogProvenance | None = None,
        actor: str = "security-update-service",
        correlation_id: str = "security-update",
    ) -> bool:
        provenance_decision = None
        if self._provenance_policy is not None:
            if provenance is None:
                if self._audit:
                    self._audit.record(
                        event_type="SECURITY_UPDATE_REJECTED",
                        actor=actor,
                        correlation_id=correlation_id,
                        outcome="BLOCKED",
                        target=update.update_id,
                        decision_reason="provenance_missing_under_policy",
                        metadata={"verification_type": "provenance"},
                    )
                return False
            provenance_decision = self._provenance_policy.evaluate(provenance)
            if provenance_decision.disposition in (CatalogDisposition.BLOCKED, CatalogDisposition.REVIEW):
                if self._audit:
                    self._audit.record(
                        event_type="SECURITY_UPDATE_REJECTED",
                        actor=actor,
                        correlation_id=correlation_id,
                        outcome="BLOCKED",
                        target=update.update_id,
                        decision_reason=provenance_decision.reason,
                        metadata={"verification_type": "provenance"},
                    )
                return False

        valid_hash = verify_package_sha256(package, update.package_sha256)
        valid_signature = self._verify_signature(update)

        if self._audit:
            self._audit.record(
                event_type="SECURITY_UPDATE_EVALUATED",
                actor=actor,
                correlation_id=correlation_id,
                outcome="ACCEPTED" if valid_hash and valid_signature else "REJECTED",
                target=update.update_id,
                decision_reason=(
                    "hash_and_signature_valid"
                    if valid_hash and valid_signature
                    else "update_integrity_or_signature_failed"
                ),
                metadata={"verification_type": "signature+sha256"},
            )

        if not valid_hash or not valid_signature:
            return False

        self.policy.apply_manifest(update)
        return True

    def install_candidate(
        self,
        candidate: UpdateCandidate,
        *,
        provenance: CatalogProvenance | None = None,
        actor: str = "security-update-service",
        correlation_id: str = "security-update",
    ) -> None:
        if not self.evaluate(
            candidate.update,
            candidate.package,
            provenance=provenance,
            actor=actor,
            correlation_id=correlation_id,
        ):
            raise ValueError("security update failed integrity/signature verification")

        self._install(candidate.update, candidate.package)
        self.policy.mark_installed(candidate.update.version)

        if self._audit:
            self._audit.record(
                event_type="SECURITY_UPDATE_INSTALLED",
                actor=actor,
                correlation_id=correlation_id,
                outcome="INSTALLED",
                target=candidate.update.update_id,
                metadata={"verification_type": "signature+sha256"},
            )
