"""Tests for the evaluation-set leakage checker (PR #209 B07), including a deliberately leaky synthetic fixture."""
import importlib.util
import json
from pathlib import Path

import pytest

from lifecycle.validation.book_to_memory_leakage import (
    NEAR_NGRAM_THRESHOLD,
    ROLE_HELDOUT,
    ROLE_SCENARIO,
    EvalSet,
    Item,
    LeakageCheckError,
    classify_pair,
    clean_subset,
    compare_sets,
    compare_within,
    content_tokens,
    jaccard,
    load_cases_json,
    load_from_spec,
    load_jsonl,
    normalise,
    run_check,
    word_ngrams,
)

REPO = Path(__file__).resolve().parents[1]
_SPEC = importlib.util.spec_from_file_location("b2m_leakage_check", REPO / "30_SCRIPTS" / "evaluation" / "b2m_leakage_check.py")
CLI = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(CLI)


def item(set_name, item_id, text, gold=(), split=""):
    return Item(set_name, item_id, text, tuple(gold), split)


def make_set(name, role, items):
    return EvalSet(name, role, f"{name}.json", [item(name, *i) if not isinstance(i, Item) else i for i in items])


# ---------------------------------------------------------------- primitives
def test_normalise_folds_case_punctuation_whitespace_and_diacritics():
    assert normalise("  Ce  este MEMORIA,  declarativă?! ") == "ce este memoria declarativa"
    assert normalise("") == "" and normalise(None) == ""


def test_jaccard_and_ngrams():
    assert jaccard({1, 2}, {2, 3}) == pytest.approx(1 / 3)
    assert jaccard(set(), set()) == 0.0
    assert word_ngrams("a b c d") == {("a", "b", "c"), ("b", "c", "d")}
    assert word_ngrams("a b") == {("a", "b")}
    assert content_tokens("What is the role of the cache?") == {"role", "cache"}


# ---------------------------------------------------------------- the synthetic leak
def test_exact_duplicate_is_found():
    f = classify_pair(item("dev", "D1", "Which document defines the confidence levels?"),
                      item("held", "H1", "Which document defines the confidence levels?"))
    assert [x.kind for x in f] == ["exact"]


def test_normalised_duplicate_is_found():
    f = classify_pair(item("dev", "D1", "Which document defines the confidence levels?"),
                      item("held", "H1", "which   DOCUMENT defines the confidence levels"))
    assert [x.kind for x in f] == ["normalised"]


def test_near_duplicate_paraphrase_is_found_by_ngram_or_token_overlap():
    a = "What procedure governs Git backup restore and rollback operations in the vault?"
    b = "Which procedure governs Git backup restore and rollback operations within the vault?"
    f = classify_pair(item("dev", "D1", a), item("held", "H1", b))
    assert f and f[0].kind == "near" and max(f[0].ngram_jaccard, f[0].token_jaccard) >= NEAR_NGRAM_THRESHOLD
    c = "How do agents restore the vault backup and roll back git operations?"
    g = classify_pair(item("dev", "D1", a), item("held", "H2", c))
    assert not g or g[0].kind in ("near", "review")


def test_unrelated_questions_produce_no_finding():
    assert classify_pair(item("a", "1", "What is the capital of France?"),
                         item("b", "2", "How is the retrieval trace emitted?")) == []


def test_shared_gold_id_is_reported_even_when_the_text_differs():
    f = classify_pair(item("dev", "D1", "What is the capital of France?", ["note-1"]),
                      item("held", "H1", "How is the retrieval trace emitted?", ["note-1", "note-2"]))
    assert [x.kind for x in f] == ["gold"] and f[0].shared_gold == ("note-1",)


def test_run_check_on_a_synthetic_leaky_pair_of_sets():
    dev = make_set("dev", ROLE_SCENARIO, [
        ("D1", "Which document defines the confidence levels?", ["n1"]),
        ("D2", "What does the vault do with duplicate UUIDs?", ["n2"]),
        ("D3", "Unrelated question about tokenizers?", []),
    ])
    held = make_set("held", ROLE_HELDOUT, [
        ("H1", "Which document defines the confidence levels?", ["n9"]),           # exact
        ("H2", "what does the VAULT do with duplicate uuids", ["n2"]),              # normalised + gold
        ("H3", "A question that shares nothing at all with the others", []),
    ])
    report = run_check([dev, held])
    s = report["summary"]
    assert s["exact"] == 1 and s["normalised"] == 1 and s["text_overlaps"] == 2 and s["shared_gold_pairs"] == 1
    assert report["pairs"][0]["relevant"] is True
    kinds = sorted(f["kind"] for f in report["pairs"][0]["findings"])
    assert kinds == ["exact", "gold", "normalised"]


