"""Gates for model-assisted concept extraction.

The point of these tests is not that the module runs. It is that a model
cannot get a fabricated or shallow concept past it. Every test below encodes
a defect this ingestion lineage has actually shipped:

  r027      extraction that was a hardcoded lookup table, so an unseen term
            must still work and a memorised one must not be the mechanism
  r027-fix  a guard that checked a proxy instead of the real source text
  r029 D2   a "paraphrase" that was a synonym swap of the source sentence
  r028 #7   a citation that was accurate but absent from the ingested book

All of them run against FakeModelProvider: deterministic, no network, no
model. What is under test is the validation, which is the part that has to
hold when the model is wrong.
"""

from __future__ import annotations

import json
import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))
sys.path.insert(0, str(_REPO / "03_IMPLEMENTATION" / "packages"))

import model_extract_concepts as M  # noqa: E402
from providers.fake_model_provider import FakeModelProvider  # noqa: E402

#: An invented term with an invented mechanism. It appears in no book, no
#: dictionary and nowhere in the extractor's source, so nothing can produce a
#: result for it by recall.
CHUNK = (
    "We introduce quenched harmonic buffering, a scheme in which the agent "
    "holds a decaying replica of each observation and releases it only once "
    "the replica's amplitude falls below the acceptance threshold. Quenched "
    "harmonic buffering therefore trades immediate recall for a bounded "
    "write rate, and in our experiments it removed the write spikes that "
    "otherwise followed every distribution shift."
)

EVIDENCE = (
    "the agent holds a decaying replica of each observation and releases it "
    "only once the replica's amplitude falls below the acceptance threshold"
)

GOOD = {
    "concept": "Quenched harmonic buffering",
    "definition": (
        "A scheme that delays committing an observation until its stored "
        "copy has decayed past a set point, which bounds how often the "
        "system writes."
    ),
    "evidence": EVIDENCE,
    "claim_type": "mechanism",
    "slot": "consolidation",
    "confidence": 0.7,
}


def _run(candidates, chunk_text=CHUNK):
    """Drive the real extraction path with a model that returns `candidates`."""
    provider = FakeModelProvider(canned_response=json.dumps(candidates))
    accepted, rejects = M.extract_from_chunk(
        provider,
        {"heading": "Section 3", "content": chunk_text},
        source_book="test_book",
        model_tier="standard",
    )
    return accepted, rejects


def test_accepts_a_grounded_concept_for_an_invented_term():
    """Extraction must be content-derived, not recalled (r027)."""
    accepted, rejects = _run([GOOD])
    assert rejects == {}, rejects
    assert len(accepted) == 1
    row = accepted[0]
    assert row["concept"] == "Quenched harmonic buffering"
    assert row["maps_to_slot"] == "consolidation"
    assert row["confidence_in_literature"] == 0.7
    #: The evidence travels with the row so a reviewer can check the
    #: definition against its source without reopening the book.
    assert row["evidence_quote"] == EVIDENCE
    assert row["extraction_method"] == "model_assisted"


def test_rejects_evidence_that_is_not_in_the_source():
    """A fabricated quote is the failure mode that matters most (r028 #7).

    The check must run against the passage that was actually sent, not
    against anything the model asserts about it.
    """
    fabricated = dict(GOOD, evidence="Vitter (1985) established this result.")
    accepted, rejects = _run([fabricated])
    assert accepted == []
    assert rejects["evidence_not_in_source"] == 1


def test_rejects_a_definition_that_is_the_source_sentence_reworded():
    """Passing a verbatim guard is a floor, not a quality bar (r029 D2).

    The swap here is the severity actually measured on the r028 promotion —
    one or two words changed, clause structure untouched, token overlap in
    the 0.68-0.72 band. This case scores 0.905.
    """
    swapped = dict(
        GOOD,
        definition=(
            "The agent holds a decaying replica of each observation and "
            "releases it only once the replica's amplitude falls under the "
            "acceptance threshold."
        ),
    )
    accepted, rejects = _run([swapped])
    assert accepted == []
    #: Both floors fire on this one; verbatim is checked first.
    assert sum(rejects.values()) == 1
    assert set(rejects) <= {"paraphrase_shallow", "verbatim_ngram"}


