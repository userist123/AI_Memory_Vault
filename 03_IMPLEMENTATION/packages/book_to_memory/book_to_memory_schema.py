"""Book-to-Memory ontology schemas and policy gate validator.

Implements strict validation for the 11 Book-to-Memory types according to:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md
- 00_GOVERNANCE/protocols/No_Fabrication_Policy.md
- 00_GOVERNANCE/protocols/Memory_Protocol.md
- 00_GOVERNANCE/protocols/Confidence_Model.md
- 03_IMPLEMENTATION/packages/lifecycle/policy.py
"""
from __future__ import annotations

import re
from enum import Enum
from typing import Any, Dict, List, Optional
from jsonschema import Draft7Validator, FormatChecker
from jsonschema.exceptions import ValidationError


# -----------------------------------------------------------------------------
# Enums
# -----------------------------------------------------------------------------

class BookToMemoryType(str, Enum):
    """The 11 canonical note types for the Book-to-Memory research track."""
    BOOK_MAP = "book_map"
    CONCEPT = "concept"
    PROCEDURE = "procedure"
    RULE = "rule"
    PATTERN = "pattern"
    PITFALL = "pitfall"
    METRIC = "metric"
    EXAMPLE = "example"
    PROBLEM = "problem"
    CONFLICT = "conflict"
    REPRO_TEST = "repro_test"


class EpistemicType(str, Enum):
    """Epistemic category ensuring separation of empirical evidence and hypotheses."""
    FACT = "FACT"
    INTERPRETATION = "INTERPRETATION"
    HYPOTHESIS = "HYPOTHESIS"
    EXPERIMENT = "EXPERIMENT"
    ENGINEERING_MECHANISM = "ENGINEERING_MECHANISM"


class ConflictSeverity(str, Enum):
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"
    CRITICAL = "critical"


class ConflictStatus(str, Enum):
    OPEN = "open"
    RESOLVED = "resolved"
    INVESTIGATING = "investigating"


class ReproducibilityStatus(str, Enum):
    REPRODUCED = "reproduced"
    FAILED = "failed"
    PENDING = "pending"


# -----------------------------------------------------------------------------
# Error Classes
# -----------------------------------------------------------------------------

class BookToMemoryValidationError(ValueError):
    """Base error for Book-to-Memory schema and policy gate violations."""


class ProvenanceGateError(BookToMemoryValidationError):
    """Raised when provenance fails completeness or quality criteria."""


class LifecycleGateError(BookToMemoryValidationError):
    """Raised when lifecycle progression violates promotion or approval rules."""


class ConflictGateError(BookToMemoryValidationError):
    """Raised when an open severe conflict blocks state progression."""


class SecurityInjectionError(BookToMemoryValidationError):
    """Raised when an untrusted input attempts instruction or tool injection."""


# -----------------------------------------------------------------------------
# Prohibited Injection Directives (Security Gate)
# -----------------------------------------------------------------------------

_PROHIBITED_SECURITY_KEYS = frozenset({
    "tool_call",
    "shell_command",
    "exec",
    "override_lifecycle",
    "grant_permission",
    "git_operation",
    "secret_access",
    "bash",
    "cmd",
    "eval",
    "system_call",
    "sudo",
    "force_active",
})

_LAZY_PROVENANCE_PATTERNS = [
    re.compile(r"\bsource\s+unknown\b", re.IGNORECASE),
    re.compile(r"\bprobably\b", re.IGNORECASE),
    re.compile(r"\baround\s+chapter\b", re.IGNORECASE),
    re.compile(r"\bfrom\s+the\s+book\b", re.IGNORECASE),
    re.compile(r"\bsomewhere\s+in\b", re.IGNORECASE),
]


# -----------------------------------------------------------------------------
# Specific Schemas for Book-to-Memory Note Types
# -----------------------------------------------------------------------------

