from datetime import datetime, timezone

import pytest

from security.security_update_policy import (
    SecurityUpdate,
    SecurityUpdatePolicy,
    SecurityUpdateRequired,
    UpdateSeverity,
    verify_package_sha256,
)


def _update(deadline=None):
    return SecurityUpdate(
        update_id="AISEC-2026-001",
        version="2.0.0",
        severity=UpdateSeverity.CRITICAL,
        released_at=datetime(2026, 10, 1, tzinfo=timezone.utc),
        mandatory_after=deadline,
        min_runtime_version="2.0.0",
        package_sha256=verify_package_sha256.__module__ and __import__("hashlib").sha256(b"pkg").hexdigest(),
    )


def test_critical_update_blocks_after_deadline():
    now = datetime(2026, 10, 3, tzinfo=timezone.utc)
    policy = SecurityUpdatePolicy("1.0.0", clock=lambda: now)
    policy.apply_manifest(_update(datetime(2026, 10, 2, tzinfo=timezone.utc)))

    state = policy.state()
    assert state.blocked is True
    with pytest.raises(SecurityUpdateRequired):
        policy.enforce()


def test_update_does_not_block_before_deadline():
    now = datetime(2026, 10, 1, tzinfo=timezone.utc)
    policy = SecurityUpdatePolicy("1.0.0", clock=lambda: now)
    policy.apply_manifest(_update(datetime(2026, 10, 2, tzinfo=timezone.utc)))

    assert policy.state().blocked is False


def test_package_hash_must_match():
    assert verify_package_sha256(b"pkg", _update().package_sha256)
    assert not verify_package_sha256(b"tampered", _update().package_sha256)
