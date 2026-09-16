"""The boundary between a working area and the ontology.

Commit 3de7fff3e wrote 201 concepts into the slot files in one step, every one
`proposed`. That is what the script was built to do; nothing was broken. 160 of
them were still unjudged five days later, and when someone finally read them,
47% did not belong in an ontology at all.

The slot files were a working area to the merge script and canonical ontology
to everything else. These tests cover the gate that makes them one thing.
"""
from __future__ import annotations

import json
import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))

from promotion_verdicts import (  # noqa: E402
    ADMITTING_VERDICTS,
    VERDICTS_SCHEMA_VERSION,
    VERDICT_MERGE,
    VERDICT_PROMOTE,
    VERDICT_REJECT,
    VERDICT_UNSURE,
    Verdict,
    VerdictError,
    VerdictManifest,
    load_manifest,
    partition,
)


def manifest(*entries: Verdict) -> VerdictManifest:
    return VerdictManifest(entries)


def row(concept: str) -> dict:
    return {"concept": concept, "maps_to_slot": "ontology", "occurrences": 5}


# --- what the gate admits ----------------------------------------------------

def test_only_promote_admits():
    """UNSURE is deliberately not admitting. A decision nobody could make is
    not a decision to proceed, and treating it as one is how "we'll look at it
    later" becomes a permanent ontology row."""
    assert ADMITTING_VERDICTS == {VERDICT_PROMOTE}


def test_a_concept_with_no_verdict_is_withheld_and_counted():
    """The group that matters. A run that silently dropped them would look
    identical to a run where they had been judged — which is precisely how 160
    rows became invisible."""
    admitted, withheld = partition(
        [row("cryptocurrency"), row("external memory")],
        manifest(Verdict("cryptocurrency", VERDICT_PROMOTE, "distinct mechanism")),
    )
    assert [r["concept"] for r in admitted] == ["cryptocurrency"]
    assert withheld == {"NO_VERDICT": ["external memory"]}


@pytest.mark.parametrize("verdict,extra", [
    (VERDICT_REJECT, {}),
    (VERDICT_UNSURE, {}),
    (VERDICT_MERGE, {"merge_into": "semantic memory"}),
])
def test_every_non_promote_verdict_withholds(verdict, extra):
    admitted, withheld = partition(
        [row("factual memory")],
        manifest(Verdict("factual memory", verdict, "a reason", **extra)),
    )
    assert admitted == []
    assert withheld == {verdict: ["factual memory"]}


# --- what the manifest refuses -----------------------------------------------

def test_a_verdict_without_a_reason_is_refused():
    """A verdict without one is a vote, and rows entering with nobody saying
    why is the failure this exists for."""
    with pytest.raises(VerdictError, match="is a vote"):
        Verdict("x", VERDICT_PROMOTE, "   ").validate()


def test_merge_must_name_its_target():
    """Otherwise the row is removed and its occurrences land nowhere."""
    with pytest.raises(VerdictError, match="must name what it merges into"):
        Verdict("factual memory", VERDICT_MERGE, "same as semantic").validate()


def test_two_different_verdicts_for_one_concept_are_refused():
    """One of them is wrong, and choosing silently is how the wrong one wins."""
    with pytest.raises(VerdictError, match="two different verdicts"):
        manifest(
            Verdict("x", VERDICT_PROMOTE, "a"),
            Verdict("x", VERDICT_REJECT, "b"),
        )


def test_an_identical_repeat_is_not_a_conflict():
    assert len(manifest(
        Verdict("x", VERDICT_PROMOTE, "a"), Verdict("x", VERDICT_PROMOTE, "a")
    )) == 1


def test_an_unknown_verdict_is_refused():
    with pytest.raises(VerdictError, match="is not a verdict"):
        Verdict("x", "MAYBE", "a reason").validate()


# --- formatting, the mistake made twice --------------------------------------

