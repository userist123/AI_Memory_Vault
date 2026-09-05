"""20_TESTS/regression/test_retrieval_facade_p17_audit.py — P1.7 Audit Gap & Readiness Tests.

Covers specific regression gaps discovered during the P1.7 audit:
1. Strict fail-closed validation of page_token (rejects non-strings, non-numeric offsets, negative offsets, and malformed formats).
2. Strict fail-closed validation of page_size type (rejects booleans like True/False).
3. Multi-page pagination sequence verification (ensures fetch_k depth allows paging across > 10 items without truncation).
4. Offset beyond total hits (returns empty list without crashing or infinite looping).
5. Deterministic evaluation runner execution with zero filesystem mutations.
"""
from __future__ import annotations

from pathlib import Path
from typing import Dict, Tuple

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


@pytest.fixture
def multi_page_vault(tmp_path) -> Tuple[ProductionRetrievalFacade, Dict[str, str]]:
    """Creates a synthetic vault with 15 ACTIVE+verified notes sharing a common search term."""
    notes_spec = [
        (f"doc_{i:02d}", f"AAAAAAAA-0000-0000-0000-{i:012d}", "ACTIVE", "verified",
         f"Architecture Audit Module {i:02d}",
         f"Architecture audit module for system integrity invariant check. Distinct token {i}. " * 10)
        for i in range(1, 16)
    ]
    for slug, nid, lc, verif, title, body in notes_spec:
        _write_note(
            tmp_path,
            f"01_ARCHITECTURE/{slug}.md",
            f"id: {nid}\ntype: knowledge\nlifecycle: {lc}\nverification: {verif}",
            f"# {title}\n{body}",
        )

    idx = VaultIndex.load(tmp_path)
    retriever = HybridRetriever(idx)
    adapter = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=adapter)
    return facade, {slug: nid for slug, nid, *_ in notes_spec}


# ---------------------------------------------------------------------------
# 1. Page token format validation (Audit Gap 1)
# ---------------------------------------------------------------------------
def test_facade_p17_page_token_validation_rejects_malformed_tokens(multi_page_vault):
    facade, _ = multi_page_vault

    # Non-string page_token
    with pytest.raises(FilterValidationError, match="page_token must be a string"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token=123))  # type: ignore

    with pytest.raises(FilterValidationError, match="page_token must be a string"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token=["offset:0"]))  # type: ignore

    # Malformed offset string
    with pytest.raises(FilterValidationError, match="Invalid page_token offset value"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token="offset:abc"))

    with pytest.raises(FilterValidationError, match="Invalid page_token offset value"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token="offset:-5"))

    with pytest.raises(FilterValidationError, match="Invalid page_token offset value"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token="offset:"))

    # Malformed format (not 'offset:<int>' or digits)
    with pytest.raises(FilterValidationError, match="Invalid page_token format"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token="random_opaque_string"))


def test_facade_p17_page_token_validation_accepts_valid_tokens(multi_page_vault):
    facade, _ = multi_page_vault

    # Valid string tokens
    resp_none = facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token=None))
    resp_empty = facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token=""))
    resp_zero = facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token="offset:0"))
    resp_digit = facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_token="0"))

    assert len(resp_none.results) > 0
    assert [r.id for r in resp_none.results] == [r.id for r in resp_empty.results]
    assert [r.id for r in resp_none.results] == [r.id for r in resp_zero.results]
    assert [r.id for r in resp_none.results] == [r.id for r in resp_digit.results]


# ---------------------------------------------------------------------------
# 2. Boolean page_size validation (Audit Gap 2)
# ---------------------------------------------------------------------------
def test_facade_p17_boolean_page_size_validation(multi_page_vault):
    facade, _ = multi_page_vault

    with pytest.raises(FilterValidationError, match="page_size must be a positive integer"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_size=True))  # type: ignore

    with pytest.raises(FilterValidationError, match="page_size must be a positive integer"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_size=False))  # type: ignore

    with pytest.raises(FilterValidationError, match="page_size must be a positive integer"):
        facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_size="5"))  # type: ignore


# ---------------------------------------------------------------------------
# 3. Multi-page pagination sequence verification (Audit Gap 3)
# ---------------------------------------------------------------------------
def test_facade_p17_multi_page_pagination_sequence(multi_page_vault):
    facade, ids = multi_page_vault

    # 15 notes total. Page size = 5. Must cleanly traverse 3 pages.
    # Page 1
    req1 = FacadeRetrievalRequest(query="Architecture", principal="human", page_size=5, page_token=None)
    resp1 = facade.retrieve(req1)
    assert len(resp1.results) == 5
    assert resp1.next_page_token == "offset:5"

    # Page 2
    req2 = FacadeRetrievalRequest(query="Architecture", principal="human", page_size=5, page_token=resp1.next_page_token)
    resp2 = facade.retrieve(req2)
    assert len(resp2.results) == 5
    assert resp2.next_page_token == "offset:10"

    # Page 3
    req3 = FacadeRetrievalRequest(query="Architecture", principal="human", page_size=5, page_token=resp2.next_page_token)
    resp3 = facade.retrieve(req3)
    assert len(resp3.results) == 5
    assert resp3.next_page_token is None  # No more results after 15

    # Check zero overlap across all 3 pages
    ids_p1 = [r.id for r in resp1.results]
    ids_p2 = [r.id for r in resp2.results]
    ids_p3 = [r.id for r in resp3.results]

    assert len(set(ids_p1) & set(ids_p2)) == 0
    assert len(set(ids_p2) & set(ids_p3)) == 0
    assert len(set(ids_p1) & set(ids_p3)) == 0

    all_retrieved = ids_p1 + ids_p2 + ids_p3
    assert len(all_retrieved) == 15
    assert set(all_retrieved) == set(ids.values())


# ---------------------------------------------------------------------------
# 4. Offset beyond total hits (Audit Gap 4)
# ---------------------------------------------------------------------------
def test_facade_p17_offset_beyond_total_hits(multi_page_vault):
    facade, _ = multi_page_vault
    req = FacadeRetrievalRequest(query="Architecture", principal="human", page_size=5, page_token="offset:999")
    resp = facade.retrieve(req)

    assert resp.results == []
    assert resp.next_page_token is None
    assert resp.total_hits >= 0


# ---------------------------------------------------------------------------
# 5. Zero storage mutation during evaluation runner
# ---------------------------------------------------------------------------
def test_facade_p17_evaluation_runner_zero_storage_mutation(tmp_path, multi_page_vault):
    facade, _ = multi_page_vault

    before = {str(p): (p.stat().st_mtime_ns, p.stat().st_size) for p in tmp_path.rglob("*") if p.is_file()}

    # Run multiple queries and pagination
    token = None
    for _ in range(3):
        resp = facade.retrieve(FacadeRetrievalRequest(query="Architecture", principal="human", page_size=5, page_token=token))
        token = resp.next_page_token

    after = {str(p): (p.stat().st_mtime_ns, p.stat().st_size) for p in tmp_path.rglob("*") if p.is_file()}
    assert before == after, "P1.7 facade operations must leave storage 100% untouched"
