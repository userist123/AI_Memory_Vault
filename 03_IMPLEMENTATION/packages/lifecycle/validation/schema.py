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
            "lesson", "preference", "resource", "hypothesis", "system", "core", "index",
            "book_map", "concept", "rule", "pattern", "pitfall", "metric", "example",
            "problem", "conflict", "repro_test"
        ]},
        "lifecycle": {"type": "string", "enum": [
            "RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED", "UNVERIFIED"
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
        },
        # Optional domain properties for Book-to-Memory ontology
        "source_identity": {"type": "string"},
        "title": {"type": "string"},
        "authors": {
            "oneOf": [
                {"type": "array", "items": {"type": "string"}},
                {"type": "string"}
            ]
        },
        "edition": {"type": "string"},
        "chapter_coverage": {"oneOf": [{"type": "object"}, {"type": "array"}]},
        "processing_status": {"type": "string"},
        "atomic_concept": {"type": "string"},
        "source_title": {"type": "string"},
        "chapter": {"type": "string"},
        "page_range": {"type": "string"},
        "exact_page": {"oneOf": [{"type": "integer"}, {"type": "string"}]},
        "evidence": {"type": "string"},
        "epistemic_type": {"type": "string"},
        "cognitive_chain": {"type": "object"},
        "problem_context": {"type": "string"},
        "ordered_steps": {"type": "array", "items": {"type": "string"}},
        "prerequisites": {"type": "array", "items": {"type": "string"}},
        "condition": {"type": "string"},
        "action_constraint": {"type": "string"},
        "scope": {"type": "string"},
        "recurring_structure": {"type": "string"},
        "applicability": {"type": "string"},
        "failure_mode": {"type": "string"},
        "cause": {"type": "string"},
        "mitigation": {"type": "string"},
        "metric_name": {"type": "string"},
        "definition": {"type": "string"},
        "measurement_method": {"type": "string"},
        "units": {"type": "string"},
        "context": {"type": "string"},
        "example_text": {"type": "string"},
        "expected_interpretation": {"type": "string"},
        "problem_statement": {"type": "string"},
        "constraints": {"oneOf": [{"type": "array", "items": {"type": "string"}}, {"type": "string"}]},
        "domain": {"type": "string"},
        "claim_a": {"type": "string"},
        "source_a": {"oneOf": [{"type": "string"}, {"type": "object"}]},
        "claim_b": {"type": "string"},
        "source_b": {"oneOf": [{"type": "string"}, {"type": "object"}]},
        "status": {"type": "string"},
        "severity": {"type": "string"},
        "resolution_state": {"type": "string"},
        "hypothesis_claim": {"type": "string"},
        "test_procedure": {"type": "string"},
        "inputs": {"oneOf": [{"type": "object"}, {"type": "string"}]},
        "expected_result": {"type": "string"},
        "actual_result": {"type": "string"},
        "reproducibility_status": {"type": "string"},
        "source_evidence": {"type": "string"},
        "usage_test_score": {"type": "number"},
        "usage_test_accuracy": {"type": "number"},
        "owner_approval": {"type": "boolean"},
        "open_conflicts": {"type": "array"}
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
