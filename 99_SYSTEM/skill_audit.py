#!/usr/bin/env python3
"""Static audit for oversized/duplicated agent skills.

Run from repository root:
    python 99_SYSTEM/skill_audit.py

The audit is intentionally read-only. It reports candidates for normalization
without changing skill files, so capability is never lost automatically.
"""
from __future__ import annotations

import hashlib
import re
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parents[1]
SKILL_ROOT = ROOT / ".agents" / "skills"
GLOBAL_MARKERS = (
    "Council",
    "Knowledge Graph",
    "Master_Skills_Catalog",
    "Memory",
    "Obsidian",
    "BRIEFING",
    "DISPATCH",
    "handoff",
    "progress",
    "full Vault",
)


def normalized(text: str) -> str:
    text = re.sub(r"```.*?```", "", text, flags=re.S)
    text = re.sub(r"https?://\S+", "URL", text)
    text = re.sub(r"\s+", " ", text).strip().lower()
    return text


def main() -> int:
    files = sorted(SKILL_ROOT.rglob("SKILL.md")) if SKILL_ROOT.exists() else []
    hashes = defaultdict(list)
    marker_hits = []
    oversized = []

    for path in files:
        raw = path.read_text(encoding="utf-8", errors="replace")
        digest = hashlib.sha256(normalized(raw).encode("utf-8")).hexdigest()
        hashes[digest].append(path)
        lines = raw.count("\n") + (1 if raw else 0)
        if len(raw.encode("utf-8")) > 16 * 1024 or lines > 300:
            oversized.append((path, len(raw.encode("utf-8")), lines))
        hits = [m for m in GLOBAL_MARKERS if m.lower() in raw.lower()]
        if hits:
            marker_hits.append((path, hits))

    duplicates = [group for group in hashes.values() if len(group) > 1]

    print(f"SKILLS={len(files)}")
    print(f"DUPLICATE_GROUPS={len(duplicates)}")
    print(f"GLOBAL_MARKER_SKILLS={len(marker_hits)}")
    print(f"OVERSIZED_SKILLS={len(oversized)}")

    if duplicates:
        print("\n[DUPLICATES]")
        for group in duplicates:
            for p in group:
                print(f"  {p.relative_to(ROOT)}")
            print()

    if oversized:
        print("\n[OVERSIZED]")
        for p, size, lines in sorted(oversized, key=lambda x: x[1], reverse=True):
            print(f"  {p.relative_to(ROOT)} bytes={size} lines={lines}")

    if marker_hits:
        print("\n[GLOBAL_MARKERS]")
        for p, hits in marker_hits:
            print(f"  {p.relative_to(ROOT)}: {', '.join(hits)}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
