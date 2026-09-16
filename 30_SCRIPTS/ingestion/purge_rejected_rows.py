#!/usr/bin/env python3
"""
purge_rejected_rows.py — Safe, guarded concept row purger for ontology slot markdown tables.

Supports:
  1. Predefined batches from catalog:
     Batch A: 36 concepts (28 RESPINGE + 8 FUZIONEAZĂ — qualitatively evaluated and rejected/fused)
     Batch B: 62 concepts (occurrences < 3 — unjudged sub-threshold mentions)
     Batch C: 23 concepts (historical unjudged rows — 13 machine_learning_report + 10 Sarfraz seed)
     Batch D1: 5 concepts (unverified renamed/merged)
     Batch D2: 3 concepts (unverified absent/fabricated)
     Batch ALL: A + B + C + D1 + D2 (122 distinct physical concepts; duplicates deduplicated with warnings)
  2. Machine-readable disposition manifest via --disposition-file (schema: ontology-row-disposition.v1):
     Deletes only DELETE entries; explicitly refuses MERGE_INTO, KEEP_PROMOTED, KEEP_PROPOSED, SPLIT, UNDECIDED.
  3. Custom concept JSON file via --concept-file.

Safety guarantees:
1. Row identification by normalized concept name in Column 1, NEVER by line number.
2. REFUSES deletion if status == "promoted" (unconditional protection for promoted concepts).
3. REFUSES deletion if promoted_note_id is non-empty (unconditional protection for linked knowledge notes).
4. REFUSES deletion if disposition is MERGE_INTO (transfer of occurrences required; not mechanically deletable).
5. STATUS GUARD: default allows "proposed" (or "proposed,unverified_source" when using disposition file).
   --allow-status extends (not replaces) allowed statuses.
6. DRY-RUN by default; only writes to disk if --apply is explicitly passed.
"""

import os
import re
import glob
import json
import argparse
import pathlib
from typing import List, Dict, Any, Tuple, Optional, Set

DEFAULT_SLOTS_DIR = "01_ARCHITECTURE/ontology/slots"
CATALOG_PATH = pathlib.Path(__file__).parent / "purge_batches_catalog.json"

#: Consulted by --batch and --concept-file so a selection cannot outrank a
#: decision. Absent, those paths fall back to the status guards alone.
DEFAULT_DISPOSITION_MANIFEST = str(
    pathlib.Path(__file__).resolve().parents[2]
    / "07_EVALUATION" / "book_corpus_conversion" / "disposition_manifest.json"
)


def normalize_concept_name(name: str) -> str:
    """
    Normalizes concept name for robust deduplication and lookup.
    Strips markdown formatting (*, `), punctuation, and collapses whitespace.
    """
    cleaned = re.sub(r'[\*`]', '', str(name)).lower()
    cleaned = re.sub(r'[^\w\s]', '', cleaned)
    return " ".join(cleaned.split())


def load_catalog(custom_path: Optional[str] = None) -> Dict[str, List[Dict[str, str]]]:
    """
    Loads predefined batches A, B, C, D1, D2 from catalog JSON.
    """
    path = pathlib.Path(custom_path) if custom_path else CATALOG_PATH
    if not path.exists():
        raise FileNotFoundError(f"Purge catalog not found at: {path}")
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def resolve_slot_file(slot_ref: str, slots_dir: str) -> str:
    """
    Resolves slot file path from relative path, filename, or slot name.
    """
    # Direct existing path
    if os.path.exists(slot_ref):
        return slot_ref

    # Path inside slots_dir
    direct_in_dir = os.path.join(slots_dir, os.path.basename(slot_ref))
    if os.path.exists(direct_in_dir):
        return direct_in_dir

    # Match by slot name suffix (e.g. '03_ontology.md' or 'ontology')
    clean_name = slot_ref.replace(".md", "")
    if "_" in clean_name:
        clean_name = clean_name.split("_")[-1]

    matches = glob.glob(os.path.join(slots_dir, f"*_{clean_name}.md"))
    if matches:
        return matches[0]

    raise FileNotFoundError(f"Could not resolve slot file for '{slot_ref}' in '{slots_dir}'")


