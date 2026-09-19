"""Precision filters in the edge proposer.

A hand-verified sample of 50 proposals measured 18% precision (r007), against
a 70% bar. The failures were structural, not incidental, and each filter here
answers one of them. These tests pin the filters so precision cannot silently
regress the next time the corpus grows.
"""
import importlib.util
import math
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "edge_proposer", REPO / "30_SCRIPTS" / "knowledge" / "edge_proposer.py"
)
ep = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ep)


class _Note:
    def __init__(self, path):
        self.path = Path(path)


# --- ephemeral notes -------------------------------------------------------


@pytest.mark.parametrize("path", [
    "10_DOCUMENTATION/resources/Obsidian/Artifacts/PERPLEXITY_TAKEOVER_PACKAGE.md",
    "00_GOVERNANCE/coordination/agents/ANTIGRAVITY/CURRENT.md",
    "00_GOVERNANCE/STATUS_SNAPSHOT_20260904_LUNA.md",
    "02_PRODUCT/projects/Continuity_Handoff.md",
])
def test_session_dumps_and_scratchpads_are_excluded(path):
    """These accounted for 37% of proposals and were judged wrong every time:
    takeover packages share system vocabulary with each other, and CURRENT.md
    files change hourly, so an edge to one is stale before review."""
    assert ep._is_ephemeral(_Note(path))


@pytest.mark.parametrize("path", [
    "01_ARCHITECTURE/knowledge/Retrieval_Architecture.md",
    "10_DOCUMENTATION/procedures/Import_Sanitization.md",
])
def test_durable_notes_are_not_excluded(path):
    assert not ep._is_ephemeral(_Note(path))


# --- entity blacklists -----------------------------------------------------


@pytest.mark.parametrize("token", [
    "and", "get", "exists", "model", "real", "http", "none", "important",
])
def test_generic_tokens_are_spurious(token):
    """The entity regex admits short lowercase words as acronyms; these joined
    entirely unrelated notes."""
    assert token in ep.SPURIOUS_ENTITIES


@pytest.mark.parametrize("token", [
    "consiliul", "european", "monitorul", "oficial", "anexa", "care", "pentru",
])
def test_legal_document_furniture_is_spurious(token):
    """Every act published in Monitorul Oficial carries these, so they linked
    a crypto-asset regulation to a military classified-information order."""
    assert token in ep.SPURIOUS_ENTITIES


@pytest.mark.parametrize("token", ["wpf", "mvvm", "httpclient", "textbox"])
def test_ui_framework_names_are_spurious(token):
    """Two notes sharing a UI toolkit are not topically related: WPF joined a
    military transfer register to a forensics tool."""
    assert token in ep.SPURIOUS_ENTITIES


@pytest.mark.parametrize("token", ["4.5.2016", "21.4.1993", "2016", "1993"])
def test_dates_are_not_entities(token):
    """CELEX citation headers tied unrelated EU regulations together."""
    assert ep.DATE_LIKE_RE.match(token)


@pytest.mark.parametrize("token", ["____", "_________", "-----", "..."])
def test_separator_runs_are_not_entities(token):
    assert ep.FILLER_RE.match(token)


@pytest.mark.parametrize("token", ["metatrader", "orniss", "eventlogreader", "xauusd"])
def test_real_domain_terms_survive_every_filter(token):
    """The filters must not swallow the signal they exist to isolate."""
    assert token not in ep.SPURIOUS_ENTITIES
    assert not ep.DATE_LIKE_RE.match(token)
    assert not ep.FILLER_RE.match(token)


# --- length normalisation --------------------------------------------------


def _coverage(shared, size_a, size_b):
    return math.sqrt((shared / size_a) * (shared / size_b))


def test_a_thousand_entity_catalogue_fails_the_coverage_gate():
    """Master_Skills_Catalog_251 holds 1079 entities against a corpus median
    of 3, and appeared in 7 of 25 sampled proposals, wrong in 6. Six shared
    entities mean something different for a 20-entity note than for it."""
    assert _coverage(6, 20, 1079) < ep.MIN_OVERLAP_COVERAGE


def test_two_comparable_notes_pass_the_coverage_gate():
    assert _coverage(6, 20, 20) >= ep.MIN_OVERLAP_COVERAGE


