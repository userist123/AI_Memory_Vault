"""An agent's candidates go through the same gates as a model's.

The point of this path is to let a capable agent read the chunks directly
instead of orchestrating a 7-8B local model. The point of these tests is that
"capable" does not mean "unchecked": every gate exists because something got
past its absence, and a fabricated citation from a better model is still a
fabricated citation.
"""

from __future__ import annotations

import json
import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))
sys.path.insert(0, str(_REPO / "03_IMPLEMENTATION" / "packages"))

import gate_agent_candidates as G  # noqa: E402

CHUNK = (
    "We introduce quenched harmonic buffering, a scheme in which the agent "
    "holds a decaying replica of each observation and releases it only once "
    "the replica's amplitude falls below the acceptance threshold."
)
EVIDENCE = (
    "the agent holds a decaying replica of each observation and releases it "
    "only once the replica's amplitude falls below the acceptance threshold"
)
GOOD = {
    "chunk_index": 0,
    "concept": "Quenched harmonic buffering",
    "definition": (
        "A scheme that delays committing an observation until its stored copy "
        "has decayed past a set point, which bounds how often it writes."
    ),
    "evidence": EVIDENCE,
    "slot": "consolidation",
    "confidence": 0.7,
}


@pytest.fixture
def book(tmp_path):
    p = tmp_path / "book.txt"
    p.write_text(f"# Section One\n\n{CHUNK}\n", encoding="utf-8")
    return p


def _gate(tmp_path, book, candidates):
    cand = tmp_path / "agent.json"
    cand.write_text(json.dumps(candidates), encoding="utf-8")
    out = tmp_path / "out.json"
    rej = tmp_path / "rej.json"
    sys.argv = [
        "x", "gate", "--input-file", str(book), "--candidates", str(cand),
        "--source-book", "b", "--output-file", str(out),
        "--rejects-file", str(rej), "--agent-label", "test-agent",
    ]
    assert G.main() == 0
    return (
        json.loads(out.read_text(encoding="utf-8")),
        json.loads(rej.read_text(encoding="utf-8")),
    )


def test_a_grounded_candidate_is_kept_and_labelled(tmp_path, book):
    kept, rejected = _gate(tmp_path, book, [GOOD])
    assert rejected == []
    assert len(kept) == 1
    assert kept[0]["extraction_method"] == "agent_direct"
    assert kept[0]["model"] == "test-agent", (
        "a later reader must be able to tell agent output from provider output"
    )
    assert kept[0]["evidence_quote"] == EVIDENCE


def test_a_fabricated_quote_is_refused_however_capable_the_agent(tmp_path, book):
    """The gate that caught three fabrications in one six-chunk run."""
    kept, rejected = _gate(
        tmp_path, book, [dict(GOOD, evidence="Vitter (1985) established this.")]
    )
    assert kept == []
    assert rejected[0]["reason"] == "evidence_not_in_source"


def test_a_reworded_source_sentence_is_refused(tmp_path, book):
    kept, rejected = _gate(tmp_path, book, [dict(
        GOOD,
        definition=(
            "The agent holds a decaying replica of each observation and "
            "releases it only once the replica's amplitude falls under the "
            "acceptance threshold."
        ),
    )])
    assert kept == []
    assert rejected[0]["reason"] in {"verbatim_ngram", "paraphrase_shallow"}


def test_an_invented_slot_is_refused(tmp_path, book):
    kept, rejected = _gate(tmp_path, book, [dict(GOOD, slot="neuroscience")])
    assert kept == []
    assert rejected[0]["reason"] == "slot_unknown"


@pytest.mark.parametrize("index", [1, -1, "0", None, 99])
def test_a_wrong_chunk_index_is_refused(tmp_path, book, index):
    """Grounding is only meaningful against the passage actually read.

    Checked against the wrong chunk it would either pass by luck or fail for
    the wrong reason, so a candidate that cannot say where it came from is
    refused before the evidence is looked at.
    """
    kept, rejected = _gate(tmp_path, book, [dict(GOOD, chunk_index=index)])
    assert kept == []
    assert rejected[0]["reason"] == "chunk_index_invalid"


def test_repeats_collapse_and_count_occurrences(tmp_path, book):
    """Recurrence is the one ranking signal that works, so it must survive."""
    kept, _ = _gate(tmp_path, book, [GOOD, dict(GOOD, concept="Quenched Harmonic Buffering")])
    assert len(kept) == 1
    assert kept[0]["occurrences"] == 2


def test_chunks_command_emits_the_slot_questions(tmp_path, book):
    out = tmp_path / "chunks.json"
    sys.argv = [
        "x", "chunks", "--input-file", str(book), "--output-file", str(out),
    ]
    assert G.main() == 0
    payload = json.loads(out.read_text(encoding="utf-8"))
    assert payload["chunk_count"] == 1
    assert payload["chunks"][0]["chunk_index"] == 0
    #: The questions, not just the names — asked against a bare list a model
    #: picks by which name sounds closest.
    assert payload["slots"]["consolidation"] == "How does experience become knowledge?"
