"""The corpus runner's two decisions that are easy to get silently wrong:
the order books are handed out in, and the difference between a book that
yielded nothing and a book nobody has done yet.
"""

import argparse
import json
import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))

import run_agent_corpus as R  # noqa: E402


def _book(tmp_path, name, headings):
    """A .txt under a fake corpus root that chunks to len(headings) sections."""
    rel = pathlib.Path("06_INBOX/Carti") / f"{name}.txt"
    path = tmp_path / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "\n\n".join(
        f"# {h}\n\nThe {h} section says something long enough to survive being "
        f"read, with several sentences of ordinary prose in it so that the "
        f"chunker has a paragraph to work with rather than a bare heading."
        for h in headings
    )
    path.write_text(body, encoding="utf-8")
    return {"short_name": name, "rel_path": str(rel).replace("\\", "/")}


@pytest.fixture
def corpus(tmp_path, monkeypatch):
    books = [
        _book(tmp_path, "small", ["one"]),
        _book(tmp_path, "large", [f"section {i}" for i in range(9)]),
        _book(tmp_path, "medium", ["a", "b", "c", "d"]),
    ]
    manifest = tmp_path / "corpus_manifest.json"
    manifest.write_text(json.dumps({"books": books}), encoding="utf-8")
    monkeypatch.setattr(R, "MANIFEST", manifest)
    return tmp_path


def _prepare(corpus, work):
    return R.prepare(argparse.Namespace(work_dir=work, corpus_root=corpus))


def test_the_largest_book_is_handed_out_first(corpus, tmp_path):
    """Recurrence is the only ranking signal, and it needs sections to count.

    Handing out the small books first is what made the local run report
    candidates_at_ge_3 == 0 for five books in a row while telling nobody that
    the arithmetic, not the extractor, produced that zero.
    """
    work = tmp_path / "work"
    assert _prepare(corpus, work) == 0

    order = json.loads((work / "manifest.json").read_text(encoding="utf-8"))["books"]
    assert [b["short_name"] for b in order] == ["large", "medium", "small"]
    assert [b["order"] for b in order] == [1, 2, 3]


def test_a_book_too_short_to_recur_is_labelled_not_dropped(corpus, tmp_path):
    """It still gets extracted. Its yield just must not be read as a result."""
    work = tmp_path / "work"
    _prepare(corpus, work)

    order = {b["short_name"]: b for b in
             json.loads((work / "manifest.json").read_text(encoding="utf-8"))["books"]}
    assert order["small"]["recurrence_measurable"] is False
    assert order["large"]["recurrence_measurable"] is True
    assert (work / "small_chunks.json").exists(), "labelled, not skipped"


def test_the_chunks_carry_the_slot_questions(corpus, tmp_path):
    """Asked against a bare list of sixteen names, a model picks by which name
    sounds closest. The questions are the actual selection criterion."""
    work = tmp_path / "work"
    _prepare(corpus, work)

    payload = json.loads((work / "large_chunks.json").read_text(encoding="utf-8"))
    assert payload["chunk_count"] == len(payload["chunks"])
    assert payload["slots"], "slot questions must travel with the chunks"
    assert [c["chunk_index"] for c in payload["chunks"]] == list(range(payload["chunk_count"]))


def test_a_missing_source_is_an_error_not_a_quiet_zero(corpus, tmp_path, capsys):
    """Eight books once reported success while producing no extractable text."""
    (corpus / "06_INBOX" / "Carti" / "large.txt").unlink()
    work = tmp_path / "work"

    assert _prepare(corpus, work) == 1
    assert "large" in capsys.readouterr().err


def test_pending_is_not_the_same_as_yielded_nothing(corpus, tmp_path, monkeypatch, capsys):
    """A book nobody has done yet must never be reported as a book that
    produced no candidates — the first is a schedule, the second is a result."""
    work = tmp_path / "work"
    _prepare(corpus, work)
    monkeypatch.setattr(R, "_REPO", tmp_path)

    #: "large" comes back empty; nobody has touched "medium" or "small".
    (work / "large_candidates.json").write_text("[]", encoding="utf-8")

    R.collect(argparse.Namespace(
        work_dir=work, corpus_root=corpus, agent_label="test-agent",
        report_name="report.json",
    ))

    report = json.loads(
        (tmp_path / "staging" / "report.json").read_text(encoding="utf-8")
    )["books"]
    assert [b["short_name"] for b in report] == ["large"], (
        "only the book that was actually attempted belongs in the report"
    )
    assert report[0]["candidates_kept"] == 0

    out = capsys.readouterr().out
    assert "pending (2)" in out and "medium" in out and "small" in out


def test_the_report_records_whether_a_local_model_server_was_up(
    corpus, tmp_path, monkeypatch
):
    """provider="agent" is only worth what the agent actually did.

    An agent that wrote a script to call a 7B model would produce rows labelled
    "agent" that are nothing of the sort. This cannot prevent that; it records
    it beside the numbers, so the label can be weighed rather than taken.
    """
    work = tmp_path / "work"
    _prepare(corpus, work)
    monkeypatch.setattr(R, "_REPO", tmp_path)
    (work / "large_candidates.json").write_text("[]", encoding="utf-8")

    monkeypatch.setattr(R, "probe_local_providers", lambda: {
        "checked_at": "2026-09-11T22:00:00+03:00",
        "local_servers_responding": {
            "ollama": {"port": 11434, "responding": True,
                       "models_loaded": ["llama3.1:8b"]},
        },
        "clean": False,
    })

    R.collect(argparse.Namespace(
        work_dir=work, corpus_root=corpus, agent_label="antigravity",
        report_name="report.json",
    ))

    report = json.loads(
        (tmp_path / "staging" / "report.json").read_text(encoding="utf-8")
    )
    assert report["local_provider_probe"]["clean"] is False
    assert (report["local_provider_probe"]["local_servers_responding"]
            ["ollama"]["models_loaded"] == ["llama3.1:8b"])


