"""Merging a corpus is not merging a book.

Extracting twenty books one at a time produces the same concept from several of
them. The merge was written when there was one staging file and one book in it,
and it kept whichever row came first — so once a second book covered the same
ground, the better-evidenced row was discarded silently and counted as a
deduplication.

Measured on seven real books: 86 rows, 14 concepts appearing in more than one,
and two concepts placed in two different slots.
"""

from __future__ import annotations

import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))

import merge_candidate_concepts as M  # noqa: E402


def _rec(concept, book, slot="ontology", occurrences=1, evidence="e", definition="d"):
    return {
        "concept": concept, "source_book": book, "maps_to_slot": slot,
        "occurrences": occurrences, "evidence_quote": evidence,
        "definition": definition, "confidence_in_literature": 0.9,
    }


def test_the_same_concept_from_two_books_becomes_one_row():
    """"declarative memory" came from Schacter at 15 sections and from Squire
    & Kandel at 22. It is one concept and one review decision."""
    rows = M.combine_across_books([
        _rec("declarative memory", "schacter", occurrences=15),
        _rec("declarative memory", "squire_kandel", occurrences=22),
    ])
    assert len(rows) == 1
    assert rows[0]["source_book"] == "schacter; squire_kandel"


def test_occurrences_add_up_across_books():
    """Sections in different books are different sections. A concept two books
    each return to a dozen times is more load-bearing than one a single book
    mentions twice, and the sum is what says so."""
    rows = M.combine_across_books([
        _rec("priming", "schacter", slot="retrieval", occurrences=20),
        _rec("priming", "squire_kandel", slot="retrieval", occurrences=8),
    ])
    assert rows[0]["occurrences"] == 28


def test_the_better_evidenced_book_supplies_the_quote():
    """Only one evidence quote fits the table, and a reviewer reads that one.
    The old code kept whichever arrived first, which was arbitrary."""
    rows = M.combine_across_books([
        _rec("sensitization", "kandel_2001", occurrences=1, evidence="thin"),
        _rec("sensitization", "squire_kandel", occurrences=10, evidence="thorough"),
    ])
    assert rows[0]["evidence_quote"] == "thorough"
    assert rows[0]["occurrences"] == 11


def test_order_of_arrival_does_not_change_the_result():
    """The books are processed in whatever order the staging files are read."""
    a = M.combine_across_books([
        _rec("x", "one", occurrences=1, evidence="thin"),
        _rec("x", "two", occurrences=9, evidence="thorough"),
    ])
    b = M.combine_across_books([
        _rec("x", "two", occurrences=9, evidence="thorough"),
        _rec("x", "one", occurrences=1, evidence="thin"),
    ])
    assert a[0]["evidence_quote"] == b[0]["evidence_quote"] == "thorough"
    assert a[0]["occurrences"] == b[0]["occurrences"] == 10


def test_a_missing_or_junk_occurrence_count_does_not_crash_the_merge():
    rows = M.combine_across_books([
        _rec("x", "one", occurrences=""),
        _rec("x", "two", occurrences=None),
        _rec("x", "three", occurrences=4),
    ])
    assert rows[0]["occurrences"] == 4


def test_one_concept_in_two_slots_is_found():
    """Two real cases out of seven books.

    "semantic memory" arrived as `retrieval` from Soar, which treats it as a
    store you retrieve from, and as `ontology` from Schacter, which treats it
    as a distinct memory system. Both are defensible in their own book.
    """
    conflicts = M.find_slot_conflicts([
        _rec("semantic memory", "soar", slot="retrieval"),
        _rec("semantic memory", "schacter", slot="ontology"),
        _rec("priming", "schacter", slot="retrieval"),
        _rec("priming", "squire_kandel", slot="retrieval"),
    ])
    assert set(conflicts) == {"semantic memory"}, "agreement is not a conflict"
    assert set(conflicts["semantic memory"]) == {"retrieval", "ontology"}
    assert conflicts["semantic memory"]["retrieval"] == ["soar"]


def test_a_slot_conflict_stops_the_merge_before_anything_is_written(tmp_path):
    """Writing both would put one concept in the ontology twice, under two
    slots, as two unrelated things — the fragmentation the slot scheme exists
    to prevent. It is a decision nobody has made, not a failure to route
    around."""
    staging = tmp_path / "s.json"
    import json
    staging.write_text(json.dumps([
        _rec("semantic memory", "soar", slot="retrieval"),
        _rec("semantic memory", "schacter", slot="ontology"),
    ]), encoding="utf-8")

    with pytest.raises(M.SlotConflict) as excinfo:
        M.merge_candidate_concepts(str(staging), slots_dir=str(tmp_path))

    message = str(excinfo.value)
    assert "semantic memory" in message
    assert "retrieval" in message and "ontology" in message
    assert "soar" in message and "schacter" in message, (
        "naming the books is what makes the disagreement resolvable"
    )
    assert not list(tmp_path.glob("*.md")), "nothing may be written"
