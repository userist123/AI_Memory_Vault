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


def test_a_timeout_is_not_retried(monkeypatch):
    """A client timeout does not cancel the server's work.

    Observed directly: after a request timed out at 500s the endpoint still
    held the model, still busy, and would not start a queued one. Retrying
    then puts a second request behind the first and deepens the backlog,
    which is the opposite of what a retry is for.
    """
    monkeypatch.setattr(M.time, "sleep", lambda _s: None)

    class TimesOut:
        calls = 0

        def generate(self, request):
            TimesOut.calls += 1
            raise RuntimeError(
                "Could not reach local model endpoint http://x/api/generate: timed out"
            )

    accepted, rejects = M.extract_from_chunk(
        TimesOut(), {"heading": "S", "content": CHUNK}, "b", "standard", attempts=3
    )
    assert TimesOut.calls == 1, "a timeout must be attempted once, not three times"
    assert accepted == []
    assert rejects["provider_timeout"] == 1


def test_a_slot_name_in_claim_type_is_refused():
    """qwen2.5-coder:7b returned claim_type "ontology" on all 8 candidates.

    `ontology` is a slot, not a claim type, and the same run put `identity`
    in the slot field for every candidate — the two fields were swapped. It
    went unnoticed because claim_type was never validated. An unvalidated
    field is a field the model may fill with anything.
    """
    accepted, rejects = _run([dict(GOOD, claim_type="ontology")])
    assert accepted == []
    assert rejects["claim_type_is_a_slot"] == 1


def test_an_unrequested_claim_type_is_tolerated_not_refused():
    """claim_type is no longer asked for, so it cannot be a rejection reason.

    It was dropped because it carried no information — every surviving
    candidate across every run said "definition" — while costing 11 of 50
    rejections in a six-chunk run by corrupting the slot beside it. A model
    that volunteers a value anyway keeps it; nothing downstream reads it.
    """
    accepted, rejects = _run([dict(GOOD, claim_type="observation")])
    assert rejects == {}, rejects
    assert accepted[0]["claim_type"] == "observation"


def test_a_candidate_with_no_claim_type_at_all_is_accepted():
    """The field is optional now, and its absence must not be a rejection."""
    without = {k: v for k, v in GOOD.items() if k != "claim_type"}
    accepted, rejects = _run([without])
    assert rejects == {}, rejects
    assert accepted[0]["claim_type"] is None


@pytest.mark.parametrize("claim_type", sorted(M.CLAIM_TYPES))
def test_every_documented_claim_type_is_accepted(claim_type):
    """The gate must not be narrower than the prompt it enforces."""
    accepted, rejects = _run([dict(GOOD, claim_type=claim_type)])
    assert rejects == {}, rejects
    assert accepted[0]["claim_type"] == claim_type


def test_min_occurrences_filters_by_the_only_observed_signal(tmp_path, monkeypatch):
    """Confidence and claim_type are constant, so neither can rank anything.

    How many distinct sections define a term is counted rather than claimed,
    which makes it the only usable selectivity signal on this pipeline.
    """
    #: Three sections; the fake provider returns the same pair every time, so
    #: both concepts end at occurrences=3 and both survive a floor of 2.
    body = "\n\n".join(f"# Section {i}\n\n{CHUNK}" for i in range(1, 4))
    book = tmp_path / "b.txt"
    book.write_text(body, encoding="utf-8")
    out = tmp_path / "o.json"

    payload = json.dumps([GOOD])
    monkeypatch.setattr(
        M, "build_provider",
        lambda *a, **k: FakeModelProvider(canned_response=payload),
    )

    def run(min_occ):
        monkeypatch.setattr(sys, "argv", [
            "x", "--input-file", str(book), "--source-book", "b",
            "--output-file", str(out), "--provider", "fake",
            "--min-occurrences", str(min_occ),
        ])
        assert M.main() == 0
        return json.loads(out.read_text(encoding="utf-8"))

    kept_all = run(1)
    assert len(kept_all) == 1
    assert kept_all[0]["occurrences"] == 3

    #: A floor above what any concept reached removes everything, and that is
    #: the honest outcome rather than an error.
    assert run(4) == []


