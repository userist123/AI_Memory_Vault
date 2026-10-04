"""Runtime execution gate with parameter-bound, expiring, single-use approvals."""
from __future__ import annotations

import hashlib
import hmac
import json
import secrets
import threading
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable
from uuid import uuid4

from .tool_capabilities import CapabilitySet
from .trust_gate import TrustDecision, TrustState
from .security_update_policy import SecurityUpdatePolicy, SecurityUpdateRequired


def _utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


@dataclass(frozen=True)
class ExecutionRequest:
    actor: str
    tool_name: str
    target: str
    parameters: dict[str, Any]
    side_effect: bool = False
    data_export: bool = False
    correlation_id: str = field(default_factory=lambda: uuid4().hex)
    operation_type: str = "execute"
    revision_id: str | None = None
    content_sha256: str | None = None

    def parameters_sha256(self) -> str:
        payload = json.dumps(
            self.parameters,
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=True,
        ).encode("utf-8")
        return hashlib.sha256(payload).hexdigest()


@dataclass(frozen=True)
class ApprovalToken:
    approval_id: str
    actor: str
    tool_name: str
    target: str
    parameters_sha256: str
    issued_at: datetime
    expires_at: datetime
    nonce: str
    signature: str = ""
    issuer: str = "external-authority-broker"
    operation_type: str = "execute"
    revision_id: str | None = None
    content_sha256: str | None = None

    def canonical_bytes(self) -> bytes:
        payload = json.dumps(
            {
                "approval_id": self.approval_id,
                "actor": self.actor,
                "content_sha256": self.content_sha256,
                "expires_at": _utc(self.expires_at).isoformat(),
                "issued_at": _utc(self.issued_at).isoformat(),
                "issuer": self.issuer,
                "nonce": self.nonce,
                "operation_type": self.operation_type,
                "parameters_sha256": self.parameters_sha256,
                "revision_id": self.revision_id,
                "target": self.target,
                "tool_name": self.tool_name,
            },
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=True,
        ).encode("utf-8")
        return payload


class ApprovalBroker:
    """External authority broker issuing cryptographically signed approval tokens."""

    def __init__(self, secret: str | bytes, issuer: str = "external-authority-broker") -> None:
        if not secret:
            raise ValueError("secret must be non-empty")
        self._secret = secret.encode("utf-8") if isinstance(secret, str) else bytes(secret)
        self.issuer = issuer

    def issue_approval(
        self,
        *,
        actor: str,
        tool_name: str,
        target: str,
        parameters_sha256: str,
        issued_at: datetime | None = None,
        expires_at: datetime | None = None,
        duration_seconds: int = 300,
        nonce: str | None = None,
        operation_type: str = "execute",
        revision_id: str | None = None,
        content_sha256: str | None = None,
        approval_id: str | None = None,
    ) -> ApprovalToken:
        now = _utc(issued_at or datetime.now(timezone.utc))
        exp = _utc(expires_at or (now + timedelta(seconds=duration_seconds)))
        token_id = approval_id or uuid4().hex
        tok_nonce = nonce or uuid4().hex
        unsigned = ApprovalToken(
            approval_id=token_id,
            actor=actor,
            tool_name=tool_name,
            target=target,
            parameters_sha256=parameters_sha256,
            issued_at=now,
            expires_at=exp,
            nonce=tok_nonce,
            issuer=self.issuer,
            operation_type=operation_type,
            revision_id=revision_id,
            content_sha256=content_sha256,
            signature="",
        )
        sig = hmac.new(self._secret, unsigned.canonical_bytes(), hashlib.sha256).hexdigest()
        return ApprovalToken(
            approval_id=unsigned.approval_id,
            actor=unsigned.actor,
            tool_name=unsigned.tool_name,
            target=unsigned.target,
            parameters_sha256=unsigned.parameters_sha256,
            issued_at=unsigned.issued_at,
            expires_at=unsigned.expires_at,
            nonce=unsigned.nonce,
            signature=sig,
            issuer=unsigned.issuer,
            operation_type=unsigned.operation_type,
            revision_id=unsigned.revision_id,
            content_sha256=unsigned.content_sha256,
        )

    def verify(self, token: ApprovalToken) -> bool:
        if not token.signature:
            return False
        expected = hmac.new(self._secret, token.canonical_bytes(), hashlib.sha256).hexdigest()
        return hmac.compare_digest(expected, token.signature)


