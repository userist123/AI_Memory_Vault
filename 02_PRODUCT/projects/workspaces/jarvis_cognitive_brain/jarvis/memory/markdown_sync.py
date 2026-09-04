"""
Bidirectional Obsidian Markdown Note Sync Engine with Atomic File Persistence.
"""

import os
import re
import tempfile
from pathlib import Path
from typing import Optional, Dict, Any, List, Tuple
import yaml

from jarvis.memory.invariants import (
    NoteFrontmatter,
    NoteType,
    Lifecycle,
    Principal,
    validate_propose_invariants,
)
from jarvis.memory.sqlite_engine import SQLiteStorageEngine


FOLDER_TYPE_MAP = {
    NoteType.KNOWLEDGE.value: "01_KNOWLEDGE",
    NoteType.PROJECT.value: "02_PROJECTS",
    NoteType.PROCEDURE.value: "03_PROCEDURES",
    NoteType.ERROR.value: "04_MEMORY/Errors",
    NoteType.LESSON.value: "04_MEMORY/Lessons",
    NoteType.EXPERIENCE.value: "04_MEMORY/Experiences",
    NoteType.DECISION.value: "04_MEMORY/Decisions",
    NoteType.PREFERENCE.value: "04_MEMORY/Preferences",
    NoteType.HYPOTHESIS.value: "04_MEMORY/Hypotheses",
    NoteType.RESOURCE.value: "05_RESOURCES",
    NoteType.SYSTEM.value: "99_SYSTEM",
    NoteType.CORE.value: "00_CORE",
}

EXCLUDED_FOLDERS = {"06_INBOX", "90_TEMPLATES", ".agents", ".checkpoints", ".git"}


class MarkdownSyncEngine:
    """Handles atomic reading, writing, and synchronizing of Obsidian Markdown notes."""

    def __init__(self, vault_root: Path):
        self.vault_root = Path(vault_root)
        os.makedirs(self.vault_root, exist_ok=True)

    @staticmethod
    def parse_markdown(file_content: str) -> Tuple[Dict[str, Any], str]:
        """Extract YAML frontmatter and body content from raw markdown text."""
        pattern = r"^---\s*\n(.*?)\n---\s*\n?(.*)$"
        match = re.search(pattern, file_content, re.DOTALL)
        if not match:
            return {}, file_content.strip()

        yaml_str, body = match.group(1), match.group(2)
        try:
            frontmatter = yaml.safe_load(yaml_str) or {}
        except yaml.YAMLError as e:
            raise ValueError(f"Failed to parse YAML frontmatter: {e}") from e

        return frontmatter, body.strip()

    @staticmethod
    def format_markdown(frontmatter: Dict[str, Any], content: str) -> str:
        """Format frontmatter and body into canonical Obsidian markdown string."""
        yaml_str = yaml.safe_dump(
            frontmatter,
            default_flow_style=False,
            sort_keys=False,
            allow_unicode=True,
        )
        return f"---\n{yaml_str}---\n\n{content.strip()}\n"

    def write_note_atomic(
        self,
        note_dict: Dict[str, Any],
        subfolder: Optional[str] = None,
        filename: Optional[str] = None,
    ) -> Path:
        """
        Atomically write note to disk using tempfile + os.fsync + os.replace.
        Guarantees zero-byte corruptions even on abrupt process termination.
        """
        # Validate frontmatter
        fm_copy = note_dict.copy()
        content = fm_copy.pop("content", "")
        # Remove sqlite internal artifacts if any
        fm_copy.pop("raw_json", None)

        validated_fm = NoteFrontmatter.model_validate(fm_copy)
        # Use mode="json" so Enums are serialized to plain strings for YAML dumper
        fm_dict = validated_fm.model_dump(mode="json")

        note_type = fm_dict.get("type", NoteType.KNOWLEDGE.value)
        target_subfolder = subfolder or FOLDER_TYPE_MAP.get(note_type, "01_KNOWLEDGE")
        dest_dir = self.vault_root / target_subfolder
        os.makedirs(dest_dir, exist_ok=True)

        note_id = fm_dict["id"]
        safe_name = filename or f"{fm_dict.get('category', 'note')}_{note_id[:8]}.md"
        if not safe_name.endswith(".md"):
            safe_name += ".md"

        target_file = dest_dir / safe_name
        full_text = self.format_markdown(fm_dict, content)

        # Atomic temp write
        temp_fd, temp_path = tempfile.mkstemp(dir=dest_dir, prefix=".tmp_")
        try:
            with open(temp_fd, "w", encoding="utf-8") as f:
                f.write(full_text)
                f.flush()
                os.fsync(f.fileno())
            os.replace(temp_path, target_file)
        except Exception:
            if os.path.exists(temp_path):
                try:
                    os.remove(temp_path)
                except Exception:
                    pass
            raise

        return target_file

    save_note_atomic = write_note_atomic

    def read_note(self, file_path: Path) -> Dict[str, Any]:
        """Read and validate note from a markdown file."""
        with open(file_path, "r", encoding="utf-8") as f:
            raw_text = f.read()

        frontmatter, content = self.parse_markdown(raw_text)
        validated_fm = NoteFrontmatter.model_validate(frontmatter)
        note_dict = validated_fm.model_dump(mode="json")
        note_dict["content"] = content
        return note_dict

    def sync_vault_to_sqlite(self, sqlite_engine: SQLiteStorageEngine) -> int:
        """Scan vault markdown files and index/update valid canonical notes into SQLite."""
        synced_count = 0
        for root, dirs, files in os.walk(self.vault_root):
            # Exclude non-canonical folders
            rel_dir = os.path.relpath(root, self.vault_root)
            top_level = rel_dir.split(os.sep)[0]
            if top_level in EXCLUDED_FOLDERS:
                continue

            for file in files:
                if not file.endswith(".md") or file.startswith("."):
                    continue
                full_path = Path(root) / file
                try:
                    note_dict = self.read_note(full_path)
                    sqlite_engine.set_note_atomic(note_dict)
                    synced_count += 1
                except Exception:
                    # Skip malformed or non-canonical markdown files
                    continue
        return synced_count

    def export_sqlite_to_vault(self, sqlite_engine: SQLiteStorageEngine) -> int:
        """Export all active/canonical SQLite notes to vault markdown files."""
        notes = sqlite_engine.query(limit=10000)
        exported_count = 0
        for note in notes:
            # Skip RAW imports from canonical folder export
            if note.get("lifecycle") == Lifecycle.RAW.value:
                continue
            self.write_note_atomic(note)
            exported_count += 1
        return exported_count
