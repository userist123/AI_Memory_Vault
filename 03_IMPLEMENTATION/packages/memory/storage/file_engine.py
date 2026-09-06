import os
import glob
import tempfile
import sys
from typing import Dict, Any, List, Optional, Tuple
# Lazy import of Lifecycle moved inside query method to avoid circular import
from memory_controller.audit.logger import audit_event
from .serializer import serialize, deserialize
from .path_resolver import resolve_path

#: Folders scanned when indexing notes from disk.
#:
#: This is the UNION of two taxonomies that the restructuring left divergent,
#: and the union is deliberate:
#:
#: * The content roots, equal to ``retrieval.vault_index.DEFAULT_ROOTS``. The
#:   engine did not scan these at all. On a fresh checkout its old list --
#:   "00_CORE", "02_PROJECTS", "03_PROCEDURES" and friends -- matched 0 files,
#:   so the production storage layer loaded zero notes while 663 id-bearing
#:   notes sat in the vault, 633 of them in 01_ARCHITECTURE alone.
#:
#: * The legacy write targets, which ``storage/path_resolver.py`` still uses:
#:   a note of type "knowledge" is written to 01_KNOWLEDGE, "procedure" to
#:   03_PROCEDURES, and so on. Reads must keep covering them or the engine
#:   would stop seeing what it itself writes.
#:
#: KNOWN GAP: the write path was never migrated to the numbered content roots,
#: so new notes still land in the legacy tree while the corpus lives in the new
#: one. Unifying them is a data migration and an architecture decision, not a
#: constant change; it is deliberately NOT done here.
#:
#: Nothing caught the read-side divergence because the suite exercises the
#: in-memory StorageEngine with fixtures. `test_storage_canonical_roots.py`
#: now runs against the real vault.
CONTENT_ROOTS = (
    "01_ARCHITECTURE",
    "02_PRODUCT",
    "10_DOCUMENTATION",
    "00_GOVERNANCE",
)

LEGACY_WRITE_ROOTS = (
    "00_CORE",
    "01_KNOWLEDGE",
    "02_PROJECTS",
    "03_PROCEDURES",
    "04_MEMORY",
    "05_RESOURCES",
    "99_SYSTEM",
)

CANONICAL_FOLDERS = CONTENT_ROOTS + LEGACY_WRITE_ROOTS


