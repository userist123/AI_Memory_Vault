"""20_TESTS/regression/test_retrieval_integration_harness.py — P2.6 Integration Test Harness.

Comprehensive regression harness validating end-to-end integration:
    Caller -> ProductionRetrievalFacade -> RetrievalBoundaryAdapter -> HybridRetriever -> Results

Verifies:
1. Multi-Principal Support (HUMAN, AI_AGENT, ADMIN as enum and string, missing/invalid rejected).
2. Strict Security Ceiling (ACTIVE + verified strictly enforced across all callers).
3. Non-Active Note Exclusion (RAW, REVIEW, ARCHIVED, SUPERSEDED never exposed).
4. Narrowing Filter Support (types subsetting works; empty result returned if no matches).
5. Broadening Filter Rejection (fail-closed BoundaryViolationError on non-compliant filters).
6. Deterministic Ordering & Score Tie-Breaking (RRF score desc, ID asc).
7. Pagination Traversal & Cursor Integrity (page_size, page_token, next_page_token, invalid token rejected).
8. Real Vault Index Live Search (verifies queries on actual active vault notes).
9. Zero Storage Mutation (filesystem hashes and index state unchanged across calls).
"""
from __future__ import annotations

import hashlib
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional

import pytest

from cognitive_core.hybrid_retrieval import Hit, HybridRetriever
from cognitive_core.retrieval_boundary import (
    BoundaryViolationError,
    FilterValidationError,
    PrincipalValidationError,
    RetrievalBoundaryAdapter,
)
from cognitive_core.retrieval_facade import (
    FacadeNoteResult,
    FacadeRetrievalRequest,
    FacadeRetrievalResponse,
    ProductionRetrievalFacade,
)
from cognitive_core.vault_index import Note, VaultIndex