def test_coverage_is_symmetric():
    assert _coverage(5, 30, 60) == pytest.approx(_coverage(5, 60, 30))


def test_thresholds_are_the_calibrated_values():
    """Loosening these silently is how precision regresses; change them
    deliberately, with a fresh hand-verified sample."""
    assert ep.MIN_OVERLAP_COVERAGE == 0.10
    assert ep.RARE_ENTITY_DF_MAX == 5


# --- OpenStax curriculum boilerplate filter (Point 5) ----------------------


@pytest.mark.parametrize("token", [
    "openstax", "psychology", "curriculum", "ch08",
    "provenance_manifest", "verified-source", "cc-by",
    "source_ref", "source_date", "extraction_date", "original_path",
])
def test_openstax_boilerplate_is_spurious(token):
    """Every OpenStax note carries these in provenance or headings; without this
    filter, all 120 section-pairs in Chapter 8 share them as spurious entities."""
    assert token in ep.SPURIOUS_ENTITIES


def test_openstax_boilerplate_filtered_from_note_entities():
    """Verify that build_entity_df excludes OpenStax boilerplate."""
    class _MockNote:
        id = "openstax-test-note"
        text = "openstax psychology curriculum ch08 AtkinsonShiffrin Model"
        tags = ["verified-source", "curriculum"]

    class _MockIndex:
        notes = [_MockNote()]

    ents, _ = ep.build_entity_df(_MockIndex())
    note_ents = ents["openstax-test-note"]
    assert "openstax" not in note_ents
    assert "psychology" not in note_ents
    assert "curriculum" not in note_ents
    assert "ch08" not in note_ents
    assert "verified-source" not in note_ents


def test_openstax_filter_negative_control_fails_on_unfiltered_spurious():
    """NEGATIVE CONTROL: If a spurious token were removed from SPURIOUS_ENTITIES,
    this check must be able to fail."""
    fake_allowed = "openstax"
    assert fake_allowed in ep.SPURIOUS_ENTITIES, (
        "NEGATIVE CONTROL: openstax MUST be in SPURIOUS_ENTITIES to prevent false cross-section edges"
    )


# --- Python internals, dunders, and version strings filter (Point 4) --------


@pytest.mark.parametrize("token", [
    "__future__", "__main__", "__all__", "__dict__", "__class__",
    "__module__", "__annotations__", "__builtins__",
    "sys", "os", "pathlib", "typing", "datetime", "subprocess",
    "argparse", "hashlib", "collections", "itertools", "functools",
    "unittest", "pytest",
    "5.0.45", "127.0.0.1", "1.0.0", "v1.2.3", "3.14.2", "9.0.2",
])
def test_python_internals_and_versions_are_spurious(token):
    """Python dunders, standard library modules, and version strings must be
    flagged as spurious to prevent vacuous proposals between unrelated modules."""
    assert ep.is_spurious_entity(token)


def test_regression_round2_python_internals_and_version_edge_rejected():
    """REGRESSION: Round 2 edge_proposer linked two notes with 'depends_on'
    purely based on __future__, __main__, and 5.0.45.
    Verify that two notes sharing only python internals and version strings
    are completely filtered out and generate 0 proposals."""
    class _MockNoteA:
        id = "note-worker-a"
        title = "Worker A Module"
        path = Path("01_ARCHITECTURE/Worker_A.md")
        text = "from __future__ import annotations\nimport sys\nimport os\nif __name__ == '__main__':\n    version = '5.0.45'\n"
        body = text
        tags = []
        type = "architecture"
        updated = "2026-09-01"

    class _MockNoteB:
        id = "note-runner-b"
        title = "Runner B Script"
        path = Path("01_ARCHITECTURE/Runner_B.md")
        text = "from __future__ import annotations\nimport sys\nimport os\nif __name__ == '__main__':\n    release = '5.0.45'\n"
        body = text
        tags = []
        type = "architecture"
        updated = "2026-09-02"

    class _MockIndex:
        notes = [_MockNoteA(), _MockNoteB()]
        by_id = {"note-worker-a": _MockNoteA(), "note-runner-b": _MockNoteB()}
        def __len__(self):
            return 2

    ents, df = ep.build_entity_df(_MockIndex())
    assert len(ents["note-worker-a"]) == 0
    assert len(ents["note-runner-b"]) == 0

    proposals, count = ep.deterministic_candidates(_MockIndex())
    assert len(proposals) == 0
    assert count == 0


