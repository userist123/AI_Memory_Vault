"""20_TESTS/regression/test_retrieval_facade.py — P1.6 Production Retrieval Facade Test Suite.

Covers the 20 mandatory verification scenarios:
 1. Valid request execution and envelope response.
 2. Default request returns ACTIVE + verified only.
 3. Explicit narrowing (ACTIVE / verified subset) succeeds.
 4. Type narrowing (e.g. types=["procedure"]) succeeds.
 5. Broadening attempt (e.g. including REVIEW) is rejected (BoundaryViolationError).
 6. Unknown lifecycle value is rejected (FilterValidationError).
 7. Unknown verification value is rejected (FilterValidationError).
 8. Missing principal is rejected fail-closed (PrincipalValidationError).
 9. Unknown principal is rejected fail-closed (PrincipalValidationError).
10. Malicious/faulty retriever returning RAW is purged by defense-in-depth.
11. Malicious/faulty retriever returning REVIEW is purged by defense-in-depth.
12. Malicious/faulty retriever returning unverified is purged by defense-in-depth.
13. Malicious/faulty retriever returning ARCHIVED/SUPERSEDED is purged by defense-in-depth.
14. Deterministic repeated requests produce identical results and ordering.
15. Principal identity is preserved without loss across the pipeline.
16. Trace comprehensively preserves request ID, caller principal, query fingerprint, and filters.
17. Pagination metadata preservation (page_size, page_token, next_page_token, total_hits).
18. Zero storage or filesystem mutation during facade execution.
19. Retriever is never invoked for invalid requests (pre-retrieval fail-closed).
20. Facade does not perform authorization itself (authorization is caller responsibility).
"""
from __future__ import annotations

from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

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


def _write_note(root: Path, rel_path: str, frontmatter: str, body: str) -> Path:
    p = root / rel_path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(f"---\n{frontmatter}\n---\n{body}", encoding="utf-8")
    return p


class DummyPrincipal(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"


@pytest.fixture
def facade_env(tmp_path) -> Tuple[ProductionRetrievalFacade, Dict[str, str]]:
    """Builds a test vault and returns an initialized ProductionRetrievalFacade."""
    notes_spec = [
        ("proc_01", "11111111-0001-0001-0001-000000000001", "ACTIVE", "verified", "procedure",
         "Security Kernel Procedure Alpha", "Step 1 verify kernel invariants. Step 2 verify trust ceiling. "),
        ("know_01", "11111111-0002-0002-0002-000000000002", "ACTIVE", "verified", "knowledge",
         "Security Kernel Knowledge Beta", "Durable architecture knowledge of memory isolation and boundary gates. "),
        ("know_unverif", "11111111-0003-0003-0003-000000000003", "ACTIVE", "unverified", "knowledge",
         "Security Kernel Unverified Gamma", "Telemetry logs awaiting human attestation. "),
        ("review_note", "11111111-0004-0004-0004-000000000004", "REVIEW", "verified", "knowledge",
         "Security Kernel Inflight Delta", "Draft proposal currently in review queue. "),
        ("archived_note", "11111111-0005-0005-0005-000000000005", "ARCHIVED", "verified", "knowledge",
         "Security Kernel Archived Epsilon", "Deprecated legacy system design from previous version. "),
    ]

    for slug, nid, lc, verif, ntype, title, body in notes_spec:
        _write_note(
            tmp_path,
            f"01_ARCHITECTURE/{slug}.md",
            f"id: {nid}\ntype: {ntype}\nlifecycle: {lc}\nverification: {verif}",
            f"# {title}\n" + (body * 15),
        )

    idx = VaultIndex.load(
        tmp_path,
        lifecycles=["ACTIVE", "REVIEW", "ARCHIVED", "NONE"],
        include_raw=True,
        include_archived=True,
    )
    retriever = HybridRetriever(idx)
    adapter = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=adapter)
    return facade, {slug: nid for slug, nid, *_ in notes_spec}


# ---------------------------------------------------------------------------
# 1. Valid request execution
# ---------------------------------------------------------------------------
def test_facade_01_valid_request(facade_env):
    facade, ids = facade_env
    req = FacadeRetrievalRequest(
        query="Security Kernel",
        principal="human",
        page_size=5,
        request_id="REQ-001",
    )
    resp = facade.retrieve(req)
    assert isinstance(resp, FacadeRetrievalResponse)
    assert resp.principal == "human"
    assert resp.query == "Security Kernel"
    assert resp.request_id == "REQ-001"
    assert resp.total_hits > 0
    assert len(resp.results) <= 5
    assert isinstance(resp.results[0], FacadeNoteResult)