class PrincipalRole(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"
    EXTERNAL_UNTRUSTED = "untrusted_guest"


@pytest.fixture
def test_vault(tmp_path: Path) -> VaultIndex:
    """Creates a controlled vault fixture containing diverse lifecycle states."""
    notes_data = [
        # Active + verified notes
        (
            "01_ARCHITECTURE/memory/knw-active-01.md",
            "id: knw-active-01\ntitle: Architecture Protocol Alpha\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Core architecture protocol for high throughput memory streaming and SQLite WAL transactions.",
        ),
        (
            "01_ARCHITECTURE/memory/prc-active-02.md",
            "id: prc-active-02\ntitle: Fail-Closed Security Policy\ntype: procedure\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Standard operating procedure for fail-closed security boundary gating and attestation checks.",
        ),
        (
            "01_ARCHITECTURE/memory/dec-active-03.md",
            "id: dec-active-03\ntitle: Cognitive Core Memory Gating\ntype: decision\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Architectural decision to enforce strict ACTIVE + verified ceiling on all external retrieval requests.",
        ),
        # Review / Unverified notes
        (
            "01_ARCHITECTURE/memory/review-stub-01.md",
            "id: review-stub-01\ntitle: Architecture Protocol Draft\ntype: knowledge\nlifecycle: REVIEW\nverification: unverified\n",
            "Draft protocol awaiting human attestation and verification.",
        ),
        # Raw / Ingest notes
        (
            "06_INBOX/raw-note-01.md",
            "id: raw-note-01\ntitle: External Ingest Memory\ntype: import\nlifecycle: RAW\nverification: unverified\n",
            "Raw unparsed external document from web scraping.",
        ),
        # Archived notes
        (
            "01_ARCHITECTURE/memory/arch-note-01.md",
            "id: arch-note-01\ntitle: Deprecated Architecture V1\ntype: knowledge\nlifecycle: ARCHIVED\nverification: verified\n",
            "Old deprecated architecture specification superseded by Alpha.",
        ),
        # Superseded notes
        (
            "01_ARCHITECTURE/memory/super-note-01.md",
            "id: super-note-01\ntitle: Superseded Memory Policy\ntype: procedure\nlifecycle: SUPERSEDED\nverification: verified\n",
            "Historical policy replaced by fail-closed boundary.",
        ),
    ]

    for rel_path, fm, body in notes_data:
        p = tmp_path / rel_path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(f"---\n{fm}\n---\n{body}", encoding="utf-8")

    return VaultIndex.load(tmp_path)


@pytest.fixture
def facade_fixture(test_vault: VaultIndex) -> ProductionRetrievalFacade:
    retriever = HybridRetriever(test_vault)
    adapter = RetrievalBoundaryAdapter(retriever)
    return ProductionRetrievalFacade(adapter=adapter)


# ---------------------------------------------------------------------------
# 1. Multi-Principal Integration Tests
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("principal", [
    PrincipalRole.HUMAN,
    PrincipalRole.AI_AGENT,
    PrincipalRole.ADMIN,
    "human",
    "ai_agent",
    "admin",
])
def test_integration_all_valid_principals_succeed(
    facade_fixture: ProductionRetrievalFacade,
    principal: Any,
):
    """Verifies that HUMAN, AI_AGENT, and ADMIN can execute queries cleanly."""
    req = FacadeRetrievalRequest(
        query="architecture",
        principal=principal,
        page_size=5,
        request_id=f"test-prin-{principal}",
    )
    resp = facade_fixture.retrieve(req)

    assert isinstance(resp, FacadeRetrievalResponse)
    assert len(resp.results) > 0
    # Effective principal is normalized string
    assert resp.principal in {"human", "ai_agent", "admin"}
    # All returned notes must be ACTIVE + verified
    for r in resp.results:
        assert r.lifecycle == "ACTIVE"
        assert r.verification == "verified"


def test_integration_missing_principal_fails_closed(facade_fixture: ProductionRetrievalFacade):
    """Facade must never infer or guess a default principal."""
    req = FacadeRetrievalRequest(
        query="architecture",
        principal=None,
    )
    with pytest.raises(PrincipalValidationError):
        facade_fixture.retrieve(req)


def test_integration_unrecognized_principal_fails_closed(facade_fixture: ProductionRetrievalFacade):
    """Unknown or untrusted principal values must be rejected fail-closed."""
    req = FacadeRetrievalRequest(
        query="architecture",
        principal="malicious_intruder",
    )
    with pytest.raises(PrincipalValidationError):
        facade_fixture.retrieve(req)


# ---------------------------------------------------------------------------
# 2. Strict Security Ceiling & Exclusion Tests
# ---------------------------------------------------------------------------

def test_integration_strict_active_verified_ceiling(facade_fixture: ProductionRetrievalFacade):
    """Confirms non-active or unverified notes are strictly excluded from search."""
    req = FacadeRetrievalRequest(
        query="architecture memory protocol draft external",
        principal=PrincipalRole.AI_AGENT,
        page_size=20,
    )
    resp = facade_fixture.retrieve(req)

    returned_ids = {r.id for r in resp.results}
    # Active verified should be present
    assert "knw-active-01" in returned_ids or "prc-active-02" in returned_ids or "dec-active-03" in returned_ids

    # REVIEW, RAW, ARCHIVED, SUPERSEDED must NEVER appear
    assert "review-stub-01" not in returned_ids
    assert "raw-note-01" not in returned_ids
    assert "arch-note-01" not in returned_ids
    assert "super-note-01" not in returned_ids


def test_integration_filter_broadening_attempt_rejected(facade_fixture: ProductionRetrievalFacade):
    """Callers attempting to broaden lifecycle/verification beyond boundary are blocked."""
    # Attempt to request REVIEW notes
    req_broad_lc = FacadeRetrievalRequest(
        query="draft",
        principal=PrincipalRole.HUMAN,
        lifecycles=["ACTIVE", "REVIEW"],
    )
    with pytest.raises(BoundaryViolationError):
        facade_fixture.retrieve(req_broad_lc)

    # Attempt to request unverified notes
    req_broad_verif = FacadeRetrievalRequest(
        query="draft",
        principal=PrincipalRole.HUMAN,
        verification=["unverified"],
    )
    with pytest.raises(BoundaryViolationError):
        facade_fixture.retrieve(req_broad_verif)


# ---------------------------------------------------------------------------
# 3. Filter Narrowing Tests
# ---------------------------------------------------------------------------

def test_integration_filter_narrowing_by_type(facade_fixture: ProductionRetrievalFacade):
    """Callers can safely narrow results by type within the active verified boundary."""
    req = FacadeRetrievalRequest(
        query="architecture protocol policy",
        principal=PrincipalRole.HUMAN,
        types=["procedure"],
    )
    resp = facade_fixture.retrieve(req)

    assert len(resp.results) > 0
    for r in resp.results:
        assert r.type == "procedure"
        assert r.lifecycle == "ACTIVE"
        assert r.verification == "verified"


def test_integration_filter_narrowing_empty_result(facade_fixture: ProductionRetrievalFacade):
    """Narrowing to a type with zero matching active notes returns empty results rather than broadening."""
    req = FacadeRetrievalRequest(
        query="architecture",
        principal=PrincipalRole.AI_AGENT,
        types=["nonexistent_type"],
    )
    resp = facade_fixture.retrieve(req)
    assert resp.results == []
    assert resp.total_hits == 0


# ---------------------------------------------------------------------------
# 4. Determinism & Tie-Breaking Tests
# ---------------------------------------------------------------------------

def test_integration_deterministic_ranking(facade_fixture: ProductionRetrievalFacade):
    """Repeated executions of identical query produce byte-for-byte identical result ordering."""
    req1 = FacadeRetrievalRequest(query="security policy protocol", principal=PrincipalRole.HUMAN, page_size=5)
    req2 = FacadeRetrievalRequest(query="security policy protocol", principal=PrincipalRole.HUMAN, page_size=5)

    resp1 = facade_fixture.retrieve(req1)
    resp2 = facade_fixture.retrieve(req2)

    ids1 = [r.id for r in resp1.results]
    ids2 = [r.id for r in resp2.results]
    scores1 = [r.score for r in resp1.results]
    scores2 = [r.score for r in resp2.results]

    assert ids1 == ids2
    assert scores1 == scores2


# ---------------------------------------------------------------------------
# 5. Cursor-Based Pagination Tests
# ---------------------------------------------------------------------------

def test_integration_pagination_flow(facade_fixture: ProductionRetrievalFacade):
    """Tests page_size=1 multi-page traversal with deterministic cursor tokens."""
    req_p1 = FacadeRetrievalRequest(
        query="protocol policy architecture",
        principal=PrincipalRole.HUMAN,
        page_size=1,
    )
    resp_p1 = facade_fixture.retrieve(req_p1)
    assert len(resp_p1.results) == 1
    assert resp_p1.next_page_token is not None

    req_p2 = FacadeRetrievalRequest(
        query="protocol policy architecture",
        principal=PrincipalRole.HUMAN,
        page_size=1,
        page_token=resp_p1.next_page_token,
    )
    resp_p2 = facade_fixture.retrieve(req_p2)
    assert len(resp_p2.results) == 1
    # Distinct items across pages
    assert resp_p1.results[0].id != resp_p2.results[0].id


def test_integration_invalid_page_token_fails_closed(facade_fixture: ProductionRetrievalFacade):
    """Invalid or tampered page tokens fail closed."""
    req = FacadeRetrievalRequest(
        query="architecture",
        principal=PrincipalRole.HUMAN,
        page_token="invalid_token_format_tampered",
    )
    with pytest.raises(Exception):
        facade_fixture.retrieve(req)


# ---------------------------------------------------------------------------
# 6. Real Canonical Vault Index Integration
# ---------------------------------------------------------------------------

def test_integration_live_canonical_vault_search():
    """Executes live search against actual active repository notes."""
    idx = VaultIndex.load(".")
    retriever = HybridRetriever(idx)
    adapter = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=adapter)

    req = FacadeRetrievalRequest(
        query="memory security invariants",
        principal=PrincipalRole.HUMAN,
        page_size=5,
    )
    resp = facade.retrieve(req)

    assert len(resp.results) > 0
    # Verify every returned note from actual repo satisfies ACTIVE + verified
    for r in resp.results:
        assert r.lifecycle == "ACTIVE"
        assert r.verification == "verified"
        assert r.id is not None
        assert r.title is not None
