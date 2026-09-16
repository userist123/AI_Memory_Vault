"""A filter on a hardcoded pair does not report what it skipped.

Ten rows carrying `status=unverified_source` sat in the ontology for five days
and no audit saw them. The state-accuracy test, the conflict checker, the purge
catalogue and several scripts written while investigating all filtered on
`proposed` and `promoted`, because those were the statuses anyone knew about.

They agreed on 203 against a real 213 — not because they shared a bug, but
because they shared an assumption. Several independent tools returning the same
wrong number is more convincing than one, which is what made it survive.
"""
from __future__ import annotations

import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))

from slot_rows import (  # noqa: E402
    ACTIONABLE_STATUSES,
    KNOWN_STATUSES,
    STATUS_PROMOTED,
    STATUS_PROPOSED,
    STATUS_UNVERIFIED_SOURCE,
    UnknownSlotStatus,
    count_by_status,
    parse_row,
    read_all,
)

ROW = "| a concept | book | 0.95 | {status} | 2026-09-12 | {note} | a quote | 7 |"


def test_an_undeclared_status_raises_instead_of_being_skipped():
    """The whole mechanism. A status nobody declared cannot appear in a file
    and be quietly dropped by everything that reads it."""
    with pytest.raises(UnknownSlotStatus, match="which no tool declares"):
        parse_row(ROW.format(status="archived", note=""), "03_ontology.md", 12)


def test_the_refusal_names_the_file_and_line():
    """A count sends nobody anywhere; a location does."""
    with pytest.raises(UnknownSlotStatus) as excinfo:
        parse_row(ROW.format(status="draft", note=""), "05_state.md", 47)
    message = str(excinfo.value)
    assert "05_state.md:47" in message
    assert "draft" in message
    assert "unverified_source" in message, "the known set must be shown"


@pytest.mark.parametrize("status", sorted(KNOWN_STATUSES))
def test_every_declared_status_parses(status):
    row = parse_row(ROW.format(status=status, note=""), "03_ontology.md", 5)
    assert row is not None and row.status == status


def test_unverified_source_is_known_but_not_actionable():
    """It is not a weaker `proposed`. It means the extractor that produced the
    row was replaced and the replacement could not reproduce it — the
    provenance chain broke, so the row cannot be re-derived or defended.

    Collapsing the two is exactly how it became invisible.
    """
    assert STATUS_UNVERIFIED_SOURCE in KNOWN_STATUSES
    assert STATUS_UNVERIFIED_SOURCE not in ACTIONABLE_STATUSES
    assert ACTIONABLE_STATUSES == {STATUS_PROPOSED}


def test_counting_reports_what_is_there_not_what_was_expected():
    rows = [
        parse_row(ROW.format(status=s, note=""), "f.md", i)
        for i, s in enumerate([STATUS_PROPOSED, STATUS_PROPOSED,
                               STATUS_PROMOTED, STATUS_UNVERIFIED_SOURCE])
    ]
    assert count_by_status(rows) == {
        STATUS_PROPOSED: 2, STATUS_PROMOTED: 1, STATUS_UNVERIFIED_SOURCE: 1,
    }


def test_markdown_emphasis_is_stripped_from_concept_names():
    """Names arrive as ``**`architecture`**`` in some documents and bare in
    others. A comparison that misses the difference reports zero overlap
    between two lists of the same concepts — a mistake made twice here."""
    row = parse_row(
        "| **`architecture`** | book | 0.9 | proposed | 2026-09-12 | | q | 4 |",
        "13_agents.md", 35,
    )
    assert row.concept == "architecture"


def test_a_separator_or_header_is_not_a_row():
    assert parse_row("|---|---|---|---|", "f.md", 2) is None
    assert parse_row("| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |", "f.md", 1) is None
    assert parse_row("some prose", "f.md", 3) is None


def test_the_real_ontology_has_no_undeclared_status():
    """Runs against the live slot files. If someone introduces a fourth status
    without declaring it, this fails here rather than in a count that silently
    shrinks."""
    rows = read_all(_REPO / "01_ARCHITECTURE" / "ontology" / "slots")
    assert rows, "the slot files must be readable"
    counts = count_by_status(rows)
    assert set(counts) <= KNOWN_STATUSES
    assert sum(counts.values()) == len(rows)


def test_every_promoted_row_carries_a_note_and_no_proposed_row_does():
    """The invariant that makes purging safe: a row with a note has a REVIEW
    document and graph edges behind it, and deleting it leaves orphans."""
    rows = read_all(_REPO / "01_ARCHITECTURE" / "ontology" / "slots")
    for row in rows:
        if row.status == STATUS_PROMOTED:
            assert row.has_note, f"{row.slot_file}:{row.line_number} promoted without a note"
        else:
            assert not row.has_note, (
                f"{row.slot_file}:{row.line_number} is {row.status} but carries "
                "a note id — deleting it would orphan the document"
            )
