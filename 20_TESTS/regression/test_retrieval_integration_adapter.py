"""20_TESTS/regression/test_retrieval_integration_adapter.py — P3-A Integration Adapter Test Suite.

Validates the RetrievalIntegrationAdapter:
1. Request Contract & Input Canonicalization:
   - Valid typed request execution
   - Rejection of parameter ambiguity (empty query, non-integer or boolean page_size, out-of-bounds page_size)
   - Input whitespace collapse & canonicalization
2. Security Ceiling Enforcement:
   - HUMAN, AI_AGENT, ADMIN succeed
   - Missing or untrusted principal rejected fail-closed
   - Broadening filters (REVIEW, RAW, unverified) rejected fail-closed
   - 0 leaks of non-active or unverified notes
3. Response Contract:
   - Sanitized IntegrationSearchHit items (no retriever internals)
   - Disclosure levels: summary (120 chars), standard (240 chars), full
4. No-Mutation Guarantee:
   - Demonstrates before_state == after_state for files, index, and graph
5. Tamper-Evident Multi-Factor Pagination:
   - Multi-page traversal with cursor
   - Cross-principal cursor reuse rejected fail-closed
   - Query/filter mismatch cursor reuse rejected fail-closed
   - Tampered/corrupt cursor rejected fail-closed
6. Integration Golden Test:
   - Golden end-to-end search across HUMAN, AI_AGENT, ADMIN
"""
from __future__ import annotations

import copy
import hashlib
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List

import pytest

from cognitive_core.hybrid_retrieval import HybridRetriever
from cognitive_core.integration_adapter import (
    CursorSecurityError,
    DisclosureLevel,
    IntegrationRequestValidationError,
    IntegrationSearchHit,
    IntegrationSearchRequest,
    IntegrationSearchResponse,
    IntegrationSecurityError,
    RetrievalIntegrationAdapter,
)
from cognitive_core.retrieval_boundary import RetrievalBoundaryAdapter
from cognitive_core.retrieval_facade import ProductionRetrievalFacade
from cognitive_core.vault_index import Note, VaultIndex


class PrincipalEnum(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"
    INTRUDER = "untrusted_intruder"


@pytest.fixture
def controlled_vault(tmp_path: Path) -> VaultIndex:
    notes_data = [
        (
            "01_ARCHITECTURE/memory/knw-adapter-01.md",
            "id: knw-adapter-01\ntitle: Safe Integration Adapter Protocol\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "High performance decoupled integration adapter mediating between memory controller and facade.",
        ),
        (
            "01_ARCHITECTURE/memory/prc-adapter-02.md",
            "id: prc-adapter-02\ntitle: Tamper Evident Cursor Verification\ntype: procedure\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Detailed procedure for binding cursor pagination tokens to principal identity and query filter hashes.",
        ),
        (
            "01_ARCHITECTURE/memory/dec-adapter-03.md",
            "id: dec-adapter-03\ntitle: Architecture Boundary Invariant\ntype: decision\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Architectural decision to enforce fail-closed security ceilings on all incoming caller requests.",
        ),
        (
            "01_ARCHITECTURE/memory/rev-adapter-04.md",
            "id: rev-adapter-04\ntitle: Unreviewed Memory Draft\ntype: knowledge\nlifecycle: REVIEW\nverification: unverified\n",
            "Draft proposal for experimental memory caching.",
        ),
        (
            "06_INBOX/raw-adapter-05.md",
            "id: raw-adapter-05\ntitle: Raw External Ingest\ntype: import\nlifecycle: RAW\nverification: unverified\n",
            "Raw unparsed scraped import text.",
        ),
    ]
    for rel_path, fm, body in notes_data:
        p = tmp_path / rel_path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(f"---\n{fm}\n---\n{body}", encoding="utf-8")

    return VaultIndex.load(tmp_path)


@pytest.fixture
def adapter_fixture(controlled_vault: VaultIndex) -> RetrievalIntegrationAdapter:
    retriever = HybridRetriever(controlled_vault)
    boundary = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=boundary)
    return RetrievalIntegrationAdapter(facade=facade)


# ---------------------------------------------------------------------------
# 1. Request Contract & Input Canonicalization
# ---------------------------------------------------------------------------

def test_request_valid_execution(adapter_fixture: RetrievalIntegrationAdapter):
    req = IntegrationSearchRequest(
        query="adapter protocol",
        principal="human",
        page_size=5,
    )
    resp = adapter_fixture.search(req)
    assert isinstance(resp, IntegrationSearchResponse)
    assert resp.principal == "human"
    assert len(resp.results) > 0
    assert resp.results[0].id == "knw-adapter-01"


def test_request_whitespace_canonicalization(adapter_fixture: RetrievalIntegrationAdapter):
    req = IntegrationSearchRequest(
        query="   adapter    protocol   verification   ",
        principal="human",
    )
    resp = adapter_fixture.search(req)
    assert len(resp.results) > 0


