"""WP-4 (r024) — gather the numbers for the write-path decision document.

This script produces NUMBERS ONLY, per the brief's explicit deliverable
("the deliverable is numbers, not a diff"). It performs no migration and
changes nothing.
"""
from __future__ import annotations

import json
import os
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from retrieval.vault_index import VaultIndex  # noqa: E402

CONTENT_ROOTS = ("01_ARCHITECTURE", "02_PRODUCT", "10_DOCUMENTATION", "00_GOVERNANCE")
LEGACY_ROOTS = ("00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES", "04_MEMORY", "05_RESOURCES", "99_SYSTEM")


def top_root(path) -> str:
    rel = os.path.relpath(str(path), str(REPO))
    return rel.replace("\\", "/").split("/")[0]


def main() -> int:
    idx = VaultIndex.load(REPO, roots=CONTENT_ROOTS + LEGACY_ROOTS, include_raw=True, include_archived=True)
    by_root = Counter(top_root(n.path) for n in idx.notes)
    content_count = sum(by_root.get(r, 0) for r in CONTENT_ROOTS)
    legacy_count = sum(by_root.get(r, 0) for r in LEGACY_ROOTS)

    by_id = idx.by_id
    declared_cross = 0
    declared_total = 0
    wikilink_cross = 0
    wikilink_total = 0
    for n in idx.notes:
        n_in_content = top_root(n.path) in CONTENT_ROOTS
        for tid in n.outgoing_ids():
            t = by_id.get(tid)
            if t:
                declared_total += 1
                t_in_content = top_root(t.path) in CONTENT_ROOTS
                if n_in_content != t_in_content:
                    declared_cross += 1
        for raw in n.wikilinks():
            t = idx.resolve(raw)
            if t:
                wikilink_total += 1
                t_in_content = top_root(t.path) in CONTENT_ROOTS
                if n_in_content != t_in_content:
                    wikilink_cross += 1

    # File-name identity note: content-root notes keep their exact file name
    # as their link identity (Obsidian + VaultIndex.by_slug resolve wikilinks
    # by file name -- see file_engine.py's _target_path_for() docstring).
    # Legacy-root notes are named `{category}_{id[:8]}.md` by resolve_path().
    # A migration in EITHER direction that renames files breaks wikilinks
    # unless the new location preserves the old file name.

    report = {
        "content_roots": list(CONTENT_ROOTS),
        "legacy_write_roots": list(LEGACY_ROOTS),
        "notes_by_root": dict(by_root),
        "notes_in_content_roots": content_count,
        "notes_in_legacy_roots": legacy_count,
        "total_notes": len(idx.notes),
        "declared_relations_total": declared_total,
        "declared_relations_crossing_content_legacy_boundary": declared_cross,
        "wikilinks_total": wikilink_total,
        "wikilinks_crossing_content_legacy_boundary": wikilink_cross,
    }
    out = HERE / "writepath_numbers.json"
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(report, indent=2))
    print(f"\n-> {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
