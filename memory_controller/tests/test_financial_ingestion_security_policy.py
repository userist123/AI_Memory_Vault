"""Regression tests for direct financial-ingestion trust-boundary state."""

import pytest

from memory_controller.financial_ingestion_security import (
    canonicalize_financial_ingest_frontmatter,
)


def test_ingestion_always_creates_unverified_review_candidate():
    payload = {
        "id": "note-1",
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "verification": "partially_verified",
        "category": "financial",
    }

    safe = canonicalize_financial_ingest_frontmatter(payload)

    assert safe["lifecycle"] == "REVIEW"
    assert safe["verification"] == "unverified"
    assert payload["verification"] == "partially_verified"


@pytest.mark.parametrize("lifecycle", [
    "ACTIVE",
    "VERIFIED",
    "ARCHIVED",
    "SUPERSEDED",
    "RECONSOLIDATING",
    "active",
    "verified",
])
def test_ingestion_rejects_privileged_lifecycle_injection(lifecycle):
    payload = {"lifecycle": lifecycle, "verification": "unverified"}

    with pytest.raises(ValueError) as exc:
        canonicalize_financial_ingest_frontmatter(payload)

    assert lifecycle.upper() in str(exc.value)


@pytest.mark.parametrize("verification", ["verified", "VERIFIED", " Verified "])
def test_ingestion_rejects_verified_injection_case_insensitively(verification):
    with pytest.raises(ValueError, match="verified"):
        canonicalize_financial_ingest_frontmatter(
            {"lifecycle": "REVIEW", "verification": verification}
        )


def test_ingestion_normalizes_benign_unverified_states():
    for verification in ("unverified", "partially_verified", "UNVERIFIED"):
        safe = canonicalize_financial_ingest_frontmatter(
            {"lifecycle": "review", "verification": verification}
        )
        assert safe["lifecycle"] == "REVIEW"
        assert safe["verification"] == "unverified"


def test_ingestion_uses_a_deep_copy_before_normalization():
    payload = {
        "lifecycle": "REVIEW",
        "verification": "partially_verified",
        "provenance": {"source_ref": "trusted-test"},
    }

    safe = canonicalize_financial_ingest_frontmatter(payload)
    safe["provenance"]["source_ref"] = "mutated-after-check"

    assert payload["provenance"]["source_ref"] == "trusted-test"