resolve_slot_path = resolve_slot_file


def annotate_with_dispositions(
    target_items: List[Dict[str, Any]],
    manifest_path: Optional[str],
) -> Optional[List[Dict[str, Any]]]:
    """Attach each target's disposition from the manifest, or None if disabled.

    `--batch` selects rows from a catalogue built before anyone had decided
    what should happen to them. Eight of those rows are merges, and deleting a
    merge loses the occurrences it was supposed to carry into its target; two
    more were recovered by the unverified_source triage. Neither fact is
    expressible in the catalogue, so the manifest has to supply it.

    A target the manifest does not mention keeps an empty disposition, which
    the non-DELETE guard then refuses. That is the intended outcome: the
    manifest covers every row on disk, so a target missing from it is a target
    nobody judged.
    """
    if not manifest_path:
        return None
    path = pathlib.Path(manifest_path)
    if not path.exists():
        return None
    try:
        from disposition_manifest import load_manifest
    except ImportError:
        from .disposition_manifest import load_manifest

    by_row = {
        (os.path.basename(row.slot_file), normalize_concept_name(row.concept)): row
        for row in load_manifest(str(path)).rows
    }
    annotated: List[Dict[str, Any]] = []
    for item in target_items:
        slot_ref = item.get("slot_file") or item.get("slot") or ""
        key = (os.path.basename(slot_ref), normalize_concept_name(item.get("concept", "")))
        row = by_row.get(key)
        merged = dict(item)
        merged["disposition"] = row.disposition if row else ""
        if row is not None and getattr(row, "merge_into", None):
            merged["merge_into"] = row.merge_into
        annotated.append(merged)
    return annotated