# ---------------------------------------------------------------------------
# 2. Default returns ACTIVE + verified only
# ---------------------------------------------------------------------------
def test_facade_02_default_active_verified(facade_env):
    facade, ids = facade_env
    req = FacadeRetrievalRequest(query="Security Kernel", principal="ai_agent", page_size=10)
    resp = facade.retrieve(req)

    result_ids = {r.id for r in resp.results}
    assert ids["proc_01"] in result_ids
    assert ids["know_01"] in result_ids

    # Unverified, Review, and Archived must be excluded
    assert ids["know_unverif"] not in result_ids
    assert ids["review_note"] not in result_ids
    assert ids["archived_note"] not in result_ids

    for r in resp.results:
        assert r.lifecycle == "ACTIVE"
        assert r.verification == "verified"


# ---------------------------------------------------------------------------
# 3. Explicit narrowing succeeds
# ---------------------------------------------------------------------------
def test_facade_03_explicit_narrowing(facade_env):
    facade, ids = facade_env
    req = FacadeRetrievalRequest(
        query="Security Kernel",
        principal="human",
        lifecycles=["ACTIVE"],
        verification=["verified"],
    )
    resp = facade.retrieve(req)
    assert resp.total_hits > 0
    for r in resp.results:
        assert r.lifecycle == "ACTIVE"
        assert r.verification == "verified"


# ---------------------------------------------------------------------------
# 4. Type narrowing succeeds
# ---------------------------------------------------------------------------
def test_facade_04_type_narrowing(facade_env):
    facade, ids = facade_env
    req = FacadeRetrievalRequest(
        query="Security Kernel",
        principal="human",
        types=["procedure"],
    )
    resp = facade.retrieve(req)
    assert resp.total_hits == 1
    assert resp.results[0].id == ids["proc_01"]
    assert resp.results[0].type == "procedure"


# ---------------------------------------------------------------------------
# 5. Broadening reject
# ---------------------------------------------------------------------------
def test_facade_05_broadening_reject(facade_env):
    facade, _ = facade_env
    req = FacadeRetrievalRequest(
        query="Security Kernel",
        principal="human",
        lifecycles=["ACTIVE", "REVIEW"],
    )
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        facade.retrieve(req)


# ---------------------------------------------------------------------------
# 6. Unknown lifecycle reject
# ---------------------------------------------------------------------------
def test_facade_06_unknown_lifecycle_reject(facade_env):
    facade, _ = facade_env
    req = FacadeRetrievalRequest(
        query="Security Kernel",
        principal="human",
        lifecycles=["NON_EXISTENT_STATE"],
    )
    with pytest.raises(FilterValidationError, match="Unknown lifecycle values"):
        facade.retrieve(req)


# ---------------------------------------------------------------------------
# 7. Unknown verification reject
# ---------------------------------------------------------------------------
def test_facade_07_unknown_verification_reject(facade_env):
    facade, _ = facade_env
    req = FacadeRetrievalRequest(
        query="Security Kernel",
        principal="human",
        verification=["unsupported_verification_tag"],
    )
    with pytest.raises(FilterValidationError, match="Unknown verification values"):
        facade.retrieve(req)


# ---------------------------------------------------------------------------
# 8. Missing principal reject
# ---------------------------------------------------------------------------
def test_facade_08_missing_principal_reject(facade_env):
    facade, _ = facade_env
    with pytest.raises(PrincipalValidationError, match="Principal is required"):
        facade.retrieve(FacadeRetrievalRequest(query="Security", principal=None))

    with pytest.raises(PrincipalValidationError, match="Principal cannot be empty"):
        facade.retrieve(FacadeRetrievalRequest(query="Security", principal=""))


# ---------------------------------------------------------------------------
# 9. Unknown principal reject
# ---------------------------------------------------------------------------
def test_facade_09_unknown_principal_reject(facade_env):
    facade, _ = facade_env
    with pytest.raises(PrincipalValidationError, match="Unknown principal"):
        facade.retrieve(FacadeRetrievalRequest(query="Security", principal="intruder_ai"))

    with pytest.raises(PrincipalValidationError, match="Invalid principal type"):
        facade.retrieve(FacadeRetrievalRequest(query="Security", principal=999))


