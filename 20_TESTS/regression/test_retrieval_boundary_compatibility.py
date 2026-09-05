"""20_TESTS/regression/test_retrieval_boundary_compatibility.py — P1.5 Compatibility Gate Test Suite.

Demonstrates that RetrievalBoundaryAdapter can be seamlessly consumed by a
simulated MemoryController runtime caller in TEST MODE without touching
production controller code or bypassing security invariants:

1. Principal identity is preserved across request -> boundary -> trace -> response -> audit log.
2. ACTIVE + verified remains the maximum allowable trust boundary.
3. Caller may narrow (types, explicit ACTIVE/verified subset).
4. Caller cannot broaden (BoundaryViolationError on REVIEW, RAW, ARCHIVED, unverified).
5. Ineligible notes leaked by a faulty/mock retriever are strictly purged by the defense-in-depth sanitize loop.
6. Unknown lifecycle and verification tokens are rejected (FilterValidationError).
7. Deterministic ordering of ranked hits is strictly preserved.
8. Storage is completely immutable (0 file creations, 0 modifications, 0 mtime changes).
9. Boundary validation errors fail-closed BEFORE reaching the retriever (retriever is never invoked).
10. Trace comprehensively preserves request ID, caller principal, query fingerprint, and applied filter states.
"""
from __future__ import annotations

import hashlib
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
    RetrievalRequest,
    SimulatedAuditEvent,
    StubMemoryControllerConsumer,
)
from cognitive_core.vault_index import Note, VaultIndex


def _write_note(root: Path, rel_path: str, frontmatter: str, body: str) -> Path:
    p = root / rel_path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(f"---\n{frontmatter}\n---\n{body}", encoding="utf-8")
    return p