def test_known_gap_heavy_rewording_passes_both_paraphrase_floors():
    """A limit, recorded as a test so it cannot be forgotten.

    Reword every content word while keeping the clause structure and token
    overlap falls to 0.43 — under the ceiling — with no shared 8-gram. Both
    floors pass it. Neither gate can see sentence structure, only vocabulary.

    The ceiling is NOT lowered to catch this: 0.60 is set by the measured
    0.68-0.72 defect, and tightening it far enough to reject this case would
    start rejecting genuine definitions, which in a technical corpus
    legitimately reuse the source's terminology.
    """
    reworded = dict(
        GOOD,
        definition=(
            "The agent keeps a decaying duplicate of every observation and "
            "emits it only after the duplicate's amplitude drops beneath the "
            "acceptance limit."
        ),
    )
    assert M._jaccard(reworded["definition"], EVIDENCE) < M.MAX_JACCARD
    accepted, _ = _run([reworded])
    assert len(accepted) == 1, "documents the gap; change this only with data"


def test_rejects_a_definition_copied_verbatim():
    copied = dict(GOOD, definition=EVIDENCE + " and this is the definition.")
    accepted, rejects = _run([copied])
    assert accepted == []
    assert rejects["verbatim_ngram"] == 1


@pytest.mark.parametrize(
    "term,reason",
    [
        ("What", "term_generic"),
        ("Chapter 7", "term_shape"),
        ("Holder Facts Deny Yer Now", "term_length"),
    ],
)
def test_rejects_terms_the_previous_extractor_actually_emitted(term, reason):
    """Every one of these was a real row produced by the rule-based path."""
    accepted, rejects = _run([dict(GOOD, concept=term)])
    assert accepted == []
    assert rejects[reason] == 1


def test_rejects_a_definition_truncated_mid_clause():
    """7% of the rule-based corpus run ended like this.

    Long enough to clear the minimum-length gate, so that what is under test
    is the truncation check and not the word count.
    """
    accepted, rejects = _run(
        [
            dict(
                GOOD,
                definition=(
                    "A scheme in which the agent defers committing each "
                    "observation to storage until the replica held for it in."
                ),
            )
        ]
    )
    assert accepted == []
    assert rejects["definition_truncated"] == 1


def test_rejects_an_invented_slot():
    """The model does not get to extend the ontology."""
    accepted, rejects = _run([dict(GOOD, slot="neuroscience")])
    assert accepted == []
    assert rejects["slot_unknown"] == 1


def test_rejects_a_missing_or_out_of_range_confidence():
    accepted, rejects = _run([dict(GOOD, confidence="very high")])
    assert accepted == []
    assert rejects["confidence_missing"] == 1

    accepted, rejects = _run([dict(GOOD, confidence=1.4)])
    assert accepted == []
    assert rejects["confidence_range"] == 1


def test_unparseable_model_output_yields_nothing_rather_than_guesses():
    """FakeModelProvider's default reply is not JSON.

    Counted, not silently zero: "the model answered something we could not
    read" and "this passage defines nothing" are the same zero downstream and
    are not the same event.
    """
    provider = FakeModelProvider()
    accepted, rejects = M.extract_from_chunk(
        provider, {"heading": "S", "content": CHUNK}, "test_book", "standard"
    )
    assert accepted == []
    assert rejects["unparseable_response"] == 1


def test_an_empty_response_is_counted_rather_than_read_as_no_concepts():
    """Measured: Ollama's `format: "json"` returns "" from glm-4.7-flash.

    Silently that reads as a passage with nothing in it, which is why the
    option is not set and why this case has its own counter.
    """
    provider = FakeModelProvider(canned_response="")
    accepted, rejects = M.extract_from_chunk(
        provider, {"heading": "S", "content": CHUNK}, "test_book", "standard"
    )
    assert accepted == []
    assert rejects["empty_response"] == 1
    assert "unparseable_response" not in rejects


