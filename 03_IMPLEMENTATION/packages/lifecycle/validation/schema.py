# schema.py
"""Canonical front‑matter validation.
Implements `validate_frontmatter` using the JSON Schema derived from
`99_SYSTEM/Canonical_Frontmatter.md`. The schema captures all required
fields, enum constraints and format checks required by the Vault.
"""

import json
from jsonschema import Draft7Validator, FormatChecker
from jsonschema.exceptions import ValidationError

# JSON Schema derived from the canonical front‑matter specification.
_CANONICAL_SCHEMA = {
    "type": "object",
    "required": ["id", "type", "lifecycle", "category", "tags", "created", "updated", "provenance", "confidence", "verification", "relations"],
    "properties": {
        "id": {"type": "string", "format": "uuid"},
        "type": {"type": "string", "enum": [
            "knowledge", "project", "procedure", "decision", "experience", "error",
            "lesson", "preference", "resource", "hypothesis", "system", "core", "index"
        ]},
        "lifecycle": {"type": "string", "enum": [
            "RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED"
        ]},
        "category": {"type": "string"},
        "tags": {"type": "array", "items": {"type": "string"}},
        "created": {"type": "string", "format": "date"},
        "updated": {"type": "string", "format": "date"},
        "provenance": {
            "type": "object",
            "required": ["source_type", "source_ref"],
            "properties": {
                "source_type": {"type": "string", "enum": ["user", "official", "execution", "experience", "ai", "inference", "import", "unknown"]},
                "source_ref": {"type": "string"},
                "source_date": {"type": "string", "format": "date"},
                "original_path": {"type": "string"},
                "extraction_date": {"type": "string", "format": "date"},
                "redaction": {"type": "string", "enum": ["none", "applied", "not_applicable"]},
                "provenance_status": {"type": "string", "enum": ["complete", "incomplete"]}
            },
            "additionalProperties": False
        },
        "confidence": {"type": "string", "enum": ["very_high", "high", "medium", "low", "unknown"]},
        "verification": {"type": "string", "enum": ["verified", "partially_verified", "unverified", "inferred"]},
        "valid_from": {"type": "string", "format": "date"},
        "valid_until": {"type": "string", "format": "date"},
        "version_range": {"type": "string"},
        "applies_to": {"type": "string"},
        "supersedes": {"type": "string", "format": "uuid"},
        "superseded_by": {"type": "string", "format": "uuid"},
        "conflicts_with": {"type": "string", "format": "uuid"},
        "last_verified": {"type": "string", "format": "date"},
        "verification_source": {"type": "string"},
        "relations": {
            "type": "array",
            # Two accepted shapes, because until now the schema and the graph
            # reader were mutually exclusive and the schema won every time:
            #
            #   this schema required   relation + target, forbade `type`
            #   SynapseStore reads     type + target_id (synapse_store.py:234)
            #
            # so a note that validated could not produce an edge, and a note
            # that produced an edge could not validate. Every promoted note
            # was an island by construction, not by mistake. Measured on
            # Promoted_reservoir_sampling.md: 0 neighbours while passing the
            # validator.
            #
            # `target_id` also carried format: uuid, which forbids linking to
            # an ontology slot at all — slot ids are `slot-06-procedures`, not
            # UUIDs. That constraint is dropped rather than worked around.
            #
            # This is additive: every note that validated before still
            # validates. The graph shape is now merely also allowed.
            "items": {
                "type": "object",
                "anyOf": [
                    {"required": ["relation", "target"]},
                    {"required": ["type", "target_id"]},
                ],
                "properties": {
                    "relation": {"type": "string"},
                    "target": {"type": "string"},
                    "type": {"type": "string"},
                    "target_id": {"type": "string"}
                },
                "additionalProperties": False
            }
        }
    },
    "additionalProperties": False
}

def validate_frontmatter(data):
    """Validate a note's front‑matter against the canonical schema.
    Returns True if validation passes; raises jsonschema.ValidationError otherwise.
    """
    validator = Draft7Validator(_CANONICAL_SCHEMA, format_checker=FormatChecker())
    validator.validate(data)
    return True