# ---------------------------------------------------------------------------
# 10. Malicious/faulty retriever returning RAW
# ---------------------------------------------------------------------------
class MockLeakingRetriever:
    """Mock retriever returning raw, review, unverified, and archived notes."""
    def search_with_trace(self, *args, **kwargs) -> Tuple[List[Hit], Dict[str, Any]]:
        n_ok = Note(id="OK-1", path=Path("ok.md"), title="OK Note", body="text",
                    meta={"lifecycle": "ACTIVE", "verification": "verified", "type": "knowledge"})
        n_raw = Note(id="RAW-1", path=Path("raw.md"), title="Raw Note", body="text",
                     meta={"lifecycle": "RAW", "verification": "unverified", "type": "raw"})
        n_review = Note(id="REV-1", path=Path("rev.md"), title="Review Note", body="text",
                        meta={"lifecycle": "REVIEW", "verification": "verified", "type": "knowledge"})
        n_unverif = Note(id="UNV-1", path=Path("unv.md"), title="Unverif Note", body="text",
                         meta={"lifecycle": "ACTIVE", "verification": "unverified", "type": "knowledge"})
        n_arch = Note(id="ARC-1", path=Path("arc.md"), title="Archived Note", body="text",
                      meta={"lifecycle": "ARCHIVED", "verification": "verified", "type": "knowledge"})
        n_super = Note(id="SUP-1", path=Path("sup.md"), title="Superseded Note", body="text",
                       meta={"lifecycle": "SUPERSEDED", "verification": "verified", "type": "knowledge"})

        hits = [
            Hit(note=n_ok, score=1.0, signals={"bm25": 1}),
            Hit(note=n_raw, score=0.9, signals={"bm25": 2}),
            Hit(note=n_review, score=0.8, signals={"bm25": 3}),
            Hit(note=n_unverif, score=0.7, signals={"bm25": 4}),
            Hit(note=n_arch, score=0.6, signals={"bm25": 5}),
            Hit(note=n_super, score=0.5, signals={"bm25": 6}),
        ]
        return hits, {"mock_trace": True}


def test_facade_10_malicious_retriever_returning_raw():
    facade = ProductionRetrievalFacade(retriever=MockLeakingRetriever())  # type: ignore
    resp = facade.retrieve(FacadeRetrievalRequest(query="any", principal="human"))
    result_ids = {r.id for r in resp.results}
    assert "RAW-1" not in result_ids, "RAW notes must be eliminated by defense in depth"


# ---------------------------------------------------------------------------
# 11. Malicious retriever returning REVIEW
# ---------------------------------------------------------------------------
def test_facade_11_malicious_retriever_returning_review():
    facade = ProductionRetrievalFacade(retriever=MockLeakingRetriever())  # type: ignore
    resp = facade.retrieve(FacadeRetrievalRequest(query="any", principal="human"))
    result_ids = {r.id for r in resp.results}
    assert "REV-1" not in result_ids, "REVIEW notes must be eliminated by defense in depth"


# ---------------------------------------------------------------------------
# 12. Malicious retriever returning unverified
# ---------------------------------------------------------------------------
def test_facade_12_malicious_retriever_returning_unverified():
    facade = ProductionRetrievalFacade(retriever=MockLeakingRetriever())  # type: ignore
    resp = facade.retrieve(FacadeRetrievalRequest(query="any", principal="human"))
    result_ids = {r.id for r in resp.results}
    assert "UNV-1" not in result_ids, "unverified notes must be eliminated by defense in depth"


# ---------------------------------------------------------------------------
# 13. Malicious retriever returning ARCHIVED/SUPERSEDED
# ---------------------------------------------------------------------------
def test_facade_13_malicious_retriever_returning_archived_superseded():
    facade = ProductionRetrievalFacade(retriever=MockLeakingRetriever())  # type: ignore
    resp = facade.retrieve(FacadeRetrievalRequest(query="any", principal="human"))
    result_ids = {r.id for r in resp.results}
    assert "ARC-1" not in result_ids, "ARCHIVED notes must be eliminated"
    assert "SUP-1" not in result_ids, "SUPERSEDED notes must be eliminated"
    assert result_ids == {"OK-1"}, "Only the single valid ACTIVE+verified note must survive"


# ---------------------------------------------------------------------------
# 14. Deterministic repeated request
# ---------------------------------------------------------------------------
def test_facade_14_deterministic_repeated_request(facade_env):
    facade, _ = facade_env
    req = FacadeRetrievalRequest(query="Security Kernel Procedure Knowledge", principal="ai_agent", page_size=5)
    resp1 = facade.retrieve(req)
    resp2 = facade.retrieve(req)

    items1 = [(r.id, r.score) for r in resp1.results]
    items2 = [(r.id, r.score) for r in resp2.results]

    assert items1 == items2, "Identical requests must yield byte-for-byte deterministic results"


# ---------------------------------------------------------------------------
# 15. Principal propagation
# ---------------------------------------------------------------------------
def test_facade_15_principal_propagation(facade_env):
    facade, _ = facade_env

    for p in ("human", "ai_agent", "admin", DummyPrincipal.HUMAN, DummyPrincipal.AI_AGENT):
        expected_str = p.value if hasattr(p, "value") else str(p)
        resp = facade.retrieve(FacadeRetrievalRequest(query="Security", principal=p))
        assert resp.principal == expected_str
        assert resp.trace["caller_principal"] == expected_str


