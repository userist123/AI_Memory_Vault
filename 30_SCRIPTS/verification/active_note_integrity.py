"""Record what the vault's 57 ACTIVE notes said when they were attested, and notice when that changes.

ACTIVE is the only state the vault stands behind. Everything else is a
proposal, a draft or history. Yet an ACTIVE note is an ordinary Markdown file:
an editor, a sync client, a script or an agent can rewrite it, and nothing
today would say so. The note keeps its `verification: verified` and its
provenance while its content drifts away from whatever was attested.

This records a SHA-256 per ACTIVE note and reports three kinds of change:
content drift, a note that left ACTIVE, and a note that entered it. Entering is
not a fault — it is how the vault grows — but it must be seen and accepted
rather than absorbed silently.

    python 30_SCRIPTS/verification/active_note_integrity.py            # verify
    python 30_SCRIPTS/verification/active_note_integrity.py --record   # accept current state
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MANIFEST = REPO / "07_EVALUATION" / "integrity" / "active_notes.sha256.json"

sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "x" * 40)


def active_note_digests() -> dict[str, str]:
    """note id -> SHA-256 of its file, for every note the index reports as ACTIVE."""
    from retrieval.vault_index import VaultIndex  # imported late: loading the index is slow

    cwd = os.getcwd()
    os.chdir(REPO)
    try:
        index = VaultIndex.load(Path("."), include_raw=True, include_archived=True)
    finally:
        os.chdir(cwd)

    digests: dict[str, str] = {}
    for note in index.notes:
        if str(getattr(note, "lifecycle", None)) != "ACTIVE":
            continue
        path = REPO / str(getattr(note, "path", ""))
        if not path.is_file():
            continue
        # Line endings differ between a Windows checkout and CI, and say nothing
        # about the content, so they are normalised before hashing.
        text = path.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")
        digests[note.id] = hashlib.sha256(text.encode("utf-8")).hexdigest()
    return digests


def compare(recorded: dict[str, str], current: dict[str, str]) -> dict[str, list[str]]:
    return {
        "drifted": sorted(i for i in recorded.keys() & current.keys() if recorded[i] != current[i]),
        "left_active": sorted(recorded.keys() - current.keys()),
        "entered_active": sorted(current.keys() - recorded.keys()),
    }


def load_manifest() -> dict:
    if not MANIFEST.exists():
        return {"schema": "active-note-integrity.v1", "notes": {}}
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def write_manifest(digests: dict[str, str]) -> None:
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "schema": "active-note-integrity.v1",
        "note": ("SHA-256 per ACTIVE note, line endings normalised. Drift means the file "
                 "changed after it was recorded; it is a question, not a verdict."),
        "count": len(digests),
        "notes": dict(sorted(digests.items())),
    }
    MANIFEST.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8", newline="\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--record", action="store_true",
                        help="accept the current state as the baseline")
    args = parser.parse_args()

    current = active_note_digests()
    if args.record:
        write_manifest(current)
        print(f"ACTIVE_NOTES={len(current)}")
        print(f"recorded to {MANIFEST.relative_to(REPO)}")
        return 0

    recorded = load_manifest().get("notes", {})
    if not recorded:
        print("no manifest yet; run with --record")
        return 1

    changes = compare(recorded, current)
    print(f"ACTIVE_NOTES={len(current)} RECORDED={len(recorded)}")
    print(f"DRIFTED={len(changes['drifted'])}")
    print(f"LEFT_ACTIVE={len(changes['left_active'])}")
    print(f"ENTERED_ACTIVE={len(changes['entered_active'])}")
    for kind, ids in changes.items():
        for note_id in ids[:20]:
            print(f"  {kind}: {note_id}")
    # Entering ACTIVE is growth, not damage: it fails so a person accepts it
    # with --record, but it is reported apart from drift so the two are never
    # confused in a CI log.
    return 1 if any(changes.values()) else 0


if __name__ == "__main__":
    sys.exit(main())