def test_a_clean_pair_of_sets_reports_zero():
    dev = make_set("dev", ROLE_SCENARIO, [("D1", "Alpha beta gamma delta?", ["n1"])])
    held = make_set("held", ROLE_HELDOUT, [("H1", "Completely different sentence about oranges.", ["n2"])])
    s = run_check([dev, held])["summary"]
    assert s["text_overlaps"] == 0 and s["shared_gold_pairs"] == 0 and s["review_pairs"] == 0


def test_two_scenario_sets_are_not_leakage_relevant():
    a = make_set("a", ROLE_SCENARIO, [("1", "same question text here", [])])
    b = make_set("b", ROLE_SCENARIO, [("2", "same question text here", [])])
    rep = run_check([a, b])
    assert rep["pairs"][0]["relevant"] is False and rep["summary"]["text_overlaps"] == 0


def test_within_set_split_comparison_and_clean_subset():
    s = make_set("h1", ROLE_HELDOUT, [
        Item("h1", "A", "alpha question one", ("g1",), "development"),
        Item("h1", "B", "totally other words here", ("g2",), "held_out"),
        Item("h1", "C", "alpha question one", ("g3",), "held_out"),
        Item("h1", "D", "yet another distinct sentence", ("g1",), "held_out"),
    ])
    rep = run_check([s], within=[("h1", "development", "held_out")])
    w = rep["within"][0]
    assert w["comparisons"] == 3 and w["counts"]["exact"] == 1 and w["counts"]["gold"] == 1
    dev = make_set("dev", ROLE_SCENARIO, [i for i in s.items if i.split == "development"])
    held = make_set("heldout", ROLE_HELDOUT, [i for i in s.items if i.split == "held_out"])
    sub = clean_subset(held, [dev])
    assert sub["kept"] == ["B"] and set(sub["dropped"]) == {"C", "D"}
    assert any(r.startswith("exact") for r in sub["dropped"]["C"]) and any(r.startswith("gold") for r in sub["dropped"]["D"])
    assert compare_within(s, "development", "development") != []   # an item against itself is excluded by key only across sets


# ---------------------------------------------------------------- loading
def test_loaders(tmp_path):
    (tmp_path / "s.json").write_text(json.dumps({"cases": [
        {"id": "1", "query": "q one", "gold_relevant_notes": ["n1"], "split": "a"},
        {"id": "2", "query": "q two", "gold_relevant_notes": "n2", "split": "b"},
    ]}), encoding="utf-8")
    (tmp_path / "s.jsonl").write_text('{"id":"1","query":"q one","relevant_ids":["x"]}\n\n{"id":"2","query":"q two"}\n', encoding="utf-8")
    all_ = load_cases_json("s", tmp_path / "s.json", ROLE_SCENARIO)
    assert [i.gold_ids for i in all_.items] == [("n1",), ("n2",)] and len(all_.file_sha256) == 64
    assert [i.item_id for i in load_cases_json("s", tmp_path / "s.json", ROLE_SCENARIO, only_split="b").items] == ["2"]
    assert [i.item_id for i in load_cases_json("s", tmp_path / "s.json", ROLE_SCENARIO, exclude_split="b").items] == ["1"]
    assert [i.gold_ids for i in load_jsonl("j", tmp_path / "s.jsonl", ROLE_SCENARIO).items] == [("x",), ()]
    spec = load_from_spec({"name": "p", "path": "s.json", "role": "heldout"}, base=tmp_path)
    assert spec.role == ROLE_HELDOUT and len(spec.items) == 2
    assert len(load_from_spec({"name": "q", "path": "s.jsonl", "format": "jsonl"}, base=tmp_path).items) == 2


