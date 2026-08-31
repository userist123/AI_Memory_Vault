"""
Tier 2 Boundary & Invariants: Cognitive Memory Trust Boundaries (P0-P18).
Enforces AI agent verification restrictions, human attestation requirement,
provenance immutability, and SQL injection sanitization.
"""

import pytest
import uuid
from jarvis.memory.invariants import (
    Principal,
    Lifecycle,
    NoteType,
    ProvenanceModel,
    validate_propose_invariants,
    validate_update_invariants,
    validate_attest_invariants,
)
from jarvis.memory.sqlite_engine import SQLiteStorageEngine


def test_invariant_p0_ai_agent_cannot_self_verify():
    """Test Invariant P0: AI_AGENT is forbidden from proposing notes with verification='verified'."""
    note = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "core",
        "provenance": {"source_type": "execution", "source_ref": "test"},
        "verification": "verified",  # FORBIDDEN FOR AI_AGENT
    }

    with pytest.raises(ValueError, match="cannot be set via propose"):
        validate_propose_invariants(Principal.AI_AGENT, note)


def test_invariant_p1_human_attestation_required(sqlite_storage: SQLiteStorageEngine):
    """Test Invariant P1: Only HUMAN and ADMIN can attest memory notes."""
    note_id = str(uuid.uuid4())
    sqlite_storage.propose(
        Principal.AI_AGENT,
        {
            "id": note_id,
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "core",
            "provenance": {"source_type": "execution", "source_ref": "test"},
            "confidence": "high",
            "verification": "unverified",
            "content": "Proposed memory note",
        },
    )

    # AI_AGENT attempting to attest must fail
    with pytest.raises(PermissionError, match="not allowed to perform attest"):
        sqlite_storage.attest(Principal.AI_AGENT, note_id, reason="Self attest")

    # HUMAN attestation succeeds
    attested = sqlite_storage.attest(Principal.HUMAN, note_id, reason="Human verified and approved")
    assert attested["verification"] == "verified"


def test_invariant_p2_provenance_immutability(sqlite_storage: SQLiteStorageEngine):
    """Test Invariant P2: source_type and source_ref cannot be mutated post-creation."""
    note_id = str(uuid.uuid4())
    sqlite_storage.propose(
        Principal.AI_AGENT,
        {
            "id": note_id,
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "core",
            "provenance": {"source_type": "execution", "source_ref": "test_orig"},
            "confidence": "high",
            "verification": "unverified",
            "content": "Original note",
        },
    )

    # Attempt to change source_type
    with pytest.raises(ValueError, match="provenance.source_type is immutable"):
        sqlite_storage.update(
            Principal.AI_AGENT,
            note_id,
            {"provenance": {"source_type": "official", "source_ref": "test_orig"}},
        )


def test_sql_injection_fuzzing_resilience(sqlite_storage: SQLiteStorageEngine):
    """Test parameterized queries block SQL injection payloads in search and retrieve."""
    malicious_payloads = [
        "' OR 1=1 --",
        "'; DROP TABLE notes; --",
        "UNION SELECT null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null --",
        "\" OR \"\"=\"",
    ]

    for payload in malicious_payloads:
        results = sqlite_storage.search_bm25(payload)
        assert isinstance(results, list)

        count = sqlite_storage.count()
        assert isinstance(count, int)
        assert count >= 0


def test_extreme_content_payload_limits(sqlite_storage: SQLiteStorageEngine):
    """Test storing large 1MB markdown content without database corruption."""
    large_content = "# Massive Stress Test Note\n" + ("Lorem ipsum dolor sit amet. " * 50000)
    note_id = str(uuid.uuid4())

    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": note_id,
            "type": "resource",
            "lifecycle": "ACTIVE",
            "category": "stress",
            "provenance": {"source_type": "execution", "source_ref": "stress_test"},
            "confidence": "medium",
            "verification": "unverified",
            "content": large_content,
        },
    )

    retrieved = sqlite_storage.get(note_id)
    assert retrieved is not None
    assert len(retrieved["content"]) == len(large_content)
