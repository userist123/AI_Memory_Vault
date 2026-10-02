from datetime import datetime, timezone

from security.audit_trail import AuditTrail
from security.memory_adapter import MemoryAdapter
from security.runtime_adapter import RuntimeAdapter
from security.runtime_enforcer import ExecutionRequest
from security.tool_integrity import ToolDefinition
from security.trust_gate import TrustDecision, TrustState


def _trusted():
    return TrustDecision(TrustState.TRUSTED, "verified", False)


def _tool():
    return ToolDefinition(
        server_id="srv-1",
        name="lookup",
        description="Read a record",
        input_schema={"type": "object"},
    )


def test_audit_trail_is_hash_chained_and_tamper_evident():
    trail = AuditTrail()
    trail.record(
        event_type="TOOL_REQUEST",
        actor="agent-1",
        correlation_id="corr-1",
        outcome="STARTED",
        tool_name="lookup",
        parameters_sha256="abc",
        timestamp=datetime(2026, 10, 2, tzinfo=timezone.utc),
    )
    trail.record(
        event_type="TOOL_EXECUTION",
        actor="agent-1",
        correlation_id="corr-1",
        outcome="ALLOWED",
        tool_name="lookup",
        timestamp=datetime(2026, 10, 2, 0, 0, 1, tzinfo=timezone.utc),
    )

    assert trail.verify() is True
    assert trail.records[1].previous_sha256 == trail.records[0].sha256

    tampered = trail.records[0]
    object.__setattr__(tampered, "outcome", "FORGED")
    assert trail.verify() is False


def test_runtime_adapter_audits_request_decision_and_response():
    trail = AuditTrail()
    adapter = RuntimeAdapter(audit_trail=trail)
    adapter.register(_tool(), lambda request: {"ok": True})

    request = ExecutionRequest("agent-1", "lookup", "record:42", {"id": "42"}, correlation_id="corr-runtime")
    result = adapter.execute(request, _tool(), _trusted())

    assert result.authorization.allowed is True
    event_types = [record.event_type for record in trail.records]
    assert event_types == ["TOOL_REQUEST", "TOOL_AUTHORIZATION", "TOOL_RESPONSE"]
    assert all(record.correlation_id == "corr-runtime" for record in trail.records)
    assert trail.verify() is True


def test_runtime_adapter_audits_executor_failure_without_secret_details():
    trail = AuditTrail()

    def fail(request):
        raise RuntimeError("secret-token=do-not-log")

    adapter = RuntimeAdapter(audit_trail=trail)
    adapter.register(_tool(), fail)
    request = ExecutionRequest("agent-1", "lookup", "record:42", {"id": "42"}, correlation_id="corr-fail")

    try:
        adapter.execute(request, _tool(), _trusted())
    except RuntimeError:
        pass

    failure = trail.records[-1]
    assert failure.event_type == "TOOL_EXECUTION_ERROR"
    assert failure.outcome == "ERROR"
    assert "secret-token" not in str(failure.to_dict())
    assert trail.verify() is True


def test_memory_adapter_audits_denial_persistence_and_commit():
    trail = AuditTrail()
    writes = []
    adapter = MemoryAdapter(lambda namespace, payload: writes.append((namespace, payload)), audit_trail=trail)

    denied = adapter.write("agent", {"fact": "blocked"}, TrustState.UNTRUSTED)
    allowed = adapter.write("agent", {"fact": "verified"}, TrustState.TRUSTED)

    assert denied.allowed is False
    assert allowed.allowed is True
    assert [r.event_type for r in trail.records] == [
        "MEMORY_WRITE_DENIED",
        "MEMORY_PERSIST",
        "MEMORY_COMMIT",
    ]
    assert all(r.correlation_id for r in trail.records)
    assert trail.verify() is True
