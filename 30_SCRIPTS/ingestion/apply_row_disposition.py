#!/usr/bin/env python3
"""
apply_row_disposition.py — Apply row dispositions from manifest to ontology slots.

Governed by disposition_manifest.json (213 rows):
- KEEP_PROMOTED: 84 (43 already promoted + 41 proposed kept)
- DELETE: 112 (removed from slot tables)
- MERGE_INTO: 8 (occurrences and evidence merged into target promoted rows, source row removed)
- UNDECIDED: 6 (left exactly as-is on disk)
- KEEP_PROPOSED: 2 (left exactly as-is on disk)
- SPLIT: 1 (left exactly as-is on disk)

Conditions (Mandatory):
1. --dry-run is default. Writing requires explicit --apply.
2. Total coverage verified at entry: exactly 213 rows must match disk.
3. 43 promoted rows protected: status, concept, promoted_note_id preserved;
   35 non-merge target rows byte-for-byte identical;
   8 merge target rows receive merged occurrences & evidence.
4. 7 pending rows untouched (6 UNDECIDED + 1 SPLIT familiarity).
5. Merge sums occurrences: target_occ = old_target_occ + source_occ.
"""
from __future__ import annotations

import argparse
import difflib
import json
import os
import pathlib
import re
import sys
from typing import Dict, List, Optional, Set, Tuple

# Support relative import or path import
CURRENT_DIR = pathlib.Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

import slot_rows
from disposition_manifest import (
    DISPOSITION_KEEP_PROMOTED,
    DISPOSITION_KEEP_PROPOSED,
    DISPOSITION_DELETE,
    DISPOSITION_MERGE_INTO,
    DISPOSITION_SPLIT,
    DISPOSITION_UNDECIDED,
    DispositionManifest,
    RowDisposition,
    load_manifest,
)

DEFAULT_MANIFEST = pathlib.Path("07_EVALUATION/book_corpus_conversion/disposition_manifest.json")
DEFAULT_SLOTS_DIR = pathlib.Path("01_ARCHITECTURE/ontology/slots")

SEPARATOR_PATTERN = re.compile(r"^\|[\s\-:|]+\|$")


def _clean_concept(name: str) -> str:
    return slot_rows._clean(name).lower()


def combine_evidence(target_ev: str, source_ev: str) -> str:
    """Combines evidence quotes cleanly without breaking markdown table format."""
    t = target_ev.strip()
    s = source_ev.strip()
    if not s:
        return t
    if not t:
        return s
    if s in t:
        return t
    # Sanitize pipes and newlines
    s_clean = " ".join(s.replace("|", "/").split())
    t_clean = " ".join(t.replace("|", "/").split())
    combined = f"{t_clean} / {s_clean}"
    return combined[:500]


def combine_sources(target_src: str, source_src: str) -> str:
    """Combines semicolon-separated source books without duplicates."""
    t_books = [b.strip() for b in target_src.split(";") if b.strip()]
    s_books = [b.strip() for b in source_src.split(";") if b.strip()]
    for b in s_books:
        if b not in t_books:
            t_books.append(b)
    return "; ".join(t_books)


def format_table_row(
    concept: str,
    source_book: str,
    confidence: str,
    status: str,
    date_added: str,
    promoted_note_id: str,
    evidence: str,
    occurrences: Optional[int],
) -> str:
    occ_str = str(occurrences) if occurrences is not None else ""
    return (
        f"| {concept} | {source_book} | {confidence} | {status} | "
        f"{date_added} | {promoted_note_id} | {evidence} | {occ_str} |"
    )


