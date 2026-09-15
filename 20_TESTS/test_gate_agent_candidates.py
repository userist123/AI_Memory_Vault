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
    """Recurrence is the one ranking signal that works, so it must survive.

    Two submissions of one term out of one section are one section. This test
    asserted 2 until an agent run over Newell showed what that number was worth:
    "impasse" arrived from chunks 62, 62 and 63 and reported 3 — the review
    floor — on two sections.
    """
    kept, _ = _gate(tmp_path, book, [GOOD, dict(GOOD, concept="Quenched Harmonic Buffering")])
    assert len(kept) == 1
    assert kept[0]["occurrences"] == 1, "same section twice is one section"


def test_the_same_term_in_two_sections_counts_two(tmp_path):
    """And the signal still works when the recurrence is real."""
    two = tmp_path / "two.txt"
    body = "\n".join([
        "# Section One", "", CHUNK, "", "# Section Two", "", CHUNK, "",
    ])
    two.write_text(body, encoding="utf-8")
    kept, _ = _gate(tmp_path, two, [
        GOOD,
        dict(GOOD, chunk_index=1, concept="Quenched Harmonic Buffering"),
    ])
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


def test_the_local_default_names_a_model_that_actually_fits():
    """A default that cannot run is worse than no default.

    --model defaulted to mixtral:8x7b, which is ~26 GB against an 8 GB card
    and was never installed here. The 19 GB model that WAS installed ended in
    "llama-server process has terminated" after spilling two thirds to CPU.
    Anyone running the bare command would have hit that, not a result.
    """
    src = (_REPO / "30_SCRIPTS" / "ingestion" / "model_extract_concepts.py").read_text(
        encoding="utf-8"
    )
    assert '"--model", default="llama3.1:8b"' in src
    assert "mixtral" not in src, (
        "a default naming a model larger than the GPU sends the reader into "
        "the failure this file documents"
    )


# --- the hole that 3,342 fabricated rows went through ---------------------

_TEMPLATES = [
    "Denotes a specialized functional paradigm whereby functional interactions "
    "govern {a} and {b} and {c} within the underlying system architecture.",
    "Characterizes an operational mechanism in which functional interactions "
    "govern {a} and {b} and {c} within the underlying system architecture.",
]


def _templated(count):
    """What a generator produces: one frame, three words swapped in."""
    return [
        _TEMPLATES[i % 2].format(a=f"alpha{i}", b=f"beta{i}", c=f"gamma{i}")
        for i in range(count)
    ]


def test_a_template_is_invisible_to_every_other_gate():
    """Why this gate had to exist at all.

    A content-free frame shares no 8-gram with the evidence — it is not derived
    from the evidence — so the paraphrase gate passes it. The evidence is a
    verbatim copy, so grounding passes. The term is short and the definition is
    long enough and ends in a period, so shape passes. Every gate was satisfied
    by construction rather than by the candidate saying anything, and 3,342 rows
    reached the ontology with zero rejections.
    """
    frames = G.find_reused_frames(_templated(20))
    assert frames, "the frame must be visible when definitions are compared"
    assert max(frames.values()) >= G.FRAME_REUSE_ROW_LIMIT


def test_a_shared_domain_phrase_is_not_a_template():
    """Measured, not assumed. On the real llama3.1:8b run four definitions
    shared "essential for the stabilization of the spatial map" — across PKA,
    late LTP, protein synthesis and gene expression — and each still said
    something specific about its own concept. Four is reported; it is not a
    refusal."""
    real = [
        "A protein kinase that plays a critical role in the transformation of "
        "short term memory into long term memory in the hippocampus.",
        "A phase of long term potentiation that requires PKA and is essential "
        "for the stabilization of the spatial map.",
        "A process that is essential for the stabilization of the spatial map "
        "and underlies persistent synaptic change.",
        "Components of long term potentiation that depend on PKA and are "
        "essential for the stabilization of the spatial map.",
    ]
    frames = G.find_reused_frames(real)
    assert frames, "the reuse is real and should be reported"
    assert max(frames.values()) < G.FRAME_REUSE_ROW_LIMIT, (
        "four definitions sharing a domain phrase must not be refused"
    )


def test_coverage_was_tried_and_does_not_separate_the_two():
    """Pinned because it is the wrong threshold to reach for next time.

    The fabricated definitions sat at 0.47 of their tokens inside a reused
    frame; the real ones reached 0.83. The distributions overlap, so a coverage
    threshold convicts the wrong rows. Reuse *count* is what separates them:
    4 at most across 82 real candidates, against 168 in the fabricated run.
    """
    assert G.FRAME_REUSE_ROW_LIMIT > 4, "must clear the real run's maximum"


def test_a_wholly_templated_batch_is_refused_even_below_the_row_limit(tmp_path):
    """Thirteen of the fabricated books put 100% of their rows inside a reused
    frame. Real books ran 0%, 0%, 0%, 0%, 0%, 33%, 50%."""
    defs = _templated(12)
    frames = G.find_reused_frames(defs)
    touched = [d for d in defs if G._frames_in(d) & frames.keys()]
    fraction = len(touched) / len(defs)

    assert len(defs) >= G.FRAME_REUSE_BATCH_FLOOR
    assert fraction > G.FRAME_REUSE_BATCH_FRACTION


