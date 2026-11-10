from datetime import datetime, timezone
import hashlib

from security.audit_trail import AuditTrail
from security.security_update_manager import SecurityUpdateManager, UpdateCandidate
from security.security_update_policy import SecurityUpdate, SecurityUpdatePolicy, UpdateSeverity


def test_verified_update_installs_and_clears_gate():
    package = b"security-patch"
    update = SecurityUpdate(
        update_id="AISEC-2026-002",
        version="2.0.0",
        severity=UpdateSeverity.CRITICAL,
        released_at=datetime(2026, 10, 2, tzinfo=timezone.utc),
        mandatory_after=datetime(2026, 10, 2, tzinfo=timezone.utc),
        min_runtime_version="2.0.0",
        package_sha256=hashlib.sha256(package).hexdigest(),
    )
    policy = SecurityUpdatePolicy(
        "1.0.0",
        clock=lambda: datetime(2026, 10, 3, tzinfo=timezone.utc),
    )
    trail = AuditTrail()
    installed = []

    manager = SecurityUpdateManager(
        policy,
        verify_signature=lambda item: True,
        install=lambda item, data: installed.append((item.update_id, data)),
        audit_trail=trail,
    )

    manager.install_candidate(UpdateCandidate(update, package))

    assert installed == [("AISEC-2026-002", package)]
    assert policy.state().blocked is False
    assert [r.event_type for r in trail.records] == [
        "SECURITY_UPDATE_EVALUATED",
        "SECURITY_UPDATE_INSTALLED",
    ]


def test_tampered_package_never_installs():
    package = b"security-patch"
    update = SecurityUpdate(
        update_id="AISEC-2026-003",
        version="3.0.0",
        severity=UpdateSeverity.CRITICAL,
        released_at=datetime(2026, 10, 2, tzinfo=timezone.utc),
        mandatory_after=None,
        min_runtime_version="3.0.0",
        package_sha256=hashlib.sha256(package).hexdigest(),
    )
    installed = []
    manager = SecurityUpdateManager(
        SecurityUpdatePolicy("1.0.0"),
        verify_signature=lambda item: True,
        install=lambda item, data: installed.append(data),
    )

    try:
        manager.install_candidate(UpdateCandidate(update, b"tampered"))
    except ValueError:
        pass

    assert installed == []
