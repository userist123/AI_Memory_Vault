"""20_TESTS/regression/test_retrieval_boundary.py — P1.3 Integration Boundary Contract Tests.

Tests the boundary adapter between MemoryController and HybridRetriever:
- Valid request execution and response envelope
- Default secure filtering (ACTIVE + verified only)
- Narrowing allowed (types, explicit subset)
- Broadening rejected (BoundaryViolationError)
- Unknown lifecycles rejected (FilterValidationError)
- Unknown verification rejected (FilterValidationError)
- RAW rejected
- REVIEW rejected
- ARCHIVED rejected
- SUPERSEDED rejected
- RECONSOLIDATING rejected
- Determinism of ranking and results
- Request without principal rejected (PrincipalValidationError)
- Request with unknown principal rejected (PrincipalValidationError)
- Empty filter sets rejected
- Zero storage mutation / audit trace preservation
"""
from __future__ import annotations

import os
from enum import Enum
from pathlib import Path
from typing import Dict, List, Tuple

import pytest

from cognitive_core.hybrid_retrieval import HybridRetriever
from cognitive_core.retrieval_boundary import (
    ALLOWED_SECURE_LIFECYCLES,
    ALLOWED_SECURE_VERIFICATION,
    BoundaryViolationError,
    FilterValidationError,
    PrincipalValidationError,
    RetrievalBoundaryAdapter,
    RetrievalRequest,
    RetrievalResponse,
)
from cognitive_core.vault_index import VaultIndex


def _write_note(root: Path, rel_path: str, frontmatter: str, body: str) -> Path:
    p = root / rel_path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(f"---\n{frontmatter}\n---\n{body}", encoding="utf-8")
    return p


