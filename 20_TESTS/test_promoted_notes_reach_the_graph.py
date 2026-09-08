"""A promoted note that produces no edge is an island, not memory.

`SynapseStore.from_index()` reads `target_id` and `type` from a note's
`relations:` block, and silently skips anything else — `synapse_store.py`
does a bare `continue` when `target_id` is absent. So a note can declare a
relation, validate on write, read back fine in Obsidian, and contribute
nothing to retrieval.

That is exactly what `Promoted_reservoir_sampling.md` did. It carried

    relations:
      - relation: derived_from
        target: "01_ARCHITECTURE/ontology/slots/06_procedures.md"

which is wrong three times over: `target` instead of `target_id`, a file path
where a note id belongs, `relation` instead of `type`, and `derived_from`
which is not in ALLOWED_RELATIONS and degrades to `related_to`. Measured
before the fix: 0 neighbours. After: 1, and the corpus went from 260 edges to
262.

These tests run against the real index and the real store, because the defect
was invisible to every schema check that already existed.
"""

from __future__ import annotations

import pathlib
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "03_IMPLEMENTATION" / "packages"))

from graph.synapse_store import ALLOWED_RELATIONS, SynapseStore  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

PROMOTED = sorted((_REPO / "01_ARCHITECTURE" / "knowledge").glob("Promoted_*.md"))


@pytest.fixture(scope="module")
def index():
    return VaultIndex.load(str(_REPO))


@pytest.fixture(scope="module")
def store(index):
    return SynapseStore.from_index(index)


def test_there_is_something_to_check():
    """Guards the guard: a glob that matches nothing passes vacuously."""
    assert PROMOTED, "no Promoted_*.md notes found; this suite proves nothing"


@pytest.mark.parametrize("path", PROMOTED, ids=lambda p: p.name)
def test_a_promoted_note_reaches_the_graph(path, index, store):
    """Not 'the frontmatter looks right' — an actual edge in the actual store."""
    note = next((n for n in index.notes if n.path == path or str(n.path) == str(path)), None)
    if note is None:
        note = next((n for n in index.notes if pathlib.Path(str(n.path)).name == path.name), None)
    assert note is not None, f"{path.name} is not in the index at all"

    neighbours = store.neighbors(note.id)
    assert neighbours, (
        f"{path.name} declares relations but produces no edge. "
        "from_index() reads `target_id` and `type`; check for `target`, "
        "`relation`, a file path where a note id belongs, or a relation not "
        f"in {sorted(ALLOWED_RELATIONS)}"
    )


@pytest.mark.parametrize("path", PROMOTED, ids=lambda p: p.name)
def test_a_promoted_note_names_only_allowed_relations(path, index):
    """A relation outside the enum degrades to related_to without complaint.

    Silent degradation is worse than rejection here: the note keeps a
    relation type that reads as meaningful in the file and is not the one the
    graph holds.
    """
    note = next((n for n in index.notes if pathlib.Path(str(n.path)).name == path.name), None)
    assert note is not None
    for rel in note.relations():
        declared = str(rel.get("type") or "").lower()
        assert declared in ALLOWED_RELATIONS, (
            f"{path.name} declares {declared!r}, which is not in "
            f"{sorted(ALLOWED_RELATIONS)} and will silently become 'related_to'"
        )
        assert rel.get("target_id"), (
            f"{path.name} has a relation with no target_id; it will be skipped"
        )