def test_the_prompt_names_no_desirable_concept():
    """Naming good outputs turns extraction into recall.

    Measured: a prompt version listing "catastrophic forgetting", "episodic
    memory", "synaptic consolidation" and "stability-plasticity trade-off" as
    examples of good concepts made the model return those four in nearly
    every passage — one in seven of twelve chunks — and they were four of the
    five surviving candidates. Yield looked like it had improved by 85%. It
    had not; the model was echoing the prompt.

    This is r027's defect reintroduced through the prompt rather than the
    code, and the code test for it cannot see the prompt, so this guards the
    prompt directly.
    """
    banned = [
        "catastrophic forgetting",
        "episodic memory",
        "synaptic consolidation",
        "stability-plasticity trade-off",
    ]
    prompt = M.PROMPT.lower()
    #: The exclusion list is fine — an exclusion cannot be echoed as output.
    #: What must not appear is a concept named as desirable.
    good_section = prompt.split("not concepts:")[0]
    for term in banned:
        assert term not in good_section, (
            f"{term!r} is named before the exclusion list; the model will "
            "return it regardless of the passage"
        )


def test_the_chunk_limit_follows_the_context_window(tmp_path, monkeypatch):
    """A fixed limit silently discarded most of a real book.

    26 of Schacter & Tulving's 29 chunks exceed the old hardcoded 24,000
    characters, so a monograph run would have processed three sections and
    reported success. Page-mode chunking produces large chunks by design.
    The real constraint is num_ctx, and the limit is now derived from it.
    """
    #: ~45k characters: over the old fixed limit, comfortably inside a 32k
    #: token window at the provider's (chars + 2) // 3 estimate.
    big = "word " * 9000
    book = tmp_path / "b.txt"
    book.write_text(f"# Section One\n\n{big}\n", encoding="utf-8")
    out = tmp_path / "o.json"

    monkeypatch.setattr(
        M, "build_provider",
        lambda *a, **k: FakeModelProvider(canned_response=json.dumps([])),
    )
    monkeypatch.setattr(sys, "argv", [
        "x", "--input-file", str(book), "--source-book", "b",
        "--output-file", str(out), "--provider", "fake",
    ])
    assert M.main() == 0

    #: Not skipped: the provider was actually asked about this chunk.
    assert len(big) > 24000, "fixture must exceed the old fixed limit"
    assert M.CONTEXT_RESERVE_TOKENS > 0
    derived = (32768 - M.CONTEXT_RESERVE_TOKENS) * 3
    assert derived > len(big), "derived limit must admit a page-mode chunk"


def test_an_explicit_chunk_limit_still_wins(tmp_path, monkeypatch):
    """The derivation is a default, not a policy the caller cannot override."""
    book = tmp_path / "b.txt"
    book.write_text("# S\n\n" + "word " * 2000 + "\n", encoding="utf-8")
    out = tmp_path / "o.json"
    monkeypatch.setattr(
        M, "build_provider",
        lambda *a, **k: FakeModelProvider(canned_response=json.dumps([])),
    )
    monkeypatch.setattr(sys, "argv", [
        "x", "--input-file", str(book), "--source-book", "b",
        "--output-file", str(out), "--provider", "fake",
        "--max-chunk-chars", "100",
    ])
    assert M.main() == 0
    assert json.loads(out.read_text(encoding="utf-8")) == []


#: A long passage, so the near-miss case is realistic: on monograph chunks of
#: ~44,000 characters models quote accurately but drop a word or two.
LONG_SOURCE = (
    "The distinction between episodic and semantic memory systems is "
    "traceable in some form to the Greek philosophers and is present in the "
    "analyses of numerous later writers. Maine de Biran postulated the "
    "existence of three separate kinds of memory that depend on different "
    "mechanisms and can be characterized by different properties."
)


def test_an_accurate_quote_missing_a_word_is_still_grounded():
    """Measured: 17 of 51 refusals quoted the source at 80% or better.

    One matched 27 of its 30 words. An exact-match rule threw those away —
    they are accurate quotes with a word dropped, not inventions.
    """
    near = (
        "The distinction between episodic and semantic memory systems is "
        "traceable in some form to the Greek philosophers and is present in "
        "the analyses of later writers."
    )
    assert M.is_grounded(near, LONG_SOURCE)

    accepted, rejects = _run(
        [dict(GOOD, evidence=near, definition=(
            "Two memory systems that later writers separated on grounds of "
            "what each one stores about the past."
        ))],
        chunk_text=LONG_SOURCE,
    )
    assert rejects == {}, rejects
    assert len(accepted) == 1