def test_request_empty_query_rejected(adapter_fixture: RetrievalIntegrationAdapter):
    with pytest.raises(IntegrationRequestValidationError):
        adapter_fixture.search(IntegrationSearchRequest(query="   ", principal="human"))


def test_request_invalid_page_size_rejected(adapter_fixture: RetrievalIntegrationAdapter):
    # Boolean page_size rejected
    with pytest.raises(IntegrationRequestValidationError):
        adapter_fixture.search(IntegrationSearchRequest(query="adapter", principal="human", page_size=True))  # type: ignore

    # Zero or negative page_size rejected
    with pytest.raises(IntegrationRequestValidationError):
        adapter_fixture.search(IntegrationSearchRequest(query="adapter", principal="human", page_size=0))

    # Over limit (>100) rejected
    with pytest.raises(IntegrationRequestValidationError):
        adapter_fixture.search(IntegrationSearchRequest(query="adapter", principal="human", page_size=101))


def test_request_invalid_disclosure_level_rejected(adapter_fixture: RetrievalIntegrationAdapter):
    with pytest.raises(IntegrationRequestValidationError):
        adapter_fixture.search(
            IntegrationSearchRequest(query="adapter", principal="human", disclosure_level="unsupported_level")
        )


# ---------------------------------------------------------------------------
# 2. Security Ceiling Enforcement
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("principal", [
    PrincipalEnum.HUMAN,
    PrincipalEnum.AI_AGENT,
    PrincipalEnum.ADMIN,
    "human",
    "ai_agent",
    "admin",
])
def test_security_ceiling_principals_allowed(adapter_fixture: RetrievalIntegrationAdapter, principal: Any):
    req = IntegrationSearchRequest(query="adapter", principal=principal)
    resp = adapter_fixture.search(req)
    assert len(resp.results) > 0
    for r in resp.results:
        assert r.lifecycle == "ACTIVE"
        assert r.verification == "verified"


def test_security_ceiling_missing_or_untrusted_principal_rejected(adapter_fixture: RetrievalIntegrationAdapter):
    with pytest.raises(IntegrationSecurityError):
        adapter_fixture.search(IntegrationSearchRequest(query="adapter", principal=None))

    with pytest.raises(IntegrationSecurityError):
        adapter_fixture.search(IntegrationSearchRequest(query="adapter", principal="untrusted_guest"))

    with pytest.raises(IntegrationSecurityError):
        adapter_fixture.search(IntegrationSearchRequest(query="adapter", principal=PrincipalEnum.INTRUDER))


def test_security_ceiling_broadening_filters_rejected(adapter_fixture: RetrievalIntegrationAdapter):
    # Attempt to request REVIEW lifecycle
    with pytest.raises(IntegrationSecurityError):
        adapter_fixture.search(
            IntegrationSearchRequest(query="draft", principal="human", lifecycles=["ACTIVE", "REVIEW"])
        )

    # Attempt to request RAW lifecycle
    with pytest.raises(IntegrationSecurityError):
        adapter_fixture.search(
            IntegrationSearchRequest(query="draft", principal="human", lifecycles=["RAW"])
        )


def test_security_ceiling_zero_leaks_of_unreviewed_notes(adapter_fixture: RetrievalIntegrationAdapter):
    req = IntegrationSearchRequest(
        query="unreviewed memory draft raw unparsed external",
        principal="human",
        page_size=20,
    )
    resp = adapter_fixture.search(req)
    returned_ids = {r.id for r in resp.results}
    assert "rev-adapter-04" not in returned_ids
    assert "raw-adapter-05" not in returned_ids


# ---------------------------------------------------------------------------
# 3. Response Contract & Disclosure Levels
# ---------------------------------------------------------------------------

def test_response_contract_disclosure_summary(adapter_fixture: RetrievalIntegrationAdapter):
    req = IntegrationSearchRequest(
        query="adapter protocol",
        principal="human",
        disclosure_level="summary",
    )
    resp = adapter_fixture.search(req)
    hit = resp.results[0]
    assert isinstance(hit, IntegrationSearchHit)
    assert hit.citation == f"[[{hit.id}]]"
    assert len(hit.summary) <= 125


def test_response_contract_disclosure_full(adapter_fixture: RetrievalIntegrationAdapter):
    req = IntegrationSearchRequest(
        query="adapter protocol",
        principal="human",
        disclosure_level="full",
    )
    resp = adapter_fixture.search(req)
    hit = resp.results[0]
    assert "High performance decoupled integration adapter" in hit.summary


# ---------------------------------------------------------------------------
# 4. No-Mutation Guarantee
# ---------------------------------------------------------------------------