def apply_dispositions(
    manifest_path: pathlib.Path,
    slots_dir: pathlib.Path,
    dry_run: bool = True,
) -> Tuple[bool, str, Dict[str, any]]:
    """
    Validates coverage and executes row dispositions across slot files.
    Returns (success, log_or_diff_output, summary_stats).
    """
    if not manifest_path.exists():
        raise FileNotFoundError(f"Manifest not found: {manifest_path}")
    if not slots_dir.exists():
        raise FileNotFoundError(f"Slots directory not found: {slots_dir}")

    manifest = load_manifest(manifest_path)

    # 1. Total coverage check against disk
    disk_rows = slot_rows.read_all(slots_dir)
    if len(disk_rows) != 213:
        raise ValueError(
            f"Entry coverage check failed: expected 213 rows on disk, found {len(disk_rows)}"
        )
    if len(manifest) != 213:
        raise ValueError(
            f"Manifest coverage check failed: expected 213 rows in manifest, found {len(manifest)}"
        )

    # Manifest validation against slots (bit-for-bit check)
    manifest.validate_against_slots(slots_dir)

    # Record initial state of the 43 promoted rows for I-003 invariant guard
    initial_promoted = {
        _clean_concept(r.concept): r for r in disk_rows if r.status == slot_rows.STATUS_PROMOTED
    }
    if len(initial_promoted) != 43:
        raise ValueError(f"Expected exactly 43 promoted rows on disk before execution, found {len(initial_promoted)}")

    # Map merges: target_concept_norm -> list of source RowDisposition & source SlotRow
    merge_map: Dict[str, List[Tuple[RowDisposition, slot_rows.SlotRow]]] = {}
    for mr in manifest.merge_rows():
        target_norm = _clean_concept(mr.merge_into or "")
        src_row = next(
            (r for r in disk_rows if r.slot_file == mr.slot_file and r.line_number == mr.line),
            None,
        )
        if src_row is None:
            raise ValueError(f"Source row for merge not found: {mr.slot_file}:{mr.line}")
        merge_map.setdefault(target_norm, []).append((mr, src_row))

    # Read each slot file lines and prepare modifications
    slot_files = sorted(slots_dir.glob("*.md"))
    diff_outputs: List[str] = []
    file_updates: Dict[pathlib.Path, str] = {}

    rows_deleted_count = 0
    rows_merged_count = 0
    rows_kept_count = 0
    pending_count = 0

    for slot_file in slot_files:
        original_text = slot_file.read_text(encoding="utf-8")
        lines = original_text.splitlines()
        new_lines: List[str] = []
        table_started = False

        for line_num, line in enumerate(lines, start=1):
            if not line.startswith("|") or SEPARATOR_PATTERN.match(line.strip()):
                new_lines.append(line)
                continue

            parsed = slot_rows.parse_row(line, slot_file.name, line_num)
            if parsed is None:
                # Table header line (e.g. | concept | ...)
                new_lines.append(line)
                table_started = True
                continue

            # Lookup disposition in manifest
            disp_entry = manifest.get_by_loc(slot_file.name, line_num)
            if disp_entry is None:
                raise ValueError(
                    f"Row {slot_file.name}:{line_num} '{parsed.concept}' has no manifest entry!"
                )

            disp = disp_entry.disposition

            if disp == DISPOSITION_DELETE:
                # Omit line entirely
                rows_deleted_count += 1
                continue

            elif disp == DISPOSITION_MERGE_INTO:
                # Omit source line from slot table; its occurrences/evidence are merged into target
                rows_merged_count += 1
                continue

            elif disp in (DISPOSITION_UNDECIDED, DISPOSITION_SPLIT):
                # Untouched pending rows: preserve exact original line
                pending_count += 1
                new_lines.append(line)

            elif disp == DISPOSITION_KEEP_PROPOSED:
                # Preserved proposed rows: preserve line
                rows_kept_count += 1
                new_lines.append(line)

            elif disp == DISPOSITION_KEEP_PROMOTED:
                rows_kept_count += 1
                concept_norm = _clean_concept(parsed.concept)

                if concept_norm in merge_map:
                    # This row is a MERGE TARGET! Combine occurrences and evidence
                    merges = merge_map[concept_norm]
                    total_occ = parsed.occurrences or 0
                    comb_ev = parsed.evidence
                    comb_src = parsed.source_book

                    for _, s_row in merges:
                        total_occ += s_row.occurrences or 0
                        comb_ev = combine_evidence(comb_ev, s_row.evidence)
                        comb_src = combine_sources(comb_src, s_row.source_book)

                    updated_row_line = format_table_row(
                        concept=parsed.concept,
                        source_book=comb_src,
                        confidence=parsed.confidence,
                        status=parsed.status,
                        date_added=parsed.date_added,
                        promoted_note_id=parsed.promoted_note_id,
                        evidence=comb_ev,
                        occurrences=total_occ,
                    )
                    new_lines.append(updated_row_line)
                else:
                    # Regular KEEP_PROMOTED row: preserve exact line
                    new_lines.append(line)
            else:
                raise ValueError(f"Unknown disposition: {disp}")

        new_text = "\n".join(new_lines) + ("\n" if original_text.endswith("\n") else "")
        file_updates[slot_file] = new_text

        # Generate unified diff for output
        orig_lines = original_text.splitlines(keepends=True)
        mod_lines = new_text.splitlines(keepends=True)
        diff = list(
            difflib.unified_diff(
                orig_lines,
                mod_lines,
                fromfile=f"a/{slot_file.name}",
                tofile=f"b/{slot_file.name}",
            )
        )
        if diff:
            diff_outputs.extend(diff)

    diff_report = "".join(diff_outputs)

    # Perform post-modification verification in-memory before writing
    # Verify remaining rows count
    total_remaining = rows_kept_count + pending_count
    if total_remaining != 93:
        raise ValueError(
            f"Verification error: expected 93 remaining rows (84 + 2 + 7), got {total_remaining}"
        )
    if rows_deleted_count != 112:
        raise ValueError(f"Verification error: expected 112 deleted rows, got {rows_deleted_count}")
    if rows_merged_count != 8:
        raise ValueError(f"Verification error: expected 8 merged rows, got {rows_merged_count}")
    if pending_count != 7:
        raise ValueError(f"Verification error: expected 7 pending rows (6 undecided + 1 split), got {pending_count}")

    # Write changes if --apply is set
    if not dry_run:
        for slot_file, content in file_updates.items():
            slot_file.write_text(content, encoding="utf-8")

        # Post-write verification on disk
        post_disk_rows = slot_rows.read_all(slots_dir)
        if len(post_disk_rows) != 93:
            raise ValueError(f"Post-write check failed: expected 93 rows on disk, got {len(post_disk_rows)}")

        # Verify all 43 promoted rows are preserved with intact I-003 invariant
        post_promoted = {
            _clean_concept(r.concept): r for r in post_disk_rows if r.status == slot_rows.STATUS_PROMOTED
        }
        if len(post_promoted) != 43:
            raise ValueError(
                f"I-003 guard failed: expected 43 promoted rows, found {len(post_promoted)}"
            )

        for c_norm, init_r in initial_promoted.items():
            post_r = post_promoted.get(c_norm)
            if post_r is None:
                raise ValueError(f"I-003 guard failed: promoted concept '{init_r.concept}' was lost!")
            if post_r.status != slot_rows.STATUS_PROMOTED:
                raise ValueError(f"I-003 guard failed: promoted concept '{init_r.concept}' lost promoted status!")
            if post_r.promoted_note_id != init_r.promoted_note_id:
                raise ValueError(
                    f"I-003 guard failed: promoted_note_id changed for '{init_r.concept}'!"
                )

        # Verify 8 merges accurately summed occurrences
        for target_norm, merges in merge_map.items():
            post_r = post_promoted.get(target_norm)
            init_r = initial_promoted.get(target_norm)
            expected_occ = (init_r.occurrences or 0) + sum(s_r.occurrences or 0 for _, s_r in merges)
            if post_r.occurrences != expected_occ:
                raise ValueError(
                    f"Merge occurrences mismatch for target '{init_r.concept}': "
                    f"expected {expected_occ}, got {post_r.occurrences}"
                )

    summary = {
        "initial_rows": len(disk_rows),
        "deleted_rows": rows_deleted_count,
        "merged_rows": rows_merged_count,
        "kept_rows": rows_kept_count,
        "pending_rows": pending_count,
        "final_rows": total_remaining,
        "promoted_rows": len(initial_promoted),
        "dry_run": dry_run,
    }

    return True, diff_report, summary