class MockPrincipal(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"


@pytest.fixture
def compatibility_env(tmp_path) -> Tuple[StubMemoryControllerConsumer, HybridRetriever, Dict[str, str]]:
    """Builds a full test fixture for the compatibility gate."""
    notes_spec = [
        ("proc_active", "00000000-0001-0001-0001-000000000001", "ACTIVE", "verified", "procedure",
         "Security Kernel Verification Procedure", "Step 1 verify kernel invariants. Step 2 check attestation. "),
        ("know_active", "00000000-0002-0002-0002-000000000002", "ACTIVE", "verified", "knowledge",
         "Security Architecture Knowledge", "Durable architecture knowledge of memory isolation and boundaries. "),
        ("know_unverif", "00000000-0003-0003-0003-000000000003", "ACTIVE", "unverified", "knowledge",
         "Unverified Security Draft", "Draft notes with unconfirmed security telemetry. "),
        ("review_note", "00000000-0004-0004-0004-000000000004", "REVIEW", "verified", "knowledge",
         "Inflight Review Note", "In-flight note proposed by agent awaiting human review. "),
        ("archived_note", "00000000-0005-0005-0005-000000000005", "ARCHIVED", "verified", "knowledge",
         "Deprecated Legacy Security", "Obsolete deprecated instructions from previous vault major version. "),
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
    consumer = StubMemoryControllerConsumer(adapter)
    return consumer, retriever, {slug: nid for slug, nid, *_ in notes_spec}


# ---------------------------------------------------------------------------
# 1. Principal is not lost across the pipeline
# ---------------------------------------------------------------------------
def test_compatibility_principal_not_lost(compatibility_env):
    consumer, _, _ = compatibility_env

    for p in ("human", "ai_agent", "admin", MockPrincipal.AI_AGENT, MockPrincipal.ADMIN):
        expected_str = p.value if hasattr(p, "value") else str(p)
        res = consumer.search(principal=p, query="Security", page_size=5)

        assert res["agentId"] == expected_str, "Consumer response must reflect caller principal"
        assert res["trace"]["caller_principal"] == expected_str, "Trace must preserve caller principal"

        # Check simulated audit log entry
        last_audit = consumer.audit_log[-1]
        assert last_audit.principal == expected_str
        assert last_audit.success is True


# ---------------------------------------------------------------------------
# 2. ACTIVE + verified remains the maximum allowable trust boundary
# ---------------------------------------------------------------------------
def test_compatibility_active_verified_remains_maximum_boundary(compatibility_env):
    consumer, _, ids = compatibility_env
    res = consumer.search(principal="human", query="Security", page_size=10)

    returned_ids = {item["id"] for item in res["results"]}
    assert ids["proc_active"] in returned_ids
    assert ids["know_active"] in returned_ids

    # Must NOT contain unverified, review, or archived notes
    assert ids["know_unverif"] not in returned_ids, "Unverified note must never leak across boundary"
    assert ids["review_note"] not in returned_ids, "REVIEW note must never leak across boundary"
    assert ids["archived_note"] not in returned_ids, "ARCHIVED note must never leak across boundary"

    for r in res["results"]:
        assert r["lifecycle"] == "ACTIVE"
        assert r["verification"] == "verified"


# ---------------------------------------------------------------------------
# 3. Caller can narrow (types, explicit boundary subset)
# ---------------------------------------------------------------------------
def test_compatibility_caller_can_narrow(compatibility_env):
    consumer, _, ids = compatibility_env

    # Narrowing by type
    res_type = consumer.search(principal="human", query="Security", types=["procedure"])
    assert res_type["totalHits"] == 1
    assert res_type["results"][0]["id"] == ids["proc_active"]
    assert res_type["results"][0]["type"] == "procedure"

    # Narrowing by explicit allowed lifecycle subset
    res_lc = consumer.search(principal="ai_agent", query="Security", lifecycles=["ACTIVE"], verification=["verified"])
    assert res_lc["totalHits"] > 0
    for r in res_lc["results"]:
        assert r["lifecycle"] == "ACTIVE"
        assert r["verification"] == "verified"


# ---------------------------------------------------------------------------
# 4. Caller cannot broaden (BoundaryViolationError + audit recording)
# ---------------------------------------------------------------------------
def test_compatibility_caller_cannot_broaden(compatibility_env):
    consumer, _, _ = compatibility_env

    # 1. Attempting to include REVIEW
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        consumer.search(principal="human", query="Security", lifecycles=["ACTIVE", "REVIEW"])

    last_audit = consumer.audit_log[-1]
    assert last_audit.success is False
    assert "BoundaryViolationError" in last_audit.details["error_type"]

    # 2. Attempting to retrieve unverified
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        consumer.search(principal="human", query="Security", verification=["unverified"])

    last_audit = consumer.audit_log[-1]
    assert last_audit.success is False


# ---------------------------------------------------------------------------
# 5. Ineligible results are eliminated even if fake retriever leaks them
# ---------------------------------------------------------------------------
class LeakingRetrieverStub:
    """Simulates a broken or malicious retriever that attempts to return forbidden notes."""
    def search_with_trace(self, *args, **kwargs) -> Tuple[List[Hit], Dict[str, Any]]:
        n_valid = Note(id="V-1", path=Path("valid.md"), title="Valid Note", body="ok",
                       meta={"lifecycle": "ACTIVE", "verification": "verified", "type": "knowledge"})
        n_leaked_review = Note(id="L-1", path=Path("review.md"), title="Leaked Review", body="bad",
                               meta={"lifecycle": "REVIEW", "verification": "verified", "type": "knowledge"})
        n_leaked_raw = Note(id="L-2", path=Path("raw.md"), title="Leaked Raw", body="bad",
                            meta={"lifecycle": "RAW", "verification": "unverified", "type": "raw"})
        n_leaked_unverif = Note(id="L-3", path=Path("unverif.md"), title="Leaked Unverif", body="bad",
                                meta={"lifecycle": "ACTIVE", "verification": "unverified", "type": "knowledge"})

        hits = [
            Hit(note=n_valid, score=1.0, signals={"bm25": 1}),
            Hit(note=n_leaked_review, score=0.9, signals={"bm25": 2}),
            Hit(note=n_leaked_raw, score=0.8, signals={"bm25": 3}),
            Hit(note=n_leaked_unverif, score=0.7, signals={"bm25": 4}),
        ]
        return hits, {"trace_id": "test-trace"}


def test_compatibility_ineligible_results_eliminated_if_retriever_leaks():
    leaking_retriever = LeakingRetrieverStub()
    adapter = RetrievalBoundaryAdapter(leaking_retriever)  # type: ignore
    consumer = StubMemoryControllerConsumer(adapter)

    res = consumer.search(principal="human", query="any query", page_size=10)

    # Only n_valid must survive! All 3 leaked notes must be dropped by defense-in-depth
    assert res["totalHits"] == 1
    assert len(res["results"]) == 1
    assert res["results"][0]["id"] == "V-1"
    assert res["results"][0]["lifecycle"] == "ACTIVE"
    assert res["results"][0]["verification"] == "verified"


# ---------------------------------------------------------------------------
# 6. Unknown lifecycle / verification are rejected (FilterValidationError)
# ---------------------------------------------------------------------------
def test_compatibility_unknown_lifecycle_verification_rejected(compatibility_env):
    consumer, _, _ = compatibility_env

    with pytest.raises(FilterValidationError, match="Unknown lifecycle values"):
        consumer.search(principal="human", query="Security", lifecycles=["INVALID_LC"])

    with pytest.raises(FilterValidationError, match="Unknown verification values"):
        consumer.search(principal="human", query="Security", verification=["INVALID_VERIF"])


# ---------------------------------------------------------------------------
# 7. Deterministic ordering is preserved
# ---------------------------------------------------------------------------
def test_compatibility_deterministic_ordering_preserved(compatibility_env):
    consumer, _, _ = compatibility_env

    res1 = consumer.search(principal="ai_agent", query="Security Kernel Architecture", page_size=5)
    res2 = consumer.search(principal="ai_agent", query="Security Kernel Architecture", page_size=5)

    order1 = [(r["id"], r["score"]) for r in res1["results"]]
    order2 = [(r["id"], r["score"]) for r in res2["results"]]

    assert order1 == order2, "Consumer results must be identically ordered and scored across identical calls"


# ---------------------------------------------------------------------------
# 8. Storage is completely immutable
# ---------------------------------------------------------------------------
def test_compatibility_zero_storage_mutation(tmp_path, compatibility_env):
    consumer, _, _ = compatibility_env

    # Record all file mtimes and sizes
    before = {str(p): (p.stat().st_mtime_ns, p.stat().st_size) for p in tmp_path.rglob("*") if p.is_file()}

    # Execute a variety of searches
    consumer.search(principal="human", query="Security", page_size=5)
    consumer.search(principal="ai_agent", query="Procedure", types=["procedure"])
    try:
        consumer.search(principal="human", query="Security", lifecycles=["REVIEW"])
    except BoundaryViolationError:
        pass

    after = {str(p): (p.stat().st_mtime_ns, p.stat().st_size) for p in tmp_path.rglob("*") if p.is_file()}

    assert before == after, "Retrieval operations through boundary gate must never modify storage"


# ---------------------------------------------------------------------------
# 9. Boundary validation errors do NOT reach the retriever
# ---------------------------------------------------------------------------
class SpyRetriever:
    def __init__(self):
        self.call_count = 0

    def search_with_trace(self, *args, **kwargs):
        self.call_count += 1
        return [], {}


def test_compatibility_boundary_errors_do_not_reach_retriever():
    spy = SpyRetriever()
    adapter = RetrievalBoundaryAdapter(spy)  # type: ignore
    consumer = StubMemoryControllerConsumer(adapter)

    # 1. Invalid principal
    with pytest.raises(PrincipalValidationError):
        consumer.search(principal="", query="test")
    assert spy.call_count == 0, "Retriever must not be called if principal validation fails"

    # 2. Broadening attempt
    with pytest.raises(BoundaryViolationError):
        consumer.search(principal="human", query="test", lifecycles=["REVIEW"])
    assert spy.call_count == 0, "Retriever must not be called if lifecycle boundary is breached"

    # 3. Empty filter set
    with pytest.raises(FilterValidationError):
        consumer.search(principal="human", query="test", lifecycles=[])
    assert spy.call_count == 0, "Retriever must not be called if filter set is empty"


# ---------------------------------------------------------------------------
# 10. Trace preserves request, caller principal, and filter state
# ---------------------------------------------------------------------------
def test_compatibility_trace_preserves_request_principal_and_filter_state(compatibility_env):
    consumer, _, _ = compatibility_env
    query = "Security Architecture"
    query_fp = hashlib.sha256(query.encode("utf-8")).hexdigest()

    res = consumer.search(
        principal="ai_agent",
        query=query,
        page_size=3,
        types=["knowledge"],
        request_id="REQ-TEST-42",
    )

    assert res["requestId"] == "REQ-TEST-42"
    assert res["auditRef"] == query_fp
    assert res["effectiveFilters"]["lifecycles"] == ["ACTIVE"]
    assert res["effectiveFilters"]["verification"] == ["verified"]
    assert res["effectiveFilters"]["types"] == ["knowledge"]

    trace = res["trace"]
    assert trace["caller_principal"] == "ai_agent"
    assert trace["audit_ref"] == query_fp
    assert trace["request_id"] == "REQ-TEST-42"
    assert trace["boundary_filters"]["lifecycles"] == ["ACTIVE"]
    assert trace["boundary_filters"]["verification"] == ["verified"]
    assert trace["boundary_filters"]["types"] == ["knowledge"]
