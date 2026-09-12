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



class SlotConflict(Exception):
    """One concept proposed for two different slots.

    Not a merge failure to be worked around — a decision nobody has made yet.
    Extracting the corpus book by book produced this the moment a second book
    covered the same ground: "semantic memory" arrived as `retrieval` from Soar,
    which treats it as a store you retrieve from, and as `ontology` from
    Schacter, which treats it as a distinct memory system. Both readings are
    defensible in their own book. The vault holds the concept once.

    Merging anyway would put one concept in the ontology twice, under two
    slots, as two unrelated things — the exact fragmentation the slot scheme
    exists to prevent. So this stops and names the disagreement.
    """


def find_slot_conflicts(records):
    """Concepts proposed for more than one slot, and who proposed what."""
    seen = {}
    for item in records:
        name = normalize_concept_name(item["concept"])
        slot = str(item.get("maps_to_slot", "")).lower()
        book = str(item.get("source_book", "?"))
        by_slot = seen.setdefault(name, {})
        books = by_slot.setdefault(slot, [])
        if book not in books:
            books.append(book)
    return {name: by_slot for name, by_slot in seen.items() if len(by_slot) > 1}


def _as_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0



class DuplicateRows(Exception):
    """The same extracted row loaded more than once.

    `combine_across_books` sums occurrences across rows sharing a concept and a
    slot, because a term defined in fifteen sections of one book and twenty-two
    of another is defined in thirty-seven sections. That is correct for two
    books and catastrophic for one row read twice, and nothing in the rows
    themselves distinguishes the cases.

    It happened. `staging/` held twenty-one per-book files and an aggregate
    containing the same 264 rows, so a glob over the directory loaded 528. The
    measured effect on "declarative memory": occurrences [15, 22, 8, 15, 22, 8]
    summing to 90 against a true value of 45.

    The ontology escaped it by luck rather than design — the promotion run
    happened to take the aggregate as a single input file while the conflict
    check globbed the directory. Two callers, two answers, and no complaint
    from either.

    So identity is (concept, source_book, evidence_quote). Two rows agreeing on
    all three are not two observations. A book that genuinely defines a term
    twice produces two different quotes.
    """


def find_duplicate_rows(records: List[Dict[str, Any]]) -> Dict[Tuple[str, str], int]:
    """Rows appearing more than once with identical concept, book and evidence."""
    seen: Dict[Tuple[str, str, str], int] = {}
    for item in records:
        key = (
            normalize_concept_name(item.get("concept", "")),
            str(item.get("source_book", "")),
            " ".join(str(item.get("evidence_quote", "")).split()),
        )
        seen[key] = seen.get(key, 0) + 1
    return {
        (concept, book): count
        for (concept, book, _evidence), count in seen.items() if count > 1
    }


