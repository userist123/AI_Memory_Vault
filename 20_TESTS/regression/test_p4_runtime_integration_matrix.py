"""20_TESTS/regression/test_p4_runtime_integration_matrix.py — P4-C Runtime Integration Test Matrix.

Comprehensive combinatorial test matrix validating the interaction between:
Caller Contract (MemoryController-style requests)
  → RetrievalIntegrationAdapter
  → ProductionRetrievalFacade
  → RetrievalBoundaryAdapter
  → HybridRetriever

Covers:
1. Principals: HUMAN, AI_AGENT, ADMIN, untrusted/unknown, None.
2. Lifecycles: ACTIVE, REVIEW, RAW, RECONSOLIDATING, ARCHIVED, SUPERSEDED, mixed sets.
3. Verification: verified, unverified, missing, malformed.
4. Filters: no filters, ACTIVE only, invalid/empty types, malformed disclosure.
5. Pagination: multi-page, cross-principal rejection, query tampering, filter tampering,
   page_size tampering, expiration, corrupt tokens.
6. Disclosure: summary, standard, full, invalid.
7. Early Fail-Closed: proves retriever/facade is NOT invoked when early violations occur.
"""
from __future__ import annotations

import base64
import json
import time
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List
from unittest.mock import MagicMock

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


class MockControllerPrincipal(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"
    INTRUDER = "untrusted_intruder"


@pytest.fixture
def matrix_vault(tmp_path: Path) -> VaultIndex:
    """Constructs a rich test vault containing various lifecycle and verification states."""
    notes_data = [
        # Valid ACTIVE + verified notes
        (
            "01_ARCHITECTURE/knw-active-01.md",
            "id: knw-active-01\ntitle: Distributed Consensus Engine\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Core consensus algorithm documentation with raft leader election protocol.",
        ),
        (
            "01_ARCHITECTURE/knw-active-02.md",
            "id: knw-active-02\ntitle: Distributed Storage Engine\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Storage engine persistence and WAL replication across multi-region nodes.",
        ),
        (
            "01_ARCHITECTURE/prc-active-03.md",
            "id: prc-active-03\ntitle: Distributed Failover Procedure\ntype: procedure\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Operational failover steps to rebalance cluster when node becomes unreachable.",
        ),
        # Poison: ACTIVE but unverified / missing / malformed verification
        (
            "01_ARCHITECTURE/knw-active-unverified.md",
            "id: knw-active-unverified\ntitle: Distributed Unverified Draft\ntype: knowledge\nlifecycle: ACTIVE\nverification: unverified\nprovenance:\n  source_type: official\n",
            "Unverified distributed note that must be excluded by boundary.",
        ),
        (
            "01_ARCHITECTURE/knw-active-missing-verif.md",
            "id: knw-active-missing-verif\ntitle: Distributed Missing Verification\ntype: knowledge\nlifecycle: ACTIVE\nprovenance:\n  source_type: official\n",
            "Note with missing verification field that must never be returned.",
        ),
        (
            "01_ARCHITECTURE/knw-active-malformed-verif.md",
            "id: knw-active-malformed-verif\ntitle: Distributed Malformed Verification\ntype: knowledge\nlifecycle: ACTIVE\nverification: 12345\nprovenance:\n  source_type: official\n",
            "Note with malformed verification that must never be returned.",
        ),
        # Non-ACTIVE lifecycles
        (
            "01_ARCHITECTURE/knw-review-01.md",
            "id: knw-review-01\ntitle: Distributed Review Candidate\ntype: knowledge\nlifecycle: REVIEW\nverification: unverified\n",
            "Pending review consensus note.",
        ),
        (
            "06_INBOX/raw-ingest-01.md",
            "id: raw-ingest-01\ntitle: Distributed Raw Ingest\ntype: import\nlifecycle: RAW\nverification: unverified\n",
            "Scraped consensus note from raw import.",
        ),
        (
            "01_ARCHITECTURE/knw-reconsolidating-01.md",
            "id: knw-reconsolidating-01\ntitle: Distributed Reconsolidating State\ntype: knowledge\nlifecycle: RECONSOLIDATING\nverification: verified\n",
            "Note currently undergoing reflexive reconsolidation.",
        ),
        (
            "01_ARCHITECTURE/knw-archived-01.md",
            "id: knw-archived-01\ntitle: Distributed Archived Architecture\ntype: knowledge\nlifecycle: ARCHIVED\nverification: verified\n",
            "Archived consensus engine specification from legacy release.",
        ),
        (
            "01_ARCHITECTURE/knw-superseded-01.md",
            "id: knw-superseded-01\ntitle: Distributed Superseded Protocol\ntype: knowledge\nlifecycle: SUPERSEDED\nverification: verified\n",
            "Superseded consensus mechanism replaced by newer algorithm.",
        ),
    ]
    for rel_path, fm, body in notes_data:
        p = tmp_path / rel_path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(f"---\n{fm}\n---\n{body}", encoding="utf-8")

    return VaultIndex.load(tmp_path)


@pytest.fixture
def matrix_adapter(matrix_vault: VaultIndex) -> RetrievalIntegrationAdapter:
    retriever = HybridRetriever(matrix_vault)
    boundary = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=boundary)
    return RetrievalIntegrationAdapter(facade=facade)