def purge_rows(
    target_items: List[Dict[str, Any]],
    slots_dir: str = DEFAULT_SLOTS_DIR,
    apply: bool = False,
    allow_status: Optional[str] = None,
    from_disposition_manifest: bool = False,
) -> Dict[str, Any]:
    """
    Executes or simulates the purge of target concepts from slot markdown tables.

    target_items: list of dicts with keys 'slot_file' (or 'slot') and 'concept' (and optionally 'disposition', 'merge_into').
    allow_status: if specified, extends allowed statuses with comma-separated values (e.g. 'unverified_source').
    from_disposition_manifest: if True, indicates targets come from disposition manifest.
    """
    allowed_statuses: Set[str] = {"proposed"}
    if from_disposition_manifest:
        allowed_statuses.add("unverified_source")

    if allow_status:
        for s in allow_status.split(","):
            s_clean = s.strip().lower()
            if s_clean:
                allowed_statuses.add(s_clean)

    deleted_rows: List[Dict[str, Any]] = []
    refused_rows: List[Dict[str, Any]] = []
    not_found_rows: List[Dict[str, Any]] = []
    files_modified: List[str] = []

    # Deduplicate targets by (resolved_path, normalized_concept)
    # Track duplicates and generate explicit warnings
    grouped: Dict[str, Dict[str, Dict[str, Any]]] = {}
    duplicate_warnings: List[Dict[str, str]] = []
    seen_targets: Dict[Tuple[str, str], str] = {}

    for item in target_items:
        slot_ref = item.get("slot_file") or item.get("slot")
        concept = item.get("concept")
        if not slot_ref or not concept:
            continue
        try:
            resolved_path = resolve_slot_path(slot_ref, slots_dir=slots_dir)
        except FileNotFoundError as e:
            not_found_rows.append({
                "file": slot_ref,
                "concept": concept,
                "reason": str(e)
            })
            continue

        norm_c = normalize_concept_name(concept)
        target_key = (os.path.basename(resolved_path), norm_c)

        if target_key in seen_targets:
            duplicate_warnings.append({
                "file": os.path.basename(resolved_path),
                "concept": concept,
                "original": seen_targets[target_key]
            })
            continue

        seen_targets[target_key] = concept
        if resolved_path not in grouped:
            grouped[resolved_path] = {}
        grouped[resolved_path][norm_c] = item

    distinct_targets_count = len(seen_targets)

    for file_path, targets in grouped.items():
        if not os.path.exists(file_path):
            for norm_c, target_item in targets.items():
                not_found_rows.append({
                    "file": file_path,
                    "concept": target_item.get("concept", norm_c),
                    "reason": "Slot file not found on disk"
                })
            continue

        with open(file_path, "r", encoding="utf-8") as f:
            content = f.read()

        # Locate Candidate concepts table
        table_pattern = r"(##\s+Candidate\s+[Cc]oncepts\s*\n)([\s\S]*?)(?=\n##|\Z)"
        match = re.search(table_pattern, content)
        if not match:
            for norm_c, target_item in targets.items():
                not_found_rows.append({
                    "file": file_path,
                    "concept": target_item.get("concept", norm_c),
                    "reason": "Could not locate '## Candidate concepts' table"
                })
            continue

        section_header = match.group(1)
        table_text = match.group(2)
        lines = table_text.strip().split("\n")

        retained_lines: List[str] = []
        found_in_file: Set[str] = set()
        file_changed = False

        for idx, line in enumerate(lines):
            # Preserve headers and separator lines (| concept | ..., |---|...)
            if idx < 2 or not line.strip().startswith("|") or line.strip().startswith("|---"):
                retained_lines.append(line)
                continue

            cols = [c.strip() for c in line.split("|")[1:-1]]
            if len(cols) < 1:
                retained_lines.append(line)
                continue

            row_concept = cols[0]
            row_norm = normalize_concept_name(row_concept)

            if row_norm in targets:
                found_in_file.add(row_norm)
                target_item = targets[row_norm]
                status = cols[3].lower() if len(cols) >= 4 else ""
                promoted_note_id = cols[5].strip() if len(cols) >= 6 else ""
                disp = str(target_item.get("disposition", "")).strip().upper()
                merge_target = target_item.get("merge_into")

                # UNCONDITIONAL GUARD 0: Status 'promoted' is strictly protected under any flag
                if status == "promoted":
                    refused_rows.append({
                        "file": file_path,
                        "concept": row_concept,
                        "status": status,
                        "note_id": promoted_note_id,
                        "reason": "Status 'promoted' is unconditionally protected (guard against deleting promoted concepts)"
                    })
                    retained_lines.append(line)
                    continue

                if disp == "KEEP_PROMOTED":
                    refused_rows.append({
                        "file": file_path,
                        "concept": row_concept,
                        "status": status,
                        "note_id": promoted_note_id,
                        "reason": "Disposition is 'KEEP_PROMOTED' (candidate admitted by verdicts gate; protected from deletion)"
                    })
                    retained_lines.append(line)
                    continue

                # UNCONDITIONAL GUARD 1: promoted_note_id must be empty under any flag
                if promoted_note_id != "":
                    refused_rows.append({
                        "file": file_path,
                        "concept": row_concept,
                        "status": status,
                        "note_id": promoted_note_id,
                        "reason": f"promoted_note_id is non-empty ('{promoted_note_id}') (guard against deleting linked notes)"
                    })
                    retained_lines.append(line)
                    continue

                # DISPOSITION GUARD 2: MERGE_INTO is strictly refused as a simple deletion
                if disp == "MERGE_INTO" or merge_target:
                    refused_rows.append({
                        "file": file_path,
                        "concept": row_concept,
                        "status": status,
                        "note_id": promoted_note_id,
                        "reason": f"Disposition is 'MERGE_INTO' targeting '{merge_target}' (merge transfers occurrences and is not implemented; cannot mechanically delete)"
                    })
                    retained_lines.append(line)
                    continue

                # DISPOSITION GUARD 3: Non-DELETE disposition in manifest
                if from_disposition_manifest and disp != "DELETE":
                    refused_rows.append({
                        "file": file_path,
                        "concept": row_concept,
                        "status": status,
                        "note_id": promoted_note_id,
                        "reason": f"Disposition is '{disp}' (only 'DELETE' entries are permitted for deletion)"
                    })
                    retained_lines.append(line)
                    continue

                # STATUS GUARD 4: Status must be in allowed_statuses
                if status not in allowed_statuses:
                    if len(allowed_statuses) == 1:
                        target_allowed_status = next(iter(allowed_statuses))
                        reason_msg = f"Status '{status}' != '{target_allowed_status}' (guard against deleting non-{target_allowed_status} concepts)"
                    else:
                        reason_msg = f"Status '{status}' not in permitted statuses ({sorted(allowed_statuses)}) (guard against deleting non-permitted concepts)"
                    refused_rows.append({
                        "file": file_path,
                        "concept": row_concept,
                        "status": status,
                        "note_id": promoted_note_id,
                        "reason": reason_msg
                    })
                    retained_lines.append(line)
                    continue

                # Deletion permitted
                deleted_rows.append({
                    "file": file_path,
                    "concept": row_concept,
                    "status": status,
                    "source_book": cols[1] if len(cols) >= 2 else "",
                    "occurrences": cols[7] if len(cols) >= 8 else "",
                    "raw_line": line
                })
                file_changed = True
            else:
                retained_lines.append(line)

        # Track any targets not found in table
        for norm_c, target_item in targets.items():
            if norm_c not in found_in_file:
                not_found_rows.append({
                    "file": file_path,
                    "concept": target_item.get("concept", norm_c),
                    "reason": "Concept not found in Candidate concepts table"
                })

        # If changes occurred and apply is set, write back file
        if file_changed:
            files_modified.append(file_path)
            new_table_text = "\n".join(retained_lines)
            new_content = content.replace(section_header + table_text, section_header + new_table_text)
            if apply:
                with open(file_path, "w", encoding="utf-8") as f:
                    f.write(new_content)

    return {
        "apply": apply,
        "dry_run": not apply,
        "allow_status": allow_status,
        "allowed_statuses": sorted(list(allowed_statuses)),
        "total_targets": distinct_targets_count,
        "input_targets_count": len(target_items),
        "duplicate_targets_count": len(duplicate_warnings),
        "duplicate_warnings": duplicate_warnings,
        "deleted_count": len(deleted_rows),
        "refused_count": len(refused_rows),
        "not_found_count": len(not_found_rows),
        "files_modified_count": len(files_modified),
        "files_modified": sorted(files_modified),
        "deleted": deleted_rows,
        "refused": refused_rows,
        "not_found": not_found_rows
    }


