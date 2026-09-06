"""Every benchmark case must be answerable from the real corpus.

v1 shipped 36 held-out and 12 dev cases whose gold references —
`note_agents_contract` and `note_vault_cognitive_rules` — resolve to nothing
in the vault. Scoring is `bool(gold & candidate_ids)`, so candidate and
context recall were structurally 0 for every case, in every configuration: a
graph-on versus graph-off comparison would have returned 0 versus 0 and
reported "no significant difference" about nothing.

Nobody noticed because the freeze guard hashed raw bytes and tripped on a
Windows CRLF checkout, so the runner never reached the cases.

These tests make that class of failure impossible to ship again.
"""
import hashlib
import json
from pathlib import Path

import pytest

from retrieval.vault_index import VaultIndex

REPO = Path(__file__).resolve().parents[1]
V2 = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2"
SETS = ("dev.json", "heldout.json")
REQUIRED_CLASSES = {
    "exact_identifier_lookup",
    "paraphrase",
    "synonym_substitution",
    "lexical_trap",
    "one_hop_graph_expansion",
    "unanswerable",
}


@pytest.fixture(scope="module")
def index():
    return VaultIndex.load(REPO, include_raw=True, include_archived=True)


def _cases(name):
    return json.loads((V2 / name).read_text(encoding="utf-8"))["cases"]


def _canonical(path: Path) -> bytes:
    return path.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")


@pytest.mark.parametrize("set_name", SETS)
def test_every_gold_reference_resolves_in_the_production_corpus(set_name, index):
    """THE v1 REGRESSION. Checked separately per set: a valid held-out set and
    a broken dev set would still poison threshold tuning."""
    missing = [
        (c["id"], g)
        for c in _cases(set_name)
        for g in c["gold_relevant_notes"]
        if g not in index.by_id
    ]
    assert not missing, f"gold ids absent from the corpus: {missing}"


@pytest.mark.parametrize("set_name", SETS)
def test_required_facts_are_present_in_the_gold_note(set_name, index):
    """A resolvable gold note whose text lacks the required fact makes the case
    unanswerable in a subtler way."""
    bad = []
    for c in _cases(set_name):
        for g in c["gold_relevant_notes"]:
            text = index.by_id[g].text.lower()
            bad += [(c["id"], f) for f in c["required_facts"] if f.lower() not in text]
    assert not bad, f"required facts absent from their gold note: {bad}"


@pytest.mark.parametrize("set_name", SETS)
def test_abstain_and_gold_are_consistent(set_name):
    for c in _cases(set_name):
        if c["abstain"]:
            assert not c["gold_relevant_notes"], f"{c['id']}: abstain case carries gold"
        else:
            assert c["gold_relevant_notes"], f"{c['id']}: answerable case has no gold"


@pytest.mark.parametrize("set_name", SETS)
def test_all_query_classes_are_represented(set_name):
    present = {c["class"] for c in _cases(set_name)}
    assert present == REQUIRED_CLASSES, f"class drift: {present ^ REQUIRED_CLASSES}"


def test_the_multihop_class_name_matches_what_the_runtime_does():
    """`MemoryController.search()` traverses one hop along `neighbors(seed)`.
    A class named `cross_cluster_multihop` would assert a capability the
    runtime does not have."""
    controller = (REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "controller.py").read_text(
        encoding="utf-8", errors="ignore"
    )
    assert "Traverse 1 hop" in controller, (
        "the controller's traversal depth changed; revisit the class name and "
        "the subpopulation figures in CONTRACT.md before renaming anything"
    )
    for name in SETS:
        assert not any(c["class"] == "cross_cluster_multihop" for c in _cases(name))


@pytest.mark.parametrize("set_name", SETS)
def test_frozen_hash_matches_over_canonical_bytes(set_name):
    path = V2 / set_name
    recorded = (V2 / (set_name + ".sha256")).read_text(encoding="utf-8").strip()
    assert hashlib.sha256(_canonical(path)).hexdigest() == recorded


@pytest.mark.parametrize("set_name", SETS)
def test_line_ending_conversion_does_not_change_the_digest(set_name):
    """The v1 guard hashed raw bytes and failed on a Windows checkout for
    content that was never modified."""
    raw = (V2 / set_name).read_bytes()
    as_lf = raw.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    as_crlf = as_lf.replace(b"\n", b"\r\n")
    digest = lambda b: hashlib.sha256(b.replace(b"\r\n", b"\n").replace(b"\r", b"\n")).hexdigest()
    assert digest(as_lf) == digest(as_crlf)


def test_a_real_content_change_still_breaks_the_hash():
    """Normalisation must not weaken the guard it repairs."""
    raw = _canonical(V2 / "heldout.json")
    tampered = raw.replace(b"Very High.", b"Very Low.", 1)
    assert tampered != raw, "probe string missing; update this test"
    assert hashlib.sha256(tampered).hexdigest() != hashlib.sha256(raw).hexdigest()


def test_v1_is_marked_invalid_and_not_silently_repaired():
    v1 = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v1"
    assert (v1 / "INVALID.md").exists(), "v1 must stay in the tree, marked, as an audit artefact"
