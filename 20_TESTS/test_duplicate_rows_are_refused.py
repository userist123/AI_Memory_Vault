"""Summing occurrences is right for two books and catastrophic for one row twice.

`combine_across_books` adds occurrences across rows sharing a concept and a
slot, because a term defined in fifteen sections of one book and twenty-two of
another is defined in thirty-seven. Nothing in the rows distinguishes that from
the same row loaded twice.

It happened. `staging/` held twenty-one per-book files and an aggregate carrying
the same 264 rows, so a glob over the directory loaded 528. Measured on
"declarative memory": occurrences `[15, 22, 8, 15, 22, 8]` summing to 90
against a true value of 45.

The ontology escaped by luck. The promotion run took the aggregate as a single
input file while the conflict check globbed the directory — two callers, two
answers, no complaint from either.
"""
from __future__ import annotations

import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))

from merge_candidate_concepts import (  # noqa: E402
    DuplicateRows,
    combine_across_books,
    find_duplicate_rows,
)


def row(concept="declarative memory", book="schacter", evidence="a quote",
        occurrences=15, slot="ontology"):
    return {
        "concept": concept, "source_book": book, "maps_to_slot": slot,
        "occurrences": occurrences, "evidence_quote": evidence,
        "definition": "a definition", "confidence_in_literature": 0.9,
    }


def test_the_measured_case_is_refused():
    """The exact shape that produced 90 instead of 45."""
    rows = [
        row(book="schacter", occurrences=15),
        row(book="squire", occurrences=22),
        row(book="wcs_1488", occurrences=8),
    ]
    doubled = rows + [dict(r) for r in rows]

    assert combine_across_books(rows)[0]["occurrences"] == 45
    with pytest.raises(DuplicateRows, match="multiply occurrences"):
        combine_across_books(doubled)


def test_identity_is_concept_book_and_evidence():
    """A book that genuinely defines a term twice produces two different
    quotes. Agreeing on all three means the row was loaded twice."""
    assert find_duplicate_rows([row(), row()]) == {("declarative memory", "schacter"): 2}
    assert find_duplicate_rows([row(evidence="first"), row(evidence="second")]) == {}


def test_two_books_are_still_two_observations():
    """The behaviour this guard must not break: the same concept from
    different books sums, because different books are different sections."""
    combined = combine_across_books([
        row(book="schacter", occurrences=15),
        row(book="squire", occurrences=22),
    ])
    assert len(combined) == 1
    assert combined[0]["occurrences"] == 37


def test_the_same_book_with_different_evidence_still_sums():
    """A book defining a term in two places is two sections, and the quotes
    differ. That is the case the guard must let through."""
    combined = combine_across_books([
        row(book="schacter", evidence="first passage", occurrences=8),
        row(book="schacter", evidence="second passage", occurrences=7),
    ])
    assert combined[0]["occurrences"] == 15


def test_the_refusal_names_what_is_duplicated():
    """A count alone sends nobody anywhere. The message carries concept and
    book so the offending file is findable."""
    with pytest.raises(DuplicateRows) as excinfo:
        combine_across_books([row(concept="priming", book="squire"),
                              row(concept="priming", book="squire")])
    message = str(excinfo.value)
    assert "priming" in message and "squire" in message
    assert "2 identical rows" in message


def test_a_long_list_of_duplicates_is_truncated_not_dumped():
    """528 rows produced 54 duplicated concepts. A traceback carrying all of
    them buries the one line that says what to do."""
    rows = []
    for i in range(30):
        rows += [row(concept=f"concept {i}", book="b", evidence=f"q{i}")] * 2
    with pytest.raises(DuplicateRows) as excinfo:
        combine_across_books(rows)
    message = str(excinfo.value)
    assert "and 20 more" in message
    assert message.count("identical rows") == 10


def test_whitespace_does_not_make_two_rows_look_different():
    """Re-serialising a file can change whitespace inside a quote without
    changing the quote."""
    assert find_duplicate_rows([
        row(evidence="a  quote   with   spacing"),
        row(evidence="a quote with spacing"),
    ]) == {("declarative memory", "schacter"): 2}
