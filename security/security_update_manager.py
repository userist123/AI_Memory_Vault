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
    ) -> None:
        self.policy = policy
        self._verify_signature = verify_signature
        self._install = install
        self._audit = audit_trail

    def evaluate(
        self,
        update: SecurityUpdate,
        package: bytes,
        *,
        actor: str = "security-update-service",
        correlation_id: str = "security-update",
    ) -> bool:
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
        actor: str = "security-update-service",
        correlation_id: str = "security-update",
    ) -> None:
        if not self.evaluate(
            candidate.update,
            candidate.package,
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
