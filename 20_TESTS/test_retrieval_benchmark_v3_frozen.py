"""Retrieval benchmark v3 is frozen, and its labels hold against the corpus it was labelled from.

The benchmark was labelled by an independent evaluator from a committed corpus
snapshot, before any search arm was run. These tests keep it that way: the
bytes cannot change without the hash failing, and every label is re-checked
against the snapshot rather than against today's vault, so later edits to
notes cannot silently invalidate or "repair" a label.
"""
from __future__ import annotations

import collections
import hashlib
import json
import re
from pathlib import Path

import pytest

DIR = Path(__file__).resolve().parents[1] / "07_EVALUATION" / "retrieval_benchmark_v3"
BENCH = DIR / "retrieval_benchmark_v3.json"
CORPUS = DIR / "labeling_corpus.json"

EXPECTED_CLASSES = {"direct": 60, "multi_hop": 40, "conceptual": 30, "abstain": 30}


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _recorded(path: Path) -> str:
    return (DIR / f"{path.name}.sha256").read_text(encoding="utf-8").split()[0]


def _norm(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def label_problems(bench: dict, corpus: dict) -> list[str]:
    """Every way a label can disagree with the corpus it was labelled from."""
    notes = {n["id"]: n for n in corpus["notes"]}
    pairs = collections.defaultdict(set)
    for link in corpus["links"]:
        pairs[frozenset((link["source"], link["target"]))].add(link["relation"])
    problems = []
    for case in bench["cases"]:
        cid, gold = case["id"], case.get("gold_relevant_notes") or []
        if case["class"] == "abstain":
            if gold or not case.get("abstain"):
                problems.append(f"{cid}: abstain case with gold notes or abstain=false")
            continue
        if not 1 <= len(gold) <= 3:
            problems.append(f"{cid}: {len(gold)} gold notes")
        missing = [g for g in gold if g not in notes]
        if missing:
            problems.append(f"{cid}: gold not in corpus {missing}")
            continue
        excerpt = _norm(" ".join(notes[g]["excerpt"] for g in gold))
        for fact in case.get("required_facts") or []:
            if _norm(fact) not in excerpt:
                problems.append(f"{cid}: required fact not in gold excerpt: {fact[:40]!r}")
        if case["class"] == "multi_hop":
            hops = case.get("hops") or []
            if len(hops) != 2 or hops[1] not in gold:
                problems.append(f"{cid}: hops must be [start, gold]")
                continue
            relations = pairs.get(frozenset(hops), set())
            if not relations:
                problems.append(f"{cid}: no link between the hop notes")
            elif case.get("relation") and case["relation"] not in relations:
                problems.append(f"{cid}: relation {case['relation']} not among {sorted(relations)}")
    return problems


@pytest.mark.parametrize("path", [BENCH, CORPUS], ids=lambda p: p.name)
def test_the_frozen_bytes_have_not_changed(path):
    assert _sha(path) == _recorded(path), f"{path.name} changed after it was frozen"


def test_the_benchmark_is_the_one_the_labeller_delivered():
    assert _sha(BENCH) == "eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa"


def test_composition_matches_the_preregistration():
    bench = json.loads(BENCH.read_text(encoding="utf-8"))
    assert bench["labeller"] == "chatgpt"
    assert bench["vault_commit"] == json.loads(CORPUS.read_text(encoding="utf-8"))["vault_commit"]
    counts = collections.Counter(c["class"] for c in bench["cases"])
    assert dict(counts) == EXPECTED_CLASSES
    ids = [c["id"] for c in bench["cases"]]
    assert len(ids) == len(set(ids)) == 160


def test_every_label_holds_against_the_labelling_corpus():
    bench = json.loads(BENCH.read_text(encoding="utf-8"))
    corpus = json.loads(CORPUS.read_text(encoding="utf-8"))
    assert label_problems(bench, corpus) == []


# --- the checks can fail --------------------------------------------------------

def _mini():
    corpus = {"notes": [{"id": "a", "excerpt": "alpha beta gamma"}, {"id": "b", "excerpt": "delta epsilon"}],
              "links": [{"source": "a", "target": "b", "relation": "related_to"}]}
    good = {"id": "X", "class": "multi_hop", "gold_relevant_notes": ["b"], "required_facts": ["delta"],
            "hops": ["a", "b"], "relation": "related_to", "abstain": False}
    return corpus, good


def test_a_fact_missing_from_the_gold_excerpt_is_caught():
    corpus, case = _mini()
    case["required_facts"] = ["omega"]
    assert any("required fact" in p for p in label_problems({"cases": [case]}, corpus))


def test_a_hop_without_a_link_is_caught():
    corpus, case = _mini()
    corpus["links"] = []
    assert any("no link" in p for p in label_problems({"cases": [case]}, corpus))


def test_a_gold_note_absent_from_the_corpus_is_caught():
    corpus, case = _mini()
    case["gold_relevant_notes"] = ["zzz"]
    assert any("gold not in corpus" in p for p in label_problems({"cases": [case]}, corpus))


def test_a_valid_case_passes():
    corpus, case = _mini()
    assert label_problems({"cases": [case]}, corpus) == []
