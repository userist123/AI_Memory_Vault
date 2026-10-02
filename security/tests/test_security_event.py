from datetime import datetime, timezone
from security.security_event import SecurityEvent


def test_security_event_serializes_stable_core_fields():
    event = SecurityEvent(
        event_type="TOOL_BLOCKED",
        source="trust_gate",
        actor="agent",
        correlation_id="corr-1",
        timestamp="2026-10-02T12:00:00Z",
        trust_state="BLOCKED",
        tool_name="shell.exec",
        tool_allowed=False,
        decision_reason="blocked_content",
        artifact_sha256="abc",
    )
    data = event.to_dict()
    assert data["schema_version"] == 1
    assert data["event_type"] == "TOOL_BLOCKED"
    assert data["trust_state"] == "BLOCKED"
    assert data["tool"]["allowed"] is False
    assert data["artifact"]["sha256"] == "abc"


def test_security_event_never_accepts_raw_secret_payload():
    event = SecurityEvent(
        event_type="EXTERNAL_CONTENT_IMPORTED",
        source="scanner",
        actor="agent",
        correlation_id="corr-2",
        timestamp="2026-10-02T12:00:00Z",
        trust_state="UNTRUSTED",
        metadata={"api_key": "must-not-be-stored"},
    )
    data = event.to_dict()
    assert "api_key" not in data
    assert data["metadata"] == {}


def test_security_event_defaults_to_utc_timestamp():
    event = SecurityEvent(
        event_type="TRUST_DECISION",
        source="trust_gate",
        actor="agent",
        correlation_id="corr-3",
        trust_state="TRUSTED",
    )
    parsed = datetime.fromisoformat(event.timestamp.replace("Z", "+00:00"))
    assert parsed.tzinfo is not None
