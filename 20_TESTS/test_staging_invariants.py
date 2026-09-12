"""
test_staging_invariants.py — Enforce Staging Directory Purity and Ingestion Anti-Doubling Invariants.

Guards against:
1. Aggregate files (like corpus_reconciled.json) sitting alongside atomic per-book files,
   which silently double concept occurrences when globbed by merge_candidate_concepts.
2. Cross-file concept/book provenance collisions.
3. Slot assignment conflicts across books.
"""

import json
import pathlib
import re
import pytest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[1]
STAGING_DIR = REPO_ROOT / "staging"


def normalize_concept_name(name: str) -> str:
    cleaned = re.sub(r'[^\w\s]', '', name.lower())
    return ' '.join(cleaned.split())


def get_staging_files():
    if not STAGING_DIR.exists():
        return []
    return [
        f for f in sorted(STAGING_DIR.glob('*.json'))
        if not f.name.endswith('_rej.json') and 'report' not in f.name
    ]


def test_staging_directory_no_aggregate_files():
    """
    Every JSON file in staging must represent exactly one source book matching its stem.
    Multi-book aggregate files are forbidden in staging because combine_across_books()
    sums occurrences by design, turning co-existing aggregates into silent occurrence doublers.
    """
    files = get_staging_files()
    assert len(files) > 0, "No staging files found to audit"

    violations = []
    for f in files:
        data = json.loads(f.read_text(encoding='utf-8'))
        if not data:
            continue
        source_books = {r.get('source_book') for r in data}
        if len(source_books) > 1:
            violations.append(
                f"AGGREGATE FORBIDDEN: {f.name} aggregates multiple source books: {source_books}. "
                "Aggregate files must never reside alongside atomic book files in staging/."
            )
        elif len(source_books) == 1:
            sb = next(iter(source_books))
            if sb != f.stem:
                violations.append(
                    f"STEM MISMATCH: {f.name} contains source_book '{sb}' != stem '{f.stem}'"
                )

    assert not violations, "\n".join(violations)


def test_staging_no_cross_file_concept_book_duplicates():
    """
    Each (concept, source_book) pair across staging must be strictly unique.
    """
    files = get_staging_files()
    seen = {}
    duplicates = []

    for f in files:
        data = json.loads(f.read_text(encoding='utf-8'))
        for idx, row in enumerate(data):
            c_norm = normalize_concept_name(row.get('concept', ''))
            sb = row.get('source_book', f.stem)
            pair = (c_norm, sb)
            if pair in seen:
                duplicates.append(
                    f"DUPLICATE PROVENANCE: {pair} in {f.name} (row {idx}) already seen in {seen[pair]}"
                )
            else:
                seen[pair] = f.name

    assert not duplicates, "\n".join(duplicates)


def test_staging_has_zero_slot_conflicts():
    """
    Every concept proposed across all books must resolve to exactly one canonical slot.
    """
    import sys
    sys.path.insert(0, str(REPO_ROOT / '30_SCRIPTS' / 'ingestion'))
    from merge_candidate_concepts import find_slot_conflicts

    files = get_staging_files()
    all_rows = []
    for f in files:
        all_rows.extend(json.loads(f.read_text(encoding='utf-8')))

    conflicts = find_slot_conflicts(all_rows)
    assert not conflicts, f"Unresolved slot conflicts found: {conflicts}"
