"""Authorization boundary between trust decisions and tool execution.

The authorization result is policy data only. This module never invokes tools.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

from .trust_gate import TrustDecision, TrustState


class AuthorizationReason(str, Enum):
    ALLOWED = "allowed"
    UNTRUSTED_CONTENT = "untrusted_content"
    REVIEW = "review"
    BLOCKED_CONTENT = "blocked_content"
    HUMAN_APPROVAL_REQUIRED = "human_approval_required"
    EXTERNAL_CONTENT_NOT_AUTHORITY = "external_content_not_authority"


@dataclass(frozen=True)
class ToolRequest:
    tool_name: str
    side_effect: bool = False
    data_export: bool = False
    requested_by: str = "policy"


@dataclass(frozen=True)
class ToolAuthorization:
    allowed: bool
    reason: str
    requires_human_approval: bool = False


def authorize_tool(
    decision: TrustDecision,
    request: ToolRequest,
    *,
    human_approved: bool = False,
) -> ToolAuthorization:
    if request.requested_by == "external_content":
        return ToolAuthorization(False, AuthorizationReason.EXTERNAL_CONTENT_NOT_AUTHORITY.value)

    if decision.state is TrustState.BLOCKED:
        return ToolAuthorization(False, AuthorizationReason.BLOCKED_CONTENT.value)

    if decision.state is TrustState.UNTRUSTED:
        return ToolAuthorization(False, AuthorizationReason.UNTRUSTED_CONTENT.value)

    approval_required = (
        decision.state is TrustState.REVIEW
        or decision.requires_human_approval
        or request.side_effect
        or request.data_export
    )

    if approval_required and not human_approved:
        return ToolAuthorization(
            False,
            AuthorizationReason.HUMAN_APPROVAL_REQUIRED.value,
            requires_human_approval=True,
        )

    if decision.state is TrustState.REVIEW and not human_approved:
        return ToolAuthorization(False, AuthorizationReason.REVIEW.value, requires_human_approval=True)

    return ToolAuthorization(True, AuthorizationReason.ALLOWED.value)
