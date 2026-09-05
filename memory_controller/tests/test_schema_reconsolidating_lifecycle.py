import pytest

from memory_controller.validation.schema import validate_frontmatter


def _frontmatter(lifecycle):
    return {
        "id": "123e4567-e89b-12d3-a456-426614174000",
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "user", "source_ref": "schema-test"},
        "confidence": "medium",
        "verification": "unverified",
        "relations": [],
    }


def test_reconsolidating_is_a_valid_canonical_lifecycle():
    assert validate_frontmatter(_frontmatter("RECONSOLIDATING")) is True


def test_unknown_lifecycle_remains_rejected():
    with pytest.raises(Exception):
        validate_frontmatter(_frontmatter("NOT_A_LIFECYCLE"))
