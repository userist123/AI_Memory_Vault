from datetime import datetime, timedelta, timezone

from security.tool_capabilities import Capability, CapabilitySet


def test_capability_is_scoped_to_actor_tool_and_expiry():
    now = datetime.now(timezone.utc)
    cap = Capability(
        actor="agent-1",
        tool_name="artifact.write",
        scope="project/Casa3D",
        expires_at=now + timedelta(minutes=5),
    )
    caps = CapabilitySet([cap])

    assert caps.allows("agent-1", "artifact.write", "project/Casa3D", now)
    assert not caps.allows("agent-2", "artifact.write", "project/Casa3D", now)
    assert not caps.allows("agent-1", "artifact.write", "project/Other", now)


def test_expired_capability_is_denied():
    now = datetime.now(timezone.utc)
    cap = Capability(
        actor="agent-1",
        tool_name="artifact.write",
        scope="project/Casa3D",
        expires_at=now - timedelta(seconds=1),
    )
    assert not CapabilitySet([cap]).allows("agent-1", "artifact.write", "project/Casa3D", now)