def test_loader_errors(tmp_path):
    with pytest.raises(LeakageCheckError):
        load_cases_json("x", tmp_path / "missing.json", ROLE_SCENARIO)
    (tmp_path / "bad.json").write_text('{"nope": 1}', encoding="utf-8")
    with pytest.raises(LeakageCheckError):
        load_cases_json("x", tmp_path / "bad.json", ROLE_SCENARIO)
    with pytest.raises(LeakageCheckError):
        load_from_spec({"name": "x", "path": str(tmp_path / "bad.json"), "role": "weird"})


# ---------------------------------------------------------------- the CLI on synthetic data
def _write_sets(tmp_path, leaky):
    (tmp_path / "dev.json").write_text(json.dumps({"cases": [
        {"id": "D1", "query": "Which document defines the confidence levels in the architecture?", "gold_relevant_notes": ["n1"]}]}), encoding="utf-8")
    held_q = "which document defines the confidence levels in the architecture" if leaky else "Why does the planner prune expired entries?"
    (tmp_path / "held.json").write_text(json.dumps({"cases": [
        {"id": "H1", "query": held_q, "gold_relevant_notes": ["n2"]}]}), encoding="utf-8")
    cfg = tmp_path / "sets.json"
    cfg.write_text(json.dumps([{"name": "dev", "path": "dev.json", "role": "scenario"},
                               {"name": "held", "path": "held.json", "role": "heldout"}]), encoding="utf-8")
    return cfg


def test_cli_fails_the_check_on_a_synthetic_leak_and_writes_the_report(tmp_path):
    cfg = _write_sets(tmp_path, leaky=True)
    out = tmp_path / "out"
    assert CLI.main(["--sets-config", str(cfg), "--out-dir", str(out), "--check"]) == 1
    report = json.loads((out / "LEAKAGE_REPORT.json").read_text(encoding="utf-8"))
    assert report["summary"]["normalised"] == 1 and report["summary"]["unregistered_text_overlaps"] == 1
    md = (out / "LEAKAGE_REPORT.md").read_text(encoding="utf-8")
    assert "dev:D1" in md and "NOT REGISTERED" in md


def test_cli_passes_a_clean_synthetic_pair(tmp_path):
    cfg = _write_sets(tmp_path, leaky=False)
    assert CLI.main(["--sets-config", str(cfg), "--out-dir", str(tmp_path / "out"), "--check"]) == 0


# ---------------------------------------------------------------- the real data
@pytest.fixture(scope="module")
def real_report():
    sets = CLI.real_sets()
    rep = run_check(sets)
    rep["h1"] = CLI.h1_clean_heldout(sets)
    return rep


def test_real_data_has_no_unregistered_text_overlap(real_report):
    found = {frozenset({f["a"], f["b"]}) for p in real_report["pairs"] if p["relevant"] for f in p["findings"]
             if f["kind"] in ("exact", "normalised", "near")}
    assert found == set(CLI.KNOWN_TEXT_OVERLAPS), "text overlaps changed: fix the set or update KNOWN_TEXT_OVERLAPS"


def test_h1_sets_have_no_text_overlap_with_each_other_or_the_b03_tasks(real_report):
    names = {"h1_development", "h1_calibration", "h1_held_out", "b03_tasks"}
    for p in real_report["pairs"]:
        if p["a"] in names and p["b"] in names:
            assert p["text_overlaps"] == 0, (p["a"], p["b"])


def test_committed_report_matches_a_fresh_run(real_report):
    committed = json.loads((REPO / "07_EVALUATION" / "b2m_leakage" / "LEAKAGE_REPORT.json").read_text(encoding="utf-8"))
    fresh = dict(real_report)
    assert committed["summary"]["text_overlaps"] == fresh["summary"]["text_overlaps"]
    assert committed["summary"]["shared_gold_pairs"] == fresh["summary"]["shared_gold_pairs"]
    assert [s["file_sha256"] for s in committed["sets"]] == [s["file_sha256"] for s in fresh["sets"]], \
        "a set changed since the report was generated; re-run 30_SCRIPTS/evaluation/b2m_leakage_check.py"
    assert committed["h1_clean_heldout"]["subset"]["kept"] == real_report["h1"]["subset"]["kept"]


def test_the_h1_gold_sharing_is_reported_not_hidden(real_report):
    sub = real_report["h1"]["subset"]
    assert sub["items"] == 28 and len(sub["dropped"]) > 0
    assert len(sub["kept"]) + len(sub["dropped"]) == 28
