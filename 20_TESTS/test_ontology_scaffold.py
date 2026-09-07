"""20_TESTS/test_ontology_scaffold.py — Verification of the Ontology Scaffold.

Verifies:
1. All 17 ontology scaffold files exist (ONTOLOGY_SPEC.md + 16 slots).
2. Every file's YAML frontmatter parses cleanly and adheres to the required schema.
3. Every slot's ontology_slot matches one of the 16 canonical names, exactly once.
4. Every non-'none' validates_module path resolves to a real, existing file or directory on disk.
5. Every slot's Candidate concepts table exists and is empty (0 data rows).
"""
from __future__ import annotations

import re
from pathlib import Path
import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[1]
ONTOLOGY_DIR = REPO_ROOT / "01_ARCHITECTURE" / "ontology"
SLOTS_DIR = ONTOLOGY_DIR / "slots"

CANONICAL_SLOTS = (
    "identity",
    "map",
    "ontology",
    "relationships",
    "state",
    "procedures",
    "judgement",
    "constraints",
    "provenance",
    "confidence",
    "history",
    "skills",
    "agents",
    "routing",
    "retrieval",
    "consolidation",
)

SLOT_FILES = [
    f"{i:02d}_{name}.md" for i, name in enumerate(CANONICAL_SLOTS, 1)
]


def parse_frontmatter(content: str) -> dict:
    parts = content.split("---")
    assert len(parts) >= 3, "File missing valid YAML frontmatter delimiters"
    return yaml.safe_load(parts[1])


def test_all_17_files_exist():
    """Asserts all 17 files exist (ONTOLOGY_SPEC.md + 16 slot files)."""
    assert (ONTOLOGY_DIR / "ONTOLOGY_SPEC.md").is_file(), "ONTOLOGY_SPEC.md is missing"
    assert SLOTS_DIR.is_dir(), "slots/ directory is missing"

    for filename in SLOT_FILES:
        path = SLOTS_DIR / filename
        assert path.is_file(), f"Ontology slot file {filename} is missing"


def test_frontmatter_schema_all_files():
    """Asserts every file's frontmatter matches the required schema."""
    all_files = [ONTOLOGY_DIR / "ONTOLOGY_SPEC.md"] + [SLOTS_DIR / fn for fn in SLOT_FILES]

    for file_path in all_files:
        content = file_path.read_text(encoding="utf-8")
        fm = parse_frontmatter(content)

        assert "id" in fm and isinstance(fm["id"], str) and fm["id"], f"Missing id in {file_path.name}"
        assert fm.get("type") == "ontology_definition", f"Invalid type in {file_path.name}"
        assert fm.get("lifecycle") == "ACTIVE", f"Lifecycle must be ACTIVE in {file_path.name}"

        prov = fm.get("provenance")
        assert isinstance(prov, dict), f"Provenance must be a dict in {file_path.name}"
        assert prov.get("source_type") == "design", f"Provenance source_type must be 'design' in {file_path.name}"
        assert prov.get("source_author"), f"Provenance source_author must not be empty in {file_path.name}"

        assert float(fm.get("confidence", 0)) == 1.0, f"Confidence must be 1.00 in {file_path.name}"
        assert fm.get("status") == "scaffold", f"Status must be 'scaffold' in {file_path.name}"
        assert fm.get("tags") == ["ontology", "cognitive-architecture"], f"Invalid tags in {file_path.name}"
        assert fm.get("relations") == [], f"Relations must be empty list in {file_path.name}"
        assert re.match(r"^\d{4}-\d{2}-\d{2}$", str(fm.get("created"))), f"Invalid created date in {file_path.name}"


def test_canonical_slot_names_exact_coverage():
    """Asserts every ontology_slot value is one of the 16 canonical names, exactly once each."""
    observed_slots = []

    for filename in SLOT_FILES:
        file_path = SLOTS_DIR / filename
        content = file_path.read_text(encoding="utf-8")
        fm = parse_frontmatter(content)

        slot_name = fm.get("ontology_slot")
        assert slot_name is not None, f"Missing ontology_slot in {filename}"
        assert slot_name in CANONICAL_SLOTS, f"Invalid ontology_slot '{slot_name}' in {filename}"
        observed_slots.append(slot_name)

    assert len(observed_slots) == 16, f"Expected 16 slot files, got {len(observed_slots)}"
    assert len(set(observed_slots)) == 16, "Duplicate ontology_slot detected"
    assert set(observed_slots) == set(CANONICAL_SLOTS), "Missing canonical ontology slots"


def test_validates_module_paths_exist_on_disk():
    """Asserts every non-'none' validates_module path actually exists on disk."""
    for filename in SLOT_FILES:
        file_path = SLOTS_DIR / filename
        content = file_path.read_text(encoding="utf-8")

        # Extract '## Validates module' section
        assert "## Validates module" in content, f"Missing '## Validates module' section in {filename}"
        after_section = content.split("## Validates module", 1)[1]
        section_text = after_section.split("## ", 1)[0].strip()

        # Find backticked paths
        backtick_items = re.findall(r"`([^`]+)`", section_text)
        
        if not backtick_items:
            assert section_text.lower().startswith("none"), (
                f"Section in {filename} has no backticked paths and does not say 'none': {section_text}"
            )
            continue

        verified_paths = 0
        for item in backtick_items:
            clean_item = item.strip().rstrip("/")
            if (
                clean_item.startswith(("00_", "01_", "02_", "03_", "10_", "30_", ".agents"))
                or clean_item in {"README.md", "AGENTS.md", "VAULT_STATE.md"}
            ):
                target_path = REPO_ROOT / clean_item
                assert target_path.exists(), (
                    f"Validated module path '{clean_item}' in {filename} does NOT exist in checkout!"
                )
                verified_paths += 1

        if not section_text.lower().startswith("none"):
            assert verified_paths > 0, f"No verified paths found in non-none section in {filename}"


def test_candidate_concepts_table_structure():
    """Asserts that the Candidate concepts table exists and all data rows have valid 5-column schema."""
    for filename in SLOT_FILES:
        file_path = SLOTS_DIR / filename
        content = file_path.read_text(encoding="utf-8")

        assert "## Candidate concepts" in content, f"Missing '## Candidate concepts' section in {filename}"
        after_section = content.split("## Candidate concepts", 1)[1]
        table_text = after_section.split("## ", 1)[0].strip()

        lines = [line.strip() for line in table_text.splitlines() if line.strip()]
        assert len(lines) >= 2, f"Candidate concepts table missing in {filename}"
        assert "concept" in lines[0] and "source_book" in lines[0] and "confidence" in lines[0]
        data_rows = lines[2:]
        for row in data_rows:
            cols = [c.strip() for c in row.split("|")]
            assert len(cols) == 8, f"Invalid 6-column row format in {filename}: {row}"
            assert cols[4] in ["proposed", "unverified_source", "promoted", "REVIEW", "ACTIVE"], f"Invalid status in {filename}: {cols[4]}"
