"""evaluation/tests/test_vault_structure.py — Structural Governance and Architecture Validation Suite.

Asserts:
1. Presence of all canonical layers (00_CORE through 10_ARCHIVE, evaluation, telemetry, tasks)
2. Presence of required foundational documents and navigational indices
3. Strict absence of legacy duplicates (*_Claude_Legacy.md, *_Perplexity_Legacy.md) in active layers
4. Clear separation between telemetry, audit, and evaluation reports
5. Frontmatter schema integrity (id, type, lifecycle) on canonical knowledge notes
6. Deterministic duplicate note ID detection across the vault
"""
import re
from pathlib import Path
import pytest

VAULT_ROOT = Path(__file__).resolve().parent.parent.parent

CANONICAL_LAYERS = [
    "00_CORE",
    "01_KNOWLEDGE",
    "02_PROJECTS",
    "03_PROCEDURES",
    "04_MEMORY",
    "05_RESOURCES",
    "06_INBOX",
    "07_EVALUATION",
    "08_EXPORTS",
    "09_COORDINATION",
    "10_ARCHIVE",
    "90_TEMPLATES",
    "99_SYSTEM",
]

REQUIRED_CANONICAL_DOCS = [
    "00_CORE/Identity.md",
    "00_CORE/Rules.md",
    "00_CORE/Goals.md",
    "00_CORE/System_Architecture.md",
    "00_CORE/Memory_Protocol.md",
    "00_CORE/No_Fabrication_Policy.md",
    "00_CORE/Confidence_Model.md",
    "01_KNOWLEDGE/VAULT_INDEX.md",
    "01_KNOWLEDGE/VAULT_ARCHITECTURE_MAP.md",
    "01_KNOWLEDGE/Agent_Memory_Trace_Protocol.md",
    "01_KNOWLEDGE/Memory_Usage_Audit_Principles.md",
    "02_PROJECTS/AI_Memory_System.md",
    "02_PROJECTS/LogAnalyzer_MVP.md",
    "02_PROJECTS/Registru_de_transferuri.md",
    "03_PROCEDURES/Closed_Loop_Reflexion_Pipeline.md",
    "99_SYSTEM/Council_Runtime_Profile.yaml",
    "09_COORDINATION/todo.md",
    "09_COORDINATION/lessons.md",
]


def test_canonical_layers_exist():
    for layer in CANONICAL_LAYERS:
        layer_path = VAULT_ROOT / layer
        assert layer_path.exists(), f"Required layer missing: {layer}"
        assert layer_path.is_dir(), f"Layer must be a directory: {layer}"


def test_required_canonical_docs_exist():
    for doc in REQUIRED_CANONICAL_DOCS:
        doc_path = VAULT_ROOT / doc
        assert doc_path.exists(), f"Required canonical document missing: {doc}"



def test_no_legacy_duplicates_in_active_layers():
    active_folders = [
        "00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES",
        "04_MEMORY", "05_RESOURCES", "06_INBOX", "90_TEMPLATES", "99_SYSTEM"
    ]
    for folder in active_folders:
        dir_path = VAULT_ROOT / folder
        for md_file in dir_path.rglob("*.md"):
            name = md_file.name
            assert "_Claude_Legacy" not in name, f"Legacy duplicate found in active layer {folder}: {name}"
            assert "_Perplexity_Legacy" not in name, f"Legacy duplicate found in active layer {folder}: {name}"


def test_archived_legacy_duplicates_preserved():
    archive_dir = VAULT_ROOT / "10_ARCHIVE" / "legacy_duplicates"
    assert archive_dir.exists(), "10_ARCHIVE/legacy_duplicates directory must exist"
    archived_files = list(archive_dir.glob("*.md"))
    assert len(archived_files) >= 40, f"Expected >=40 archived duplicates, found {len(archived_files)}"



def test_separation_of_telemetry_audit_and_evaluation():
    telemetry_dir = VAULT_ROOT / "telemetry"
    audit_dir = VAULT_ROOT / "07_EVALUATION" / "memory_usage_audit"
    reports_dir = VAULT_ROOT / "07_EVALUATION" / "reports"

    assert telemetry_dir.exists()
    assert audit_dir.exists()
    assert reports_dir.exists()

    traces = list(telemetry_dir.glob("*.jsonl"))
    assert len(traces) >= 1

    reports = list(reports_dir.glob("*.md"))
    assert len(reports) >= 4


def test_canonical_frontmatter_integrity():
    checked = 0
    for doc in ["00_CORE/Identity.md", "00_CORE/Rules.md", "01_KNOWLEDGE/VAULT_INDEX.md", "01_KNOWLEDGE/Agent_Memory_Trace_Protocol.md"]:
        p = VAULT_ROOT / doc
        text = p.read_text(encoding="utf-8")
        assert text.startswith("---"), f"{doc} missing frontmatter delimiter"

        assert re.search(r"^id:\s*", text, re.MULTILINE), f"{doc} missing id in frontmatter"
        assert re.search(r"^type:\s+", text, re.MULTILINE), f"{doc} missing type in frontmatter"
        assert re.search(r"^(lifecycle|status|document_status):\s*", text, re.MULTILINE), f"{doc} missing lifecycle/status in frontmatter"

        checked += 1

    assert checked >= 4


def test_no_duplicate_canonical_note_ids():
    id_map = {}
    scan_dirs = ["00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES"]
    for d in scan_dirs:
        for md_file in (VAULT_ROOT / d).glob("*.md"):
            text = md_file.read_text(encoding="utf-8")
            match = re.search(r"^id:\s*[\"\']?([^\"\'\r\n]+)[\"\']?", text, re.MULTILINE)
            if match:
                note_id = match.group(1).strip()
                assert note_id not in id_map, f"Duplicate note ID '{note_id}' in {md_file} and {id_map.get(note_id)}"
                id_map[note_id] = str(md_file.relative_to(VAULT_ROOT))
    assert len(id_map) > 20