def test_provider_failure_is_recorded_not_raised(monkeypatch):
    """A timeout must not read as 'the model found nothing here'."""
    #: Without this the retry backoff is really slept and the suite goes from
    #: 0.06s to 15s. A slow test is a test that stops being run.
    monkeypatch.setattr(M.time, "sleep", lambda _s: None)

    class Failing:
        def generate(self, request):
            raise TimeoutError("no response")

    accepted, rejects = M.extract_from_chunk(
        Failing(), {"heading": "S", "content": CHUNK}, "test_book", "standard"
    )
    assert accepted == []
    assert rejects["provider_error:TimeoutError"] == 1


def test_every_canonical_slot_carries_a_question():
    """The prompt is built from the vault's own slot files, not a copy.

    If a slot file loses its `## Question`, the slot silently stops being
    offered to the model and stops being selected. That must fail loudly.
    """
    questions = M.load_slot_questions()
    assert set(questions) >= set(M.CANONICAL_SLOTS)
    assert questions["consolidation"] == "How does experience become knowledge?"

    block = M.format_slot_block(questions)
    for slot in M.CANONICAL_SLOTS:
        assert slot in block
        assert questions[slot] in block


def test_deduplicate_collapses_repeats_and_counts_occurrences():
    """One book defines its central terms repeatedly (measured: 3x in 3 chunks)."""
    rows = [
        dict(GOOD, concept="Synaptic consolidation", confidence=0.6),
        dict(GOOD, concept="Episodic memory", confidence=0.8),
        dict(GOOD, concept="synaptic  Consolidation", confidence=0.6),
    ]
    #: shape them like accepted rows
    for i, r in enumerate(rows):
        r["confidence_in_literature"] = r.pop("confidence")
        r["definition"] = GOOD["definition"]
        r["source_location"] = f"Section {i}"

    merged, collapsed = M.deduplicate(rows)
    assert collapsed == 1
    assert [r["concept"] for r in merged] == [
        "Synaptic consolidation",
        "Episodic memory",
    ]
    assert merged[0]["occurrences"] == 2
    assert merged[0]["also_found_in"] == ["Section 2"]
    assert merged[1]["occurrences"] == 1


def test_deduplicate_keeps_the_better_definition_but_the_full_count():
    """Occurrences must survive the swap, or the observed signal is lost."""
    weak = {
        "concept": "Reservoir sampling",
        "definition": "Short one.",
        "confidence_in_literature": 0.4,
        "source_location": "Section 1",
    }
    strong = dict(
        weak,
        definition="A much longer and more explanatory definition of the term.",
        confidence_in_literature=0.9,
        source_location="Section 2",
    )
    merged, collapsed = M.deduplicate([weak, strong])
    assert collapsed == 1
    assert merged[0]["confidence_in_literature"] == 0.9
    assert merged[0]["occurrences"] == 2
    assert merged[0]["also_found_in"] == ["Section 2"]


def test_parse_model_json_survives_fencing_and_preamble():
    payload = "Here you go:\n```json\n[{\"concept\": \"X\"}]\n```"
    assert M.parse_model_json(payload) == [{"concept": "X"}]
    assert M.parse_model_json("not json at all") == []
    assert M.parse_model_json("[{broken}]") == []


def test_acronym_gloss_is_stripped_not_rejected():
    """"Continual learning (CL)" is a term, not malformed input.

    Found on a live run: the shape check rejected the very first candidate
    the model returned, because academic prose introduces a term with its
    acronym in parentheses.
    """
    assert M.clean_term("Continual learning (CL)") == "Continual learning"
    assert M.clean_term("Reservoir Sampling") == "Reservoir Sampling"

    accepted, rejects = _run([dict(GOOD, concept="Quenched harmonic buffering (QHB)")])
    assert rejects == {}, rejects
    assert accepted[0]["concept"] == "Quenched harmonic buffering"


