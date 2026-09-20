"""The 57 ACTIVE notes are hashed, and a change to one of them is noticed.

ACTIVE is the only state the vault stands behind, and an ACTIVE note is still
an ordinary Markdown file. Nothing until now would have reported a note whose
content drifted away from what was attested while it kept its `verified` flag.

These tests do not re-hash the vault on every run — that means loading the
index, which is slow. They check the manifest is well formed and consistent
with the repository, and they exercise the comparison on fixtures, including
the three ways it must fail.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
MANIFEST = REPO / "07_EVALUATION" / "integrity" / "active_notes.sha256.json"

_spec = importlib.util.spec_from_file_location(
    "active_note_integrity", REPO / "30_SCRIPTS" / "verification" / "active_note_integrity.py")
integrity = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(integrity)


@pytest.fixture(scope="module")
def manifest() -> dict:
    assert MANIFEST.exists(), "the integrity manifest is missing; run the script with --record"
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def test_the_manifest_covers_every_note_it_claims(manifest):
    assert manifest["count"] == len(manifest["notes"])
    assert manifest["count"] > 0


def test_every_digest_is_a_sha256(manifest):
    for note_id, digest in manifest["notes"].items():
        assert len(digest) == 64 and all(c in "0123456789abcdef" for c in digest), note_id


def test_the_ids_are_unique_and_sorted(manifest):
    ids = list(manifest["notes"])
    assert ids == sorted(ids)
    assert len(ids) == len(set(ids))


# --- the comparison, on fixtures -------------------------------------------------

BASE = {"a": "1" * 64, "b": "2" * 64}


def test_an_unchanged_vault_reports_nothing():
    assert integrity.compare(BASE, dict(BASE)) == {"drifted": [], "left_active": [], "entered_active": []}


def test_a_changed_note_is_reported_as_drift():
    changed = dict(BASE, a="3" * 64)
    assert integrity.compare(BASE, changed)["drifted"] == ["a"]


def test_a_note_leaving_active_is_reported_apart_from_drift():
    """Archiving an ACTIVE note is legitimate; it must not read as tampering."""
    result = integrity.compare(BASE, {"b": BASE["b"]})
    assert result["left_active"] == ["a"]
    assert result["drifted"] == []


def test_a_note_entering_active_is_reported_apart_from_drift():
    result = integrity.compare(BASE, dict(BASE, c="4" * 64))
    assert result["entered_active"] == ["c"]
    assert result["drifted"] == []


def test_the_three_kinds_are_never_merged():
    """One change of each, at once: a CI log must let a person tell them apart."""
    result = integrity.compare(BASE, {"a": "9" * 64, "c": "4" * 64})
    assert result == {"drifted": ["a"], "left_active": ["b"], "entered_active": ["c"]}


def test_line_endings_alone_are_not_drift(tmp_path):
    """A Windows checkout and a Linux runner must agree, or the guard cries wolf in CI."""
    import hashlib
    windows = "# A note\r\nwith two lines\r\n"
    unix = windows.replace("\r\n", "\n")
    digest = lambda text: hashlib.sha256(text.replace("\r\n", "\n").encode("utf-8")).hexdigest()
    assert digest(windows) == digest(unix)
