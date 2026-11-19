"""Owner-principal attestation rule shared by the security and lifecycle layers.

Lives in the security package (L0) so that ``trust_gate`` does not need an upward
import into the proposal queue; ``lifecycle.proposal_queue`` re-exports these names.
"""
from __future__ import annotations

from typing import Any

from .authorizer import DefaultAuthorizer, Operation, Principal


def owner_principal_values() -> frozenset:
    """Values of the principals the vault allows to ATTEST (the owner attestation matrix)."""
    authorizer = DefaultAuthorizer()
    return frozenset(p.value for p in Principal if authorizer.is_allowed(p, Operation.ATTEST))


def require_owner_principal(approver: Any) -> str:
    """Return the principal value when `approver` is a typed owner principal, else raise.

    The check is on the type, not on spelling: a plain string, a missing principal and an
    AI_AGENT are all refused. ``Principal`` is compared by class name and value because the
    ``cognitive_core`` and ``memory_controller`` import shims each load their own copy of the
    enum, so identity cannot be used.
    """
    if isinstance(approver, str) or type(approver).__name__ != "Principal":
        raise PermissionError(
            "approval requires an owner Principal (HUMAN or ADMIN); a reviewer name is only a label"
        )
    value = str(getattr(approver, "value", "")).lower()
    if value not in owner_principal_values():
        raise PermissionError(f"principal '{value}' is not allowed to attest an approval")
    return value