class DummyPrincipalEnum(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"


@pytest.fixture
def test_retriever(tmp_path) -> Tuple[HybridRetriever, Dict[str, str]]:
    """Build a test retriever with diverse lifecycle/verification/type notes."""
    notes_spec = [
        ("n_active_verif_proc", "11111111-0001-0001-0001-000000000001", "ACTIVE", "verified", "procedure", "Architecture Procedure Alpha"),
        ("n_active_verif_know", "11111111-0002-0002-0002-000000000002", "ACTIVE", "verified", "knowledge", "Architecture Knowledge Beta"),
        ("n_active_unverif",    "11111111-0003-0003-0003-000000000003", "ACTIVE", "unverified", "knowledge", "Architecture Unverified Gamma"),
        ("n_review_verif",      "11111111-0004-0004-0004-000000000004", "REVIEW", "verified", "knowledge", "Architecture Inflight Delta"),
        ("n_archived",          "11111111-0005-0005-0005-000000000005", "ARCHIVED", "verified", "knowledge", "Architecture Archived Epsilon"),
        ("n_raw",               "11111111-0006-0006-0006-000000000006", "RAW", "unverified", "raw", "Architecture Raw Zeta"),
        ("n_superseded",        "11111111-0007-0007-0007-000000000007", "SUPERSEDED", "verified", "knowledge", "Architecture Superseded Eta"),
        ("n_reconsolidating",   "11111111-0008-0008-0008-000000000008", "RECONSOLIDATING", "unverified", "knowledge", "Architecture Reconsolidating Theta"),
    ]

    for slug, nid, lc, verif, ntype, body in notes_spec:
        _write_note(
            tmp_path,
            f"01_ARCHITECTURE/{slug}.md",
            f"id: {nid}\ntype: {ntype}\nlifecycle: {lc}\nverification: {verif}",
            f"# {body}\n" + f"{body} content details for system design. " * 15,
        )

    idx = VaultIndex.load(
        tmp_path,
        lifecycles=["ACTIVE", "REVIEW", "ARCHIVED", "RAW", "RECONSOLIDATING", "SUPERSEDED", "NONE"],
        include_raw=True,
        include_archived=True,
    )
    return HybridRetriever(idx), {slug: nid for slug, nid, *_ in notes_spec}


# ---------------------------------------------------------------------------
# 1. Valid request & envelope structure
# ---------------------------------------------------------------------------
def test_boundary_valid_request(test_retriever):
    retriever, ids = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(
        query="Architecture",
        principal="human",
        top_k=5,
        audit_ref="AUD-100",
        request_id="REQ-001",
    )
    resp = adapter.execute(req)
    assert isinstance(resp, RetrievalResponse)
    assert resp.principal == "human"
    assert resp.query == "Architecture"
    assert resp.audit_ref == "AUD-100"
    assert resp.request_id == "REQ-001"
    assert resp.total_hits > 0
    assert "caller_principal" in resp.trace
    assert resp.trace["caller_principal"] == "human"


# ---------------------------------------------------------------------------
# 2. Default secure request returns ONLY ACTIVE + verified
# ---------------------------------------------------------------------------
def test_boundary_default_secure_returns_active_verified_only(test_retriever):
    retriever, ids = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(query="Architecture", principal="ai_agent", top_k=20)
    resp = adapter.execute(req)

    returned_ids = {h.note.id for h in resp.hits}
    assert ids["n_active_verif_proc"] in returned_ids
    assert ids["n_active_verif_know"] in returned_ids

    # Exclusions enforced
    assert ids["n_active_unverif"] not in returned_ids, "Unverified note must be excluded"
    assert ids["n_review_verif"] not in returned_ids, "REVIEW note must be excluded"
    assert ids["n_archived"] not in returned_ids, "ARCHIVED note must be excluded"
    assert ids["n_raw"] not in returned_ids, "RAW note must be excluded"
    assert ids["n_superseded"] not in returned_ids, "SUPERSEDED note must be excluded"
    assert ids["n_reconsolidating"] not in returned_ids, "RECONSOLIDATING note must be excluded"

    for hit in resp.hits:
        assert hit.note.lifecycle == "ACTIVE"
        assert hit.note.verification == "verified"


# ---------------------------------------------------------------------------
# 3. Narrowing is permitted (type filtering, explicit ACTIVE subset)
# ---------------------------------------------------------------------------
def test_boundary_narrowing_permitted(test_retriever):
    retriever, ids = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)

    # Narrowing by type
    req_type = RetrievalRequest(
        query="Architecture",
        principal="human",
        types=["procedure"],
    )
    resp_type = adapter.execute(req_type)
    assert resp_type.total_hits == 1
    assert resp_type.hits[0].note.id == ids["n_active_verif_proc"]
    assert resp_type.hits[0].note.type == "procedure"

    # Narrowing by explicit allowed lifecycle (ACTIVE)
    req_lc = RetrievalRequest(
        query="Architecture",
        principal="human",
        lifecycles=["ACTIVE"],
        verification=["verified"],
    )
    resp_lc = adapter.execute(req_lc)
    assert resp_lc.total_hits > 0
    for h in resp_lc.hits:
        assert h.note.lifecycle == "ACTIVE"
        assert h.note.verification == "verified"


