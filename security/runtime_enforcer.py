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

    def __init__(self, secret: str | bytes, issuer: str = "external-authority-broker", *, production_mode: bool = False) -> None:
        if not secret:
            raise ValueError("secret must be non-empty")
        secret_bytes = secret.encode("utf-8") if isinstance(secret, str) else bytes(secret)
        if production_mode:
            if len(secret_bytes) < 32:
                raise ValueError("production secret must be at least 32 bytes")
            if secret_bytes.startswith(b"test-") or secret_bytes.startswith(b"dev-"):
                raise ValueError("test/dev secret cannot be used in production mode")
        self._secret = secret_bytes
        self.issuer = issuer
        self.production_mode = production_mode

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
    """Multi-process atomic persistent store to prevent approval replay across processes and restarts."""

    def __init__(self, storage_path: str | Path) -> None:
        import sqlite3
        self._sqlite3 = sqlite3
        self.path = Path(storage_path).resolve()
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()
        self._init_db()

    def _get_connection(self):
        conn = self._sqlite3.connect(str(self.path), timeout=5.0, isolation_level=None)
        conn.execute("PRAGMA busy_timeout = 5000")
        try:
            conn.execute("PRAGMA journal_mode = WAL")
        except self._sqlite3.OperationalError:
            pass
        return conn

    def _init_db(self) -> None:
        with self._lock:
            try:
                conn = self._get_connection()
                try:
                    conn.execute("""
                        CREATE TABLE IF NOT EXISTS consumed_identifiers (
                            identifier TEXT PRIMARY KEY,
                            consumed_at TEXT NOT NULL
                        )
                    """)
                finally:
                    conn.close()
            except self._sqlite3.DatabaseError as e:
                raise RuntimeError(f"Failed to initialize persistent nonce store: {e}") from e

    def has_seen(self, identifier: str) -> bool:
        if not identifier:
            return False
        with self._lock:
            try:
                conn = self._get_connection()
                try:
                    cur = conn.execute(
                        "SELECT 1 FROM consumed_identifiers WHERE identifier = ?",
                        (identifier,),
                    )
                    return cur.fetchone() is not None
                finally:
                    conn.close()
            except self._sqlite3.DatabaseError:
                # Fail-closed on corrupted storage
                return True

    def check_and_mark(self, identifier: str) -> bool:
        """Atomically checks if identifier was seen. If already seen, returns False. If new, marks and returns True.

        Guaranteed atomic across separate processes and instances via PRIMARY KEY unique constraint in SQLite.
        """
        if not identifier:
            return False
        with self._lock:
            try:
                conn = self._get_connection()
                try:
                    now_str = datetime.now(timezone.utc).isoformat()
                    conn.execute(
                        "INSERT INTO consumed_identifiers (identifier, consumed_at) VALUES (?, ?)",
                        (identifier, now_str),
                    )
                    return True
                except self._sqlite3.IntegrityError:
                    return False
                finally:
                    conn.close()
            except self._sqlite3.DatabaseError:
                # Fail-closed on storage corruption or contention
                return False


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
        production_mode: bool = False,
    ) -> None:
        self._capabilities = capabilities
        self._update_policy = update_policy
        self._require_authenticated_approval = require_authenticated_approval
        self._production_mode = production_mode
        self._lock = threading.RLock()

        if production_mode:
            if broker is None:
                raise ValueError("production runtime requires an explicitly injected ApprovalBroker")
            if not isinstance(broker, ApprovalBroker):
                raise TypeError("broker must be an instance of ApprovalBroker")
            if len(broker._secret) < 32:
                raise ValueError("production approval broker secret must be at least 32 bytes")
            if broker._secret.startswith(b"test-") or broker._secret.startswith(b"dev-"):
                raise ValueError("test/dev secret cannot be used in production mode")
            self._broker = broker

            if nonce_store is not None:
                self._nonce_store: PersistentNonceStore | None = nonce_store
            elif nonce_store_path is not None:
                self._nonce_store = PersistentNonceStore(nonce_store_path)
            else:
                raise ValueError("production runtime requires an explicitly injected PersistentNonceStore")
        else:
            if broker is not None:
                self._broker = broker
            elif approval_secret is not None:
                self._broker = ApprovalBroker(approval_secret)
            else:
                self._broker = ApprovalBroker(secrets.token_hex(32))

            if nonce_store is not None:
                self._nonce_store = nonce_store
            elif nonce_store_path is not None:
                self._nonce_store = PersistentNonceStore(nonce_store_path)
            else:
                self._nonce_store = None

        self._used_approvals: set[str] = set()

    @property
    def broker(self) -> ApprovalBroker:
        if self._production_mode:
            raise PermissionError("Access to ApprovalBroker is forbidden in production runtime")
        return self._broker

    def issue_approval(self, **kwargs: Any) -> ApprovalToken:
        if self._production_mode:
            raise PermissionError("issue_approval is forbidden on RuntimeEnforcer in production mode; approvals must originate from the external authority broker")
        return self._broker.issue_approval(**kwargs)

    def request_approval(self, request: ExecutionRequest) -> str:
        """Agent-facing interface to submit an execution approval request to the external owner authority."""
        return request.correlation_id

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

            if approval.revision_id is not None:
                if request.revision_id is None:
                    return RuntimeAuthorization(False, "approval_revision_mismatch")
                if approval.revision_id != request.revision_id:
                    return RuntimeAuthorization(False, "approval_revision_mismatch")

            if approval.content_sha256 is not None:
                if request.content_sha256 is None:
                    return RuntimeAuthorization(False, "approval_content_mismatch")
                if approval.content_sha256 != request.content_sha256:
                    return RuntimeAuthorization(False, "approval_content_mismatch")

            # Mandatory binding policy for state mutating tool operations
            mandatory_operations = {"patch", "update", "state_mutate", "delete"}
            mandatory_tools = {"memory_patch", "state_mutate", "file_writer", "code_patch"}
            if request.operation_type in mandatory_operations or request.tool_name in mandatory_tools:
                if approval.revision_id is None or approval.content_sha256 is None:
                    return RuntimeAuthorization(False, "approval_binding_required_for_tool")

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
