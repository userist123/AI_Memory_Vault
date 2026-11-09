from datetime import datetime, timedelta, timezone

from security.runtime_enforcer import ApprovalBroker, ApprovalToken, ExecutionRequest, RuntimeEnforcer
from security.trust_gate import TrustDecision, TrustState

TEST_SECRET = "test-secret-key-12345678901234567890"
_broker = ApprovalBroker(TEST_SECRET)


def _token(**overrides):
    now = datetime.now(timezone.utc)
    kwargs = {
        "approval_id": "approval-1",
        "actor": "agent-1",
        "tool_name": "artifact.write",
        "target": "project/Casa3D",
        "parameters_sha256": "abc",
        "issued_at": now,
        "expires_at": now + timedelta(minutes=5),
        "nonce": "nonce-1",
    }
    kwargs.update(overrides)
    return _broker.issue_approval(**kwargs)


def test_execution_requires_matching_short_lived_approval():
    enforcer = RuntimeEnforcer(broker=_broker)
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
    enforcer = RuntimeEnforcer(broker=_broker)
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


def test_forged_approval_without_signature_is_denied():
    enforcer = RuntimeEnforcer(broker=_broker)
    now = datetime.now(timezone.utc)
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "src/a.ts", "operation": "write"},
        side_effect=True,
    )
    # Token manually constructed by adversary without signature
    forged = ApprovalToken(
        approval_id="forged-1",
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters_sha256=request.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-evil",
        signature="",
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    result = enforcer.authorize(request, decision, approval=forged, now=now)
    assert not result.allowed
    assert result.reason == "approval_unauthenticated"


def test_forged_approval_with_invalid_signature_is_denied():
    enforcer = RuntimeEnforcer(broker=_broker)
    now = datetime.now(timezone.utc)
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "src/a.ts", "operation": "write"},
        side_effect=True,
    )
    # Token signed with untrusted/wrong key
    wrong_broker = ApprovalBroker("attacker-fake-secret")
    wrong_token = wrong_broker.issue_approval(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters_sha256=request.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-evil",
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    result = enforcer.authorize(request, decision, approval=wrong_token, now=now)
    assert not result.allowed
    assert result.reason == "approval_invalid_signature"


def test_approval_revision_mismatch_is_denied():
    enforcer = RuntimeEnforcer(broker=_broker)
    now = datetime.now(timezone.utc)
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "src/a.ts", "operation": "write"},
        revision_id="rev-2",
        side_effect=True,
    )
    # Token approved for rev-1, but executed against rev-2
    token = _token(
        parameters_sha256=request.parameters_sha256(),
        revision_id="rev-1",
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    result = enforcer.authorize(request, decision, approval=token, now=now)
    assert not result.allowed
    assert result.reason == "approval_revision_mismatch"


def test_expired_or_replayed_approval_is_denied():
    enforcer = RuntimeEnforcer(broker=_broker)
    now = datetime.now(timezone.utc)
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "src/a.ts", "operation": "write"},
        side_effect=True,
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

    fresh = _token(parameters_sha256=request.parameters_sha256(), approval_id="fresh-unique-1")
    first = enforcer.authorize(request, decision, approval=fresh, now=fresh.issued_at)
    second = enforcer.authorize(request, decision, approval=fresh, now=fresh.issued_at)
    assert first.allowed
    assert not second.allowed
    assert second.reason == "approval_replayed"


def test_blocked_content_cannot_execute_even_with_valid_approval():
    enforcer = RuntimeEnforcer(broker=_broker)
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


def test_execute_never_calls_executor_when_authorization_fails():
    enforcer = RuntimeEnforcer(broker=_broker)
    request = ExecutionRequest(
        actor="agent-1",
        tool_name="artifact.write",
        target="project/Casa3D",
        parameters={"path": "security/policy.py", "operation": "write"},
        side_effect=True,
    )
    decision = TrustDecision(TrustState.BLOCKED, ("blocked",))
    called = []

    result = enforcer.execute(request, decision, lambda req: called.append(req))
    assert not result.authorization.allowed
    assert result.authorization.reason == "blocked_content"
    assert called == []

