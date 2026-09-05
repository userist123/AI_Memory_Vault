"""vault_hygiene.py — P0.3 neuron triage (owner: Antigravity front; kept here
unmodified in logic, Windows-console-safe, per claude-code's P1.2 audit).

Measured problem: most notes are REVIEW, unverified, auto-generated `lesson`
type, many being policy-blocked messages with no reusable knowledge. A brain
with 600 identical "action blocked" neurons does not think.

This tool NEVER deletes anything. It classifies and, with --apply, marks
`lifecycle: ARCHIVED` + `archived_reason`. Reversible via git.

    python 30_SCRIPTS/knowledge/vault_hygiene.py report
    python 30_SCRIPTS/knowledge/vault_hygiene.py report --json out.json
    python 30_SCRIPTS/knowledge/vault_hygiene.py apply --category boilerplate,duplicate
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Dict, List

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from cognitive_core.vault_index import VaultIndex, stats  # noqa: E402

# Empirically observed auto-generated noise patterns in the vault.
BOILERPLATE_PATTERNS = [
    r"^Action blocked by Autonomy Policy",
    r"^Reason: Action '.*' is HIGH RISK",
    r"Lesson: High-risk actions require explicit user approval",
    r"^\s*$",
]
BOILERPLATE_RE = [re.compile(p, re.M) for p in BOILERPLATE_PATTERNS]

MIN_USEFUL_CHARS = 120       # below this a note rarely carries reusable knowledge
MIN_UNIQUE_TOKENS = 15


def _ensure_utf8_stdout() -> None:
    """See 30_SCRIPTS/knowledge/edge_proposer.py::_ensure_utf8_stdout — same
    fix, same reason (Romanian-diacritic print() crashes on stock Windows
    console encoding before pytest.ini / this repo's shell ever sets
    PYTHONUTF8)."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def classify(index: VaultIndex) -> Dict[str, List[dict]]:
    buckets: Dict[str, List[dict]] = defaultdict(list)
    by_hash: Dict[str, List] = defaultdict(list)

    for note in index.notes:
        body = note.body.strip()
        reasons = []

        if any(p.search(body) for p in BOILERPLATE_RE) and len(body) < 400:
            reasons.append("boilerplate")
        if len(body) < MIN_USEFUL_CHARS:
            reasons.append("stub")
        if len(set(re.findall(r"[a-z]{3,}", body.lower()))) < MIN_UNIQUE_TOKENS:
            reasons.append("low_information")
        if note.lifecycle == "REVIEW" and note.verification == "unverified" \
                and not note.relations():
            reasons.append("orphan_review")

        by_hash[note.content_hash].append(note)

        record = {
            "id": note.id,
            "path": note.path.as_posix(),
            "title": note.title[:80],
            "type": note.type,
            "lifecycle": note.lifecycle,
            "chars": len(body),
            "reasons": reasons,
        }
        if reasons:
            buckets[reasons[0]].append(record)
        else:
            buckets["keep"].append(record)

    for h, notes in by_hash.items():
        if len(notes) > 1:
            for dup in notes[1:]:
                buckets["duplicate"].append({
                    "id": dup.id, "path": dup.path.as_posix(),
                    "title": dup.title[:80], "type": dup.type,
                    "lifecycle": dup.lifecycle, "chars": len(dup.body),
                    "reasons": ["duplicate"], "duplicate_of": notes[0].path.as_posix(),
                })
    return buckets


def apply_archive(vault: Path, records: List[dict], reason: str, dry: bool = False) -> int:
    changed = 0
    for rec in records:
        path = vault / rec["path"]
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8", errors="ignore")
        m = re.match(r"^---\n(.*?)\n---\n", text, re.S)
        if not m:
            continue
        fm = m.group(1)
        if re.search(r"^lifecycle:", fm, re.M):
            new_fm = re.sub(r"^lifecycle:.*$", "lifecycle: ARCHIVED", fm, count=1, flags=re.M)
        else:
            new_fm = fm + "\nlifecycle: ARCHIVED"
        if "archived_reason:" not in new_fm:
            new_fm += f"\narchived_reason: {reason}"
        if not dry:
            path.write_text(f"---\n{new_fm}\n---\n" + text[m.end():], encoding="utf-8")
        changed += 1
    return changed


def main() -> int:
    _ensure_utf8_stdout()
    ap = argparse.ArgumentParser()
    ap.add_argument("command", choices=["report", "apply"])
    ap.add_argument("--vault", default=".")
    ap.add_argument("--category", default="boilerplate,duplicate,low_information")
    ap.add_argument("--json", dest="json_out")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    vault = Path(args.vault)
    index = VaultIndex.load(vault, drop_navigation=False)
    buckets = classify(index)

    print("=== VAULT STATE ===")
    for k, v in stats(index).items():
        print(f"  {k}: {v}")
    print("\n=== TRIAGE ===")
    for name, recs in sorted(buckets.items(), key=lambda p: -len(p[1])):
        print(f"  {name:18s} {len(recs):5d}")
    keep = len(buckets.get("keep", []))
    print(f"\n  estimated useful neurons: {keep} / {len(index)} "
          f"({100*keep/max(len(index),1):.1f}%)")

    if args.json_out:
        Path(args.json_out).write_text(
            json.dumps({k: v for k, v in buckets.items()}, indent=2, ensure_ascii=False),
            encoding="utf-8")
        print(f"  report -> {args.json_out}")

    if args.command == "apply":
        total = 0
        for cat in args.category.split(","):
            cat = cat.strip()
            recs = buckets.get(cat, [])
            n = apply_archive(vault, recs, cat, dry=args.dry_run)
            print(f"  ARCHIVED[{cat}]: {n}")
            total += n
        print(f"  total marked: {total} ({'dry-run' if args.dry_run else 'written'})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
