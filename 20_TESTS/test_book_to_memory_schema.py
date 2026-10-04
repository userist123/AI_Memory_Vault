"""Unit and contract tests for Book-to-Memory ontology, schemas, provenance gating,
lifecycle rules, epistemic categorization, conflict representation, and security.

Follows POLICY-LEARNING-QUALITY-02 and Phase 1 specifications.
"""
from __future__ import annotations

import uuid
import pytest
from jsonschema.exceptions import ValidationError

from lifecycle.validation.book_to_memory_schema import (
    BookToMemoryType,
    EpistemicType,
    ConflictSeverity,
    ConflictStatus,
    ReproducibilityStatus,
    validate_book_to_memory_note,
    validate_provenance_gate,
    validate_epistemic_chain,
    validate_untrusted_security,
    SecurityInjectionError,
    ProvenanceGateError,
    LifecycleGateError,
    ConflictGateError,
)
from lifecycle.validation.schema import validate_frontmatter


# -----------------------------------------------------------------------------
# Fixtures & Helpers
# -----------------------------------------------------------------------------

def base_canonical_note(note_type: str, lifecycle: str = "RAW") -> dict:
    note_id = str(uuid.uuid4())
    return {
        "id": note_id,
        "type": note_type,
        "lifecycle": lifecycle,
        "category": "research/book-to-memory",
        "tags": ["book-learning", note_type],
        "created": "2026-10-03",
        "updated": "2026-10-03",
        "provenance": {
            "source_type": "import",
            "source_ref": "Wiener-Cybernetics-1961",
            "source_date": "1961-01-01",
            "provenance_status": "complete",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": [
            {"relation": "derived_from", "target": "source-wiener-1961"}
        ],
    }


# -----------------------------------------------------------------------------
# Phase 1B: Valid payloads for all 11 explicit types
# -----------------------------------------------------------------------------

