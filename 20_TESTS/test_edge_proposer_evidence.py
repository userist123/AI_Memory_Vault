"""A typed relation needs evidence in the text, not two notes sharing a word.

The 50-edge audit put strong relations at 3/25: `supersedes` 0/5, `depends_on`
3/20, `part_of` 4/10. The causes were mechanical — a similar title and a newer
date produced `supersedes`; being a project produced `part_of` in the wrong
direction; a keyword anywhere in a long note produced `caused` or `depends_on`
against whatever note happened to share vocabulary. These tests hold the
repaired rules, and each one shows the rule can still fire when the evidence is
really there.
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from types import SimpleNamespace

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

_spec = importlib.util.spec_from_file_location(
    "edge_proposer", REPO / "30_SCRIPTS" / "knowledge" / "edge_proposer.py")
ep = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ep)


def note(note_id: str, title: str, body: str, note_type: str = "knowledge", updated: str = "2026-01-01"):
    return SimpleNamespace(
        id=note_id, title=title, body=body, text=f"{title}\n{body}", type=note_type,
        updated=updated, tags=[], meta={}, path=SimpleNamespace(stem=note_id),
        relations=lambda: [], outgoing_ids=lambda: [], wikilinks=lambda: [],
    )


def classify(a, b):
    return ep.classify_relation(a, b, {"shared"})[0]


# --- supersedes needs more than a similar title ---------------------------------

def test_a_similar_title_and_a_newer_date_are_not_replacement():
    a = note("policy-v2", "Retention policy", "Keep records for seven years.", updated="2026-02-01")
    b = note("policy-v1", "Retention policy", "Keep records for five years.", updated="2026-01-01")
    assert classify(a, b) != "supersedes"


def test_supersedes_still_fires_when_one_note_points_at_the_other():
    a = note("policy-v2", "Retention policy",
             "This supersedes policy-v1 in full.", updated="2026-02-01")
    b = note("policy-v1", "Retention policy", "Keep records for five years.", updated="2026-01-01")
    relation, src, dst = ep.classify_relation(a, b, {"shared"})
    assert (relation, src, dst) == ("supersedes", "policy-v2", "policy-v1")


# --- part_of needs containment stated, not a type ------------------------------

def test_being_a_project_does_not_make_everything_part_of_it():
    project = note("gpo-baseline", "GPO Baseline Deployment", "Rollout plan.", note_type="project")
    practice = note("security-practices", "Security Practices",
                    "Domains: hardening, GPO baseline, DFIR triage.")
    assert classify(practice, project) != "part_of"


def test_part_of_fires_when_the_containing_note_names_the_part():
    whole = note("handbook", "Handbook", "Chapter two is security-practices, part of this handbook.")
    part = note("security-practices", "Security Practices", "Hardening and triage.")
    relation, src, dst = ep.classify_relation(whole, part, {"shared"})
    assert relation == "part_of"


# --- a keyword far from the reference states nothing ---------------------------

FAR = "\n".join(["filler"] * 40)


def test_a_keyword_in_another_section_does_not_bind_a_reference():
    a = note("a", "A", f"This module requires careful review.\n{FAR}\nSee also [[b-note]].")
    b = note("b-note", "B", "Unrelated content.")
    assert classify(a, b) != "depends_on"


def test_the_same_keyword_next_to_the_reference_does_bind():
    a = note("a", "A", "This module requires [[b-note]] to be installed first.")
    b = note("b-note", "B", "Unrelated content.")
    relation, src, dst = ep.classify_relation(a, b, {"shared"})
    assert (relation, src) == ("depends_on", "a")


def test_the_passage_window_spans_a_couple_of_lines():
    a = note("a", "A", "This step requires the following:\n\n- [[b-note]], installed first")
    b = note("b-note", "B", "Unrelated content.")
    assert classify(a, b) == "depends_on"


# --- everything without evidence lands on related_to ---------------------------

def test_two_notes_that_merely_share_vocabulary_get_related_to():
    a = note("a", "Alpha", "Shared vocabulary about caching.")
    b = note("b", "Beta", "More shared vocabulary about caching.")
    assert classify(a, b) == "related_to"


# --- near-identical bodies are duplicates, not relations ------------------------

def test_near_identical_bodies_are_recognised_as_duplicates():
    from collections import Counter
    # A real auto-generated policy lesson runs to a few hundred words; the id is
    # the only thing that differs between siblings. A three-line fixture would
    # not reach 0.98 and would say nothing about the rule.
    body_a = ("Error: the gate refused the write. Cause: no disposition manifest. "
              "Fix: run the manifest first. Rule: never bypass the canonical gate. " * 20
              + "id=aaa")
    body_b = body_a.replace("id=aaa", "id=bbb")
    wa, wb = Counter(body_a.split()), Counter(body_b.split())
    overlap = sum((wa & wb).values()) / max(sum(wa.values()), sum(wb.values()))
    assert overlap >= 0.98, "the duplicate rule must catch lessons differing only by an id"


def test_two_different_notes_are_not_near_duplicates():
    from collections import Counter
    wa = Counter("the ontology gate refuses an ungated canonical write".split())
    wb = Counter("retrieval benchmark v3 was labelled by an independent evaluator".split())
    overlap = sum((wa & wb).values()) / max(sum(wa.values()), sum(wb.values()))
    assert overlap < 0.98
