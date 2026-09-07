"""r025 WP-10 (measurement only, no path_resolver.py change) -- quantify the
damage from r024 WP-4's finding: `path_resolver.resolve_path()` (used by
`MemoryController.propose()`) always writes new notes into one of the
LEGACY_WRITE_ROOTS (00_CORE, 01_KNOWLEDGE, 02_PROJECTS, 03_PROCEDURES,
04_MEMORY, 05_RESOURCES, 99_SYSTEM), while `VaultIndex.DEFAULT_ROOTS` (used
by `SynapseStore`, the graph layer, and every benchmark harness this session
has run) only scans the CONTENT_ROOTS (01_ARCHITECTURE, 02_PRODUCT,
10_DOCUMENTATION, 00_GOVERNANCE). A note `propose()`-created today is
readable via `FileStorageEngine`/`MemoryController.search()` but invisible
to `VaultIndex`/the graph.

"Since the storage fix" = since commit `da99af0f6` ("fix(storage):
production storage engine could not see the vault at all", 2026-09-06),
which is what actually made `FileStorageEngine` able to see the 850-note
corpus at all -- the natural reference point for "notes written after the
storage layer became usable in practice."

This script answers, for the CURRENT worktree:
  1. How many notes currently live in a legacy write root at all.
  2. Of those, how many were added to git AFTER the storage-fix commit
     (a proxy for "written via propose() since the fix," since propose()
     is the only write path and always targets a legacy root -- a note
     added there since then was either propose()'d or hand-placed by a
     script/human bypassing propose(), which this measurement cannot
     distinguish further from file content alone).
  3. How many of the CURRENT legacy-root notes are invisible to
     `VaultIndex` (not in `index.by_id`) right now.
  4. The smallest fix and its cost, stated as options with counts, not
     implemented (per this package's own restriction).

Run: python 07_EVALUATION/r025_wp10_write_path_damage/measure_write_path_damage.py
Output: 07_EVALUATION/r025_wp10_write_path_damage/write_path_damage_report.json
"""
from __future__ import annotations

import glob
import json
import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.storage.file_engine import FileStorageEngine, LEGACY_WRITE_ROOTS  # noqa: E402
from memory_controller.storage.serializer import deserialize  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

STORAGE_FIX_COMMIT = "da99af0f6"


def git(*args: str) -> str:
    return subprocess.run(
        ["git", *args], cwd=str(REPO), capture_output=True, text=True, check=False,
    ).stdout.strip()


def first_add_commit(rel_path: str) -> str | None:
    """The earliest commit that added this exact path (renames not followed
    across a path change -- if a note moved INTO a legacy root from
    elsewhere, this reports when it arrived at its CURRENT path, which is
    what matters for "written into the legacy tree since the fix")."""
    out = git("log", "--diff-filter=A", "--format=%H", "--follow", "--", rel_path)
    lines = [l for l in out.splitlines() if l.strip()]
    return lines[-1] if lines else None


def added_after_fix(commit: str | None) -> bool | None:
    if not commit:
        return None
    # is STORAGE_FIX_COMMIT an ancestor of `commit`? If yes, commit came after (or is) the fix.
    result = subprocess.run(
        ["git", "merge-base", "--is-ancestor", STORAGE_FIX_COMMIT, commit],
        cwd=str(REPO), capture_output=True, text=True, check=False,
    )
    if result.returncode == 0:
        return True
    if commit == STORAGE_FIX_COMMIT:
        return False
    # Not an ancestor either way could mean `commit` predates the fix, or
    # the two are on unrelated history (rare here). Disambiguate by date.
    fix_date = git("show", "-s", "--format=%ct", STORAGE_FIX_COMMIT)
    commit_date = git("show", "-s", "--format=%ct", commit)
    try:
        return int(commit_date) > int(fix_date)
    except ValueError:
        return None


def main() -> int:
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))

    legacy_notes = []
    for root in LEGACY_WRITE_ROOTS:
        root_path = REPO / root
        if not root_path.exists():
            continue
        for filepath in sorted(set(glob.glob(str(root_path / "*.md"))) |
                                set(glob.glob(str(root_path / "**" / "*.md"), recursive=True))):
            if "RAW_IMPORTS" in filepath or "Obsidian" in filepath:
                continue
            rel = os.path.relpath(filepath, REPO).replace("\\", "/")
            try:
                data = deserialize(Path(filepath).read_text(encoding="utf-8", errors="ignore"))
            except Exception as exc:
                legacy_notes.append({"path": rel, "id": None, "error": str(exc)[:200]})
                continue
            note_id = data.get("id")
            add_commit = first_add_commit(rel)
            legacy_notes.append({
                "path": rel,
                "id": note_id,
                "lifecycle": data.get("lifecycle"),
                "provenance_source_type": (data.get("provenance") or {}).get("source_type"),
                "first_add_commit": add_commit,
                "added_after_storage_fix": added_after_fix(add_commit),
                "invisible_to_vault_index": (note_id not in index.by_id) if note_id else None,
            })

    n_total = len(legacy_notes)
    n_with_id = sum(1 for n in legacy_notes if n.get("id"))
    n_after_fix = sum(1 for n in legacy_notes if n.get("added_after_storage_fix") is True)
    n_before_fix = sum(1 for n in legacy_notes if n.get("added_after_storage_fix") is False)
    n_unknown_timing = sum(1 for n in legacy_notes if n.get("added_after_storage_fix") is None)
    n_invisible = sum(1 for n in legacy_notes if n.get("invisible_to_vault_index") is True)
    n_visible = sum(1 for n in legacy_notes if n.get("invisible_to_vault_index") is False)

    by_root = {}
    for root in LEGACY_WRITE_ROOTS:
        by_root[root] = sum(1 for n in legacy_notes if n["path"].split("/", 1)[0] == root)

    report = {
        "storage_fix_commit": STORAGE_FIX_COMMIT,
        "content_roots_total_notes": len(index.notes),
        "legacy_write_roots_total_notes": n_total,
        "legacy_notes_with_parseable_id": n_with_id,
        "legacy_notes_added_after_storage_fix": n_after_fix,
        "legacy_notes_added_before_storage_fix": n_before_fix,
        "legacy_notes_add_timing_unknown": n_unknown_timing,
        "legacy_notes_invisible_to_vault_index_now": n_invisible,
        "legacy_notes_visible_to_vault_index_now": n_visible,
        "by_legacy_root": by_root,
        "all_legacy_notes": legacy_notes,
    }
    (HERE / "write_path_damage_report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in report.items() if k != "all_legacy_notes"}, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
