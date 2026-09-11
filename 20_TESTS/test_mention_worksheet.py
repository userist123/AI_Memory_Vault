"""The worksheet exists so `occurrences` stops being a number an agent guesses.

Three runs produced three useless versions of it — every row at 1, every row at
exactly 3, the same concept counted twice from one chunk — because tracking
which of 107 sections mentioned a term is a memory task and was being answered
from memory. These tests cover the part that replaces the remembering, and the
part that deliberately refuses to replace the judging.
"""

from __future__ import annotations

import json
import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))

import mention_worksheet as W  # noqa: E402


def _chunk(i, text, low=False):
    return {"chunk_index": i, "heading": f"Pages {i}", "content": text,
            "low_prose": low, "prose_rate": 0.1 if low else 0.45}


def test_it_finds_the_form_the_book_actually_uses():
    """Written from the corpus, not from grammar.

    The candidate said "papert principle" and *The Society of Mind* writes
    "Papert's Principle"; it said "k-line" and the book writes "K-lines".
    Matching only the submitted string found neither, which is how a concept
    defined in three sections reported as appearing in none.
    """
    chunks = [
        _chunk(0, "Papert's Principle says that growth comes from new ways to "
                  "use what one already knows, rather than from new skills."),
        _chunk(1, "The brain contains agents called K-lines, which record what "
                  "other agents were doing at a certain moment in the past."),
    ]
    assert [m["chunk_index"] for m in W.find_mentions(chunks, "papert principle")] == [0]
    assert [m["chunk_index"] for m in W.find_mentions(chunks, "k-line")] == [1]


@pytest.mark.parametrize("concept,text", [
    ("k-line", "a K-line is a kind of wire"),
    ("k-line", "several K-lines were attached"),
    ("papert principle", "Papert's Principle explains it"),
    ("cross-exclusion", "the cross exclusion arrangement"),
    ("frame", "a frame is a data structure"),
])
def test_variant_forms_match(concept, text):
    assert W.find_mentions([_chunk(0, text)], concept)


def test_a_word_inside_another_word_does_not_match():
    """"frame" must not match "framework", or every concept recurs everywhere
    and the column goes back to meaning nothing."""
    assert W.find_mentions([_chunk(0, "a framework for reasoning")], "frame") == []
    assert W.find_mentions([_chunk(0, "the reframing of the problem")], "frame") == []


def test_index_pages_are_skipped():
    """An index names every term in the book and defines none of them."""
    chunks = [
        _chunk(0, "K-lines, 55, 79, 112, 240 Knowledge, 12, 44", low=True),
        _chunk(1, "A K-line records what agents were active at one moment."),
    ]
    assert [m["chunk_index"] for m in W.find_mentions(chunks, "k-line")] == [1]


def test_every_entry_starts_unanswered():
    """A mention is not an occurrence. "Agents" appears on nearly every page of
    *The Society of Mind* and is defined in a handful of them; nothing here can
    tell the difference, so nothing here guesses."""
    mentions = W.find_mentions([_chunk(0, "the k-line fires")], "k-line")
    assert mentions[0]["defines_the_term"] is None


def test_the_snippet_carries_enough_to_judge_on():
    """Chunk 18 of Minsky reads "your brain contains a host of agents called
    K-lines, which you can use to make records" — a definition. Chunk 26 reads
    "could come from relatively simple K-line mechanisms" — a mention. A reader
    can separate those in two seconds given the sentence around the hit."""
    body = "x" * 400 + " the K-line is a record of active agents " + "y" * 400
    snippet = W.find_mentions([_chunk(0, body)], "k-line")[0]["snippets"][0]
    assert "record of active agents" in snippet
    assert len(snippet) < len(body), "context, not the whole section"


def test_it_lists_what_has_not_been_looked_at_yet(tmp_path):
    """The worksheet's job: subtract what was already submitted, leave the rest
    as work. Minsky came back with nine concepts at exactly 3 while "agent"
    appeared in 78 sections — 75 of them never examined."""
    chunks = [_chunk(i, "the k-line records what agents did") for i in range(6)]
    chunks_file = tmp_path / "c.json"
    chunks_file.write_text(json.dumps({"chunks": chunks}), encoding="utf-8")
    cand = tmp_path / "cand.json"
    cand.write_text(json.dumps([
        {"concept": "k-line", "chunk_index": 0},
        {"concept": "k-line", "chunk_index": 2},
    ]), encoding="utf-8")
    out = tmp_path / "w.json"

    assert W.main(["--chunks", str(chunks_file), "--candidates", str(cand),
                   "--output", str(out)]) == 0

    row = json.loads(out.read_text(encoding="utf-8"))["concepts"][0]
    assert row["already_submitted_from"] == [0, 2]
    assert row["sections_mentioning"] == 6
    assert row["sections_not_yet_looked_at"] == [1, 3, 4, 5]


def test_the_worksheet_asks_the_question_it_cannot_answer(tmp_path):
    """It must not hand back a count. The whole failure it exists to fix was a
    number being copied rather than established."""
    chunks_file = tmp_path / "c.json"
    chunks_file.write_text(
        json.dumps({"chunks": [_chunk(0, "the k-line records")]}), encoding="utf-8"
    )
    cand = tmp_path / "cand.json"
    cand.write_text(json.dumps([{"concept": "k-line", "chunk_index": 0}]),
                    encoding="utf-8")
    out = tmp_path / "w.json"
    W.main(["--chunks", str(chunks_file), "--candidates", str(cand),
            "--output", str(out)])

    payload = json.loads(out.read_text(encoding="utf-8"))
    assert "DEFINE" in payload["question"]
    assert "occurrences" not in json.dumps(payload["concepts"]), (
        "the worksheet lists sections to look at; it never reports a count"
    )
