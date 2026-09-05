"""20_TESTS/regression/test_p4_security_invariants.py — P4-D Security Invariants Suite.

Explicit, formal verification of the 8 Security Invariants:
INVARIANT 1: No principal can retrieve beyond ACTIVE + verified.
INVARIANT 2: No cursor can cross principal boundaries.
INVARIANT 3: No filter can broaden the security ceiling.
INVARIANT 4: No adapter request can mutate filesystem/database state.
INVARIANT 5: No adapter response exposes raw retriever internals.
INVARIANT 6: Pagination preserves the exact security envelope of page 1.
INVARIANT 7: Ordering remains deterministic under identical requests.
INVARIANT 8: A security rejection occurs before retriever invocation whenever detectable at adapter boundary.
"""
from __future__ import annotations

import copy
import hashlib
from pathlib import Path
from typing import Any, Dict, List
from unittest.mock import MagicMock

import pytest

from cognitive_core.hybrid_retrieval import HybridRetriever
from cognitive_core.integration_adapter import (
    CursorSecurityError,
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


@pytest.fixture
def invariant_vault(tmp_path: Path) -> VaultIndex:
    """Creates a controlled vault with active, unverified, draft, and superseded notes."""
    notes_data = [
        (
            "01_ARCHITECTURE/memory/knw-inv-01.md",
            "id: knw-inv-01\ntitle: Distributed Consensus Invariant\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Core consensus protocol validated by formal verification proofs.",
        ),
        (
            "01_ARCHITECTURE/memory/knw-inv-02.md",
            "id: knw-inv-02\ntitle: Distributed Replication Invariant\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Data replication invariants ensuring strict serializability across quorum nodes.",
        ),
        (
            "01_ARCHITECTURE/memory/prc-inv-03.md",
            "id: prc-inv-03\ntitle: Distributed Recovery Procedure\ntype: procedure\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Quorum recovery procedure executed following leader failure.",
        ),
        (
            "01_ARCHITECTURE/memory/knw-inv-unverified.md",
            "id: knw-inv-unverified\ntitle: Unverified Consensus Hypothesis\ntype: hypothesis\nlifecycle: ACTIVE\nverification: unverified\nprovenance:\n  source_type: official\n",
            "Speculative consensus optimization that has not been attested.",
        ),
        (
            "01_ARCHITECTURE/memory/knw-inv-review.md",
            "id: knw-inv-review\ntitle: Consensus Review Draft\ntype: knowledge\nlifecycle: REVIEW\nverification: unverified\n",
            "Draft consensus protocol under peer review.",
        ),
        (
            "06_INBOX/raw-inv-04.md",
            "id: raw-inv-04\ntitle: Raw Scraped Invariant Data\ntype: import\nlifecycle: RAW\nverification: unverified\n",
            "Untrusted scraped external notes.",
        ),
        (
            "01_ARCHITECTURE/memory/knw-inv-superseded.md",
            "id: knw-inv-superseded\ntitle: Superseded Consensus Algorithm\ntype: knowledge\nlifecycle: SUPERSEDED\nverification: verified\n",
            "Superseded consensus notes that are no longer active knowledge.",
        ),
    ]
    for rel_path, fm, body in notes_data:
        p = tmp_path / rel_path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(f"---\n{fm}\n---\n{body}", encoding="utf-8")

    return VaultIndex.load(tmp_path)


@pytest.fixture
def invariant_adapter(invariant_vault: VaultIndex) -> RetrievalIntegrationAdapter:
    retriever = HybridRetriever(invariant_vault)
    boundary = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=boundary)
    return RetrievalIntegrationAdapter(facade=facade)


# ===========================================================================
# INVARIANT 1: No principal can retrieve beyond ACTIVE + verified
# ===========================================================================

class TestInvariant1ActiveVerifiedCeiling:
    @pytest.mark.parametrize("principal", ["human", "ai_agent", "admin"])
    def test_all_returned_notes_are_strictly_active_and_verified(
        self, invariant_adapter: RetrievalIntegrationAdapter, principal: str
    ):
        req = IntegrationSearchRequest(query="consensus", principal=principal, page_size=100)
        resp = invariant_adapter.search(req)

        assert len(resp.results) > 0
        for hit in resp.results:
            assert hit.lifecycle == "ACTIVE", f"Note {hit.id} has non-ACTIVE lifecycle: {hit.lifecycle}"
            assert hit.verification == "verified", f"Note {hit.id} has non-verified status: {hit.verification}"

    def test_unverified_and_non_active_notes_are_never_leaked(
        self, invariant_adapter: RetrievalIntegrationAdapter
    ):
        req = IntegrationSearchRequest(query="consensus", principal="ai_agent", page_size=100)
        resp = invariant_adapter.search(req)
        returned_ids = {h.id for h in resp.results}

        forbidden_ids = {"knw-inv-unverified", "knw-inv-review", "raw-inv-04", "knw-inv-superseded"}
        leaked = returned_ids.intersection(forbidden_ids)
        assert len(leaked) == 0, f"Security Boundary Leak! Leaked IDs: {leaked}"


