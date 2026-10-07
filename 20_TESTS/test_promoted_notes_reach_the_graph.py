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


# Promoted notes whose every typed relation was rejected by the independent edge audit
# (Wave 1: 29 relations, Wave 2: 55; `07_EVALUATION/edge_audit_v2_remaining/AUDIT_RESULT.md`) and
# which therefore declare `relations: []`. They are genuine islands: no true relation is known.
# They used to be kept off the island list by sentences injected into their bodies
# ("... optimizing sample preservation in [[long-term memory]]"); that prose was unsupported and was
# removed (PR #209 B11), so they are listed here instead of being hidden. Fix an entry by declaring a
# real, audited relation in its frontmatter and deleting it from this set. The set may only shrink.
KNOWN_ISLANDS_AFTER_AUDITED_PURGE = frozenset({
    "Promoted_buffer.md",
    "Promoted_homeostat.md",
    "Promoted_regulation.md",
    "Promoted_reinforcement_learning.md",
    "Promoted_reservoir_sampling.md",
    "Promoted_retrieval.md",
    "Promoted_state_determined_system.md",
    "Promoted_transformation.md",
    "Promoted_variety.md",
})


@pytest.mark.parametrize("path", PROMOTED, ids=lambda p: p.name)
def test_a_promoted_note_reaches_the_graph(path, index, store):
    """Not 'the frontmatter looks right' — an actual edge in the actual store.

    Every promoted note outside `KNOWN_ISLANDS_AFTER_AUDITED_PURGE` must have an out-edge. The known
    islands are not excluded silently: `test_the_set_of_islands_is_pinned` below asserts that the
    set of promoted notes without an edge is exactly that list.
    """
    if path.name in KNOWN_ISLANDS_AFTER_AUDITED_PURGE:
        pytest.skip("known island after the audited purge; pinned by test_the_set_of_islands_is_pinned")
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


def test_the_set_of_islands_is_pinned(index, store):
    """Every promoted note without an out-edge is on the known list, and every entry still is one.

    A new island fails here (nothing may quietly join the list's complement); a fixed island must be
    removed from the list, so the debt only ever shrinks.
    """
    islands = set()
    for path in PROMOTED:
        note = next((n for n in index.notes if pathlib.Path(str(n.path)).name == path.name), None)
        assert note is not None, f"{path.name} is not in the index at all"
        if not store.neighbors(note.id):
            islands.add(path.name)
    assert islands == set(KNOWN_ISLANDS_AFTER_AUDITED_PURGE), (
        f"new islands: {sorted(islands - KNOWN_ISLANDS_AFTER_AUDITED_PURGE)}; "
        f"fixed but still listed: {sorted(set(KNOWN_ISLANDS_AFTER_AUDITED_PURGE) - islands)}"
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
