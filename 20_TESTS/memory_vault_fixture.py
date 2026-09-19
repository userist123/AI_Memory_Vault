"""A small temporary vault for the memory-access tests.

It has the layout of the real vault where it matters: the content tree
(01_ARCHITECTURE/knowledge, 01_ARCHITECTURE/memory, ...), a copy of the real ontology slots,
and a few ACTIVE notes to search. Nothing here touches the repository's own notes.
"""
from __future__ import annotations

import shutil
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]

SEED_NOTES = (
    ("consolidarea nocturna",
     "Consolidarea nocturna ruleaza memory_v6_cli consolidate si instaleaza dependentele proiectului inainte de a porni codul."),
    ("poarta pe ontologie",
     "Scrierea in sloturile ontologiei cere un manifest de verdicte; fara manifest scrierea canonica este refuzata."),
    ("bugetul grafului",
     "Bugetul de expansiune al grafului are valoarea implicita None si este zero cand cautarea lexicala gaseste douazeci de note."),
)


def note_text(title: str, body: str, note_id: str, lifecycle: str = "ACTIVE") -> str:
    return (
        "---\n"
        f"id: {note_id}\n"
        "type: knowledge\n"
        f"category: {title.replace(' ', '-')}\n"
        "tags: [seed]\n"
        "created: 2026-09-01\n"
        "updated: 2026-09-01\n"
        "provenance:\n  source_type: user\n  source_ref: fixture\n"
        "confidence: high\n"
        "verification: unverified\n"
        "relations: []\n"
        f"lifecycle: {lifecycle}\n"
        "---\n"
        f"# {title}\n\n{body}\n"
    )


def make_vault(root: Path) -> Path:
    """Create the vault under `root` and return its path."""
    vault = root / "vault"
    knowledge = vault / "01_ARCHITECTURE" / "knowledge"
    knowledge.mkdir(parents=True)
    (vault / "01_ARCHITECTURE" / "memory").mkdir(parents=True)
    (vault / "10_DOCUMENTATION" / "procedures").mkdir(parents=True)
    shutil.copytree(REPO / "01_ARCHITECTURE" / "ontology" / "slots", vault / "01_ARCHITECTURE" / "ontology" / "slots")
    for title, body in SEED_NOTES:
        note_id = str(uuid.uuid4())
        (knowledge / f"{title.replace(' ', '_')}.md").write_text(note_text(title, body, note_id), encoding="utf-8")
    return vault