# ===========================================================================
# INVARIANT 2: No cursor can cross principal boundaries
# ===========================================================================

class TestInvariant2CursorPrincipalBinding:
    def test_cursor_cannot_cross_from_ai_agent_to_human(
        self, invariant_adapter: RetrievalIntegrationAdapter
    ):
        # AI_AGENT issues cursor
        req_ai = IntegrationSearchRequest(query="distributed", principal="ai_agent", page_size=1)
        resp_ai = invariant_adapter.search(req_ai)
        cursor = resp_ai.next_page_token
        assert cursor is not None

        # HUMAN attempts to use AI_AGENT's cursor
        req_human = IntegrationSearchRequest(
            query="distributed", principal="human", page_size=1, page_token=cursor
        )
        with pytest.raises(CursorSecurityError) as exc_info:
            invariant_adapter.search(req_human)
        assert "cross-principal cursor violation" in str(exc_info.value).lower()

    def test_cursor_cannot_cross_from_human_to_admin(
        self, invariant_adapter: RetrievalIntegrationAdapter
    ):
        req_human = IntegrationSearchRequest(query="distributed", principal="human", page_size=1)
        resp_human = invariant_adapter.search(req_human)
        cursor = resp_human.next_page_token
        assert cursor is not None

        req_admin = IntegrationSearchRequest(
            query="distributed", principal="admin", page_size=1, page_token=cursor
        )
        with pytest.raises(CursorSecurityError) as exc_info:
            invariant_adapter.search(req_admin)
        assert "cross-principal cursor violation" in str(exc_info.value).lower()


# ===========================================================================
# INVARIANT 3: No filter can broaden the security ceiling
# ===========================================================================

class TestInvariant3NoFilterBroadening:
    @pytest.mark.parametrize("broadening_filter", [
        ["REVIEW"],
        ["RAW"],
        ["ARCHIVED"],
        ["SUPERSEDED"],
        ["ACTIVE", "REVIEW"],
        ["ACTIVE", "RAW"],
        ["ACTIVE", "SUPERSEDED"],
    ])
    def test_filter_broadening_fails_closed(
        self, invariant_adapter: RetrievalIntegrationAdapter, broadening_filter: List[str]
    ):
        req = IntegrationSearchRequest(
            query="consensus", principal="ai_agent", lifecycles=broadening_filter
        )
        with pytest.raises(IntegrationSecurityError) as exc_info:
            invariant_adapter.search(req)
        assert "ceiling violation" in str(exc_info.value).lower() or "exceeds boundary" in str(exc_info.value).lower()


# ===========================================================================
# INVARIANT 4: No adapter request can mutate filesystem/database state
# ===========================================================================

class TestInvariant4NoMutation:
    def test_filesystem_state_is_completely_immutable_during_retrieval(
        self, tmp_path: Path
    ):
        notes_data = [
            (
                "01_ARCHITECTURE/memory/knw-inv-01.md",
                "id: knw-inv-01\ntitle: Distributed Consensus Invariant\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
                "Core consensus protocol validated by formal verification proofs.",
            ),
            (
                "01_ARCHITECTURE/memory/knw-inv-02.md",
                "id: knw-inv-02\ntitle: Distributed Replication Invariant\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
                "Data replication invariants ensuring strict serializability across quorum nodes.",
            ),
        ]
        for rel_path, fm, body in notes_data:
            p = tmp_path / rel_path
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(f"---\n{fm}\n---\n{body}", encoding="utf-8")

        # 1. Capture snapshot hashes of all vault files before search
        def compute_vault_snapshot() -> Dict[str, str]:
            snap = {}
            for p in sorted(tmp_path.rglob("*.md")):
                snap[str(p.relative_to(tmp_path))] = hashlib.sha256(p.read_bytes()).hexdigest()
            return snap

        before_snapshot = compute_vault_snapshot()

        vault = VaultIndex.load(tmp_path)
        adapter = RetrievalIntegrationAdapter(
            facade=ProductionRetrievalFacade(adapter=RetrievalBoundaryAdapter(HybridRetriever(vault)))
        )
        note_count_before = len(vault.notes)

        # 2. Execute a barrage of diverse search queries
        for q in ["consensus", "distributed", "recovery", "replication", "quorum"]:
            req = IntegrationSearchRequest(query=q, principal="ai_agent", page_size=10)
            adapter.search(req)

        # 3. Capture snapshot after search
        after_snapshot = compute_vault_snapshot()
        note_count_after = len(vault.notes)

        assert before_snapshot == after_snapshot, "Filesystem mutation detected during retrieval!"
        assert note_count_before == note_count_after, "Index mutation detected during retrieval!"