@pytest.mark.parametrize(
    "fabricated",
    [
        "Vitter (1985) established this result.",
        "Tulving demonstrated in 1972 that the two systems are dissociable.",
        "The authors conclude that memory is fundamentally reconstructive.",
    ],
)
def test_a_fabricated_quote_is_still_refused(fabricated):
    """The loosened rule must not open the door it was built to close.

    Across the measured set, apparent fabrications had longest verbatim runs
    of 3 to 6 words against 25 to 27 for genuine quotes; the control here
    runs 2. The thresholds sit between those populations, not on top of one.
    """
    assert not M.is_grounded(fabricated, LONG_SOURCE)


def test_a_borrowed_phrase_cannot_carry_an_invented_sentence():
    """The run threshold alone is not enough, which is why coverage exists."""
    padded = (
        "The distinction between episodic and semantic memory systems is "
        "traceable in some form to the Greek philosophers, and it follows "
        "that recollection is impossible without a hippocampus, that all "
        "learning is reconstructive, and that forgetting is adaptive by "
        "design in every mammalian species so far examined."
    )
    run = M.longest_verbatim_run(padded, LONG_SOURCE)
    assert run >= M.MIN_VERBATIM_RUN_WORDS, "the borrowed opening does match"
    assert not M.is_grounded(padded, LONG_SOURCE), "coverage must refuse it"


def test_longest_verbatim_run_measures_what_it_claims():
    assert M.longest_verbatim_run("", LONG_SOURCE) == 0
    assert M.longest_verbatim_run("wholly unrelated wording here", LONG_SOURCE) <= 2
    exact = "Maine de Biran postulated the existence of three separate kinds of memory"
    assert M.longest_verbatim_run(exact, LONG_SOURCE) == len(exact.split())


def test_the_default_context_window_skips_no_chunks():
    """The fastest setting is the wrong one, and this records why.

    The window is allocated in VRAM beside the weights, so it decides how
    much of the model runs on the GPU. Measured on an 8 GB card with
    llama3.1:8b: 32768 gives 66% residency and 21s per four chunks, 16384
    gives 84% and 16s, 8192 gives full residency and 10s.

    8192 is fastest and drops a tenth of the corpus: its derived chunk limit
    is 12,576 characters and 48 of 463 chunks across four books exceed it.
    Speed bought by silently skipping content is the failure this whole
    lineage keeps producing.
    """
    assert M.DEFAULT_NUM_CTX == 16384
    limit = (M.DEFAULT_NUM_CTX - M.CONTEXT_RESERVE_TOKENS) * 3
    #: p90 chunk size measured over four books at 3 pages was 13,024 and the
    #: largest 18,427. The default must clear the largest, not the median.
    assert limit > 18427, (
        "the derived chunk limit must admit the largest measured chunk; "
        "anything less drops content to buy throughput"
    )


def _row(concept, location, definition="A definition long enough to survive the shape gate here."):
    return {
        "concept": concept, "definition": definition, "claim_type": None,
        "maps_to_slot": "map", "maps_to_module": None,
        "confidence_in_literature": 0.9, "source_book": "b",
        "source_location": location, "evidence_quote": "q",
        "extraction_method": "agent_direct", "model": "m", "provider": "agent",
    }


def test_occurrences_counts_sections_not_submissions():
    """The number everything is ranked by, inflated by repetition.

    An agent run over Newell submitted "subgoal" twice out of chunk 63 and
    "preference" twice out of chunk 61; both reported occurrences=2 while being
    defined in one place. "impasse" came from chunks 62, 62 and 63 and reported
    3 — the review floor — on two sections. Whoever repeats themselves was
    promoting their own candidates.
    """
    rows, _ = M.deduplicate([
        _row("subgoal", "Chunk 63"),
        _row("subgoal", "Chunk 63"),
        _row("impasse", "Chunk 62"),
        _row("impasse", "Chunk 62"),
        _row("impasse", "Chunk 63"),
        _row("problem space", "Chunk 7"),
        _row("problem space", "Chunk 36"),
        _row("problem space", "Chunk 58"),
    ])
    by = {r["concept"]: r["occurrences"] for r in rows}
    assert by["subgoal"] == 1, "twice from one section is one section"
    assert by["impasse"] == 2, "62, 62, 63 is two sections, not three"
    assert by["problem space"] == 3, "three distinct sections still count three"


def test_a_repeated_submission_still_collapses_to_one_row():
    """The dedup itself must not change: one row per concept, repeats merged."""
    rows, collapsed = M.deduplicate([_row("x", "A"), _row("x", "A"), _row("x", "B")])
    assert len(rows) == 1
    assert collapsed == 2
    assert rows[0]["also_found_in"] == ["B"]