def test_proposals_require_both_source_and_target_verbatim_quotes():
    """All proposals must contain source_quote and target_quote which are
    exact non-empty substrings in the source and target note bodies."""
    class _MockNote:
        def __init__(self, nid, title, path, body, text, tags, ntype="knowledge"):
            self.id = nid
            self.title = title
            self.path = Path(path)
            self.body = body
            self.text = text
            self.tags = tags
            self.type = ntype
            self.updated = "2026-09-01"

    n1 = _MockNote(
        "note-hebbian-1", "Hebbian Learning", "01_ARCHITECTURE/Hebbian_Learning.md",
        "HebbianPlasticity occurs when SynapticPotentiation activates LongTermPlasticity in neurons.",
        "HebbianPlasticity occurs when SynapticPotentiation activates LongTermPlasticity in neurons.",
        ["neuro"]
    )
    n2 = _MockNote(
        "note-synaptic-2", "Synaptic Rules", "01_ARCHITECTURE/Synaptic_Rules.md",
        "In the brain, HebbianPlasticity and SynapticPotentiation enable LongTermPlasticity.",
        "In the brain, HebbianPlasticity and SynapticPotentiation enable LongTermPlasticity.",
        ["neuro"]
    )
    all_notes = [n1, n2]
    for i in range(18):
        all_notes.append(_MockNote(
            f"bg-{i}", f"Background {i}", f"01_ARCHITECTURE/bg_{i}.md",
            "Unrelated background context for corpus diversity.",
            "Unrelated background context for corpus diversity.",
            []
        ))

    class _MockIndex:
        notes = all_notes
        by_id = {x.id: x for x in all_notes}
        def __len__(self):
            return len(all_notes)

    proposals, _ = ep.deterministic_candidates(_MockIndex())
    assert len(proposals) > 0
    for p in proposals:
        assert "source_quote" in p and p["source_quote"]
        assert "target_quote" in p and p["target_quote"]
        src = _MockIndex.by_id[p["source_id"]]
        dst = _MockIndex.by_id[p["target_id"]]
        assert p["source_quote"] in src.body
        assert p["target_quote"] in dst.body


def test_negative_control_missing_quote_prevents_proposal():
    """NEGATIVE CONTROL: If shared entities are present in metadata/tags but
    one note's body contains NO verbatim sentence with the entity,
    extract_evidence_quote returns empty and no proposal is created."""
    class _MockNoteA:
        id = "note-a"
        title = "Theory A"
        path = Path("01_ARCHITECTURE/Theory_A.md")
        body = "The hippocampal consolidation process stabilizes synaptic memories over time."
        text = body + " hippocampal consolidation neurogenesis"
        tags = ["hippocampal", "consolidation", "neurogenesis"]
        type = "knowledge"
        updated = "2026-09-01"

    class _MockNoteB:
        id = "note-b"
        title = "Theory B"
        path = Path("01_ARCHITECTURE/Theory_B.md")
        body = "This document discusses general cognitive architecture and abstract graphs."
        text = body + " hippocampal consolidation neurogenesis"
        tags = ["hippocampal", "consolidation", "neurogenesis"]
        type = "knowledge"
        updated = "2026-09-02"

    class _MockIndex:
        notes = [_MockNoteA(), _MockNoteB()]
        by_id = {"note-a": _MockNoteA(), "note-b": _MockNoteB()}
        def __len__(self):
            return 2

    shared = {"hippocampal", "consolidation", "neurogenesis"}
    quote_b = ep.extract_evidence_quote(_MockNoteB.body, shared)
    assert quote_b == "", "NEGATIVE CONTROL: quote must be empty when body lacks entity context"

    proposals, _ = ep.deterministic_candidates(_MockIndex())
    assert len(proposals) == 0, "NEGATIVE CONTROL: proposal must be blocked when target quote is missing"

