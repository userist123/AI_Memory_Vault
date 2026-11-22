"""Typed application errors shared by REST, MCP and GUI adapters."""

from __future__ import annotations

from typing import Any


class StudioError(Exception):
    code = "studio_error"
    http_status = 400

    def __init__(self, message: str, **details: Any) -> None:
        super().__init__(message)
        self.message = message
        self.details = details

    def to_dict(self) -> dict[str, Any]:
        return {"error": self.code, "message": self.message, "details": self.details}


class ValidationFailed(StudioError):
    code = "validation_failed"
    http_status = 422


class NotFound(StudioError):
    code = "not_found"
    http_status = 404


class ConflictError(StudioError):
    """Raised when a mutation is based on a stale project revision."""

    code = "revision_conflict"
    http_status = 409


class PolicyDenied(StudioError):
    code = "policy_denied"
    http_status = 403


class ApprovalRequired(StudioError):
    code = "approval_required"
    http_status = 428


class WorkspaceViolation(StudioError):
    code = "workspace_violation"
    http_status = 403


class CaptureFailed(StudioError):
    code = "capture_failed"
    http_status = 502


class Unavailable(StudioError):
    code = "unavailable"
    http_status = 503