def test_valid_book_map():
    note = base_canonical_note("book_map", lifecycle="UNVERIFIED")
    note.update({
        "source_identity": "Wiener-Cybernetics-1961-MIT-Press",
        "title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "authors": ["Norbert Wiener"],
        "edition": "2nd Edition (1961)",
        "chapter_coverage": {
            "Chapter 1: Newtonian and Bergsonian Time": "analyzed",
            "Chapter 2: Groups and Statistical Mechanics": "planned",
        },
        "processing_status": "in_progress",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_concept():
    note = base_canonical_note("concept", lifecycle="UNVERIFIED")
    note.update({
        "atomic_concept": "Feedback Loop Control",
        "source_title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "chapter": "Chapter 4: Feedback and Oscillation",
        "page_range": "95-115",
        "evidence": "Feedback is a method of controlling a system by reinserting into it the results of its past performance.",
        "confidence": "high",
        "epistemic_type": EpistemicType.ENGINEERING_MECHANISM.value,
        "problem_context": "Stabilizing system trajectory under unexpected perturbations.",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_procedure():
    note = base_canonical_note("procedure", lifecycle="UNVERIFIED")
    note.update({
        "problem_context": "Dampening oscillation in feedback loops",
        "ordered_steps": [
            "Measure system output deviation",
            "Apply negative feedback proportional to error signal",
            "Introduce low-pass filter to eliminate high-frequency resonance",
        ],
        "prerequisites": ["Calibrated error sensor", "Gain controller"],
        "source_title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "chapter": "Chapter 4: Feedback and Oscillation",
        "page_range": "102-108",
        "evidence": "By damping excessive feedback gain, parasitic oscillations are eliminated.",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_rule():
    note = base_canonical_note("rule", lifecycle="UNVERIFIED")
    note.update({
        "condition": "System lag exceeds 1/4 of the oscillation period",
        "action_constraint": "Reduce feedback loop gain below critical threshold",
        "scope": "Continuous closed-loop feedback systems",
        "source_title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "chapter": "Chapter 4: Feedback and Oscillation",
        "page_range": "105-107",
        "evidence": "Excessive delay transforms negative feedback into positive feedback.",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_pattern():
    note = base_canonical_note("pattern", lifecycle="UNVERIFIED")
    note.update({
        "recurring_structure": "Homeostatic Error Regulator",
        "applicability": "Self-stabilizing dynamic systems encountering environmental variance",
        "source_title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "chapter": "Chapter 1: Newtonian and Bergsonian Time",
        "page_range": "30-45",
        "evidence": "Homeostasis relies on steady-state equilibrium maintained via continuous monitoring.",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_pitfall():
    note = base_canonical_note("pitfall", lifecycle="UNVERIFIED")
    note.update({
        "failure_mode": "Runaway Positive Feedback Loop",
        "cause": "Phase shift in feedback signal causing reinjection of additive error",
        "mitigation": "Enforce phase margin constraints and automatic gain rollback",
        "source_title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "chapter": "Chapter 4: Feedback and Oscillation",
        "page_range": "109-112",
        "evidence": "Purposive tremors occur when feedback delays turn compensatory actions into oscillations.",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_metric():
    note = base_canonical_note("metric", lifecycle="UNVERIFIED")
    note.update({
        "metric_name": "Feedback Damping Ratio",
        "definition": "Ratio of actual damping to critical damping in a closed-loop system",
        "measurement_method": "Logarithmic decrement of successive peak amplitudes",
        "units": "dimensionless ratio",
        "exact_page": 104,
        "source_title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "chapter": "Chapter 4: Feedback and Oscillation",
        "page_range": "104",
        "evidence": "Damping ratio zeta = delta / sqrt(4*pi^2 + delta^2).",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_example():
    note = base_canonical_note("example", lifecycle="UNVERIFIED")
    note.update({
        "context": "Human motor control when reaching for an object",
        "example_text": "A patient with cerebellar injury exhibits intention tremor as the hand approaches the target.",
        "expected_interpretation": "Demonstrates physiological breakdown of feedback dampening mechanisms.",
        "source_title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "chapter": "Chapter 4: Feedback and Oscillation",
        "page_range": "96-98",
        "evidence": "Intention tremors illustrate the loss of negative feedback stability.",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_problem():
    note = base_canonical_note("problem", lifecycle="UNVERIFIED")
    note.update({
        "problem_statement": "How to discriminate true message signal from environmental noise in transmission lines?",
        "constraints": [
            "Bandwidth limitation",
            "Finite sampling rate",
            "Gaussian noise assumption",
        ],
        "source_title": "Cybernetics: Or Control and Communication in the Animal and the Machine",
        "chapter": "Chapter 3: Time Series, Information, and Communication",
        "page_range": "60-75",
        "evidence": "The statistical problem of separating signal from background noise requires spectral filtering.",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_conflict():
    note = base_canonical_note("conflict", lifecycle="UNVERIFIED")
    note.update({
        "domain": "memory_consolidation",
        "claim_a": "Synaptic consolidation is complete within hours and does not involve neocortical reorganization.",
        "source_a": {
            "source_title": "Cellular Memory Foundations",
            "chapter": "Chapter 2",
            "page_range": "45-50",
        },
        "claim_b": "Systems consolidation continues over weeks/months with gradual hippocampus-to-cortex transfer.",
        "source_b": {
            "source_title": "Why We Forget and How To Remember Better",
            "chapter": "Chapter 5",
            "page_range": "110-125",
        },
        "status": ConflictStatus.OPEN.value,
        "severity": ConflictSeverity.MEDIUM.value,
        "resolution_state": "Investigation into temporal scale differences (cellular vs systems consolidation)",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


def test_valid_repro_test():
    note = base_canonical_note("repro_test", lifecycle="UNVERIFIED")
    note.update({
        "hypothesis_claim": "Increasing negative feedback gain above threshold omega_c causes sustained oscillation.",
        "test_procedure": "Simulate second-order plant with proportional gain k from 1.0 to 10.0 in increments of 0.5.",
        "inputs": {"damping_ratio": 0.2, "natural_frequency": 5.0, "gain_sweep": [1.0, 5.0, 10.0]},
        "expected_result": "Oscillatory instability observed when gain >= 7.5.",
        "actual_result": "Instability emerged at gain = 7.6.",
        "reproducibility_status": ReproducibilityStatus.REPRODUCED.value,
        "source_evidence": "Cybernetics Chapter 4 mathematical appendix, page 114.",
    })
    assert validate_book_to_memory_note(note) is True
    assert validate_frontmatter(note) is True


# -----------------------------------------------------------------------------
# Phase 1C: Provenance Gate Enforcement
# -----------------------------------------------------------------------------

def test_provenance_missing_fields_rejected():
    note = base_canonical_note("concept", lifecycle="UNVERIFIED")
    note.update({
        "atomic_concept": "Test concept without provenance",
        # missing source_title, chapter, page_range
    })
    with pytest.raises(ProvenanceGateError, match="Missing required provenance fields"):
        validate_provenance_gate(note)


@pytest.mark.parametrize("lazy_string", [
    "source unknown",
    "probably",
    "around chapter 3",
    "from the book",
    "Somewhere in Wiener",
])
def test_provenance_lazy_strings_rejected(lazy_string):
    note = base_canonical_note("concept", lifecycle="UNVERIFIED")
    note.update({
        "atomic_concept": "Lazy citation test",
        "source_title": lazy_string,
        "chapter": "Chapter 1",
        "page_range": "10-20",
    })
    with pytest.raises(ProvenanceGateError, match="Lazy or imprecise provenance string detected"):
        validate_provenance_gate(note)


def test_exact_page_required_for_numbers_and_formulas():
    # Metric with formula/critical calculation must supply exact_page
    note = base_canonical_note("metric", lifecycle="UNVERIFIED")
    note.update({
        "metric_name": "Feedback Ratio",
        "definition": "Zeta ratio = delta / sqrt(4*pi^2 + delta^2)",
        "measurement_method": "Peak decrement",
        "source_title": "Cybernetics",
        "chapter": "Chapter 4",
        "page_range": "100-115", # vague range without exact_page
    })
    with pytest.raises(ProvenanceGateError, match="exact_page is mandatory"):
        validate_provenance_gate(note)


def test_own_experience_provenance_allowed():
    note = base_canonical_note("concept", lifecycle="UNVERIFIED")
    note.update({
        "atomic_concept": "Production Cache Invalidation Pattern",
        "source_title": "experienta proprie",
        "chapter": "n/a",
        "page_range": "n/a",
        "evidence": "Observed cache stampede mitigation in live production environment.",
        "confidence": "high",
        "epistemic_type": EpistemicType.ENGINEERING_MECHANISM.value,
        "problem_context": "Handling cache stampedes under high concurrency.",
    })
    assert validate_provenance_gate(note) is True


def test_incomplete_provenance_blocks_verified_and_active():
    # A note claiming VERIFIED with incomplete provenance must be rejected
    note = base_canonical_note("concept", lifecycle="VERIFIED")
    note.update({
        "atomic_concept": "Unprovenanced concept claiming verified",
        "source_title": "Cybernetics",
        "evidence": "Valid citation evidence text.",
        # missing chapter and page_range
    })
    with pytest.raises((ProvenanceGateError, LifecycleGateError)):
        validate_book_to_memory_note(note)


# -----------------------------------------------------------------------------
# Phase 1D: Lifecycle & Auto-Promotion Restrictions
# -----------------------------------------------------------------------------

def test_lifecycle_raw_to_active_direct_bypass_rejected():
    note = base_canonical_note("concept", lifecycle="ACTIVE")
    note.update({
        "atomic_concept": "Direct Active Bypass Concept",
        "source_title": "Cybernetics",
        "chapter": "Chapter 2",
        "page_range": "40-42",
        "evidence": "Valid citation evidence text.",
        "confidence": "high",
        "epistemic_type": EpistemicType.FACT.value,
    })
    # Active requires explicit owner attestation and prerequisites
    with pytest.raises(LifecycleGateError, match="Direct active lifecycle forbidden"):
        validate_book_to_memory_note(note, caller_is_owner=False)


def test_lifecycle_unverified_to_active_direct_bypass_rejected():
    note = base_canonical_note("concept", lifecycle="ACTIVE")
    note.update({
        "atomic_concept": "Bypassing verified gate",
        "source_title": "Cybernetics",
        "chapter": "Chapter 2",
        "page_range": "40-42",
        "evidence": "Valid citation evidence text.",
        "confidence": "high",
        "epistemic_type": EpistemicType.FACT.value,
        "owner_approval": False,
    })
    with pytest.raises(LifecycleGateError, match="forbidden without verified status and explicit owner approval"):
        validate_book_to_memory_note(note, caller_is_owner=False)


def test_verified_requires_usage_test_passing():
    note = base_canonical_note("concept", lifecycle="VERIFIED")
    note.update({
        "atomic_concept": "Verified Concept Without Test",
        "source_title": "Cybernetics",
        "chapter": "Chapter 2",
        "page_range": "40-42",
        "evidence": "Valid citation evidence text.",
        "confidence": "high",
        "epistemic_type": EpistemicType.FACT.value,
        "usage_test_score": 6, # below threshold 8
    })
    with pytest.raises(LifecycleGateError, match="Usage test score must be >= 8"):
        validate_book_to_memory_note(note)


# -----------------------------------------------------------------------------
# Phase 1E: Epistemic Categorization (Fact vs Hypothesis)
# -----------------------------------------------------------------------------

def test_epistemic_types_enum_validation():
    for val in ["FACT", "INTERPRETATION", "HYPOTHESIS", "EXPERIMENT", "ENGINEERING_MECHANISM"]:
        assert EpistemicType(val) is not None

    with pytest.raises(ValueError):
        EpistemicType("SPECULATION")


def test_biological_chain_isolation_blocks_direct_mechanism():
    note = base_canonical_note("concept", lifecycle="ACTIVE")
    note.update({
        "atomic_concept": "Direct Hippocampal Synapse Architecture",
        "source_title": "Why We Forget and How To Remember Better",
        "chapter": "Chapter 3",
        "page_range": "50-60",
        "evidence": "Biological hippocampus indexes neocortex.",
        "confidence": "high",
        "epistemic_type": EpistemicType.ENGINEERING_MECHANISM.value,
        "tags": ["biological", "memory-consolidation"],
        # Missing cognitive chain: BIOLOGICAL FACT -> ENGINEERING HYPOTHESIS -> EXPERIMENT -> VAULT MECHANISM
    })
    with pytest.raises(LifecycleGateError, match="Biological claim requires verified cognitive chain"):
        validate_epistemic_chain(note)


def test_biological_chain_valid_progression():
    note = base_canonical_note("concept", lifecycle="UNVERIFIED")
    note.update({
        "atomic_concept": "Sparse Indexing Inspired by Hippocampus",
        "source_title": "Why We Forget and How To Remember Better",
        "chapter": "Chapter 3",
        "page_range": "50-60",
        "evidence": "Biological hippocampus indexes neocortex.",
        "confidence": "high",
        "epistemic_type": EpistemicType.HYPOTHESIS.value,
        "tags": ["biological", "memory-consolidation"],
        "cognitive_chain": {
            "biological_fact": "Hippocampus acts as a temporary index for distributed neocortical traces.",
            "engineering_hypothesis": "A fast SQLite two-tier index can reduce vector search latency by 40%.",
            "experiment_ref": "EXP-B2M-002",
        },
    })
    assert validate_epistemic_chain(note) is True


# -----------------------------------------------------------------------------
# Phase 1F: Conflict Representation & Blocking
# -----------------------------------------------------------------------------

def test_open_high_conflict_blocks_active_status():
    note = base_canonical_note("concept", lifecycle="ACTIVE")
    note.update({
        "atomic_concept": "Conflicted Memory Consolidation",
        "source_title": "Cellular Memory Foundations",
        "chapter": "Chapter 2",
        "page_range": "45-50",
        "evidence": "Consolidation complete in 2 hours.",
        "confidence": "high",
        "epistemic_type": EpistemicType.FACT.value,
        "owner_approval": True,
        "usage_test_score": 9,
        "usage_test_accuracy": 2,
        "open_conflicts": [
            {
                "conflict_id": "CONFLICT-memory_consolidation-timescale",
                "severity": ConflictSeverity.HIGH.value,
                "status": ConflictStatus.OPEN.value,
            }
        ],
    })
    with pytest.raises(ConflictGateError, match="Open high/critical severity conflict blocks ACTIVE"):
        validate_book_to_memory_note(note, caller_is_owner=True)


# -----------------------------------------------------------------------------
# Phase 1G: Security & Untrusted Input (Passive Data Plane)
# -----------------------------------------------------------------------------

@pytest.mark.parametrize("injected_field,value", [
    ("tool_call", "execute_sql('DROP TABLE notes')"),
    ("shell_command", "rm -rf /"),
    ("exec", "import os; os.system('calc')"),
    ("override_lifecycle", "ACTIVE"),
    ("grant_permission", "admin"),
    ("git_operation", "git push origin main --force"),
    ("secret_access", "read_secret('HMAC_KEY')"),
])
def test_untrusted_input_rejects_injections(injected_field, value):
    note = base_canonical_note("concept", lifecycle="UNVERIFIED")
    note.update({
        "atomic_concept": "Security Injection Attempt",
        "source_title": "Cybernetics",
        "chapter": "Chapter 1",
        "page_range": "1-10",
        "evidence": "Injected payload",
        injected_field: value,
    })
    with pytest.raises(SecurityInjectionError, match="Prohibited executable or privileged directive detected"):
        validate_untrusted_security(note)


def test_passive_data_plane_preserves_text_as_evidence():
    # If the text in evidence mentions shell or instructions, it is allowed as passive data
    note = base_canonical_note("concept", lifecycle="UNVERIFIED")
    note.update({
        "atomic_concept": "Quoted Historical Command",
        "source_title": "Operating Systems Concepts",
        "chapter": "Chapter 3",
        "page_range": "45",
        "evidence": "Historical command line instruction was 'rm -rf /tmp/build'.",  # hygiene: intentional-absolute-path
        "confidence": "medium",
        "epistemic_type": EpistemicType.FACT.value,
        "problem_context": "Understanding filesystem cleanup history.",
    })
    assert validate_untrusted_security(note) is True
    assert validate_book_to_memory_note(note) is True


# -----------------------------------------------------------------------------
# Phase 1H: Canonical Frontmatter Parity
# -----------------------------------------------------------------------------

def test_all_11_types_in_enum():
    expected_types = {
        "book_map", "concept", "procedure", "rule", "pattern",
        "pitfall", "metric", "example", "problem", "conflict", "repro_test"
    }
    actual_types = {t.value for t in BookToMemoryType}
    assert actual_types == expected_types