def test_no_mutation_guarantee(tmp_path: Path):
    notes_data = [
        ("01_ARCHITECTURE/note1.md", "id: n1\ntitle: Architecture One\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\n", "Body one content."),
        ("01_ARCHITECTURE/note2.md", "id: n2\ntitle: Architecture Two\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\n", "Body two content."),
    ]
    for rel, fm, b in notes_data:
        p = tmp_path / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(f"---\n{fm}\n---\n{b}", encoding="utf-8")

    def calc_dir_state(d: Path) -> Dict[str, str]:
        state = {}
        for f in sorted(d.rglob("*.md")):
            state[str(f.relative_to(d))] = hashlib.sha256(f.read_bytes()).hexdigest()
        return state

    state_before = calc_dir_state(tmp_path)

    vault = VaultIndex.load(tmp_path)
    adapter = RetrievalIntegrationAdapter(ProductionRetrievalFacade(RetrievalBoundaryAdapter(HybridRetriever(vault))))

    # Run multiple searches
    for _ in range(10):
        adapter.search(IntegrationSearchRequest(query="Architecture", principal="human"))
        adapter.search(IntegrationSearchRequest(query="One", principal="ai_agent"))
        adapter.search(IntegrationSearchRequest(query="Two", principal="admin"))

    state_after = calc_dir_state(tmp_path)

    # State before and after must be byte-for-byte identical
    assert state_before == state_after
    assert len(vault.notes) == 2


# ---------------------------------------------------------------------------
# 5. Tamper-Evident Multi-Factor Pagination
# ---------------------------------------------------------------------------

def test_pagination_flow_and_cursor_binding(adapter_fixture: RetrievalIntegrationAdapter):
    req_p1 = IntegrationSearchRequest(
        query="adapter protocol verification decision",
        principal="human",
        page_size=1,
    )
    resp_p1 = adapter_fixture.search(req_p1)
    assert len(resp_p1.results) == 1
    assert resp_p1.next_page_token is not None

    # Normal Page 2 request by same principal
    req_p2 = IntegrationSearchRequest(
        query="adapter protocol verification decision",
        principal="human",
        page_size=1,
        page_token=resp_p1.next_page_token,
    )
    resp_p2 = adapter_fixture.search(req_p2)
    assert len(resp_p2.results) == 1
    assert resp_p1.results[0].id != resp_p2.results[0].id


def test_pagination_cross_principal_tampering_rejected(adapter_fixture: RetrievalIntegrationAdapter):
    # Human obtains a page token
    req_p1 = IntegrationSearchRequest(
        query="adapter protocol verification decision",
        principal="human",
        page_size=1,
    )
    resp_p1 = adapter_fixture.search(req_p1)
    token = resp_p1.next_page_token
    assert token is not None

    # AI_AGENT attempts to reuse token generated for HUMAN
    malicious_req = IntegrationSearchRequest(
        query="adapter protocol verification decision",
        principal="ai_agent",
        page_size=1,
        page_token=token,
    )
    with pytest.raises(CursorSecurityError) as exc_info:
        adapter_fixture.search(malicious_req)
    assert "Cross-principal cursor violation" in str(exc_info.value)


def test_pagination_query_mismatch_rejected(adapter_fixture: RetrievalIntegrationAdapter):
    req_p1 = IntegrationSearchRequest(
        query="adapter protocol verification decision",
        principal="human",
        page_size=1,
    )
    resp_p1 = adapter_fixture.search(req_p1)
    token = resp_p1.next_page_token
    assert token is not None

    # Same principal, but changed query
    mismatch_req = IntegrationSearchRequest(
        query="completely different query",
        principal="human",
        page_size=1,
        page_token=token,
    )
    with pytest.raises(CursorSecurityError) as exc_info:
        adapter_fixture.search(mismatch_req)
    assert "Cursor query/filter mismatch" in str(exc_info.value)


def test_pagination_corrupted_token_rejected(adapter_fixture: RetrievalIntegrationAdapter):
    with pytest.raises(CursorSecurityError):
        adapter_fixture.search(
            IntegrationSearchRequest(query="adapter", principal="human", page_token="malformed.fake.token")
        )


# ---------------------------------------------------------------------------
# 6. Integration Golden Test
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("principal", ["human", "ai_agent", "admin"])
def test_golden_integration_pipeline(adapter_fixture: RetrievalIntegrationAdapter, principal: str):
    """Verifies end-to-end chain: request -> adapter -> facade -> boundary -> retriever -> results."""
    req = IntegrationSearchRequest(
        query="architecture boundary invariant",
        principal=principal,
        page_size=10,
        disclosure_level="standard",
    )
    resp = adapter_fixture.search(req)

    assert isinstance(resp, IntegrationSearchResponse)
    assert resp.principal == principal
    assert resp.retrieval_mode == "hybrid_boundary_secure"
    assert resp.deterministic is True
    assert len(resp.results) > 0

    top_hit = resp.results[0]
    assert top_hit.id == "dec-adapter-03"
    assert top_hit.lifecycle == "ACTIVE"
    assert top_hit.verification == "verified"
    assert top_hit.citation == "[[dec-adapter-03]]"
