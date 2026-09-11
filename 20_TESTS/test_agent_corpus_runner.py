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