def combine_across_books(records):
    """One row per concept, carrying what every book contributed.

    Within a slot the same concept arrives from several books — "declarative
    memory" from Schacter at 15 sections and from Squire & Kandel at 22. The
    old behaviour kept whichever came first and dropped the rest, so the
    better-evidenced row was discarded roughly half the time, silently, and
    counted as a deduplication.

    occurrences add up, because the signal is how many distinct sections define
    the term and sections in different books are different sections. A concept
    two books each return to a dozen times is more load-bearing than one a
    single book mentions twice, and the sum is what says so.

    The evidence quote comes from the book that evidenced it best, since only
    one fits in the table and a reviewer reads that one.

    Summing is the right behaviour for two books and catastrophic for one row
    read twice, and the function cannot tell those apart from the rows alone —
    so exact duplicates are refused before any arithmetic. See
    `find_duplicate_rows`.
    """
    duplicates = find_duplicate_rows(records)
    if duplicates:
        lines = [
            f"  {concept} from {book}: {count} identical rows"
            for (concept, book), count in sorted(duplicates.items())
        ]
        raise DuplicateRows(
            f"{len(duplicates)} row(s) appear more than once with identical "
            "evidence. Summing them would multiply occurrences, which is the "
            "only ranking signal this pipeline has:" + chr(10)
            + chr(10).join(lines[:10])
            + (chr(10) + f"  ... and {len(lines) - 10} more" if len(lines) > 10 else "")
        )

    combined = {}
    order = []
    for item in records:
        name = normalize_concept_name(item["concept"])
        if name not in combined:
            combined[name] = dict(item, source_books=[str(item.get("source_book", "?"))])
            order.append(name)
            continue

        kept = combined[name]
        book = str(item.get("source_book", "?"))
        if book not in kept["source_books"]:
            kept["source_books"].append(book)
        occ_total = _as_int(kept.get("occurrences")) + _as_int(item.get("occurrences"))
        if _as_int(item.get("occurrences")) > _as_int(kept.get("occurrences")):
            # Better-evidenced book wins the definition and the quote.
            combined[name] = dict(item, source_books=kept["source_books"])
        combined[name]["occurrences"] = occ_total

    for name in order:
        row = combined[name]
        row["source_book"] = "; ".join(row.pop("source_books"))
    return [combined[n] for n in order]


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
        # How many distinct sections of the book define this term. It is the
        # only ranking signal in this pipeline that carries information:
        # confidence is 1.00 on nearly everything including candidates whose
        # evidence was fabricated, so the column beside this one is the
        # misleading number and this one is the useful one. Review in
        # descending order of it; a floor of 3 leaves ~17 concepts per book.
        occurrences = item.get("occurrences", "")
        row = (
            f"| {item['concept']} | {item['source_book']} | {conf_str} "
            f"| proposed | {date_str} | | {evidence} | {occurrences} |"
        )
        new_rows.append(row)
        added_count += 1

    if not new_rows:
        return 0

    # Locate Candidate concepts table section
    # The evidence column is optional in the pattern so that a slot file which
    # has not been migrated still merges, rather than silently matching
    # nothing and reporting zero additions.
    table_match = re.search(r'(## Candidate concepts\s*\n\| concept \| source_book \| confidence \| status \| date_added \| promoted_note_id \|(?: evidence \|)?(?: occurrences \|)?\s*\n\|---\|---\|---\|---\|---\|---\|(?:---\|)?(?:---\|)?)', content)
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
    override_date: str = None,
    allow_slot_conflicts: bool = False,
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

    # Before writing anything. A concept the corpus puts in two slots is a
    # disagreement between books, and writing it into both is how one concept
    # becomes two unrelated ones.
    conflicts = find_slot_conflicts(staging_data)
    if conflicts and not allow_slot_conflicts:
        lines = [
            "  " + name + ": " + ", ".join(
                slot + " (" + ", ".join(books) + ")"
                for slot, books in sorted(by_slot.items())
            )
            for name, by_slot in sorted(conflicts.items())
        ]
        raise SlotConflict(
            str(len(conflicts)) + " concept(s) proposed for more than one slot; "
            "decide before merging, or pass allow_slot_conflicts=True to write "
            "each into its own slot deliberately:" + chr(10) + chr(10).join(lines)
        )

    merged_stats = {}
    total_added = 0
    total_already_present = 0
    total_combined = 0

    for slot_name, records in grouped_by_slot.items():
        slot_file = find_slot_file(slot_name, slots_dir)
        rows = combine_across_books(records)
        combined_here = len(records) - len(rows)
        added = append_candidate_concepts_to_slot(slot_file, rows, date_str)
        already = len(rows) - added
        merged_stats[slot_name] = {
            "added": added,
            "combined_across_books": combined_here,
            "already_in_slot_file": already,
        }
        total_added += added
        total_combined += combined_here
        total_already_present += already

    return {
        "staging_file": staging_file,
        "total_records_processed": len(staging_data),
        "net_new_concepts_merged": total_added,
        # Two different things that used to be one number called
        # "deduplicated": rows folded together because several books define the
        # same concept, and rows not written because the slot file already has
        # that concept.
        "combined_across_books": total_combined,
        "already_in_slot_file": total_already_present,
        "slot_conflicts": {k: sorted(v) for k, v in conflicts.items()},
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
