"""20_TESTS/regression/test_p5_controller_search_integration.py — P5 Controller Search Integration Regression Suite.

Validates the Phase 5 production wiring of MemoryController.search() to
RetrievalIntegrationAdapter -> ProductionRetrievalFacade -> RetrievalBoundaryAdapter -> HybridRetriever:
1. End-to-end delegation from MemoryController.search() to RetrievalIntegrationAdapter.
2. Principal identity and capability propagation (HUMAN, AI_AGENT, ADMIN).
3. Automatic rejection of non-ACTIVE lifecycles (REVIEW, RAW, RECONSOLIDATING, ARCHIVED, SUPERSEDED).
4. Strict exclusion and neutralization of unverified notes from public search.
5. Cursor-based pagination traversal, expiration, and cryptographic tampering defense.
6. Full backward-compatibility of pack structure and result fields.
7. Cache invalidation on controller mutation methods.
8. Support for custom adapter injection.
"""
from __future__ import annotations

import os
import uuid
from typing import Any, Dict, List

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import (
    InvalidPaginationTokenError,
    Lifecycle,
    MemoryController,
    StorageEngine,
)
from cognitive_core.integration_adapter import RetrievalIntegrationAdapter
from cognitive_core.retrieval_boundary import RetrievalBoundaryAdapter
from cognitive_core.retrieval_facade import ProductionRetrievalFacade
from cognitive_core.vault_index import Note, VaultIndex
from cognitive_core.hybrid_retrieval import HybridRetriever


def _create_note(
    nid: str,
    title: str,
    content: str,
    lifecycle: str = "ACTIVE",
    verification: str = "verified",
    ntype: str = "knowledge",
    source_type: str = "official",
) -> Dict[str, Any]:
    return {
        "id": nid,
        "title": title,
        "content": content,
        "type": ntype,
        "lifecycle": lifecycle,
        "verification": verification,
        "provenance": {
            "source_type": source_type,
            "source_ref": "unit_test",
        },
        "created": "2026-09-01",
        "updated": "2026-09-05",
        "category": "architecture",
        "tags": ["test", "p5"],
    }


@pytest.fixture
def controller_with_notes() -> MemoryController:
    storage = StorageEngine()
    ctrl = MemoryController(storage)

    storage.set("knw-p5-01", _create_note(
        "knw-p5-01",
        "Retrieval Architecture Core",
        "Detailed architecture of the hybrid BM25 and vector retrieval pipeline.",
    ))
    storage.set("knw-p5-02", _create_note(
        "knw-p5-02",
        "Memory Controller Invariants",
        "Operating invariants I-001 through I-012 and I-RETRIEVAL boundary rules.",
    ))
    storage.set("knw-p5-03", _create_note(
        "knw-p5-03",
        "Consensus and Sharding Model",
        "Distributed database consensus, WAL mode transaction safety and replication.",
    ))
    return ctrl


def test_controller_search_delegation_to_adapter(controller_with_notes: MemoryController):
    """Verifies MemoryController.search() delegates cleanly to RetrievalIntegrationAdapter."""
    pack = controller_with_notes.search(Principal.HUMAN, query="retrieval")

    assert pack is not None
    assert pack["requestId"] == "search"
    assert pack["agentId"] == "human"
    assert "results" in pack
    assert len(pack["results"]) >= 1
    hit = pack["results"][0]
    assert hit["id"] == "knw-p5-01"
    assert hit["verification"] == "verified"
    assert hit["lifecycle"] == "ACTIVE"
    assert "signals" in hit
    assert "trace" in pack
    assert "adapter_duration_ms" in pack["trace"]