_TYPE_SCHEMAS: Dict[str, Dict[str, Any]] = {
    BookToMemoryType.BOOK_MAP.value: {
        "type": "object",
        "required": ["source_identity", "title", "authors", "chapter_coverage", "processing_status"],
        "properties": {
            "source_identity": {"type": "string", "minLength": 3},
            "title": {"type": "string", "minLength": 3},
            "authors": {
                "oneOf": [
                    {"type": "array", "items": {"type": "string"}, "minItems": 1},
                    {"type": "string", "minLength": 2},
                ]
            },
            "edition": {"type": "string"},
            "chapter_coverage": {
                "oneOf": [
                    {"type": "object"},
                    {"type": "array", "items": {"type": "string"}},
                ]
            },
            "processing_status": {"type": "string", "enum": ["planned", "in_progress", "completed", "paused"]},
        },
    },
    BookToMemoryType.CONCEPT.value: {
        "type": "object",
        "required": ["atomic_concept", "evidence"],
        "properties": {
            "atomic_concept": {"type": "string", "minLength": 3},
            "evidence": {"type": "string", "minLength": 5},
            "confidence": {"type": "string"},
            "epistemic_type": {"type": "string", "enum": [e.value for e in EpistemicType]},
            "problem_context": {"type": "string"},
            "cognitive_chain": {"type": "object"},
        },
    },
    BookToMemoryType.PROCEDURE.value: {
        "type": "object",
        "required": ["problem_context", "ordered_steps", "evidence"],
        "properties": {
            "problem_context": {"type": "string", "minLength": 5},
            "ordered_steps": {"type": "array", "items": {"type": "string"}, "minItems": 1},
            "prerequisites": {"type": "array", "items": {"type": "string"}},
            "evidence": {"type": "string", "minLength": 5},
        },
    },
    BookToMemoryType.RULE.value: {
        "type": "object",
        "required": ["condition", "action_constraint", "evidence"],
        "properties": {
            "condition": {"type": "string", "minLength": 3},
            "action_constraint": {"type": "string", "minLength": 3},
            "scope": {"type": "string"},
            "evidence": {"type": "string", "minLength": 5},
        },
    },
    BookToMemoryType.PATTERN.value: {
        "type": "object",
        "required": ["recurring_structure", "applicability", "evidence"],
        "properties": {
            "recurring_structure": {"type": "string", "minLength": 3},
            "applicability": {"type": "string", "minLength": 3},
            "evidence": {"type": "string", "minLength": 5},
        },
    },
    BookToMemoryType.PITFALL.value: {
        "type": "object",
        "required": ["failure_mode", "cause", "mitigation", "evidence"],
        "properties": {
            "failure_mode": {"type": "string", "minLength": 3},
            "cause": {"type": "string", "minLength": 3},
            "mitigation": {"type": "string", "minLength": 3},
            "evidence": {"type": "string", "minLength": 5},
        },
    },
    BookToMemoryType.METRIC.value: {
        "type": "object",
        "required": ["metric_name", "definition", "measurement_method", "evidence"],
        "properties": {
            "metric_name": {"type": "string", "minLength": 2},
            "definition": {"type": "string", "minLength": 5},
            "measurement_method": {"type": "string", "minLength": 5},
            "units": {"type": "string"},
            "exact_page": {"oneOf": [{"type": "integer"}, {"type": "string"}]},
            "evidence": {"type": "string", "minLength": 5},
        },
    },
    BookToMemoryType.EXAMPLE.value: {
        "type": "object",
        "required": ["context", "example_text", "expected_interpretation", "evidence"],
        "properties": {
            "context": {"type": "string", "minLength": 3},
            "example_text": {"type": "string", "minLength": 5},
            "expected_interpretation": {"type": "string", "minLength": 5},
            "evidence": {"type": "string", "minLength": 5},
        },
    },
    BookToMemoryType.PROBLEM.value: {
        "type": "object",
        "required": ["problem_statement", "constraints", "evidence"],
        "properties": {
            "problem_statement": {"type": "string", "minLength": 5},
            "constraints": {
                "oneOf": [
                    {"type": "array", "items": {"type": "string"}, "minItems": 1},
                    {"type": "string", "minLength": 3},
                ]
            },
            "evidence": {"type": "string", "minLength": 5},
        },
    },
    BookToMemoryType.CONFLICT.value: {
        "type": "object",
        "required": ["domain", "claim_a", "source_a", "claim_b", "source_b", "status", "severity"],
        "properties": {
            "domain": {"type": "string", "minLength": 3},
            "claim_a": {"type": "string", "minLength": 5},
            "source_a": {"oneOf": [{"type": "string"}, {"type": "object"}]},
            "claim_b": {"type": "string", "minLength": 5},
            "source_b": {"oneOf": [{"type": "string"}, {"type": "object"}]},
            "status": {"type": "string", "enum": [s.value for s in ConflictStatus]},
            "severity": {"type": "string", "enum": [s.value for s in ConflictSeverity]},
            "resolution_state": {"type": "string"},
        },
    },
    BookToMemoryType.REPRO_TEST.value: {
        "type": "object",
        "required": ["hypothesis_claim", "test_procedure", "inputs", "expected_result", "reproducibility_status", "source_evidence"],
        "properties": {
            "hypothesis_claim": {"type": "string", "minLength": 5},
            "test_procedure": {"type": "string", "minLength": 5},
            "inputs": {"oneOf": [{"type": "object"}, {"type": "string"}]},
            "expected_result": {"type": "string", "minLength": 3},
            "actual_result": {"type": "string"},
            "reproducibility_status": {"type": "string", "enum": [r.value for r in ReproducibilityStatus]},
            "source_evidence": {"type": "string", "minLength": 5},
        },
    },
}


