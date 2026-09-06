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
