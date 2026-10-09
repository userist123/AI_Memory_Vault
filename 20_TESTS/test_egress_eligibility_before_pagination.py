"""A note the pack builder would reject at egress must not take a page slot.

Measured on benchmark v3 before this change (07_EVALUATION/reranker_envelope/
DEVIATIONS.md): the agent's page was shorter than five in 41 of 130 cases and
empty in 22. The notes filling those slots had passed the lifecycle floor and
ranked at the top, then `ContextPackBuilder._verify_and_reduce()` rejected them
for having no provenance mapping -- and nothing was backfilled, while up to 195
showable candidates sat unused. The trace labelled every such drop
BUDGET_EXCEEDED, which is how the real cause stayed hidden.

The fix mirrors the builder's deterministic rejection rules before pagination,
records the true reason, and backfills the page after a genuine budget drop.
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "x" * 40)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController, egress_ineligibility  # noqa: E402
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine  # noqa: E402
from observability.retrieval_trace import ExclusionReason  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

QUERY = "lifecycle floor retrieval policy"
# BM25 saturates term frequency and penalises length, so the "strong" note is the short exact
# match and the "weak" one carries the terms once under sixty filler words. Among tied fused
# scores the id breaks the tie, so the eligible note is named to sort last.
STRONG = "lifecycle floor retrieval policy"
WEAK = "retrieval policy " + " ".join(f"filler{i}" for i in range(60))


def note(nid: str, lifecycle: str, verification: str, content: str, provenance=None) -> dict:
    n = {
        "id": nid, "type": "knowledge", "category": "egress-test", "tags": [],
        "created": "2026-10-10", "updated": "2026-10-10", "lifecycle": lifecycle,
        "verification": verification, "confidence": "high", "relations": [], "content": content,
    }
    if provenance is not None:
        n["provenance"] = provenance
    return n


def controller_with(notes):
    storage = SQLiteStorageEngine(":memory:")
    for n in notes:
        storage.set(n["id"], n)  # straight into storage: propose() would (rightly) refuse a note without provenance
    return MemoryController(storage=storage, index=VaultIndex([]), enable_graph_expansion=False)


def decisions(pack: dict) -> dict:
    return (pack.get("retrieval_trace") or {}).get("decisions") or {}


# --- the rule itself -----------------------------------------------------------

def test_no_provenance_and_no_source_ref_is_ineligible():
    assert egress_ineligibility({"id": "a", "verification": "verified_source"}, allow_unverified=True) \
        is ExclusionReason.PROVENANCE_MISSING


def test_a_source_ref_is_enough_because_the_builder_derives_provenance_from_it():
    assert egress_ineligibility({"id": "a", "source_ref": "doc-1", "verification": "verified"}, allow_unverified=True) is None


def test_untrusted_status_is_ineligible_only_without_the_quarantine_view():
    n = {"id": "a", "provenance": {"source_type": "x", "source_ref": "y"}, "verification": "unverified"}
    assert egress_ineligibility(n, allow_unverified=False) is ExclusionReason.UNVERIFIED_AT_EGRESS
    assert egress_ineligibility(n, allow_unverified=True) is None


def test_trusted_statuses_pass_in_any_case():
    for status in ("verified", "VERIFIED_SOURCE", "Trusted", {"status": "safe"}):
        n = {"id": "a", "provenance": {"source_type": "x", "source_ref": "y"}, "verification": status}
        assert egress_ineligibility(n, allow_unverified=False) is None, status


# --- the page --------------------------------------------------------------------

def test_provenance_less_notes_no_longer_empty_the_page():
    """The R3-004 shape: the eight best-matching notes have no provenance; the one weaker note does."""
    notes = [note(f"review-{i}", "REVIEW", "verified", STRONG) for i in range(8)]
    notes.append(note("zz-active", "ACTIVE", "verified", WEAK, {"source_type": "execution", "source_ref": "this test"}))
    ctrl = controller_with(notes)
    pack = ctrl.search(Principal.AI_AGENT, QUERY, page_size=5)

    fused = [e["id"] for e in pack["candidate_trace"]["fused_ranking"]]
    assert fused[-1] == "zz-active" and fused[:5] == [f"review-{i}" for i in range(5)], (
        f"the fixture must rank the showable note outside the page, or this proves nothing: {fused}")
    # Verified against the unfixed controller on 2026-10-10: this exact fixture returned [].
    ids = [r.get("id") for r in pack["results"]]
    assert ids == ["zz-active"], f"the one showable note must be on the page; got {ids}"

    trace = pack["candidate_trace"]
    assert trace["egress_ineligible_count"] == 8
    reasons = {nid: d.get("reason_code") for nid, d in decisions(pack).items() if nid.startswith("review-")}
    assert set(reasons.values()) == {"PROVENANCE_MISSING"}, reasons
    assert "BUDGET_EXCEEDED" not in {d.get("reason_code") for d in decisions(pack).values()}, \
        "the old mislabel must be gone: nothing here exceeded any budget"


def test_the_gate_does_not_hide_review_notes_the_agent_may_see():
    """Counter-test: with provenance present, the same REVIEW notes are returned (quarantined, not dropped)."""
    prov = {"source_type": "official", "source_ref": "statute"}
    notes = [note(f"review-{i}", "REVIEW", "verified", STRONG, prov) for i in range(5)]
    notes.append(note("active-ok", "ACTIVE", "verified", WEAK, prov))
    ctrl = controller_with(notes)
    pack = ctrl.search(Principal.AI_AGENT, QUERY, page_size=5)
    ids = {r.get("id") for r in pack["results"]}
    assert len(ids) == 5 and any(i.startswith("review-") for i in ids), ids
    assert pack["candidate_trace"]["egress_ineligible_count"] == 0


def test_the_owner_is_gated_by_the_same_provenance_rule():
    """Provenance is required on every model-facing result, whoever asks; the owner's extra right is the quarantine view."""
    notes = [note("no-prov", "ACTIVE", "verified", STRONG)]
    ctrl = controller_with(notes)
    pack = ctrl.search(Principal.HUMAN, QUERY, page_size=5)
    assert pack["results"] == []
    assert pack["candidate_trace"]["egress_ineligible_count"] == 1


def test_backfill_is_recorded_and_bounded():
    prov = {"source_type": "execution", "source_ref": "t"}
    ctrl = controller_with([note(f"n-{i}", "ACTIVE", "verified", STRONG, prov) for i in range(8)])
    pack = ctrl.search(Principal.AI_AGENT, QUERY, page_size=5)
    assert len(pack["results"]) == 5
    assert pack["candidate_trace"]["backfill_rounds"] == 0, "nothing was dropped, so nothing was backfilled"
    assert pack.get("next_page_token"), "three notes remain, so a continuation token is owed"