def main():
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(encoding="utf-8")
        except Exception:
            pass

    parser = argparse.ArgumentParser(description="Apply row dispositions to ontology slots.")
    parser.add_argument(
        "--manifest",
        type=pathlib.Path,
        default=DEFAULT_MANIFEST,
        help="Path to disposition_manifest.json",
    )
    parser.add_argument(
        "--slots-dir",
        type=pathlib.Path,
        default=DEFAULT_SLOTS_DIR,
        help="Path to 01_ARCHITECTURE/ontology/slots directory",
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        default=False,
        help="Execute the changes in-place. Default is --dry-run.",
    )
    args = parser.parse_args()

    dry_run = not args.apply

    print("=" * 70)
    print(f"ONTOLOGY ROW DISPOSITION EXECUTOR {'(DRY RUN)' if dry_run else '(EXECUTING WRITE)'}")
    print("=" * 70)
    print(f"Manifest : {args.manifest}")
    print(f"Slots dir: {args.slots_dir}\n")

    try:
        success, diff_report, summary = apply_dispositions(
            manifest_path=args.manifest,
            slots_dir=args.slots_dir,
            dry_run=dry_run,
        )

        if dry_run:
            print("--- DIFF REPORT (ROW BY ROW) ---")
            if diff_report:
                print(diff_report)
            else:
                print("(No differences)")
            print("\n[DRY RUN SUMMARY]")
        else:
            print("[WRITE COMPLETED SUCCESSFULLY]")

        for k, v in summary.items():
            print(f"  {k:<16}: {v}")

        if dry_run:
            print("\nDry-run complete. Use --apply to execute changes to disk.")
        else:
            print("\nChanges applied to disk. Verified 93 rows remaining.")

    except Exception as e:
        print(f"\nERROR: Execution failed: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