class PersistentNonceStore:
    """Thread-safe persistent store to prevent approval replay across process restarts."""

    def __init__(self, storage_path: str | Path) -> None:
        self.path = Path(storage_path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        if not self.path.exists():
            self.path.write_text("", encoding="utf-8")
        self._lock = threading.Lock()

    def has_seen(self, identifier: str) -> bool:
        if not identifier:
            return False
        with self._lock:
            if not self.path.exists():
                return False
            for line in self.path.read_text(encoding="utf-8").splitlines():
                if line.strip() == identifier:
                    return True
            return False

    def check_and_mark(self, identifier: str) -> bool:
        """Atomically checks if identifier was seen. If already seen, returns False. If new, marks and returns True."""
        if not identifier:
            return False
        with self._lock:
            if not self.path.exists():
                self.path.write_text("", encoding="utf-8")
            for line in self.path.read_text(encoding="utf-8").splitlines():
                if line.strip() == identifier:
                    return False
            with open(self.path, "a", encoding="utf-8") as f:
                f.write(f"{identifier}\n")
            return True


@dataclass(frozen=True)
class RuntimeAuthorization:
    allowed: bool
    reason: str


@dataclass(frozen=True)
class ExecutionResult:
    authorization: RuntimeAuthorization
    value: Any = None


class RuntimeEnforcer:
    def __init__(
        self,
        capabilities: CapabilitySet | None = None,
        update_policy: SecurityUpdatePolicy | None = None,
        *,
        broker: ApprovalBroker | None = None,
        approval_secret: str | bytes | None = None,
        require_authenticated_approval: bool = True,
        nonce_store: PersistentNonceStore | None = None,
        nonce_store_path: str | Path | None = None,
    ) -> None:
        self._capabilities = capabilities
        self._update_policy = update_policy
        self._require_authenticated_approval = require_authenticated_approval
        self._lock = threading.RLock()
        if broker is not None:
            self._broker = broker
        elif approval_secret is not None:
            self._broker = ApprovalBroker(approval_secret)
        else:
            self._broker = ApprovalBroker(secrets.token_hex(32))
        self._used_approvals: set[str] = set()
        if nonce_store is not None:
            self._nonce_store: PersistentNonceStore | None = nonce_store
        elif nonce_store_path is not None:
            self._nonce_store = PersistentNonceStore(nonce_store_path)
        else:
            self._nonce_store = None

    @property
    def broker(self) -> ApprovalBroker:
        return self._broker

    def issue_approval(self, **kwargs: Any) -> ApprovalToken:
        return self._broker.issue_approval(**kwargs)

    def authorize(
        self,
        request: ExecutionRequest,
        decision: TrustDecision,
        *,
        approval: ApprovalToken | None = None,
        now: datetime | None = None,
    ) -> RuntimeAuthorization:
        with self._lock:
            current = _utc(now or datetime.now(timezone.utc))

            if self._update_policy is not None:
                try:
                    self._update_policy.enforce(protected_operation=True)
                except SecurityUpdateRequired:
                    return RuntimeAuthorization(False, "security_update_required")

            if decision.state is TrustState.BLOCKED:
                return RuntimeAuthorization(False, "blocked_content")
            if decision.state is TrustState.UNTRUSTED:
                return RuntimeAuthorization(False, "untrusted_content")

            if self._capabilities is not None and not self._capabilities.allows(
                request.actor, request.tool_name, request.target, current
            ):
                return RuntimeAuthorization(False, "capability_denied")

            approval_required = (
                decision.state is TrustState.REVIEW
                or decision.requires_human_approval
                or request.side_effect
                or request.data_export
            )

            if not approval_required:
                return RuntimeAuthorization(True, "allowed")

            if approval is None:
                return RuntimeAuthorization(False, "approval_required")

            if not approval.nonce or not approval.nonce.strip():
                return RuntimeAuthorization(False, "approval_missing_nonce")

            if self._require_authenticated_approval:
                if not approval.signature:
                    return RuntimeAuthorization(False, "approval_unauthenticated")
                if not self._broker.verify(approval):
                    return RuntimeAuthorization(False, "approval_invalid_signature")

            if approval.approval_id in self._used_approvals or (
                self._nonce_store is not None
                and (
                    self._nonce_store.has_seen(approval.approval_id)
                    or self._nonce_store.has_seen(approval.nonce)
                )
            ):
                return RuntimeAuthorization(False, "approval_replayed")

            if current >= _utc(approval.expires_at):
                return RuntimeAuthorization(False, "approval_expired")
            if current < _utc(approval.issued_at):
                return RuntimeAuthorization(False, "approval_not_yet_valid")

            if (
                approval.actor != request.actor
                or approval.tool_name != request.tool_name
                or approval.target != request.target
                or approval.parameters_sha256 != request.parameters_sha256()
            ):
                return RuntimeAuthorization(False, "approval_parameter_mismatch")

            if approval.operation_type != request.operation_type:
                return RuntimeAuthorization(False, "approval_operation_mismatch")

            if approval.revision_id != request.revision_id:
                return RuntimeAuthorization(False, "approval_revision_mismatch")

            if approval.content_sha256 != request.content_sha256:
                return RuntimeAuthorization(False, "approval_content_mismatch")

            self._used_approvals.add(approval.approval_id)
            if self._nonce_store is not None:
                if not self._nonce_store.check_and_mark(approval.approval_id) or not self._nonce_store.check_and_mark(approval.nonce):
                    return RuntimeAuthorization(False, "approval_replayed")
            return RuntimeAuthorization(True, "allowed")

    def execute(
        self,
        request: ExecutionRequest,
        decision: TrustDecision,
        executor: Callable[[ExecutionRequest], Any],
        *,
        approval: ApprovalToken | None = None,
        now: datetime | None = None,
    ) -> ExecutionResult:
        authorization = self.authorize(
            request,
            decision,
            approval=approval,
            now=now,
        )
        if not authorization.allowed:
            return ExecutionResult(authorization)
        return ExecutionResult(authorization, executor(request))