# -----------------------------------------------------------------------------
# Validation Subroutines
# -----------------------------------------------------------------------------

def validate_untrusted_security(note: Dict[str, Any]) -> bool:
    """Ensure that untrusted book inputs cannot inject execution or control instructions.

    Suspect instructions in evidence text remain inert data.
    """
    for key in note.keys():
        if key in _PROHIBITED_SECURITY_KEYS:
            raise SecurityInjectionError(
                f"Prohibited executable or privileged directive detected in note metadata: '{key}'"
            )

    return True


def validate_provenance_gate(note: Dict[str, Any]) -> bool:
    """Enforce POLICY-LEARNING-QUALITY-02 provenance requirements."""
    note_type = note.get("type")
    if note_type == BookToMemoryType.BOOK_MAP.value:
        # Book maps have source_identity, title, authors validated by type schema
        return True

    if note_type == BookToMemoryType.CONFLICT.value:
        # Conflict notes have source_a and source_b
        for src_key in ("source_a", "source_b"):
            src = note.get(src_key)
            if not src:
                raise ProvenanceGateError(f"Missing required provenance field '{src_key}' in conflict note.")
            if isinstance(src, dict):
                title = src.get("source_title")
                if not title:
                    raise ProvenanceGateError(f"Missing 'source_title' in '{src_key}' of conflict note.")
                for pattern in _LAZY_PROVENANCE_PATTERNS:
                    if pattern.search(str(title)):
                        raise ProvenanceGateError(f"Lazy provenance string detected in '{src_key}': '{title}'.")
        return True

    if note_type == BookToMemoryType.REPRO_TEST.value:
        # Repro tests have source_evidence
        evidence = note.get("source_evidence")
        if not evidence or not str(evidence).strip():
            raise ProvenanceGateError("Missing required provenance field 'source_evidence' in repro_test note.")
        for pattern in _LAZY_PROVENANCE_PATTERNS:
            if pattern.search(str(evidence)):
                raise ProvenanceGateError(f"Lazy provenance string detected in 'source_evidence': '{evidence}'.")
        return True

    source_title = note.get("source_title")
    if not source_title or not isinstance(source_title, str) or not source_title.strip():
        raise ProvenanceGateError("Missing required provenance fields: 'source_title' is mandatory.")

    # Allow documented own experience
    if source_title.strip().lower() in ("experienta proprie", "own experience"):
        return True

    chapter = note.get("chapter")
    page_range = note.get("page_range")

    if not chapter or not isinstance(chapter, str) or not chapter.strip():
        raise ProvenanceGateError("Missing required provenance fields: 'chapter' is mandatory for book sources.")

    if not page_range or not str(page_range).strip():
        raise ProvenanceGateError("Missing required provenance fields: 'page_range' is mandatory for book sources.")

    # Check for lazy strings
    for field_name, value in [("source_title", source_title), ("chapter", chapter), ("page_range", str(page_range))]:
        for pattern in _LAZY_PROVENANCE_PATTERNS:
            if pattern.search(value):
                raise ProvenanceGateError(
                    f"Lazy or imprecise provenance string detected in '{field_name}': '{value}'."
                )

    # Check exact_page requirement for formulas, calculations, numbers, or metrics
    if note_type == BookToMemoryType.METRIC.value:
        definition = note.get("definition", "")
        evidence = note.get("evidence", "")
        has_formula_or_calc = any(c in definition + evidence for c in ["=", "sqrt", "delta", "zeta", "/", "*", "^", "%"])
        if has_formula_or_calc:
            exact_page = note.get("exact_page")
            if exact_page is None or not str(exact_page).strip():
                raise ProvenanceGateError(
                    "exact_page is mandatory for precise formulas, numbers, and critical calculations."
                )

    return True