# ===========================================================================
# 1. PRINCIPALS MATRIX
# ===========================================================================

class TestPrincipalsMatrix:
    @pytest.mark.parametrize("principal", [
        MockControllerPrincipal.HUMAN,
        MockControllerPrincipal.AI_AGENT,
        MockControllerPrincipal.ADMIN,
        "human",
        "ai_agent",
        "admin",
        "HUMAN",
        "AI_AGENT",
        "ADMIN",
    ])
    def test_authorized_principals_succeed(self, matrix_adapter: RetrievalIntegrationAdapter, principal: Any):
        req = IntegrationSearchRequest(query="distributed", principal=principal, page_size=10)
        resp = matrix_adapter.search(req)
        assert isinstance(resp, IntegrationSearchResponse)
        assert len(resp.results) == 3  # exactly the 3 ACTIVE+verified notes
        for hit in resp.results:
            assert hit.lifecycle == "ACTIVE"
            assert hit.verification == "verified"

    @pytest.mark.parametrize("untrusted_principal", [
        MockControllerPrincipal.INTRUDER,
        "untrusted_intruder",
        "guest",
        "root",
        "anonymous",
        "",
        " ",
    ])
    def test_untrusted_principals_fail_closed(self, matrix_adapter: RetrievalIntegrationAdapter, untrusted_principal: Any):
        req = IntegrationSearchRequest(query="distributed", principal=untrusted_principal)
        with pytest.raises(IntegrationSecurityError) as exc_info:
            matrix_adapter.search(req)
        assert "principal" in str(exc_info.value).lower()

    def test_none_principal_fails_closed(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(query="distributed", principal=None)
        with pytest.raises(IntegrationSecurityError) as exc_info:
            matrix_adapter.search(req)
        assert "principal cannot be none" in str(exc_info.value).lower()


# ===========================================================================
# 2. LIFECYCLE & VERIFICATION MATRIX
# ===========================================================================

class TestLifecycleAndVerificationMatrix:
    def test_default_lifecycle_narrows_to_active_only(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(query="distributed", principal=MockControllerPrincipal.AI_AGENT, lifecycles=None)
        resp = matrix_adapter.search(req)
        returned_ids = {h.id for h in resp.results}
        assert returned_ids == {"knw-active-01", "knw-active-02", "prc-active-03"}

    def test_explicit_active_lifecycle_succeeds(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(query="distributed", principal=MockControllerPrincipal.AI_AGENT, lifecycles=["ACTIVE"])
        resp = matrix_adapter.search(req)
        assert len(resp.results) == 3

    @pytest.mark.parametrize("invalid_lc", [
        "REVIEW",
        "RAW",
        "RECONSOLIDATING",
        "ARCHIVED",
        "SUPERSEDED",
        "review",
        "raw",
        "archived",
        "UNKNOWN_STATE",
    ])
    def test_non_active_lifecycles_fail_closed(self, matrix_adapter: RetrievalIntegrationAdapter, invalid_lc: str):
        req = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            lifecycles=[invalid_lc],
        )
        with pytest.raises(IntegrationSecurityError) as exc_info:
            matrix_adapter.search(req)
        assert "ceiling violation" in str(exc_info.value).lower() or "exceeds boundary" in str(exc_info.value).lower()

    def test_mixed_active_and_invalid_lifecycle_fails_closed(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            lifecycles=["ACTIVE", "REVIEW"],
        )
        with pytest.raises(IntegrationSecurityError):
            matrix_adapter.search(req)

    def test_empty_lifecycles_collection_fails_closed(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            lifecycles=[],
        )
        with pytest.raises(IntegrationSecurityError):
            matrix_adapter.search(req)

    def test_unverified_and_malformed_notes_are_strictly_excluded(self, matrix_adapter: RetrievalIntegrationAdapter):
        """Even with generic query, notes lacking verification: 'verified' MUST NOT leak."""
        req = IntegrationSearchRequest(query="distributed", principal=MockControllerPrincipal.AI_AGENT, page_size=50)
        resp = matrix_adapter.search(req)
        result_ids = [r.id for r in resp.results]
        assert "knw-active-unverified" not in result_ids
        assert "knw-active-missing-verif" not in result_ids
        assert "knw-active-malformed-verif" not in result_ids
        assert "knw-review-01" not in result_ids
        assert "raw-ingest-01" not in result_ids


# ===========================================================================
# 3. FILTERS & DISCLOSURE MATRIX
# ===========================================================================

class TestFiltersAndDisclosureMatrix:
    def test_type_filter_filtering(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            types=["procedure"],
        )
        resp = matrix_adapter.search(req)
        assert len(resp.results) == 1
        assert resp.results[0].id == "prc-active-03"
        assert resp.results[0].type == "procedure"

    def test_empty_types_list_fails_validation(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            types=[],
        )
        with pytest.raises(IntegrationRequestValidationError):
            matrix_adapter.search(req)

    def test_non_string_type_fails_validation(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            types=[123],  # type: ignore
        )
        with pytest.raises(IntegrationRequestValidationError):
            matrix_adapter.search(req)

    @pytest.mark.parametrize("level, expected_max_len", [
        ("summary", 123),   # 120 + "..."
        ("standard", 243),  # 240 + "..."
        ("full", 1000),     # full text
    ])
    def test_disclosure_levels(self, matrix_adapter: RetrievalIntegrationAdapter, level: str, expected_max_len: int):
        req = IntegrationSearchRequest(
            query="consensus",
            principal=MockControllerPrincipal.AI_AGENT,
            disclosure_level=level,
        )
        resp = matrix_adapter.search(req)
        assert len(resp.results) > 0
        hit = resp.results[0]
        assert len(hit.summary) <= expected_max_len

    def test_invalid_disclosure_level_fails_validation(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(
            query="consensus",
            principal=MockControllerPrincipal.AI_AGENT,
            disclosure_level="UNSUPPORTED_LEAK",
        )
        with pytest.raises(IntegrationRequestValidationError):
            matrix_adapter.search(req)


# ===========================================================================
# 4. PAGINATION MATRIX
# ===========================================================================

class TestPaginationMatrix:
    def test_two_page_traversal_with_cursor(self, matrix_adapter: RetrievalIntegrationAdapter):
        # Page 1 (size 2 of 3 total)
        req1 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=2,
        )
        resp1 = matrix_adapter.search(req1)
        assert len(resp1.results) == 2
        assert resp1.next_page_token is not None

        # Page 2
        req2 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=2,
            page_token=resp1.next_page_token,
        )
        resp2 = matrix_adapter.search(req2)
        assert len(resp2.results) == 1
        assert resp2.next_page_token is None

        # Results across pages must be disjoint and complete
        all_ids = [h.id for h in resp1.results] + [h.id for h in resp2.results]
        assert len(set(all_ids)) == 3

    def test_cross_principal_cursor_reuse_rejected(self, matrix_adapter: RetrievalIntegrationAdapter):
        # AI_AGENT creates cursor
        req1 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=1,
        )
        resp1 = matrix_adapter.search(req1)
        token = resp1.next_page_token
        assert token is not None

        # HUMAN presents AI_AGENT's cursor -> MUST FAIL
        req2 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.HUMAN,
            page_size=1,
            page_token=token,
        )
        with pytest.raises(CursorSecurityError) as exc_info:
            matrix_adapter.search(req2)
        assert "cross-principal cursor violation" in str(exc_info.value).lower()

    def test_altered_query_cursor_reuse_rejected(self, matrix_adapter: RetrievalIntegrationAdapter):
        req1 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=1,
        )
        resp1 = matrix_adapter.search(req1)
        token = resp1.next_page_token

        # Query altered on page 2
        req2 = IntegrationSearchRequest(
            query="consensus",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=1,
            page_token=token,
        )
        with pytest.raises(CursorSecurityError) as exc_info:
            matrix_adapter.search(req2)
        assert "cursor query/filter mismatch" in str(exc_info.value).lower()

    def test_altered_page_size_cursor_reuse_rejected(self, matrix_adapter: RetrievalIntegrationAdapter):
        req1 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=1,
        )
        resp1 = matrix_adapter.search(req1)
        token = resp1.next_page_token

        # page_size altered from 1 to 2
        req2 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=2,
            page_token=token,
        )
        with pytest.raises(CursorSecurityError) as exc_info:
            matrix_adapter.search(req2)
        assert "cursor query/filter mismatch" in str(exc_info.value).lower()

    def test_tampered_cursor_signature_rejected(self, matrix_adapter: RetrievalIntegrationAdapter):
        req1 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=1,
        )
        resp1 = matrix_adapter.search(req1)
        token = resp1.next_page_token
        assert token is not None

        # Tamper with token base64 payload
        raw_bundle = json.loads(base64.urlsafe_b64decode(token.encode("ascii")).decode("ascii"))
        raw_bundle["s"] = "0123456789abcdef"  # forged signature
        forged_token = base64.urlsafe_b64encode(json.dumps(raw_bundle).encode("ascii")).decode("ascii")

        req2 = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_size=1,
            page_token=forged_token,
        )
        with pytest.raises(CursorSecurityError) as exc_info:
            matrix_adapter.search(req2)
        assert "signature verification failed" in str(exc_info.value).lower()

    def test_malformed_cursor_rejected(self, matrix_adapter: RetrievalIntegrationAdapter):
        req = IntegrationSearchRequest(
            query="distributed",
            principal=MockControllerPrincipal.AI_AGENT,
            page_token="not-a-valid-token-string-at-all",
        )
        with pytest.raises(CursorSecurityError):
            matrix_adapter.search(req)