def format_report(results: Dict[str, Any]) -> str:
    """
    Formats the purge results into human-readable text.
    """
    lines = []
    mode_str = "APPLIED (MODIFIED ON DISK)" if results["apply"] else "DRY RUN (NO CHANGES WRITTEN)"
    lines.append(f"=== PURGE CONCEPT ROWS REPORT [{mode_str}] ===")
    if results.get("allow_status"):
        lines.append(f"Explicit Allow Status: {results['allow_status']} [INVOKED]")
    lines.append(f"Allowed Statuses: {', '.join(results.get('allowed_statuses', []))}")
    lines.append(f"Total Targets: {results['total_targets']} (input: {results.get('input_targets_count', results['total_targets'])})")
    if results.get("duplicate_targets_count", 0) > 0:
        lines.append(f"Duplicate Targets: {results['duplicate_targets_count']} (deduplicated)")
    lines.append(f"Deleted Rows:  {results['deleted_count']}")
    lines.append(f"Refused Rows:  {results['refused_count']}")
    lines.append(f"Not Found:     {results['not_found_count']}")
    lines.append(f"Files Modified:{results['files_modified_count']}")
    lines.append("-" * 50)

    if results.get("duplicate_warnings"):
        lines.append("\n[!] WARNING: Duplicate target rows detected in input (collapsed to distinct physical rows):")
        for dw in results["duplicate_warnings"]:
            lines.append(f"  - '{dw['concept']}' in {dw['file']} (already queued as '{dw['original']}')")

    if results["refused"]:
        lines.append("\n[!] REFUSED DELETIONS (GUARDS TRIGGERED):")
        for r in results["refused"]:
            lines.append(f"  - {r['concept']} in {os.path.basename(r['file'])}: {r['reason']}")

    if results["deleted"]:
        lines.append("\n[-] DELETED ROWS:")
        for d in results["deleted"]:
            src = f" (src: {d['source_book']}, occ: {d['occurrences']})" if d.get('source_book') else ""
            lines.append(f"  - {d['concept']}{src} from {os.path.basename(d['file'])}")

    if results["not_found"]:
        lines.append("\n[?] NOT FOUND IN SLOTS:")
        for nf in results["not_found"]:
            lines.append(f"  - {nf['concept']} in {os.path.basename(nf['file'])} ({nf['reason']})")

    lines.append("-" * 50)
    if results["dry_run"]:
        lines.append("NOTE: This was a dry run. To execute changes on disk, run with --apply.")
    else:
        lines.append("NOTE: Changes have been written to disk.")

    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(
        description="Safely purge unpromoted candidate concept rows from ontology slot markdown tables."
    )
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument(
        "--batch",
        choices=["A", "B", "C", "D1", "D2", "ALL"],
        help="Predefined batch to purge: A (28 RESPINGE + 8 FUZIONEAZĂ), B (62 occ < 3), C (23 historical), D1 (5 redenumit/comasat), D2 (3 absent/fabricat), ALL (A+B+C+D1+D2)"
    )
    group.add_argument(
        "--concept-file",
        help="Path to JSON file with custom list of [{'slot_file': ..., 'concept': ...}]"
    )
    group.add_argument(
        "--disposition-file",
        help="Path to disposition_manifest.json (schema: ontology-row-disposition.v1). Acts on DELETE entries and refuses others."
    )
    parser.add_argument(
        "--disposition-manifest",
        default=DEFAULT_DISPOSITION_MANIFEST,
        help="Manifest consulted to constrain --batch and --concept-file targets. "
             "A target it does not mark DELETE is refused. Pass '' to disable, "
             "which removes the only protection those two paths have."
    )

    parser.add_argument(
        "--allow-status",
        help="Explicitly permit purging rows with specified status(es), comma-separated (e.g. 'unverified_source' or 'proposed,unverified_source'). Default includes 'proposed'."
    )
    parser.add_argument(
        "--slots-dir",
        default=DEFAULT_SLOTS_DIR,
        help=f"Directory containing ontology slot markdown files (default: {DEFAULT_SLOTS_DIR})"
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Actually apply changes to disk (default is dry-run)"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output raw JSON instead of text report"
    )

    args = parser.parse_args()

    target_items: List[Dict[str, Any]] = []
    from_disposition_manifest = False

    if args.batch:
        catalog = load_catalog()
        if args.batch == "ALL":
            for b in ["A", "B", "C", "D1", "D2"]:
                target_items.extend(catalog.get(b, []))
        else:
            target_items.extend(catalog.get(args.batch, []))
    elif args.concept_file:
        with open(args.concept_file, "r", encoding="utf-8") as f:
            target_items = json.load(f)
    elif args.disposition_file:
        try:
            from disposition_manifest import load_manifest
        except ImportError:
            from .disposition_manifest import load_manifest
        manifest = load_manifest(args.disposition_file)
        target_items = [r.to_dict() for r in manifest.rows]
        from_disposition_manifest = True

    #: A batch is a selection, not a decision. The catalogue was built before
    #: the disposition manifest existed and knows nothing about MERGE_INTO or
    #: about the two unverified_source rows the triage recovered — so
    #: `--batch ALL --allow-status unverified_source` deletes ten rows the
    #: manifest protects, eight of them merges whose occurrences would simply
    #: be lost. Annotating the targets makes the manifest authoritative no
    #: matter how they were selected; a target the manifest does not mark
    #: DELETE is then refused by the same guard, and a target absent from it
    #: is refused too, which is correct: the manifest covers every row on disk.
    if target_items and not from_disposition_manifest:
        annotated = annotate_with_dispositions(target_items, args.disposition_manifest)
        if annotated is not None:
            target_items = annotated
            from_disposition_manifest = True

    results = purge_rows(
        target_items,
        slots_dir=args.slots_dir,
        apply=args.apply,
        allow_status=args.allow_status,
        from_disposition_manifest=from_disposition_manifest,
    )

    if args.json:
        print(json.dumps(results, indent=2))
    else:
        print(format_report(results))


if __name__ == "__main__":
    main()