def validate_epistemic_chain(note: Dict[str, Any]) -> bool:
    """Enforce isolation of biological literature findings from engineering production mechanisms."""
    epistemic_type = note.get("epistemic_type")
    if epistemic_type:
        try:
            EpistemicType(epistemic_type)
        except ValueError:
            raise ValueError(f"Invalid epistemic_type '{epistemic_type}'. Must be one of {[e.value for e in EpistemicType]}")

    tags = [t.lower() for t in note.get("tags", []) if isinstance(t, str)]
    is_biological = any(bio in tags for bio in ["biological", "cognitive", "neuroscience"])

    if is_biological:
        # Check cognitive chain
        cognitive_chain = note.get("cognitive_chain")
        if epistemic_type == EpistemicType.ENGINEERING_MECHANISM.value or note.get("lifecycle") == "ACTIVE":
            if not cognitive_chain or not isinstance(cognitive_chain, dict):
                raise LifecycleGateError(
                    "Biological claim requires verified cognitive chain (BIOLOGICAL FACT -> ENGINEERING HYPOTHESIS -> EXPERIMENT -> VAULT MECHANISM)"
                )
            required_chain_keys = ["biological_fact", "engineering_hypothesis", "experiment_ref"]
            for rk in required_chain_keys:
                if not cognitive_chain.get(rk):
                    raise LifecycleGateError(
                        f"Incomplete cognitive chain: missing '{rk}' for biological claim."
                    )

    return True


def validate_conflict_gate(note: Dict[str, Any]) -> bool:
    """Enforce that open high/critical conflicts block ACTIVE status."""
    lifecycle = note.get("lifecycle", "").upper()
    open_conflicts = note.get("open_conflicts", [])

    if lifecycle == "ACTIVE":
        for conf in open_conflicts:
            status = conf.get("status", "").lower()
            severity = conf.get("severity", "").lower()
            if status == ConflictStatus.OPEN.value and severity in (ConflictSeverity.HIGH.value, ConflictSeverity.CRITICAL.value):
                raise ConflictGateError(
                    f"Open high/critical severity conflict blocks ACTIVE status (conflict: {conf.get('conflict_id')})."
                )

    if note.get("type") == BookToMemoryType.CONFLICT.value:
        status = note.get("status", "").lower()
        severity = note.get("severity", "").lower()
        if lifecycle == "ACTIVE" and status == ConflictStatus.OPEN.value and severity in (ConflictSeverity.HIGH.value, ConflictSeverity.CRITICAL.value):
            raise ConflictGateError("A high/critical open conflict note cannot be in ACTIVE lifecycle.")

    return True


def validate_lifecycle_gate(note: Dict[str, Any], caller_is_owner: bool = False) -> bool:
    """Enforce lifecycle boundaries: no auto-promotion, owner approval for ACTIVE, usage test thresholds."""
    lifecycle = note.get("lifecycle", "").upper()

    if lifecycle == "ACTIVE":
        # Requires explicit owner approval
        has_owner_approval = note.get("owner_approval", False) is True or caller_is_owner is True
        if not has_owner_approval:
            raise LifecycleGateError(
                "Direct active lifecycle forbidden without verified status and explicit owner approval."
            )

    if lifecycle in ("VERIFIED", "ACTIVE"):
        # Must have valid provenance
        validate_provenance_gate(note)

        # Must have passed usage test
        usage_test_score = note.get("usage_test_score")
        if usage_test_score is not None:
            if usage_test_score < 8:
                raise LifecycleGateError(
                    f"Usage test score must be >= 8/10 to reach VERIFIED or ACTIVE (actual: {usage_test_score})."
                )

    return True


def validate_book_to_memory_note(note: Dict[str, Any], caller_is_owner: bool = False) -> bool:
    """Validate a Book-to-Memory note against all schemas and governance gates.

    Returns True if valid; raises an appropriate validation or gate error otherwise.
    """
    # 1. Security Gate (Untrusted input check)
    validate_untrusted_security(note)

    # 2. Type Schema Check
    note_type = note.get("type")
    if note_type not in [t.value for t in BookToMemoryType]:
        raise BookToMemoryValidationError(f"Unknown Book-to-Memory note type: '{note_type}'")

    type_schema = _TYPE_SCHEMAS.get(note_type)
    if type_schema:
        validator = Draft7Validator(type_schema, format_checker=FormatChecker())
        validator.validate(note)

    # 3. Provenance Gate
    validate_provenance_gate(note)

    # 4. Epistemic Chain Gate
    validate_epistemic_chain(note)

    # 5. Conflict Gate
    validate_conflict_gate(note)

    # 6. Lifecycle Gate
    validate_lifecycle_gate(note, caller_is_owner=caller_is_owner)

    return True