def test_the_fraction_is_ignored_on_a_batch_too_small_to_mean_anything():
    """Four candidates, three sharing a phrase, is 75% and is evidence of
    nothing. The floor is why the real 6-row book at 50% was not convicted."""
    assert G.FRAME_REUSE_BATCH_FLOOR >= 10


def test_the_template_gate_actually_runs_inside_gate(tmp_path, capsys):
    """End to end through main(), not around it.

    A function in this package once shipped with passing unit tests and no
    caller. The batch check runs between validate() and deduplicate(), and only
    a real pass proves it is wired in.
    """
    names = ["hippocampal trace", "cortical replay", "synaptic tagging",
             "engram cell", "retrieval cue", "systems transfer",
             "sleep spindle", "reconsolidation window", "place field",
             "pattern separation", "dentate gating", "schema assimilation"]
    chunk_sentences = []
    candidates = []
    for i in range(12):
        sentence = (
            f"The regulatory element number {i} governs how the hippocampal "
            f"circuit stabilises a trace across repeated retrieval episodes "
            f"over a period of consolidation lasting several weeks in total."
        )
        chunk_sentences.append(f"# Section {i}\n\n{sentence}")
        candidates.append({
            "chunk_index": 0,
            "concept": names[i],
            #: Evidence copied verbatim, so grounding passes.
            "evidence": sentence,
            #: One frame, three words swapped. Every other gate passes it.
            "definition": _TEMPLATES[i % 2].format(
                a=f"alpha{i}", b=f"beta{i}", c=f"gamma{i}"
            ),
            "slot": "consolidation",
            "confidence": 0.9,
        })

    book = tmp_path / "book.txt"
    book.write_text("\n\n".join(chunk_sentences), encoding="utf-8")
    cand = tmp_path / "cand.json"
    cand.write_text(json.dumps(candidates), encoding="utf-8")
    out = tmp_path / "out.json"
    rej = tmp_path / "rej.json"

    G.main([
        "gate", "--input-file", str(book), "--candidates", str(cand),
        "--source-book", "book", "--output-file", str(out),
        "--rejects-file", str(rej), "--agent-label", "test",
    ])

    kept = json.loads(out.read_text(encoding="utf-8"))
    refused = json.loads(rej.read_text(encoding="utf-8"))
    assert kept == [], "a wholly templated submission must keep nothing"
    assert refused, "and must say why, per row"
    reasons = {r["reason"] for r in refused}
    assert reasons <= {"batch_is_templated", "definition_frame_reused"}
    assert any(r.get("shared_frame") for r in refused)


def test_a_generator_that_varies_its_frames_still_repeats_its_openings():
    """The 8-gram check was evaded, not defeated.

    A second submission used five frames that varied further along, so they
    stopped sharing eight consecutive words: 20% of rows caught where the first
    submission had lost 99%. What did not vary was how each definition started.

    Measured across three runs — 23 distinct openings across 25 real
    definitions, against exactly 5 across 762 generated ones.
    """
    menu = [
        "A functional mechanism that", "A theoretical model accounting",
        "An analytical construct describing", "A structural property governing",
        "An operational formulation defining",
    ]
    generated = [
        f"{menu[i % 5]} the way that component number {i} behaves under load."
        for i in range(60)
    ]
    cover, distinct = G.opening_concentration(generated)
    assert distinct == 5
    assert cover > G.MAX_TOP_OPENING_COVERAGE


def test_real_definitions_open_differently_because_the_concept_leads():
    """Real openings follow the concept. Measured: the top five covered 28%,
    32% and 40% of a book, never more."""
    real = [
        "A protein kinase that relays a signal into the nucleus of the cell.",
        "The process by which a trace becomes independent of the hippocampus.",
        "Components of potentiation that depend on new protein being made.",
        "Slow oscillations that group spindles during non-REM sleep stages.",
        "An account in which retrieval itself renders a memory labile again.",
        "Structures on the dendrite that change shape as learning proceeds.",
        "Repeated reactivation during rest, measured as ordered place cells.",
        "The window after retrieval when a trace can still be disrupted here.",
        "Encoding that binds an event to the place and time it happened in.",
        "A gradient over which older memories resist damage more than new.",
        "Cells that fire when an animal occupies one location in its space.",
        "Transfer of dependence from one structure to another over weeks.",
    ]
    cover, distinct = G.opening_concentration(real)
    assert distinct >= 10, "real writing opens almost every definition differently"
    assert cover < G.MAX_TOP_OPENING_COVERAGE


def test_a_templated_batch_is_refused_whole_not_filtered():
    """Keeping the rows that happen not to share an 8-gram would keep the same
    generator's output minus the ones it varied most — the submission is not
    trusted and the survivors are not better, only less similar to each other.
    """
    src = (_REPO / "30_SCRIPTS" / "ingestion" / "gate_agent_candidates.py").read_text(
        encoding="utf-8"
    )
    assert "if not hit and not batch_templated:" in src, (
        "a templated batch must not be filtered row by row"
    )
