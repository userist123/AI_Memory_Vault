"""The promoter must emit a relation the graph reader actually reads.

`test_promoted_notes_reach_the_graph.py` checks notes that already exist. It
is a backstop and it fires too late: by the time it fails, an island note has
been written into the vault and someone has to go and repair it.

This checks the generator. `promote_candidate_concept.py` emitted

    relations:
      - relation: derived_from
        target: "01_ARCHITECTURE/ontology/slots/06_procedures.md"

which `SynapseStore.from_index()` skips three times over: it reads `type` and
not `relation`, it reads `target_id` and not `target`, and `derived_from` is
not in ALLOWED_RELATIONS. Every note it produced was an island by
construction. Repairing the one existing note and widening the schema left
the generator untouched, so the next promotion would have reproduced it.

The tests run the real function against temporary directories, so nothing is
written into the vault.
"""

from __future__ import annotations

import pathlib
import sys
import uuid

import pytest
import yaml

_REPO = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "ingestion"))
sys.path.insert(0, str(_REPO / "03_IMPLEMENTATION" / "packages"))

import promote_candidate_concept as P  # noqa: E402
from graph.synapse_store import ALLOWED_RELATIONS, SynapseStore  # noqa: E402

SLOT_ID = "slot-06-procedures"
CONCEPT = "Quenched Harmonic Buffering"


@pytest.fixture
def vault(tmp_path):
    """A slots dir with one slot holding one proposed candidate, and a notes dir."""
    slots = tmp_path / "slots"
    slots.mkdir()
    (slots / "06_procedures.md").write_text(
        "---\n"
        f"id: {SLOT_ID}\n"
        "ontology_slot: procedures\n"
        "type: ontology_definition\n"
        "---\n\n"
        "# Ontology Slot: Procedures\n\n"
        "## Candidate concepts\n"
        "| concept | source_book | confidence | status | date_added "
        "| promoted_note_id | evidence | occurrences |\n"
        "|---|---|---|---|---|---|---|---|\n"
        f"| {CONCEPT} | Test Book | 0.85 | proposed | 2026-09-11 |  |  | 3 |\n",
        encoding="utf-8",
    )
    notes = tmp_path / "knowledge"
    notes.mkdir()
    return slots, notes


def _promote(vault):
    slots, notes = vault
    P.promote_candidate_concept(
        concept_name=CONCEPT,
        slot_name="procedures",
        rewritten_definition="A scheme that defers a write until its replica has decayed.",
        slots_dir=str(slots),
        notes_dir=str(notes),
        judgment_reasoning="load-bearing for the write path",
    )
    written = list(notes.glob("*.md"))
    assert len(written) == 1, f"expected one note, got {written}"
    text = written[0].read_text(encoding="utf-8")
    return yaml.safe_load(text.split("---", 2)[1])


def test_the_emitted_relation_uses_the_keys_the_graph_reads(vault):
    front = _promote(vault)
    rels = front["relations"]
    assert len(rels) == 1
    rel = rels[0]

    assert "type" in rel, "from_index reads `type`, not `relation`"
    assert "target_id" in rel, "from_index reads `target_id`, not `target`"
    assert rel["type"] in ALLOWED_RELATIONS, (
        f"{rel['type']!r} is outside the closed vocabulary and degrades "
        "silently to related_to"
    )
    assert rel["target_id"] == SLOT_ID, (
        "target_id must be the slot's note id; a file path resolves to "
        "nothing in the index"
    )


def test_the_emitted_note_produces_a_real_edge(vault):
    """Against the real SynapseStore, not against the frontmatter's shape.

    The note and the slot are presented to `from_index` through the same
    interface a VaultIndex offers, so this exercises the actual parsing that
    skipped the old shape.
    """
    front = _promote(vault)

    class _Note:
        def __init__(self, note_id, relations, text="x" * 500):
            self.id = note_id
            self.text = text
            self._relations = relations

        def relations(self):
            return self._relations

    class _Index:
        def __init__(self, notes):
            self.notes = notes
            self.by_id = {n.id: n for n in notes}

    index = _Index([
        _Note(front["id"], front["relations"]),
        _Note(SLOT_ID, []),
    ])
    store = SynapseStore.from_index(index)
    edges = store.neighbors(front["id"])

    assert edges, (
        "the promoted note produced no edge; from_index skipped its relation"
    )
    assert edges[0].target_id == SLOT_ID
    assert edges[0].relation in ALLOWED_RELATIONS


def test_the_old_shape_would_have_produced_nothing(vault):
    """Proves the test can fail, by feeding it what the promoter used to emit."""
    class _Note:
        def __init__(self, note_id, relations):
            self.id = note_id
            self.text = "x" * 500
            self._relations = relations

        def relations(self):
            return self._relations

    class _Index:
        def __init__(self, notes):
            self.notes = notes
            self.by_id = {n.id: n for n in notes}

    note_id = str(uuid.uuid4())
    old_shape = [{
        "relation": "derived_from",
        "target": "01_ARCHITECTURE/ontology/slots/06_procedures.md",
    }]
    index = _Index([_Note(note_id, old_shape), _Note(SLOT_ID, [])])
    store = SynapseStore.from_index(index)

    assert store.neighbors(note_id) == [], (
        "the pre-fix shape is expected to produce no edge; if it does, this "
        "suite is not testing what it claims to"
    )
