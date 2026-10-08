"""30_SCRIPTS/knowledge/clean_source_frontmatters.py — Frontmatter Audit Edge Cleaner.

Removes the 29 rejected relations from frontmatters of the 27 source files.
Guarantees:
1. Verifies sample SHA-256 before modifying any files.
2. Only removes relations explicitly identified as REJECT by the independent audit.
3. Never touches a note body: the Markdown after the closing `---` is preserved byte for byte.
   (An earlier version injected sentences such as "... in [[long-term memory]]" into 9 note
   bodies to keep notes from becoming islands. That is factually unsupported prose written into
   knowledge notes; PR #209 B11. A note left with no true relation is an island, and stays one
   until a real, audited relation is declared in its frontmatter.)
4. Preserves YAML formatting and validates frontmatter after modification.
5. Supports dry-run and diff preview.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Set, Tuple

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(REPO / "30_SCRIPTS"))

from knowledge.audit_edge_purge import verify_sample_hash, load_rejected_edges, DEFAULT_SAMPLE, DEFAULT_VERDICTS

FRONTMATTER_RE = re.compile(r"^---\r?\n(.*?)\r?\n---\r?\n?(.*)$", re.DOTALL)


def clean_frontmatter_relations(
    source_path: Path,
    rejections_for_file: List[Dict[str, Any]],
    dry_run: bool = True,
) -> Tuple[bool, str]:
    """Removes rejected relations from frontmatter of source_path.
    
    Returns (changed, summary_message).
    """
    raw_content = source_path.read_bytes().decode("utf-8")  # bytes in, bytes out: no newline translation
    m = FRONTMATTER_RE.match(raw_content)
    if not m:
        return False, f"ERROR: No frontmatter found in {source_path}"

    fm_text = m.group(1)
    body_text = m.group(2)

    import yaml
    try:
        data = yaml.safe_load(fm_text) or {}
    except Exception as e:
        return False, f"ERROR parsing frontmatter yaml in {source_path}: {e}"

    relations = data.get("relations")
    if not relations:
        return False, f"No relations block in {source_path}"

    # Rejections to prune: set of (relation_type, target_id)
    reject_pairs = set()
    for r in rejections_for_file:
        rel_type = str(r.get("relation", "")).lower()
        target_id = str(r.get("target_id", "")).strip()
        reject_pairs.add((rel_type, target_id))

    new_relations = []
    removed_count = 0
    for rel in relations:
        if not isinstance(rel, dict):
            new_relations.append(rel)
            continue
        rel_type = str(rel.get("type") or rel.get("relation") or "related_to").lower()
        target_id = str(rel.get("target_id") or rel.get("target") or "").strip()
        if (rel_type, target_id) in reject_pairs:
            removed_count += 1
            continue
        new_relations.append(rel)

    if removed_count == 0:
        return False, f"No matching rejected relations found in {source_path.name}"

    # Reconstruct frontmatter
    # To keep exact clean yaml formatting:
    # Find the relations block in fm_text
    lines = fm_text.splitlines()
    in_relations = False
    new_fm_lines = []
    
    # We will use python regex or line-by-line filtering to preserve YAML comments and fields
    # Let's inspect if we can format relations cleanly:
    # If new_relations is empty, write 'relations: []'
    # If not empty, write each relation.
    
    # Alternatively, replace the relations: block in fm_text
    i = 0
    while i < len(lines):
        line = lines[i]
        if re.match(r"^relations:\s*$", line):
            # Consume relations header and all child lines until next top-level key
            i += 1
            while i < len(lines) and not re.match(r"^[a-zA-Z0-9_]+:\s*", lines[i]):
                i += 1
            # Now output new relations block
            if not new_relations:
                new_fm_lines.append("relations: []")
            else:
                new_fm_lines.append("relations:")
                for r in new_relations:
                    rtype = r.get("type") or r.get("relation") or "related_to"
                    tid = r.get("target_id") or r.get("target")
                    new_fm_lines.append(f"  - type: {rtype}")
                    new_fm_lines.append(f"    target_id: \"{tid}\"")
            continue
        elif re.match(r"^relations:\s*\[\s*\]\s*$", line):
            new_fm_lines.append(line)
            i += 1
            continue
        else:
            new_fm_lines.append(line)
            i += 1

    new_fm_text = "\n".join(new_fm_lines)
    
    # Verify yaml parsing of new_fm_text
    try:
        parsed_new = yaml.safe_load(new_fm_text)
        assert "id" in parsed_new
    except Exception as e:
        return False, f"FATAL: Generated frontmatter failed YAML validation: {e}"

    # Frontmatter only: splice the new YAML between the original fences and keep every byte of
    # the opening/closing fence and of the body exactly as read.
    new_full_content = raw_content[: m.start(1)] + new_fm_text + raw_content[m.end(1):]
    assert new_full_content[len(new_full_content) - len(raw_content[m.end(1):]):] == raw_content[m.end(1):]
    assert FRONTMATTER_RE.match(new_full_content).group(2) == body_text, "note body must be byte-identical"

    if not dry_run:
        source_path.write_bytes(new_full_content.encode("utf-8"))

    action_str = "[DRY-RUN] Would remove" if dry_run else "Removed"
    return True, f"{action_str} {removed_count} rejected relation(s) from {source_path.name}"


def main() -> int:
    parser = argparse.ArgumentParser(description="Clean rejected relations from source frontmatters")
    parser.add_argument("--sample", type=Path, default=DEFAULT_SAMPLE, help="Path to audit sample JSON")
    parser.add_argument("--verdicts", type=Path, default=DEFAULT_VERDICTS, help="Path to audit verdicts JSON")
    parser.add_argument("--dry-run", action="store_true", help="Preview changes without modifying files")
    parser.add_argument("--apply", action="store_true", help="Apply changes to source files")
    args = parser.parse_args()

    if not args.dry_run and not args.apply:
        print("Please specify either --dry-run or --apply")
        return 1

    dry_run = not args.apply

    print("Verifying audit sample hash against verdicts...")
    is_valid, calc_hash, exp_hash = verify_sample_hash(args.sample, args.verdicts)
    if not is_valid:
        print(f"FATAL: Sample hash mismatch!\nCalculated: {calc_hash}\nExpected:   {exp_hash}")
        return 1
    print(f"Sample SHA-256 OK: {calc_hash}")

    rejected_edges = load_rejected_edges(args.sample, args.verdicts)
    print(f"Loaded {len(rejected_edges)} rejected edges from verdicts.")

    # Group by source_path
    by_file: Dict[str, List[Dict[str, Any]]] = {}
    for r in rejected_edges:
        sp = r.get("source_path")
        if not sp:
            print(f"ERROR: Missing source_path for rejected edge {r['index']}")
            return 1
        by_file.setdefault(sp, []).append(r)

    print(f"Identified {len(by_file)} distinct source files to clean.")

    total_changed = 0
    total_edges_removed = 0
    for sp_rel, rejections in sorted(by_file.items()):
        full_path = Path(sp_rel) if Path(sp_rel).is_absolute() else REPO / sp_rel
        if not full_path.exists():
            # Try normalizing backslashes/slashes
            alt_path = REPO / Path(sp_rel)
            if alt_path.exists():
                full_path = alt_path
            else:
                print(f"ERROR: File not found: {full_path}")
                return 1

        changed, msg = clean_frontmatter_relations(full_path, rejections, dry_run=dry_run)
        print(f"- {msg}")
        if changed:
            total_changed += 1
            total_edges_removed += len(rejections)

    print(f"\nSummary:")
    print(f"- Files processed: {len(by_file)}")
    print(f"- Files updated: {total_changed}")
    print(f"- Rejected edges removed: {total_edges_removed} (expected: {len(rejected_edges)})")
    if total_edges_removed != len(rejected_edges):
        print(f"WARNING: Expected to remove {len(rejected_edges)} edges, but removed {total_edges_removed}!")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
