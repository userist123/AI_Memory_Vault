from datetime import datetime, timezone
import hashlib
import pytest

from security.catalog_provenance_policy import CatalogProvenance, CatalogProvenancePolicy
from security.security_update_manager import SecurityUpdateManager, UpdateCandidate
from security.security_update_policy import SecurityUpdate, SecurityUpdatePolicy, UpdateSeverity


def test_manager_never_installs_blocked_origin():
    package = b"patch"
    update = SecurityUpdate(
        update_id="AISEC-ORIGIN-1",
        version="2.0.0",
        severity=UpdateSeverity.CRITICAL,
        released_at=datetime(2026, 10, 2, tzinfo=timezone.utc),
        mandatory_after=None,
        min_runtime_version="2.0.0",
        package_sha256=hashlib.sha256(package).hexdigest(),
    )
    installed = []
    manager = SecurityUpdateManager(
        SecurityUpdatePolicy("1.0.0"),
        verify_signature=lambda _: True,
        install=lambda u, p: installed.append(u.update_id),
        provenance_policy=CatalogProvenancePolicy(blocked_countries={"Russia", "China", "India", "North Korea"}),
    )
    origin = CatalogProvenance(
        catalog_id="cat",
        source_url="https://mirror.invalid",
        publisher="publisher",
        country="Russia",
        jurisdiction="RU",
        signer_key_id="trusted-key",
        package_sha256=update.package_sha256,
    )

    with pytest.raises(ValueError):
        manager.install_candidate(UpdateCandidate(update, package), provenance=origin)

    assert installed == []
