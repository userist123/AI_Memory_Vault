#!/usr/bin/env python3
"""validate_corpus_index.py — Canonical Corpus Index Integrity Validator.

P1.0 Corpus Integrity Gate (owner: ANTIGRAVITY).

Validates canonical corpus index invariants:
  1. Duplicate note IDs
  2. Duplicate content hashes (normalized content body collisions)
  3. Invalid UUIDs (notes with malformed UUID identifiers)
  4. Missing or empty frontmatter
  5. Broken targets in relations (targets not resolvable in the index)

Outputs structured JSON reports for CI/forensics evidence.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional

REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from cognitive_core.vault_index import Note, VaultIndex, UUID_RE


def _ensure_utf8_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def validate_corpus_index(
    vault_root: Path | str,
    include_raw: bool = False,
    include_archived: bool = False,
) -> Dict[str, Any]:
    """Validate the integrity of the canonical vault index.

    Returns a structured dictionary with issues detected.
    """
    root = Path(vault_root).resolve()
    index = VaultIndex.load(
        root,
        include_raw=include_raw,
        include_archived=include_archived,
        drop_navigation=False,
    )

    # 1. Duplicate IDs
    id_map: Dict[str, List[str]] = defaultdict(list)
    for note in index.notes:
        id_map[note.id].append(str(note.path))

    duplicate_ids = [
        {"id": nid, "count": len(paths), "paths": paths}
        for nid, paths in id_map.items()
        if len(paths) > 1
    ]

    # 2. Duplicate Content Hashes
    hash_map: Dict[str, List[Dict[str, str]]] = defaultdict(list)
    for note in index.notes:
        hash_map[note.content_hash].append({"id": note.id, "path": str(note.path)})

    duplicate_hashes = [
        {"content_hash": h, "count": len(items), "notes": items}
        for h, items in hash_map.items()
        if len(items) > 1
    ]

    # 3. Invalid UUIDs
    invalid_uuids = []
    for note in index.notes:
        raw_id = str(note.meta.get("id") or "").strip()
        if raw_id:
            if not UUID_RE.fullmatch(raw_id):
                invalid_uuids.append({
                    "id": raw_id,
                    "path": str(note.path),
                    "reason": "Does not conform to canonical UUIDv4 format"
                })
        else:
            # If frontmatter exists but has no id, record as missing id
            if note.meta:
                invalid_uuids.append({
                    "id": note.id,
                    "path": str(note.path),
                    "reason": "Missing canonical 'id' attribute in frontmatter"
                })

    # 4. Missing Frontmatter
    missing_frontmatter = []
    for note in index.notes:
        if not note.meta:
            missing_frontmatter.append({
                "path": str(note.path),
                "title": note.title
            })

    # 5. Broken Relations
    broken_relations = []
    for note in index.notes:
        for tid in note.outgoing_ids():
            if tid not in index.by_id:
                broken_relations.append({
                    "source_id": note.id,
                    "source_path": str(note.path),
                    "target_id": tid
                })

    total_issues = (
        len(duplicate_ids)
        + len(duplicate_hashes)
        + len(invalid_uuids)
        + len(missing_frontmatter)
        + len(broken_relations)
    )

    is_clean = (total_issues == 0)

    report = {
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "vault_root": str(root),
        "total_notes": len(index),
        "include_raw": include_raw,
        "include_archived": include_archived,
        "issues": {
            "duplicate_ids": duplicate_ids,
            "duplicate_content_hashes": duplicate_hashes,
            "invalid_uuids": invalid_uuids,
            "missing_frontmatter": missing_frontmatter,
            "broken_relations": broken_relations,
        },
        "issue_counts": {
            "duplicate_ids": len(duplicate_ids),
            "duplicate_content_hashes": len(duplicate_hashes),
            "invalid_uuids": len(invalid_uuids),
            "missing_frontmatter": len(missing_frontmatter),
            "broken_relations": len(broken_relations),
            "total": total_issues,
        },
        "is_clean": is_clean,
    }
    return report


def main(argv: Optional[List[str]] = None) -> int:
    _ensure_utf8_stdout()
    parser = argparse.ArgumentParser(description="Validate Canonical Corpus Index Integrity")
    parser.add_argument("--vault-root", default=".", help="Root directory of the vault")
    parser.add_argument("--output", help="Optional path to output structured JSON report")
    parser.add_argument("--include-raw", action="store_true", help="Include RAW notes in validation")
    parser.add_argument("--include-archived", action="store_true", help="Include ARCHIVED notes in validation")
    parser.add_argument("--exit-zero", action="store_true", help="Exit 0 even if issues are found")
    args = parser.parse_args(argv)

    report = validate_corpus_index(
        vault_root=args.vault_root,
        include_raw=args.include_raw,
        include_archived=args.include_archived,
    )

    json_str = json.dumps(report, indent=2, ensure_ascii=False)
    if args.output:
        out_path = Path(args.output)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(json_str, encoding="utf-8")
        print(f"Report written to: {out_path}")
    else:
        print(json_str)

    if report["is_clean"]:
        print("\n[OK] Canonical corpus index is clean. Zero integrity issues found.")
        return 0
    else:
        print(f"\n[WARN] Found {report['issue_counts']['total']} corpus integrity issues.")
        return 0 if args.exit_zero else 1


if __name__ == "__main__":
    sys.exit(main())
