"""The external-skill registry tells the truth about which skills still exist.

01_ARCHITECTURE/knowledge/EXTERNAL_SKILLS/_REGISTRY/SKILL_REGISTRY.json is a provenance record
(source repository, URL, licence of imported skills). On 2026-10-24 all 251 entries said
`active` while 41 pointed at skills removed from the repo in September. Removed entries are
kept for provenance with status `removed` and the commit that deleted them.
"""
from __future__ import annotations

import json
import os
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
REG = REPO / "01_ARCHITECTURE" / "knowledge" / "EXTERNAL_SKILLS" / "_REGISTRY" / "SKILL_REGISTRY.json"


def _load():
    return json.loads(REG.read_text(encoding="utf-8"))


def test_registry_is_marked_as_provenance():
    d = _load()
    assert d["registry_kind"] == "provenance"
    assert "not the list of skills currently available" in d["note"]


def test_no_active_entry_points_at_a_missing_skill():
    dead = [e["name"] for e in _load()["skills"]
            if e.get("status") == "active" and not (REPO / e["source_path"]).is_file()]
    assert dead == [], f"active entries whose skill no longer exists: {dead}"


def test_every_removed_entry_names_the_commit_that_removed_it():
    for e in _load()["skills"]:
        if e.get("status") == "removed":
            assert e.get("removed_in", {}).get("commit"), e["name"]


def test_counters_match_the_entries():
    d = _load()
    s = d["skills"]
    assert d["total_skills"] == len(s)
    assert d["active_skills"] == sum(1 for e in s if e.get("status") == "active")
    assert d["removed_skills"] == sum(1 for e in s if e.get("status") == "removed")
    assert {e.get("status") for e in s} <= {"active", "removed"}