def test_principal_propagation_human_ai_admin(controller_with_notes: MemoryController):
    """Verifies all authorized principals propagate correctly and unauthorized principals fail."""
    # HUMAN
    pack_h = controller_with_notes.search(Principal.HUMAN, query="invariants")
    assert pack_h["agentId"] == "human"
    assert len(pack_h["results"]) >= 1

    # AI_AGENT
    pack_ai = controller_with_notes.search(Principal.AI_AGENT, query="invariants")
    assert pack_ai["agentId"] == "ai_agent"
    assert len(pack_ai["results"]) >= 1

    # ADMIN
    pack_adm = controller_with_notes.search(Principal.ADMIN, query="invariants")
    assert pack_adm["agentId"] == "admin"
    assert len(pack_adm["results"]) >= 1

    class FakePrincipal:
        value = "untrusted_intruder"

    with pytest.raises(PermissionError):
        controller_with_notes.search(FakePrincipal, query="invariants")


def test_rejection_of_non_active_lifecycles():
    """Verifies non-ACTIVE notes (REVIEW, RAW, RECONSOLIDATING, ARCHIVED, SUPERSEDED) are never returned."""
    storage = StorageEngine()
    ctrl = MemoryController(storage)

    storage.set("knw-active", _create_note("knw-active", "Active Architecture", "Core database specifications.", lifecycle="ACTIVE"))
    storage.set("knw-review", _create_note("knw-review", "Review Architecture", "Core database specifications.", lifecycle="REVIEW"))
    storage.set("knw-raw", _create_note("knw-raw", "Raw Architecture", "Core database specifications.", lifecycle="RAW"))
    storage.set("knw-recon", _create_note("knw-recon", "Reconsolidating Architecture", "Core database specifications.", lifecycle="RECONSOLIDATING"))
    storage.set("knw-arch", _create_note("knw-arch", "Archived Architecture", "Core database specifications.", lifecycle="ARCHIVED"))
    storage.set("knw-super", _create_note("knw-super", "Superseded Architecture", "Core database specifications.", lifecycle="SUPERSEDED"))

    pack = ctrl.search(Principal.HUMAN, query="database")
    returned_ids = [r["id"] for r in pack["results"]]
    assert "knw-active" in returned_ids
    assert "knw-review" not in returned_ids
    assert "knw-raw" not in returned_ids
    assert "knw-recon" not in returned_ids
    assert "knw-arch" not in returned_ids
    assert "knw-super" not in returned_ids
    assert len(returned_ids) == 1

    with pytest.raises(PermissionError, match="Security Boundary Violation"):
        ctrl.search(Principal.HUMAN, query="database", lifecycles=[Lifecycle.REVIEW])

    with pytest.raises(PermissionError, match="Security Boundary Violation"):
        ctrl.search(Principal.HUMAN, query="database", lifecycles=[Lifecycle.RAW])


def test_rejection_of_unverified_notes():
    """Verifies unverified notes are strictly excluded and cannot be retrieved."""
    storage = StorageEngine()
    ctrl = MemoryController(storage)

    storage.set("knw-ver", _create_note("knw-ver", "Verified Note", "Quantum computing cryptographic keys.", verification="verified"))
    storage.set("knw-unver", _create_note("knw-unver", "Unverified Note", "Quantum computing cryptographic keys.", verification="unverified"))
    storage.set("knw-ai-gen", _create_note("knw-ai-gen", "AI Generated Note", "Quantum computing cryptographic keys.", verification="ai_generated"))

    pack = ctrl.search(Principal.HUMAN, query="Quantum")
    returned_ids = [r["id"] for r in pack["results"]]

    assert "knw-ver" in returned_ids
    assert "knw-unver" not in returned_ids
    assert "knw-ai-gen" not in returned_ids
    assert len(returned_ids) == 1