def test_the_probe_never_raises_and_never_blocks(monkeypatch):
    """It is an observation, not a gate. A run must not die because a probe
    could not reach a port, and a local server must not stop the collection —
    absence of a server is weak evidence anyway, since the work could have
    gone through a remote endpoint."""
    import urllib.request

    def explode(*a, **k):
        raise OSError("no network of any kind")

    monkeypatch.setattr(urllib.request, "urlopen", explode)
    probe = R.probe_local_providers(timeout=0.01)
    assert probe["clean"] is True
    assert probe["local_servers_responding"] == {}
    assert probe["checked_at"]


def test_no_script_can_reach_for_a_local_model_without_saying_so():
    """--provider has no default, and that is the whole point.

    It defaulted to "local". A runner that called this script without thinking
    about the provider therefore started Ollama, and one did — repeatedly,
    after the repository owner had asked for local models to stop being used.
    The defaults sat in a function signature nobody read.

    Reaching for a 7B model here is a decision with measured consequences: 82
    candidates kept across 7 books, 1 above the review floor, the top rejection
    being definitions that were the evidence reworded. A decision that costs
    that much has to be typed out.
    """
    src = (_REPO / "30_SCRIPTS" / "ingestion" / "model_extract_concepts.py").read_text(
        encoding="utf-8"
    )
    assert '"--provider", required=True' in src
    assert '"--provider", default=' not in src


def test_a_chunk_that_is_not_prose_is_flagged_not_removed(tmp_path, monkeypatch):
    """An index page and an OCR'd page both land here, and both are useless to
    read for concepts — reading Newell's name index is how "Parr of mr" became
    a candidate. They stay in the file so the chunk indices keep meaning what
    they meant; they carry a flag so nobody reads them by accident.
    """
    prose = ("The system is intelligent to the degree that it approximates a "
             "knowledge level system, and this is what answers to the concept "
             "we have laid out in the chapters that come before this one.")
    index = "Recall, 10, 30 Recency, 23, 72 Receptive fields, 101 Recoding, 15"

    books = [_book(tmp_path, "mixed", ["a"])]
    path = tmp_path / books[0]["rel_path"]
    path.write_text(
        "\n\n".join([f"# Section {i}\n\n{prose}" for i in range(6)]
                    + [f"# Back matter\n\n{index}"]),
        encoding="utf-8",
    )
    manifest = tmp_path / "corpus_manifest.json"
    manifest.write_text(json.dumps({"books": books}), encoding="utf-8")
    monkeypatch.setattr(R, "MANIFEST", manifest)

    work = tmp_path / "work"
    R.prepare(argparse.Namespace(work_dir=work, corpus_root=tmp_path))

    payload = json.loads((work / "mixed_chunks.json").read_text(encoding="utf-8"))
    flagged = [c for c in payload["chunks"] if c["low_prose"]]
    assert len(flagged) == 1, "only the index page"
    assert "Recency" in flagged[0]["content"], "flagged, not dropped"
    assert payload["chunk_count"] == 7
    assert [c["chunk_index"] for c in payload["chunks"]] == list(range(7)), (
        "indices must stay contiguous; a candidate names one of these"
    )


def test_the_prose_floor_is_relative_to_the_book_not_absolute():
    """Measured across the corpus: Newell's median prose rate is 0.456 and a
    survey paper's is 0.218, and both are fine. Four document-level metrics
    have now failed here — function-word rate, intra-word punctuation,
    low-vowel rate, and a letter-trigram model trained on this corpus's own
    clean books — because OCR damage is partial and short words survive it.
    """
    clean = "the system is one of the ways that we can do this in a book " * 8
    assert R.prose_rate(clean) > 0.4
    assert R.prose_rate("Recall, 10, 30 Recency, 23, 72 Receptive fields") < 0.2
    assert R.prose_rate("") == 0.0
    assert 0 < R.LOW_PROSE_RATIO < 1


def test_a_book_that_is_not_about_its_subject_is_flagged_before_it_is_read():
    """154 chunks were read before anyone noticed the book was not the book.

    The file named as Anderson's *How Can the Human Mind Occur in the Physical
    Universe* came from a scam preview: a title page, a link to a blogspot
    storefront, then roughly 450 pages of nineteenth-century filler — golf club
    rules, bicycle advertisements, Ruskin, Hungarian Gutenberg text. Zero
    mentions of ACT-R, declarative memory, buffers or activation.

    Nothing caught it. The converter reported status OK. The prose-rate check
    passed it, correctly — filler is well-formed prose. It was ranked second in
    the reading order on chunk count alone.
    """
    real = (
        "Memory consolidation in the hippocampus depends on synaptic change, "
        "and the cognitive architecture that learns from it must represent "
        "knowledge in a form the retrieval system can reach. " * 6
    )
    filler = (
        "The golf links at Concord are open to members of the school, and the "
        "wheelmen of the league are invited to the exhibition of bicycles held "
        "in the town hall on the afternoon of the fourteenth. " * 6
    )
    assert R.topic_density(real) >= R.MIN_TOPIC_DENSITY
    assert R.topic_density(filler) < R.MIN_TOPIC_DENSITY
    assert R.topic_density("") == 0.0


def test_the_topic_floor_clears_every_real_book_in_the_corpus():
    """Measured, not guessed: the real books scored 1.21 to 9.29 mentions per
    thousand characters and the one that was not a book scored 0.20. The floor
    sits below both, so a genuine book on an unexpected subject is read rather
    than withheld — it warns, it does not gate."""
    assert 0.2 < R.MIN_TOPIC_DENSITY < 1.21
