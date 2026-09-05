"""Regression tests for direct financial-ingestion trust-boundary state."""

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


def test_ingestion_rejects_active_lifecycle_injection():
    payload = {"lifecycle": "ACTIVE", "verification": "unverified"}

    try:
        canonicalize_financial_ingest_frontmatter(payload)
    except ValueError as exc:
        assert "ACTIVE" in str(exc)
    else:
        raise AssertionError("ACTIVE lifecycle injection was not rejected")


def test_ingestion_rejects_all_privileged_lifecycle_states():
    for lifecycle in ("ARCHIVED", "SUPERSEDED", "RECONSOLIDATING"):
        try:
            canonicalize_financial_ingest_frontmatter(
                {"lifecycle": lifecycle, "verification": "unverified"}
            )
        except ValueError as exc:
            assert lifecycle in str(exc)
        else:
            raise AssertionError(
                f"{lifecycle} lifecycle injection was not rejected"
            )


def test_ingestion_rejects_verified_injection():
    try:
        canonicalize_financial_ingest_frontmatter(
            {"lifecycle": "REVIEW", "verification": "verified"}
        )
    except ValueError as exc:
        assert "verified" in str(exc)
    else:
        raise AssertionError("verified state injection was not rejected")


def test_ingestion_uses_a_deep_copy_before_normalization():
    payload = {
        "lifecycle": "REVIEW",
        "verification": "partially_verified",
        "provenance": {"source_ref": "trusted-test"},
    }

    safe = canonicalize_financial_ingest_frontmatter(payload)
    safe["provenance"]["source_ref"] = "mutated-after-check"

    assert payload["provenance"]["source_ref"] == "trusted-test"