def test_markdown_emphasis_does_not_break_the_lookup():
    """Names arrive as ``**`architecture`**`` in review tables and bare in slot
    files. A comparison that misses the difference reports zero overlap between
    two lists of the same concepts."""
    m = manifest(Verdict("**`cryptocurrency`**", VERDICT_PROMOTE, "distinct"))
    assert m.admits("cryptocurrency")
    assert m.admits("  CRYPTOCURRENCY  ")


# --- the manifest file -------------------------------------------------------

def test_a_manifest_admitting_nothing_stops_the_merge(tmp_path):
    """Merging nothing is more likely a mismatched manifest than a decision."""
    import merge_candidate_concepts as M

    staging = tmp_path / "s.json"
    staging.write_text(json.dumps([
        {"concept": "x", "maps_to_slot": "ontology", "occurrences": 3,
         "evidence_quote": "q", "definition": "d", "source_book": "b"}
    ]), encoding="utf-8")
    verdicts = tmp_path / "v.json"
    verdicts.write_text(json.dumps({
        "schema_version": VERDICTS_SCHEMA_VERSION,
        "verdicts": [{"concept": "y", "verdict": "REJECT", "reason": "a phrase"}],
    }), encoding="utf-8")

    with pytest.raises(ValueError, match="admits none of the"):
        M.merge_candidate_concepts(str(staging), slots_dir=str(tmp_path),
                                   verdicts_file=str(verdicts))


def test_an_empty_manifest_is_refused(tmp_path):
    path = tmp_path / "v.json"
    path.write_text(json.dumps({
        "schema_version": VERDICTS_SCHEMA_VERSION, "verdicts": []
    }), encoding="utf-8")
    with pytest.raises(VerdictError, match="admits nothing"):
        load_manifest(path)


def test_a_wrong_schema_version_is_refused(tmp_path):
    path = tmp_path / "v.json"
    path.write_text(json.dumps({
        "schema_version": "something-else",
        "verdicts": [{"concept": "x", "verdict": "PROMOTE", "reason": "r"}],
    }), encoding="utf-8")
    with pytest.raises(VerdictError, match="unsupported manifest version"):
        load_manifest(path)


def test_the_manifest_round_trips(tmp_path):
    path = tmp_path / "v.json"
    path.write_text(json.dumps({
        "schema_version": VERDICTS_SCHEMA_VERSION,
        "source_review": "PROMOTION_REVIEW.md",
        "decided_by": "antigravity",
        "decided_at": "2026-09-12",
        "verdicts": [
            {"concept": "cryptocurrency", "verdict": "PROMOTE", "reason": "distinct"},
            {"concept": "external memory", "verdict": "REJECT", "reason": "umbrella"},
        ],
    }), encoding="utf-8")
    m = load_manifest(path)
    assert m.decided_by == "antigravity"
    assert m.counts() == {VERDICT_PROMOTE: 1, VERDICT_REJECT: 1}
    assert m.admits("cryptocurrency") and not m.admits("external memory")


# --- the ungated path still works --------------------------------------------

def test_without_a_manifest_nothing_changes(tmp_path):
    """The gate is opt-in. Existing callers must not break, and the report says
    plainly whether it ran."""
    import merge_candidate_concepts as M

    slot = tmp_path / "03_ontology.md"
    slot.write_text(
        "## Candidate concepts\n"
        "| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |\n"
        "|---|---|---|---|---|---|---|---|\n",
        encoding="utf-8",
    )
    staging = tmp_path / "s.json"
    staging.write_text(json.dumps([
        {"concept": "x", "maps_to_slot": "ontology", "occurrences": 3,
         "evidence_quote": "q", "definition": "d", "source_book": "b"}
    ]), encoding="utf-8")

    result = M.merge_candidate_concepts(str(staging), slots_dir=str(tmp_path))
    assert result["net_new_concepts_merged"] == 1
    assert result["gated"] is False
    assert result["withheld_by_verdict"] == {}
