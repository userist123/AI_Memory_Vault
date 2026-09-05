"""Focused matrix tests for the financial-ingestion trust boundary."""

import copy

import pytest

from memory_controller.financial_ingestion_security import (
    canonicalize_financial_ingest_frontmatter,
)


@pytest.mark.parametrize(
    "lifecycle",
    ["VERIFIED", "verified", " ACTIVE ", "archived", "SUPERSEDED", "RECONSOLIDATING"],
)
def test_privileged_lifecycle_values_are_rejected_case_insensitively(lifecycle):
    with pytest.raises(ValueError):
        canonicalize_financial_ingest_frontmatter({"lifecycle": lifecycle})


@pytest.mark.parametrize("verification", ["VERIFIED", "verified", " Verified "])
def test_verified_verification_values_are_rejected_case_insensitively(verification):
    with pytest.raises(ValueError):
        canonicalize_financial_ingest_frontmatter({"verification": verification})


def test_missing_trust_fields_are_canonicalized_to_review_unverified():
    result = canonicalize_financial_ingest_frontmatter({"id": "n1"})

    assert result["lifecycle"] == "REVIEW"
    assert result["verification"] == "unverified"


def test_non_privileged_lifecycle_is_downgraded_to_review():
    result = canonicalize_financial_ingest_frontmatter({"lifecycle": "CLASSIFIED"})

    assert result["lifecycle"] == "REVIEW"
    assert result["verification"] == "unverified"


def test_case_insensitive_non_privileged_lifecycle_is_downgraded():
    result = canonicalize_financial_ingest_frontmatter({"lifecycle": "normalized"})

    assert result["lifecycle"] == "REVIEW"
    assert result["verification"] == "unverified"


def test_canonicalization_is_deep_copy_and_does_not_mutate_nested_input():
    source = {
        "lifecycle": "CLASSIFIED",
        "verification": "partially_verified",
        "provenance": {"source_ref": "external"},
        "tags": ["finance"],
    }
    original = copy.deepcopy(source)

    result = canonicalize_financial_ingest_frontmatter(source)
    result["provenance"]["source_ref"] = "changed"
    result["tags"].append("changed")

    assert source == original
    assert result["lifecycle"] == "REVIEW"
    assert result["verification"] == "unverified"


def test_non_dict_frontmatter_is_rejected_before_normalization():
    with pytest.raises(TypeError):
        canonicalize_financial_ingest_frontmatter([])
