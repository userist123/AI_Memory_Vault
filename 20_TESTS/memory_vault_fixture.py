"""A small temporary vault for the memory-access tests.

It has the layout of the real vault where it matters: the content tree
(01_ARCHITECTURE/knowledge, 01_ARCHITECTURE/memory, ...), a copy of the real ontology slots,
and a few ACTIVE notes to search. Nothing here touches the repository's own notes.

The seed notes are NOT verified, on purpose. In the real vault most notes are not verified
(23 ACTIVE without a verified stamp and 271 REVIEW, against 40 ACTIVE and verified), so a
fixture of verified notes only hides exactly the read-path regressions that matter: it did,
once (non-empty snippets fell from 38/48 to 2/48 while every test stayed green).
`seed_real_distribution()` adds the remaining states for the tests that assert the read
contract (CLAUDE.md: memory_get serves ACTIVE and REVIEW, REVIEW marked unverified).
"""
from __future__ import annotations

import shutil
import uuid
from pathlib import Path
from typing import Dict, Optional, Tuple

REPO = Path(__file__).resolve().parents[1]

SEED_NOTES = (
    ("consolidarea nocturna",
     "Consolidarea nocturna ruleaza memory_v6_cli consolidate si instaleaza dependentele proiectului inainte de a porni codul."),
    ("poarta pe ontologie",
     "Scrierea in sloturile ontologiei cere un manifest de verdicte; fara manifest scrierea canonica este refuzata."),
    ("bugetul grafului",
     "Bugetul de expansiune al grafului are valoarea implicita None si este zero cand cautarea lexicala gaseste douazeci de note."),
)


def note_text(title: str, body: str, note_id: str, lifecycle: str = "ACTIVE",
              verification: Optional[str] = "unverified", extra: str = "") -> str:
    """Frontmatter + body. `verification=None` omits the key (a legacy, never-stamped note)."""
    verification_line = "" if verification is None else f"verification: {verification}\n"
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
        f"{verification_line}"
        f"{extra}"
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


#: kind -> (lifecycle, verification stamp (None = no verification key at all), extra frontmatter)
DISTRIBUTION: Dict[str, Tuple[str, Optional[str], str]] = {
    "active_verified": ("ACTIVE", "verified", ""),
    "active_partially_verified": ("ACTIVE", "partially_verified", ""),
    "active_unstamped": ("ACTIVE", None, ""),
    "review_unverified": ("REVIEW", "unverified", ""),
    "review_unstamped": ("REVIEW", None, ""),
    "review_quarantined": ("REVIEW", "unverified", "quarantined: true\n"),
    "active_quarantined": ("ACTIVE", "verified", "quarantined: true\n"),
    "archived": ("ARCHIVED", "verified", ""),
}


def seed_real_distribution(vault: Path) -> Dict[str, Tuple[str, str]]:
    """Write one note per state of the real vault; return {kind: (note_id, body marker)}.

    Every body carries a distinct marker word (`<kind>marker`) so a test can search for it and
    assert on exactly that note's snippet and content. The controller's storage must be
    re-indexed (or created) after this call.
    """
    knowledge = vault / "01_ARCHITECTURE" / "knowledge"
    seeded: Dict[str, Tuple[str, str]] = {}
    for kind, (lifecycle, verification, extra) in DISTRIBUTION.items():
        note_id = str(uuid.uuid4())
        word = f"{kind.replace('_', '')}marker"
        text = note_text(
            f"estado {kind.replace('_', ' ')}",
            f"Corpul notei contine cuvantul {word} si nimic altceva.",
            note_id, lifecycle=lifecycle, verification=verification, extra=extra,
        )
        (knowledge / f"{kind}.md").write_text(text, encoding="utf-8")
        seeded[kind] = (note_id, word)
    return seeded
