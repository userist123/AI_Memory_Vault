from datetime import datetime, timedelta, timezone

from security.runtime_enforcer import ApprovalToken, ExecutionRequest, RuntimeEnforcer
from security.trust_gate import TrustDecision, TrustState


def _token(**overrides):
    now = datetime.now(timezone.utc)
    values = {
        "approval_id": "approval-1",
        "actor": "agent-1",
        "tool_name": "artifact.write",
        "target": "project/Casa3D",
        "parameters_sha256": "abc",
        "issued_at": now,
        "expires_at": now + timedelta(minutes=5),
        "nonce": "nonce-1",
    }
    values.update(overrides)
    return ApprovalToken(**values)


def test_execution_requires_matching_short_lived_approval():
    enforcer = RuntimeEnforcer()
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "src/a.ts", "operation": "write"},
    )
    token = _token(parameters_sha256=request.parameters_sha256())
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    result = enforcer.authorize(request, decision, approval=token, now=token.issued_at)
    assert result.allowed


def test_parameter_change_invalidates_approval():
    enforcer = RuntimeEnforcer()
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "src/a.ts", "operation": "write"},
    )
    token = _token(parameters_sha256=request.parameters_sha256())
    changed = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "security/policy.py", "operation": "write"},
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    result = enforcer.authorize(changed, decision, approval=token, now=token.issued_at)
    assert not result.allowed
    assert result.reason == "approval_parameter_mismatch"


def test_expired_or_replayed_approval_is_denied():
    enforcer = RuntimeEnforcer()
    now = datetime.now(timezone.utc)
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "src/a.ts", "operation": "write"},
    )
    token = _token(
        parameters_sha256=request.parameters_sha256(),
        issued_at=now - timedelta(minutes=10),
        expires_at=now - timedelta(minutes=5),
    )
    decision = TrustDecision(TrustState.TRUSTED)

    expired = enforcer.authorize(request, decision, approval=token, now=now)
    assert not expired.allowed
    assert expired.reason == "approval_expired"

    fresh = _token(parameters_sha256=request.parameters_sha256())
    first = enforcer.authorize(request, decision, approval=fresh, now=fresh.issued_at)
    second = enforcer.authorize(request, decision, approval=fresh, now=fresh.issued_at)
    assert first.allowed
    assert not second.allowed
    assert second.reason == "approval_replayed"


def test_blocked_content_cannot_execute_even_with_valid_approval():
    enforcer = RuntimeEnforcer()
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "src/a.ts", "operation": "write"},
    )
    token = _token(parameters_sha256=request.parameters_sha256())
    decision = TrustDecision(TrustState.BLOCKED, ("blocked",))

    result = enforcer.authorize(request, decision, approval=token, now=token.issued_at)
    assert not result.allowed
    assert result.reason == "blocked_content"