def test_pagination_traversal_and_tampering_defense():
    """Verifies pagination traversal and robust HMAC defense against token tampering."""
    storage = StorageEngine()
    ctrl = MemoryController(storage)

    for i in range(15):
        storage.set(f"knw-page-{i}", _create_note(
            f"knw-page-{i}",
            f"Distributed Architecture System Note {i}",
            f"Detailed content regarding cluster consensus and distributed node protocol {i}.",
        ))

    p1 = ctrl.search(Principal.HUMAN, query="distributed", page_size=5)
    assert len(p1["results"]) == 5
    token1 = p1.get("next_page_token")
    assert token1 is not None

    p2 = ctrl.search(Principal.HUMAN, query="distributed", page_size=5, page_token=token1)
    assert len(p2["results"]) == 5
    token2 = p2.get("next_page_token")
    assert token2 is not None

    ids1 = {r["id"] for r in p1["results"]}
    ids2 = {r["id"] for r in p2["results"]}
    assert ids1.isdisjoint(ids2)

    p3 = ctrl.search(Principal.HUMAN, query="distributed", page_size=5, page_token=token2)
    assert len(p3["results"]) == 5
    assert p3.get("next_page_token") is None

    tampered_token = token1[:-4] + ("AAAA" if token1[-4:] != "AAAA" else "BBBB")
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.HUMAN, query="distributed", page_size=5, page_token=tampered_token)

    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, query="distributed", page_size=5, page_token=token1)

    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.HUMAN, query="completely_different_query", page_size=5, page_token=token1)

    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.HUMAN, query="distributed", page_size=5, page_token="not-a-token!!")


def test_backward_compatibility_result_structure(controller_with_notes: MemoryController):
    """Verifies complete backward compatibility of the returned dictionary and hit objects."""
    pack = controller_with_notes.search(Principal.HUMAN, query="Architecture", page_size=2)

    assert "requestId" in pack
    assert "agentId" in pack
    assert "budget" in pack
    assert "disclosureLevel" in pack
    assert "results" in pack
    assert "next_page_token" in pack
    assert "total_hits" in pack
    assert "trace" in pack

    for hit in pack["results"]:
        assert "id" in hit
        assert "title" in hit
        assert "score" in hit
        assert "lifecycle" in hit
        assert "verification" in hit
        assert "type" in hit
        assert "summary" in hit
        assert "content" in hit
        assert "citation" in hit
        assert "signals" in hit


def test_cache_invalidation_on_controller_mutations():
    """Verifies that mutations immediately invalidate cached retrieval adapter so new notes are discovered."""
    storage = StorageEngine()
    ctrl = MemoryController(storage)

    storage.set("knw-init", _create_note("knw-init", "Initial Note", "Initial architectural specification."))

    p1 = ctrl.search(Principal.HUMAN, query="Initial")
    assert len(p1["results"]) == 1

    new_id = str(uuid.uuid4())
    new_note_payload = {
        "id": new_id,
        "content": "# Fresh Mutation Note\nFresh mutation architectural specification.",
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "verification": "unverified",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "category": "architecture",
        "tags": ["fresh"],
    }
    ctrl.propose(Principal.HUMAN, new_note_payload)
    ctrl.attest(Principal.HUMAN, new_id, verification_reason="Audited and confirmed", evidence_reference="AUDIT-001")
    ctrl.promote(Principal.HUMAN, new_id)

    p2 = ctrl.search(Principal.HUMAN, query="Fresh")
    assert len(p2["results"]) == 1
    assert p2["results"][0]["id"] == new_id


def test_custom_retrieval_adapter_injection():
    """Verifies that an explicitly injected RetrievalIntegrationAdapter is respected."""
    note = Note(
        id="knw-injected",
        path=os.path.abspath("virtual/knw-injected.md"),
        title="Injected Note Title",
        body="Content inside explicitly injected custom adapter.",
        meta={"type": "knowledge", "lifecycle": "ACTIVE", "verification": "verified"},
    )
    vault = VaultIndex([note])
    retriever = HybridRetriever(vault)
    boundary = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=boundary)
    custom_adapter = RetrievalIntegrationAdapter(facade=facade)

    storage = StorageEngine()
    ctrl = MemoryController(storage, retrieval_adapter=custom_adapter)

    pack = ctrl.search(Principal.HUMAN, query="injected")
    assert len(pack["results"]) == 1
    assert pack["results"][0]["id"] == "knw-injected"
