"""Obsidian export residue must not be indexed as canonical memory.

CLAUDE.md states that Obsidian is a projection over the canonical vault and
that a second canonical database must not be created inside it. The export
directory had become exactly that: 97 of 939 indexed notes lived there, 41
without an `id:`, and several were near-copies of canonical notes carrying a
DIFFERENT id — the same content indexed twice under two identities, splitting
its edges and letting retrieval spend one context budget on two copies.
"""
from pathlib import Path

import pytest

from retrieval.vault_index import VaultIndex, _is_export_residue


def _write(root: Path, rel: str, body: str):
    p = root / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(body, encoding="utf-8")
    return p


@pytest.mark.parametrize("rel", [
    "10_DOCUMENTATION/resources/Obsidian/Artifacts/Tech_Stack.md",
    "10_DOCUMENTATION/resources/Obsidian/Artifacts/01_KNOWLEDGE__Tech_Stack.md",
])
def test_export_paths_are_recognised_as_residue(rel):
    assert _is_export_residue(Path(rel))


@pytest.mark.parametrize("rel", [
    "01_ARCHITECTURE/knowledge/Tech_Stack.md",
    "10_DOCUMENTATION/resources/Obsidian/Graph_Setup.md",
    "02_PRODUCT/projects/Artifacts_Handling.md",
])
def test_canonical_paths_are_not_residue(rel):
    """The marker is the Obsidian+Artifacts pair, not either word alone: a
    canonical note about artifacts must stay indexed."""
    assert not _is_export_residue(Path(rel))


def test_duplicate_export_copy_is_excluded_by_default(tmp_path):
    canonical = "---\nid: real-id\n---\n# Tech Stack\n" + "content " * 40
    export = "---\nid: forked-id\n---\n# Tech Stack\n" + "content " * 40
    _write(tmp_path, "01_ARCHITECTURE/knowledge/Tech_Stack.md", canonical)
    _write(tmp_path, "10_DOCUMENTATION/resources/Obsidian/Artifacts/Tech_Stack.md", export)

    index = VaultIndex.load(tmp_path)
    ids = {n.id for n in index.notes}
    assert "real-id" in ids
    assert "forked-id" not in ids, "the export copy must not be a second note"


def test_exclusion_can_be_turned_off_explicitly(tmp_path):
    """Auditing the residue requires being able to see it."""
    _write(tmp_path, "01_ARCHITECTURE/knowledge/Tech_Stack.md",
           "---\nid: real-id\n---\n# Tech Stack\n" + "x " * 40)
    _write(tmp_path, "10_DOCUMENTATION/resources/Obsidian/Artifacts/Tech_Stack.md",
           "---\nid: forked-id\n---\n# Tech Stack\n" + "x " * 40)

    index = VaultIndex.load(tmp_path, exclude_export_residue=False)
    assert {"real-id", "forked-id"} <= {n.id for n in index.notes}


def test_a_wikilink_resolves_to_the_canonical_note_not_the_copy(tmp_path):
    """With both indexed, `[[Tech Stack]]` resolved to whichever won the slug
    race. Only the canonical note should be reachable."""
    _write(tmp_path, "01_ARCHITECTURE/knowledge/Tech_Stack.md",
           "---\nid: real-id\n---\n# Tech Stack\n" + "x " * 40)
    _write(tmp_path, "10_DOCUMENTATION/resources/Obsidian/Artifacts/Tech_Stack.md",
           "---\nid: forked-id\n---\n# Tech Stack\n" + "x " * 40)

    index = VaultIndex.load(tmp_path)
    assert index.resolve("Tech_Stack").id == "real-id"
    assert index.resolve("Tech Stack").id == "real-id"
