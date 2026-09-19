"""Committed evidence from copyrighted books stays inside the quoting budget.

The repository is public and the books are not. Grounding evidence has to be
committed for a fabricated concept to be catchable, so it is kept to scattered
short sentences: `10_DOCUMENTATION/procedures/Quoting_From_Copyrighted_Sources.md`.
These tests hold that line, and show that each limit can actually fail.
"""
from __future__ import annotations

import importlib.util
import json
from collections import defaultdict
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
BOOK_ARTEFACTS = REPO / "07_EVALUATION" / "book_corpus_conversion"
POLICY = REPO / "10_DOCUMENTATION" / "procedures" / "Quoting_From_Copyrighted_Sources.md"

_spec = importlib.util.spec_from_file_location(
    "redact_long_quotes", REPO / "30_SCRIPTS" / "ingestion" / "redact_long_quotes.py")
redact = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(redact)

MAX_QUOTE_CHARS = redact.MAX_QUOTE_CHARS
MAX_QUOTES_PER_WORK = 40
MAX_CHARS_PER_WORK = 6000


def book_rows() -> list[tuple[str, dict]]:
    """(work, row) for every committed row of book-derived evidence."""
    rows = []
    for path in sorted(BOOK_ARTEFACTS.glob("*.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            continue
        if not isinstance(data, list):
            continue
        for row in data:
            if isinstance(row, dict) and any(k in row for k in redact.QUOTE_FIELDS):
                work = str(row.get("source_book") or path.name.split("_candidates")[0].split("_staging")[0])
                rows.append((work, row))
    return rows


def quotes_by_work(rows: list[tuple[str, dict]]) -> dict[str, set[str]]:
    by_work: dict[str, set[str]] = defaultdict(set)
    for work, row in rows:
        for field in redact.QUOTE_FIELDS:
            value = row.get(field)
            if isinstance(value, str) and value.strip():
                by_work[work].add(value.strip())
    return by_work


def test_the_policy_exists():
    assert POLICY.exists(), "the quoting policy these tests enforce is missing"


def test_no_committed_quote_exceeds_the_per_quote_limit():
    too_long = [
        (work, field, len(value))
        for work, row in book_rows()
        for field in redact.QUOTE_FIELDS
        if isinstance(value := row.get(field), str) and len(value) > MAX_QUOTE_CHARS
    ]
    assert too_long == [], f"quotes over {MAX_QUOTE_CHARS} chars: {too_long[:5]}"


def test_no_work_exceeds_its_quote_budget():
    over = {
        work: (len(quotes), sum(len(q) for q in quotes))
        for work, quotes in quotes_by_work(book_rows()).items()
        if len(quotes) > MAX_QUOTES_PER_WORK or sum(len(q) for q in quotes) > MAX_CHARS_PER_WORK
    }
    assert over == {}, f"works over budget (quotes, characters): {over}"


def test_a_redacted_quote_still_identifies_its_source():
    """Truncation keeps a prefix of the sentence and the hash of the whole of it."""
    original = "The native assets historically have been called cryptocurrencies or altcoins, " \
               "but we prefer the term cryptoassets, which is the term we will use throughout the book, " \
               "for reasons the next section sets out in detail."
    row, changed = redact.redact_row({"evidence_quote": original})
    assert changed == 1
    assert len(row["evidence_quote"]) <= MAX_QUOTE_CHARS
    assert original.startswith(row["evidence_quote"].rstrip("…"))
    assert len(row["evidence_quote_sha256"]) == 64
    assert row["evidence_quote_chars"] == len(original)


def test_a_quote_within_the_limit_is_left_alone():
    row, changed = redact.redact_row({"evidence_quote": "short enough"})
    assert changed == 0 and row == {"evidence_quote": "short enough"}


def test_truncation_cuts_on_a_word_boundary():
    cut = redact.truncate("alpha " * 60, limit=50)
    assert len(cut) <= 50 and not cut.rstrip("…").endswith("alph")


@pytest.mark.parametrize("length", [MAX_QUOTE_CHARS + 1, 2000])
def test_the_per_quote_check_can_fail(tmp_path, length):
    path = tmp_path / "x_candidates.json"
    path.write_text(json.dumps([{"evidence_quote": "x" * length}]), encoding="utf-8")
    assert redact.over_quota(path), "an over-long quote must be reported"


def test_the_per_quote_check_passes_a_compliant_file(tmp_path):
    path = tmp_path / "x_candidates.json"
    path.write_text(json.dumps([{"evidence_quote": "x" * MAX_QUOTE_CHARS}]), encoding="utf-8")
    assert redact.over_quota(path) == []


def test_the_per_work_check_can_fail():
    fake = [("some-book", {"evidence_quote": f"quote number {i} " + "y" * 150})
            for i in range(MAX_QUOTES_PER_WORK + 1)]
    quotes = quotes_by_work(fake)["some-book"]
    assert len(quotes) > MAX_QUOTES_PER_WORK
    assert sum(len(q) for q in quotes) > MAX_CHARS_PER_WORK