# ===========================================================================
# INVARIANT 5: No adapter response exposes raw retriever internals
# ===========================================================================

class TestInvariant5ResponseSanitization:
    def test_hits_expose_only_sanitized_public_fields(
        self, invariant_adapter: RetrievalIntegrationAdapter
    ):
        req = IntegrationSearchRequest(query="consensus", principal="ai_agent", page_size=10)
        resp = invariant_adapter.search(req)

        allowed_hit_fields = {
            "id", "title", "score", "lifecycle", "verification",
            "type", "summary", "citation", "signals"
        }

        for hit in resp.results:
            assert isinstance(hit, IntegrationSearchHit)
            hit_dict = hit.__dict__
            assert set(hit_dict.keys()) == allowed_hit_fields

            # Prohibit internal pointer leaks
            assert not hasattr(hit, "_note")
            assert not hasattr(hit, "file_path")
            assert not hasattr(hit, "raw_content")
            assert not hasattr(hit, "graph_node")
            assert not hasattr(hit, "bm25_doc_id")


# ===========================================================================
# INVARIANT 6: Pagination preserves the exact security envelope of page 1
# ===========================================================================

class TestInvariant6PaginationEnvelopePreservation:
    def test_subsequent_pages_preserve_principal_and_security_ceiling(
        self, invariant_adapter: RetrievalIntegrationAdapter
    ):
        req1 = IntegrationSearchRequest(
            query="distributed",
            principal="ai_agent",
            page_size=1,
            disclosure_level="summary",
        )
        resp1 = invariant_adapter.search(req1)
        assert resp1.next_page_token is not None

        req2 = IntegrationSearchRequest(
            query="distributed",
            principal="ai_agent",
            page_size=1,
            page_token=resp1.next_page_token,
            disclosure_level="summary",
        )
        resp2 = invariant_adapter.search(req2)

        assert resp2.principal == resp1.principal
        assert resp2.trace["effective_lifecycles"] == resp1.trace["effective_lifecycles"]
        assert resp2.trace["effective_verification"] == resp1.trace["effective_verification"]
        assert resp2.trace["filter_signature"] == resp1.trace["filter_signature"]

        for hit in resp2.results:
            assert hit.lifecycle == "ACTIVE"
            assert hit.verification == "verified"


# ===========================================================================
# INVARIANT 7: Ordering remains deterministic under identical requests
# ===========================================================================

class TestInvariant7DeterministicOrdering:
    def test_ordering_and_scores_are_100_percent_identical(
        self, invariant_adapter: RetrievalIntegrationAdapter
    ):
        req = IntegrationSearchRequest(query="distributed", principal="human", page_size=10)

        runs = [invariant_adapter.search(req) for _ in range(5)]

        first_ids = [h.id for h in runs[0].results]
        first_scores = [h.score for h in runs[0].results]

        for i, run in enumerate(runs[1:], start=2):
            run_ids = [h.id for h in run.results]
            run_scores = [h.score for h in run.results]
            assert run_ids == first_ids, f"Ordering mismatch on run {i}"
            assert run_scores == first_scores, f"Score mismatch on run {i}"


# ===========================================================================
# INVARIANT 8: Security rejection occurs before retriever invocation
# ===========================================================================

class TestInvariant8PreRetrievalRejection:
    def test_early_rejection_prevents_facade_and_retriever_calls(self):
        mock_facade = MagicMock(spec=ProductionRetrievalFacade)
        adapter = RetrievalIntegrationAdapter(facade=mock_facade)

        # 1. Invalid Principal
        with pytest.raises(IntegrationSecurityError):
            adapter.search(IntegrationSearchRequest(query="consensus", principal="anonymous_hacker"))
        mock_facade.retrieve.assert_not_called()

        # 2. Non-ACTIVE Lifecycle
        with pytest.raises(IntegrationSecurityError):
            adapter.search(IntegrationSearchRequest(query="consensus", principal="human", lifecycles=["RAW"]))
        mock_facade.retrieve.assert_not_called()

        # 3. Invalid page size
        with pytest.raises(IntegrationRequestValidationError):
            adapter.search(IntegrationSearchRequest(query="consensus", principal="human", page_size=0))
        mock_facade.retrieve.assert_not_called()

        # 4. Out of bounds page size (>100)
        with pytest.raises(IntegrationRequestValidationError):
            adapter.search(IntegrationSearchRequest(query="consensus", principal="human", page_size=105))
        mock_facade.retrieve.assert_not_called()

        # 5. Empty query
        with pytest.raises(IntegrationRequestValidationError):
            adapter.search(IntegrationSearchRequest(query=" \t \n ", principal="human"))
        mock_facade.retrieve.assert_not_called()