# ---------------------------------------------------------------------------
# 4. Broadening is rejected (BoundaryViolationError)
# ---------------------------------------------------------------------------
def test_boundary_broadening_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)

    # Attempting to include REVIEW alongside ACTIVE
    req = RetrievalRequest(
        query="Architecture",
        principal="human",
        lifecycles=["ACTIVE", "REVIEW"],
    )
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 5. Unknown lifecycle rejected (FilterValidationError)
# ---------------------------------------------------------------------------
def test_boundary_unknown_lifecycle_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(
        query="Architecture",
        principal="human",
        lifecycles=["NON_EXISTENT_STATE"],
    )
    with pytest.raises(FilterValidationError, match="Unknown lifecycle values"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 6. Unknown verification rejected (FilterValidationError)
# ---------------------------------------------------------------------------
def test_boundary_unknown_verification_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(
        query="Architecture",
        principal="human",
        verification=["magic_verified"],
    )
    with pytest.raises(FilterValidationError, match="Unknown verification values"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 7. RAW lifecycle rejected
# ---------------------------------------------------------------------------
def test_boundary_raw_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(query="Architecture", principal="human", lifecycles=["RAW"])
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 8. REVIEW lifecycle rejected
# ---------------------------------------------------------------------------
def test_boundary_review_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(query="Architecture", principal="human", lifecycles=["REVIEW"])
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 9. ARCHIVED lifecycle rejected
# ---------------------------------------------------------------------------
def test_boundary_archived_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(query="Architecture", principal="human", lifecycles=["ARCHIVED"])
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 10. SUPERSEDED lifecycle rejected
# ---------------------------------------------------------------------------
def test_boundary_superseded_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(query="Architecture", principal="human", lifecycles=["SUPERSEDED"])
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 11. RECONSOLIDATING lifecycle rejected
# ---------------------------------------------------------------------------
def test_boundary_reconsolidating_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(query="Architecture", principal="human", lifecycles=["RECONSOLIDATING"])
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 12. Verification broadening (unverified) rejected
# ---------------------------------------------------------------------------
def test_boundary_unverified_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(query="Architecture", principal="human", verification=["unverified"])
    with pytest.raises(BoundaryViolationError, match="Broadening violation"):
        adapter.execute(req)


# ---------------------------------------------------------------------------
# 13. Empty filter sets rejected (bypass prevention)
# ---------------------------------------------------------------------------
def test_boundary_empty_filters_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)

    with pytest.raises(FilterValidationError, match="lifecycles filter cannot be empty"):
        adapter.execute(RetrievalRequest(query="Architecture", principal="human", lifecycles=[]))

    with pytest.raises(FilterValidationError, match="verification filter cannot be empty"):
        adapter.execute(RetrievalRequest(query="Architecture", principal="human", verification=[]))


# ---------------------------------------------------------------------------
# 14. Determinism: identical queries produce identical results and ordering
# ---------------------------------------------------------------------------
def test_boundary_determinism(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)
    req = RetrievalRequest(query="Architecture Procedure Beta Knowledge", principal="ai_agent", top_k=5)

    resp_1 = adapter.execute(req)
    resp_2 = adapter.execute(req)

    hits_1 = [(h.note.id, h.score) for h in resp_1.hits]
    hits_2 = [(h.note.id, h.score) for h in resp_2.hits]

    assert hits_1 == hits_2, "Repeated execution must produce identical ordered hits and scores."


# ---------------------------------------------------------------------------
# 15. Request without principal rejected
# ---------------------------------------------------------------------------
def test_boundary_missing_principal_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)

    with pytest.raises(PrincipalValidationError, match="Principal is required"):
        adapter.execute(RetrievalRequest(query="Architecture", principal=None))

    with pytest.raises(PrincipalValidationError, match="Principal cannot be empty"):
        adapter.execute(RetrievalRequest(query="Architecture", principal=""))


# ---------------------------------------------------------------------------
# 16. Request with unknown principal rejected
# ---------------------------------------------------------------------------
def test_boundary_unknown_principal_rejected(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)

    with pytest.raises(PrincipalValidationError, match="Unknown principal"):
        adapter.execute(RetrievalRequest(query="Architecture", principal="untrusted_guest"))

    with pytest.raises(PrincipalValidationError, match="Invalid principal type"):
        adapter.execute(RetrievalRequest(query="Architecture", principal=999))


# ---------------------------------------------------------------------------
# 17. Principal enum propagation
# ---------------------------------------------------------------------------
def test_boundary_principal_enum_propagation(test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)

    req = RetrievalRequest(query="Architecture", principal=DummyPrincipalEnum.AI_AGENT)
    resp = adapter.execute(req)
    assert resp.principal == "ai_agent"
    assert resp.trace["caller_principal"] == "ai_agent"


# ---------------------------------------------------------------------------
# 18. Zero storage mutation audit
# ---------------------------------------------------------------------------
def test_boundary_zero_storage_mutation(tmp_path, test_retriever):
    retriever, _ = test_retriever
    adapter = RetrievalBoundaryAdapter(retriever)

    # Snapshot files and mtimes before retrieval
    files_before = {}
    for p in tmp_path.rglob("*"):
        if p.is_file():
            files_before[str(p)] = (p.stat().st_mtime_ns, p.stat().st_size)

    # Execute search
    req = RetrievalRequest(query="Architecture Knowledge", principal="human")
    resp = adapter.execute(req)
    assert resp.total_hits > 0

    # Snapshot files and mtimes after retrieval
    files_after = {}
    for p in tmp_path.rglob("*"):
        if p.is_file():
            files_after[str(p)] = (p.stat().st_mtime_ns, p.stat().st_size)

    assert files_before == files_after, "RetrievalBoundaryAdapter must not modify any files or storage."