def test_main_actually_deduplicates_and_writes_rejects(tmp_path, monkeypatch):
    """End-to-end through main(), because a unit test is not wiring.

    deduplicate() shipped with passing unit tests while main() never called
    it: the edit that added the call silently failed to apply and nothing
    caught it. A function with tests and no caller is not a feature.
    """
    #: Two chunks, so the fake provider returns the same concept twice and a
    #: duplicate genuinely has to be collapsed.
    book = tmp_path / "book.txt"
    book.write_text(
        f"# Section One\n\n{CHUNK}\n\n# Section Two\n\n{CHUNK}\n",
        encoding="utf-8",
    )
    out = tmp_path / "out.json"
    rejects = tmp_path / "rejects.json"

    payload = json.dumps([GOOD, dict(GOOD, concept="What", slot="state")])
    monkeypatch.setattr(
        M, "build_provider",
        lambda *a, **k: FakeModelProvider(canned_response=payload),
    )
    monkeypatch.setattr(
        sys, "argv",
        [
            "model_extract_concepts.py",
            "--input-file", str(book),
            "--source-book", "test_book",
            "--output-file", str(out),
            "--rejects-file", str(rejects),
            "--provider", "fake",
        ],
    )

    assert M.main() == 0

    written = json.loads(out.read_text(encoding="utf-8"))
    assert len(written) == 1, "the same concept in two sections must collapse"
    assert written[0]["occurrences"] == 2
    assert written[0]["also_found_in"] == ["Section Two"]

    #: The generic term is refused in both chunks and both are recorded with
    #: the offending value, not just counted.
    refused = json.loads(rejects.read_text(encoding="utf-8"))
    assert len(refused) == 2
    assert {r["reason"] for r in refused} == {"term_generic"}
    assert refused[0]["concept"] == "What"
    assert refused[0]["slot"] == "state"


def test_acronym_gloss_is_stripped_wherever_it_sits():
    """Refused for length on a live run: the gloss was not last.

    "Complementary Learning Systems (CLS) theory" is four words with the
    gloss removed and five with it, and four is the limit.
    """
    assert (
        M.clean_term("Complementary Learning Systems (CLS) theory")
        == "Complementary Learning Systems theory"
    )
    accepted, rejects = _run(
        [dict(GOOD, concept="Quenched harmonic (QHB) buffering")]
    )
    assert rejects == {}, rejects
    assert accepted[0]["concept"] == "Quenched harmonic buffering"


def test_a_transient_provider_failure_is_retried(monkeypatch):
    """Measured: two chunks failed and the identical request then worked.

    Unretried, a passing run silently loses those chunks — a hole in the
    extraction with nothing in the output pointing at it.
    """
    monkeypatch.setattr(M.time, "sleep", lambda _s: None)

    class FlakyOnce:
        def __init__(self):
            self.calls = 0

        def generate(self, request):
            self.calls += 1
            if self.calls == 1:
                raise ConnectionError("endpoint busy")
            return FakeModelProvider(canned_response=json.dumps([GOOD])).generate(
                request
            )

    provider = FlakyOnce()
    accepted, rejects = M.extract_from_chunk(
        provider, {"heading": "S", "content": CHUNK}, "b", "standard"
    )
    assert provider.calls == 2
    assert len(accepted) == 1
    assert rejects["provider_retry_succeeded_on_2"] == 1


def test_a_persistent_provider_failure_is_recorded_not_raised(monkeypatch):
    """One unreachable chunk must not end a multi-hour run."""
    monkeypatch.setattr(M.time, "sleep", lambda _s: None)

    class AlwaysFails:
        calls = 0

        def generate(self, request):
            AlwaysFails.calls += 1
            raise TimeoutError("no response")

    accepted, rejects = M.extract_from_chunk(
        AlwaysFails(), {"heading": "S", "content": CHUNK}, "b", "standard",
        attempts=3,
    )
    assert AlwaysFails.calls == 3
    assert accepted == []
    assert rejects["provider_error:TimeoutError"] == 1
