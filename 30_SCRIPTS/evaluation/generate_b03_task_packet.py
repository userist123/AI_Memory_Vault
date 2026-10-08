"""B03: generate the pre-registered task packet (no model is run, no answer is written).

Selection rule (PREREGISTRATION_B03_B05.md, section 3): every `Promoted_*` note added by commit ce724a575
(the 51 book-derived candidate notes of the B2M notes batch, PR #221) that is REVIEW / unverified and has a
`# term` heading, a `## Canonical Definition` paragraph and a block-quoted source passage. No sampling.

Usage:
    python 30_SCRIPTS/evaluation/generate_b03_task_packet.py                # write the packet next to the track
    python 30_SCRIPTS/evaluation/generate_b03_task_packet.py --check       # exit 1 if the committed packet differs
    python 30_SCRIPTS/evaluation/generate_b03_task_packet.py --notes-list F --out DIR   # explicit note files (tests)
"""
from __future__ import annotations

import argparse
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import List

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from lifecycle.validation.book_to_memory_real_ablation import (  # noqa: E402
    SEED,
    build_task_list,
    parse_promoted_note,
    write_task_packet,
)

SELECTION_COMMIT = "ce724a575"
NOTES_DIR = "01_ARCHITECTURE/knowledge"
PREREG = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "PREREGISTRATION_B03_B05.md"
DEFAULT_OUT = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "b03_task_packet"


def files_from_commit(commit: str) -> List[str]:
    out = subprocess.check_output(["git", "show", "--name-only", "--format=", commit], cwd=REPO, text=True)
    return sorted(p for p in out.splitlines()
                  if p.startswith(NOTES_DIR + "/Promoted_") and p.endswith(".md"))


def generate(files: List[str], out_dir: Path, selection_extra: dict | None = None) -> dict:
    notes, dropped = [], []
    for rel in files:
        note, reason = parse_promoted_note((REPO / rel).read_text(encoding="utf-8"), rel)
        if note is None:
            dropped.append({"path": rel, "reason": reason})
        else:
            notes.append(note)
    tasks = build_task_list(notes)
    selection = {"rule": "every Promoted_* note added by the commit that is REVIEW/unverified with a heading, "
                         "a Canonical Definition and a block-quoted source passage; no sampling",
                 "commit": SELECTION_COMMIT, "candidate_files": len(files), "selected": len(tasks)}
    selection.update(selection_extra or {})
    return write_task_packet(tasks, out_dir, prereg_path=PREREG.relative_to(REPO), selection=selection,
                             seed=SEED, dropped=dropped)


def main(argv: List[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    ap.add_argument("--notes-list", help="file with one note path (relative to the repo) per line instead of the commit")
    ap.add_argument("--check", action="store_true",
                    help="regenerate from the note files named in the committed packet and fail if it differs (needs no commit history)")
    args = ap.parse_args(argv)

    if args.check:
        import json
        committed = json.loads((Path(args.out) / "scoring" / "tasks.json").read_text(encoding="utf-8"))
        files = sorted([t["note_path"] for t in committed["tasks"]] + [d["path"] for d in committed["dropped"]])
    elif args.notes_list:
        files = [l.strip() for l in Path(args.notes_list).read_text(encoding="utf-8").splitlines() if l.strip()]
    else:
        files = files_from_commit(SELECTION_COMMIT)
    if not files:
        print("no candidate note files found", file=sys.stderr)
        return 1
    if args.check:
        with tempfile.TemporaryDirectory() as tmp:
            generate(files, Path(tmp))
            for name in ("trials.json", "scoring/tasks.json", "manifest.json", "run_config.template.json", "README.md"):
                if (Path(tmp) / name).read_bytes() != (Path(args.out) / name).read_bytes():
                    print(f"DIFFERS: {name}")
                    return 1
        print("packet is up to date")
        return 0
    manifest = generate(files, Path(args.out))
    print(f"packet written to {args.out}: {manifest['tasks']} tasks, {manifest['trials']} trials")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
