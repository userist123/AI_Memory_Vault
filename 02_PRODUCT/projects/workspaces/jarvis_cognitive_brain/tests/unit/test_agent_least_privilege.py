"""
Milestone 3 Unit Tests: Invariant Attack Simulations & Least-Privilege Role Boundaries (P0-P18).
"""

import pytest
import uuid
from typing import Dict, Any

from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.agents import (
    AgentRole,
    ScopedStorageProxy,
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent,
)


def test_invariant_p0_router_cannot_mutate_memory(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Asserts RouterAgent is strictly read/search only and raises PermissionError on mutation attempts."""
    router = RouterAgent(storage=sqlite_storage, llm=mock_llm)

    test_note = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "core",
        "provenance": {"source_type": "ai", "source_ref": "attack_test"},
    }

    with pytest.raises(PermissionError, match="RBAC Violation"):
        router.storage.propose(test_note)

    with pytest.raises(PermissionError, match="RBAC Violation"):
        router.storage.update(str(uuid.uuid4()), {"content": "tampered"})

    with pytest.raises(PermissionError, match="RBAC Violation"):
        router.storage.archive(str(uuid.uuid4()))

    with pytest.raises(PermissionError, match="RBAC Violation"):
        router.storage.delete(str(uuid.uuid4()))


def test_invariant_p0_retrieval_cannot_mutate_memory(sqlite_storage: SQLiteStorageEngine):
    """Asserts RetrievalAgent cannot mutate memory or escalate privileges."""
    retrieval = RetrievalAgent(storage=sqlite_storage)

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.propose({"id": str(uuid.uuid4()), "type": "knowledge"})

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.update(str(uuid.uuid4()), {"content": "tampered"})

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.attest(str(uuid.uuid4()), "fake attestation")

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.delete(str(uuid.uuid4()))


def test_invariant_p0_verifier_cannot_attest_or_promote(sqlite_storage: SQLiteStorageEngine):
    """Asserts VerifierAgent is read-only and cannot attest or promote notes."""
    verifier = VerifierAgent(storage=sqlite_storage)

    with pytest.raises(PermissionError, match="RBAC Violation"):
        verifier.storage.propose({"id": str(uuid.uuid4()), "type": "knowledge"})

    with pytest.raises(PermissionError, match="RBAC Violation"):
        verifier.storage.attest(str(uuid.uuid4()), "self attestation")

    with pytest.raises(PermissionError, match="RBAC Violation"):
        verifier.storage.promote(str(uuid.uuid4()))


def test_invariant_p0_consolidator_cannot_propose_active_lifecycle(sqlite_storage: SQLiteStorageEngine):
    """Asserts ConsolidatorAgent cannot bypass creation lifecycle and propose directly into ACTIVE (P0-004)."""
    consolidator = ConsolidatorAgent(storage=sqlite_storage)

    invalid_active_proposal = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "core",
        "created": "2026-08-28",
        "updated": "2026-08-28",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "confidence": "medium",
        "verification": "unverified",
        "relations": [],
        "content": "Attempting illegal direct ACTIVE lifecycle proposal.",
    }

    with pytest.raises(ValueError, match="cannot set lifecycle to 'ACTIVE' at creation"):
        consolidator.storage.propose(invalid_active_proposal)


def test_invariant_p0_consolidator_cannot_claim_privileged_provenance(sqlite_storage: SQLiteStorageEngine):
    """Asserts ConsolidatorAgent cannot claim privileged source_type (user/official/experience) (P0-002)."""
    consolidator = ConsolidatorAgent(storage=sqlite_storage)

    for forbidden_source in ["user", "official", "experience", "import"]:
        forbidden_note = {
            "id": str(uuid.uuid4()),
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "core",
            "created": "2026-08-28",
            "updated": "2026-08-28",
            "provenance": {"source_type": forbidden_source, "source_ref": "spoofed"},
            "confidence": "high",
            "verification": "unverified",
            "relations": [],
            "content": "Spoofed provenance claim.",
        }
        with pytest.raises(ValueError, match="not permitted to claim provenance source_type"):
            consolidator.storage.propose(forbidden_note)


def test_invariant_p0_critic_cannot_archive_or_attest(sqlite_storage: SQLiteStorageEngine):
    """Asserts CriticAgent cannot archive, attest, or delete notes."""
    critic = CriticAgent(storage=sqlite_storage)

    with pytest.raises(PermissionError, match="RBAC Violation"):
        critic.storage.archive(str(uuid.uuid4()))

    with pytest.raises(PermissionError, match="RBAC Violation"):
        critic.storage.attest(str(uuid.uuid4()), "critic attestation")

    with pytest.raises(PermissionError, match="RBAC Violation"):
        critic.storage.delete(str(uuid.uuid4()))


def test_invariant_p16_p18_hardware_telemetry_immutability_across_all_workers(sqlite_storage: SQLiteStorageEngine):
    """Asserts immutable hardware telemetry fields cannot be tampered with by AI agents (P16-P18)."""
    consolidator = ConsolidatorAgent(storage=sqlite_storage)

    tampered_hardware_note = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "hardware",
        "created": "2026-08-28",
        "updated": "2026-08-28",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "confidence": "medium",
        "verification": "unverified",
        "relations": [],
        "content": "Hardware test.",
        "hardware_serial": "TAMPERED_SERIAL_12345",
    }

    with pytest.raises(PermissionError, match="Hardware telemetry field 'hardware_serial' is strictly read-only"):
        consolidator.storage.propose(tampered_hardware_note)
