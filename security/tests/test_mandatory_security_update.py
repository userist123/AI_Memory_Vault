from datetime import datetime, timezone
import hashlib

from security.memory_adapter import MemoryAdapter
from security.runtime_enforcer import ExecutionRequest, RuntimeEnforcer
from security.security_update_policy import SecurityUpdate, SecurityUpdatePolicy, UpdateSeverity
from security.trust_gate import TrustDecision, TrustState


def _blocked_policy():
    policy = SecurityUpdatePolicy(
        "1.0.0",
        clock=lambda: datetime(2026, 10, 3, tzinfo=timezone.utc),
    )
    policy.apply_manifest(
        SecurityUpdate(
            update_id="AISEC-2026-LOCK",
            version="2.0.0",
            severity=UpdateSeverity.CRITICAL,
            released_at=datetime(2026, 10, 1, tzinfo=timezone.utc),
            mandatory_after=datetime(2026, 10, 2, tzinfo=timezone.utc),
            min_runtime_version="2.0.0",
            package_sha256=hashlib.sha256(b"pkg").hexdigest(),
        )
    )
    return policy


def test_runtime_denies_execution_when_security_update_is_mandatory():
    calls = []
    enforcer = RuntimeEnforcer(update_policy=_blocked_policy())
    result = enforcer.execute(
        ExecutionRequest("agent", "lookup", "record:1", {}),
        TrustDecision(TrustState.TRUSTED, "verified", False),
        lambda request: calls.append(request),
    )
    assert result.authorization.allowed is False
    assert result.authorization.reason == "security_update_required"
    assert calls == []


def test_memory_denies_write_when_security_update_is_mandatory():
    writes = []
    adapter = MemoryAdapter(lambda ns, payload: writes.append(payload), update_policy=_blocked_policy())
    result = adapter.write("agent", {"fact": "x"}, TrustState.TRUSTED)
    assert result.allowed is False
    assert result.reason == "security_update_required"
    assert writes == []