# ---------------------------------------------------------------------------
# 16. Trace propagation
# ---------------------------------------------------------------------------
def test_facade_16_trace_propagation(facade_env):
    facade, _ = facade_env
    req = FacadeRetrievalRequest(
        query="Security Architecture",
        principal="ai_agent",
        types=["knowledge"],
        request_id="REQ-AUDIT-99",
    )
    resp = facade.retrieve(req)

    trace = resp.trace
    assert trace["caller_principal"] == "ai_agent"
    assert trace["request_id"] == "REQ-AUDIT-99"
    assert trace["boundary_filters"]["lifecycles"] == ["ACTIVE"]
    assert trace["boundary_filters"]["verification"] == ["verified"]
    assert trace["boundary_filters"]["types"] == ["knowledge"]
    assert "facade" in trace
    assert trace["facade"]["returned_count"] == len(resp.results)


# ---------------------------------------------------------------------------
# 17. Pagination metadata preservation
# ---------------------------------------------------------------------------
def test_facade_17_pagination_metadata_preservation(facade_env):
    facade, ids = facade_env

    # Page 1 (page_size=1)
    req1 = FacadeRetrievalRequest(query="Security Kernel", principal="human", page_size=1, page_token="offset:0")
    resp1 = facade.retrieve(req1)
    assert len(resp1.results) == 1
    assert resp1.total_hits >= 2
    assert resp1.page_size == 1
    assert resp1.page_token == "offset:0"
    assert resp1.next_page_token == "offset:1"

    # Page 2 using next_page_token
    req2 = FacadeRetrievalRequest(query="Security Kernel", principal="human", page_size=1, page_token=resp1.next_page_token)
    resp2 = facade.retrieve(req2)
    assert len(resp2.results) == 1
    assert resp2.results[0].id != resp1.results[0].id


# ---------------------------------------------------------------------------
# 18. Zero storage mutation
# ---------------------------------------------------------------------------
def test_facade_18_zero_storage_mutation(tmp_path, facade_env):
    facade, _ = facade_env

    # Record snapshot before
    before = {str(p): (p.stat().st_mtime_ns, p.stat().st_size) for p in tmp_path.rglob("*") if p.is_file()}

    # Execute multiple queries
    facade.retrieve(FacadeRetrievalRequest(query="Security", principal="human"))
    facade.retrieve(FacadeRetrievalRequest(query="Procedure", principal="ai_agent", types=["procedure"]))
    try:
        facade.retrieve(FacadeRetrievalRequest(query="Security", principal="human", lifecycles=["REVIEW"]))
    except BoundaryViolationError:
        pass

    # Record snapshot after
    after = {str(p): (p.stat().st_mtime_ns, p.stat().st_size) for p in tmp_path.rglob("*") if p.is_file()}

    assert before == after, "ProductionRetrievalFacade must execute zero filesystem modifications"


# ---------------------------------------------------------------------------
# 19. Retriever not called for invalid requests
# ---------------------------------------------------------------------------
class SpyRetriever:
    def __init__(self):
        self.call_count = 0

    def search_with_trace(self, *args, **kwargs):
        self.call_count += 1
        return [], {}


def test_facade_19_retriever_not_called_for_invalid_requests():
    spy = SpyRetriever()
    facade = ProductionRetrievalFacade(retriever=spy)  # type: ignore

    # 1. Invalid principal
    with pytest.raises(PrincipalValidationError):
        facade.retrieve(FacadeRetrievalRequest(query="test", principal=""))
    assert spy.call_count == 0

    # 2. Broadening filter
    with pytest.raises(BoundaryViolationError):
        facade.retrieve(FacadeRetrievalRequest(query="test", principal="human", lifecycles=["REVIEW"]))
    assert spy.call_count == 0

    # 3. Invalid page_size
    with pytest.raises(FilterValidationError):
        facade.retrieve(FacadeRetrievalRequest(query="test", principal="human", page_size=-5))
    assert spy.call_count == 0


# ---------------------------------------------------------------------------
# 20. Facade does not perform authorization itself
# ---------------------------------------------------------------------------
def test_facade_20_facade_does_not_perform_authorization_itself(facade_env):
    facade, _ = facade_env

    # The facade accepts requests from any valid Principal name (human, ai_agent, admin)
    # without checking whether that principal has Operation.SEARCH permission or
    # invoking an Authorizer. Authorization is purely the caller's responsibility.
    resp_ai = facade.retrieve(FacadeRetrievalRequest(query="Security", principal="ai_agent"))
    resp_human = facade.retrieve(FacadeRetrievalRequest(query="Security", principal="human"))
    resp_admin = facade.retrieve(FacadeRetrievalRequest(query="Security", principal="admin"))

    assert resp_ai.principal == "ai_agent"
    assert resp_human.principal == "human"
    assert resp_admin.principal == "admin"
    assert resp_ai.total_hits == resp_human.total_hits == resp_admin.total_hits
