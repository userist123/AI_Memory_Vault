"""Every floor-passing legal note carries a provenance mapping, and the
derivation script refuses to insert one twice.

Before 30_SCRIPTS/knowledge/derive_legal_provenance.py ran, the 8 legal acts,
their 8 indexes, 10 atomic obligations and one MOC passed the agent's
lifecycle floor, ranked first on almost any legal query, took the page's
slots and were then rejected by the pack builder for having no provenance
(07_EVALUATION/reranker_envelope/DEVIATIONS.md, D-2). This test keeps that
from coming back: a new legal note without provenance fails here, not at
egress with a BUDGET_EXCEEDED label.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest
import yaml

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "30_SCRIPTS" / "knowledge"))

import derive_legal_provenance as dlp  # noqa: E402

LEGAL = REPO / "01_ARCHITECTURE" / "knowledge" / "legal"
NOTES = sorted(p for sub in ("primary", "legal_indexes", "atomic") for p in (LEGAL / sub).glob("*.md"))


def frontmatter(path: Path) -> dict:
    return yaml.safe_load(dlp.split_frontmatter(path.read_text(encoding="utf-8"))[0]) or {}


def test_the_legal_corpus_is_where_this_test_thinks_it_is():
    assert len(NOTES) == 26, [p.name for p in NOTES]


@pytest.mark.parametrize("path", NOTES, ids=lambda p: p.stem)
def test_floor_passing_legal_note_has_schema_valid_provenance(path: Path):
    fm = frontmatter(path)
    if fm.get("lifecycle") not in ("ACTIVE", "REVIEW"):
        pytest.skip("below the agent floor; egress never sees it")
    prov = fm.get("provenance")
    assert isinstance(prov, dict), f"{path.name}: no provenance mapping -> rejected at egress"
    dlp.check(prov)  # required keys, enums, no extra keys (lifecycle schema, additionalProperties: False)
    assert prov["source_ref"].startswith("http"), "legal provenance must point at the official publication"


def test_acts_are_official_and_derived_notes_are_ai_pointing_at_their_act():
    acts = {p.stem: frontmatter(p) for p in (LEGAL / "primary").glob("*.md")}
    for fm in acts.values():
        assert fm["provenance"]["source_type"] == "official"
        assert fm["provenance"]["source_ref"] == fm["official_url"]
    for p in list((LEGAL / "legal_indexes").glob("*.md")) + list((LEGAL / "atomic").glob("*.md")):
        fm = frontmatter(p)
        parent = dlp.WIKILINK.match(fm["source_act"]).group(1)
        assert fm["provenance"]["source_type"] == "ai", p.name
        assert fm["provenance"]["source_ref"] == acts[parent]["official_url"], p.name


def test_the_moc_map_has_provenance():
    assert frontmatter(dlp.MOC)["provenance"]["source_type"] == "user"


def test_the_script_refuses_to_insert_provenance_twice():
    path, prov, anchor = dlp.build_plan()[0]
    with pytest.raises(ValueError, match="already has provenance"):
        dlp.insert_block(path.read_text(encoding="utf-8"), dlp.render(prov), anchor)