# ===========================================================================
# 5. EARLY FAIL-CLOSED TEST
# ===========================================================================

class TestEarlyFailClosed:
    def test_security_violation_aborts_prior_to_facade_call(self):
        """Proves facade.retrieve is NEVER called when a security or validation violation occurs."""
        mock_facade = MagicMock(spec=ProductionRetrievalFacade)
        adapter = RetrievalIntegrationAdapter(facade=mock_facade)

        # 1. Untrusted principal
        req1 = IntegrationSearchRequest(query="distributed", principal="malicious_agent")
        with pytest.raises(IntegrationSecurityError):
            adapter.search(req1)
        mock_facade.retrieve.assert_not_called()

        # 2. Ceiling violation
        req2 = IntegrationSearchRequest(query="distributed", principal="ai_agent", lifecycles=["RAW"])
        with pytest.raises(IntegrationSecurityError):
            adapter.search(req2)
        mock_facade.retrieve.assert_not_called()

        # 3. Invalid page_size
        req3 = IntegrationSearchRequest(query="distributed", principal="ai_agent", page_size=200)
        with pytest.raises(IntegrationRequestValidationError):
            adapter.search(req3)
        mock_facade.retrieve.assert_not_called()

        # 4. Empty query
        req4 = IntegrationSearchRequest(query="   ", principal="ai_agent")
        with pytest.raises(IntegrationRequestValidationError):
            adapter.search(req4)
        mock_facade.retrieve.assert_not_called()