class FileStorageEngine:
    def __init__(self, vault_root: str):
        self.vault_root = vault_root
        self.id_to_path: Dict[str, str] = {}
        self._cache: Dict[str, Tuple[float, Dict[str, Any]]] = {}
        self._initialize_index()

    def _initialize_index(self):
        # Scan canonical folders to build the UUID -> Path index
        # EXPLICIT EXCLUSIONS: "06_INBOX" and "90_TEMPLATES" are NOT included
        canonical_folders = list(CANONICAL_FOLDERS)
        for folder in canonical_folders:
            folder_path = os.path.join(self.vault_root, folder)
            if not os.path.exists(folder_path):
                continue
            
            found_files = set(glob.glob(os.path.join(folder_path, "*.md"))).union(
                set(glob.glob(os.path.join(folder_path, "**", "*.md"), recursive=True))
            )
            for filepath in sorted(found_files):
                if "RAW_IMPORTS" in filepath or "Obsidian" in filepath:
                    continue
                try:
                    with open(filepath, 'r', encoding='utf-8') as f:
                        content = f.read()
                    data = deserialize(content)
                    note_id = data.get("id")
                    if note_id:
                        if note_id in self.id_to_path:
                            raise ValueError(f"Duplicate UUID found: {note_id} in {filepath} and {self.id_to_path[note_id]}")
                        self.id_to_path[note_id] = filepath
                        mtime = os.path.getmtime(filepath)
                        self._cache[note_id] = (mtime, data)
                except Exception as e:
                    if "Duplicate UUID" in str(e):
                        raise e
                    if "Malformed YAML" in str(e):
                        audit_event("storage_error", "system", "unknown", success=False,
                                    details={"error": "Malformed YAML", "path": filepath, "message": str(e)})
                        continue
                    continue

    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        filepath = self.id_to_path.get(note_id)
        if not filepath or not os.path.exists(filepath):
            self._cache.pop(note_id, None)
            return None
        try:
            mtime = os.path.getmtime(filepath)
            cached = self._cache.get(note_id)
            if cached and cached[0] == mtime:
                return dict(cached[1])
            with open(filepath, 'r', encoding='utf-8') as f:
                content = f.read()
            data = deserialize(content)
            self._cache[note_id] = (mtime, dict(data))
            return dict(data)
        except Exception:
            return None

    def _target_path_for(self, note_id: str, data: Dict[str, Any]) -> str:
        """Where this note should be written.

        `resolve_path()` maps a note TYPE to the legacy tree (knowledge ->
        01_KNOWLEDGE, procedure -> 03_PROCEDURES) and derives the file name
        from its category. Applied unconditionally to an existing note living
        in a content root, it writes a copy into the legacy tree and `set()`
        then deletes the canonical original as a stale duplicate.

        That path was inert only because the engine used to load zero notes:
        `id_to_path` never held a canonical note, so the delete never fired.
        Making the engine see the vault armed it, which is why this is settled
        here rather than deferred to the pending migration decision.

        An existing note under a content root therefore keeps its exact path,
        directory and file name both. The file name is not cosmetic there: it
        is the note's identity in the graph, since Obsidian and
        `VaultIndex.by_slug` both resolve `[[links]]` by file name. Renaming a
        canonical note because its category changed would silently break every
        link pointing at it.

        Notes in the legacy tree keep the previous behaviour, where the file
        name tracks the category. New notes are placed by `resolve_path()`
        unchanged; moving the existing corpus remains a separate, explicit
        migration decision.
        """
        resolved = resolve_path(self.vault_root, data)
        existing_path = self.id_to_path.get(note_id)
        if not existing_path or not os.path.exists(existing_path):
            return resolved
        existing_dir = os.path.dirname(os.path.abspath(existing_path))
        root = os.path.abspath(self.vault_root)
        try:
            rel = os.path.relpath(existing_dir, root)
        except ValueError:
            return resolved
        top = rel.replace("\\", "/").split("/")[0]
        if top in CONTENT_ROOTS:
            return existing_path
        return resolved

    def set(self, note_id: str, data: Dict[str, Any]) -> None:
        yaml_id = data.get("id")
        if str(note_id) != str(yaml_id):
            raise ValueError(f"ID mismatch: storage key '{note_id}' must equal YAML id '{yaml_id}'")
        target_path = self._target_path_for(note_id, data)
        serialized_content = serialize(data)
        os.makedirs(os.path.dirname(target_path), exist_ok=True)
        dir_name = os.path.dirname(target_path)
        fd, temp_path = tempfile.mkstemp(dir=dir_name, prefix=".tmp_")
        try:
            with os.fdopen(fd, 'w', encoding='utf-8') as f:
                f.write(serialized_content)
                f.flush()
                os.fsync(f.fileno())
            os.replace(temp_path, target_path)
        except Exception as e:
            if os.path.exists(temp_path):
                os.remove(temp_path)
            raise e
        existing_path = self.id_to_path.get(note_id)
        if existing_path and existing_path != target_path:
            if os.path.exists(existing_path):
                os.remove(existing_path)
        self.id_to_path[note_id] = target_path
        mtime = os.path.getmtime(target_path)
        self._cache[note_id] = (mtime, data)

    def delete(self, note_id: str) -> None:
        filepath = self.id_to_path.get(note_id)
        if filepath:
            if "06_INBOX" in filepath:
                raise ValueError("Cannot delete from RAW_IMPORTS")
            if os.path.exists(filepath):
                os.remove(filepath)
            del self.id_to_path[note_id]
            self._cache.pop(note_id, None)

    def query(self, intent: str, lifecycle: List[str] = None, types: List[str] = None) -> List[Dict[str, Any]]:
        """Query notes, excluding RAW notes."""
        from memory_controller.controller import Lifecycle
        results = []
        for note_id in list(self.id_to_path.keys()):
            try:
                note = self.get(note_id)
                if not note:
                    continue
                if note.get('lifecycle') == Lifecycle.RAW.value:
                    continue
                if lifecycle and note.get('lifecycle') not in lifecycle:
                    continue
                if types and note.get('type') not in types:
                    continue
                results.append(note)
            except Exception:
                continue
        return results

    def all_notes(self) -> List[Dict[str, Any]]:
        """Return all non-RAW notes for read-only indexing consumers."""
        return self.query("graph")
