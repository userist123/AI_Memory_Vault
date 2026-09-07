#!/usr/bin/env python3
"""
merge_candidate_concepts.py — Merge extracted staging concepts into ontology slot files.

Reads a staging JSON file of extracted candidate concepts, deduplicates against
existing rows in each slot's "Candidate concepts" table, and appends net-new
rows with status=proposed and date_added=YYYY-MM-DD.
"""

import os
import re
import glob
import json
import argparse
from datetime import datetime
from typing import List, Dict, Any, Tuple, Set

CANONICAL_SLOTS = [
    "identity", "map", "ontology", "relationships",
    "state", "procedures", "judgement", "constraints",
    "provenance", "confidence", "history", "skills",
    "agents", "routing", "retrieval", "consolidation"
]

SLOT_DIRECTORY = "01_ARCHITECTURE/ontology/slots"


def find_slot_file(slot_name: str, slots_dir: str = SLOT_DIRECTORY) -> str:
    """
    Locates the markdown file corresponding to slot_name (e.g. 16_consolidation.md).
    """
    pattern = os.path.join(slots_dir, f"*_{slot_name}.md")
    matches = glob.glob(pattern)
    if not matches:
        raise FileNotFoundError(f"No slot file found for slot '{slot_name}' in {slots_dir}")
    return matches[0]


def normalize_concept_name(name: str) -> str:
    """
    Normalizes concept name for fuzzy deduplication (lowercased, stripped, collapsed spaces).
    """
    cleaned = re.sub(r'[^\w\s]', '', name.lower())
    return " ".join(cleaned.split())


def extract_existing_concepts(slot_file_path: str) -> Set[str]:
    """
    Parses the 'Candidate concepts' table in a slot markdown file and returns a set
    of normalized existing concept names.
    """
    with open(slot_file_path, "r", encoding="utf-8") as f:
        content = f.read()

    existing = set()
    table_match = re.search(r'## Candidate concepts\s*\n(\|.*(?:\n\|.*)*)', content)
    if table_match:
        table_text = table_match.group(1)
        lines = [l.strip() for l in table_text.strip().split("\n")]
        # Skip header line and separator line (| concept | ...)
        for line in lines[2:]:
            cols = [c.strip() for c in line.split("|")]
            if len(cols) >= 3 and cols[1]:
                existing.add(normalize_concept_name(cols[1]))

    return existing


def append_candidate_concepts_to_slot(
    slot_file_path: str,
    new_concepts: List[Dict[str, Any]],
    date_str: str
) -> int:
    """
    Appends net-new concept rows to the Candidate concepts table of the slot file.
    Returns count of newly added rows.
    """
    with open(slot_file_path, "r", encoding="utf-8") as f:
        content = f.read()

    existing_normalized = extract_existing_concepts(slot_file_path)
    added_count = 0
    new_rows = []

    for item in new_concepts:
        norm_name = normalize_concept_name(item["concept"])
        if norm_name in existing_normalized:
            continue

        existing_normalized.add(norm_name)
        conf_str = f"{item.get('confidence_in_literature', 0.90):.2f}"
        # The sentence the definition came from, carried into the row so a
        # reviewer can judge the candidate without opening the staging file.
        # This is the grounding check's output: it was verified to be present
        # in the source text, and it is what makes a fabricated citation
        # visible. Pipes and newlines would break the table.
        evidence = str(item.get("evidence_quote", "")).replace("|", "/")
        evidence = " ".join(evidence.split())[:300]
        row = (
            f"| {item['concept']} | {item['source_book']} | {conf_str} "
            f"| proposed | {date_str} | | {evidence} |"
        )
        new_rows.append(row)
        added_count += 1

    if not new_rows:
        return 0

    # Locate Candidate concepts table section
    # The evidence column is optional in the pattern so that a slot file which
    # has not been migrated still merges, rather than silently matching
    # nothing and reporting zero additions.
    table_match = re.search(r'(## Candidate concepts\s*\n\| concept \| source_book \| confidence \| status \| date_added \| promoted_note_id \|(?: evidence \|)?\s*\n\|---\|---\|---\|---\|---\|---\|(?:---\|)?)', content)
    if not table_match:
        # Fallback to 5-column header if not yet updated
        table_match = re.search(r'(## Candidate concepts\s*\n\| concept \| source_book \| confidence \| status \| date_added \|\s*\n\|---\|---\|---\|---\|---\|)', content)

    if not table_match:
        raise ValueError(f"Could not locate '## Candidate concepts' table in {slot_file_path}")

    header_block = table_match.group(1)
    rows_to_insert = "\n" + "\n".join(new_rows)
    updated_content = content.replace(header_block, header_block + rows_to_insert)

    with open(slot_file_path, "w", encoding="utf-8") as f:
        f.write(updated_content)

    return added_count


def merge_candidate_concepts(
    staging_file: str,
    slots_dir: str = SLOT_DIRECTORY,
    override_date: str = None
) -> Dict[str, Any]:
    """
    Main merge function.
    """
    if not os.path.exists(staging_file):
        raise FileNotFoundError(f"Staging file not found: {staging_file}")

    with open(staging_file, "r", encoding="utf-8") as f:
        staging_data: List[Dict[str, Any]] = json.load(f)

    date_str = override_date or datetime.now().strftime("%Y-%m-%d")

    grouped_by_slot: Dict[str, List[Dict[str, Any]]] = {}
    for item in staging_data:
        slot = item.get("maps_to_slot", "").lower()
        if slot not in CANONICAL_SLOTS:
            raise ValueError(f"Invalid canonical slot '{slot}' in concept record: {item}")
        grouped_by_slot.setdefault(slot, []).append(item)

    merged_stats = {}
    total_added = 0
    total_deduped = 0

    for slot_name, records in grouped_by_slot.items():
        slot_file = find_slot_file(slot_name, slots_dir)
        added = append_candidate_concepts_to_slot(slot_file, records, date_str)
        deduped = len(records) - added
        merged_stats[slot_name] = {"added": added, "deduped": deduped}
        total_added += added
        total_deduped += deduped

    return {
        "staging_file": staging_file,
        "total_records_processed": len(staging_data),
        "net_new_concepts_merged": total_added,
        "concepts_deduplicated": total_deduped,
        "per_slot_summary": merged_stats
    }


def main():
    parser = argparse.ArgumentParser(description="Merge staging candidate concepts into ontology slot markdown tables.")
    parser.add_argument("--staging-file", default="staging/extracted_concepts.json", help="Path to staging JSON file")
    parser.add_argument("--slots-dir", default=SLOT_DIRECTORY, help="Path to ontology slots directory")

    args = parser.parse_args()

    results = merge_candidate_concepts(args.staging_file, args.slots_dir)
    print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
