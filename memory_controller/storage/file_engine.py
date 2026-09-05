import copy
import os
import glob
import tempfile
import sys
from typing import Dict, Any, List, Optional, Tuple
# Lazy import of Lifecycle moved inside query method to avoid circular import
from memory_controller.audit.logger import audit_event
from .serializer import serialize, deserialize
from .path_resolver import resolve_path

class FileStorageEngine:
    def __init__(self, vault_root: str):
        self.vault_root = vault_root
        self.id_to_path: Dict[str, str] = {}
        self._cache: Dict[str, Tuple[float, Dict[str, Any]]] = {}
        self._initialize_index()

    def _initialize_index(self):
        # Scan canonical folders to build the UUID -> Path index
        # EXPLICIT EXCLUSIONS: "06_INBOX" and "90_TEMPLATES" are NOT included
        canonical_folders = [
            "00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES",
            "04_MEMORY", "05_RESOURCES", "99_SYSTEM"
        ]
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
        """Returns a deep copy. A shallow `dict(data)` would still share
        nested objects (provenance dict, relations list) with this engine's
        internal `_cache`, so a caller mutating a nested field in place
        (e.g. `note['relations'].append(...)`) before validation fails would
        silently corrupt the cache without ever writing to disk, and any
        "rollback" built from such a shared copy would be a no-op for that
        field. See MemoryController.supersede() for the exact pattern this
        protects."""
        filepath = self.id_to_path.get(note_id)
        if not filepath or not os.path.exists(filepath):
            self._cache.pop(note_id, None)
            return None
        try:
            mtime = os.path.getmtime(filepath)
            cached = self._cache.get(note_id)
            if cached and cached[0] == mtime:
                return copy.deepcopy(cached[1])
            with open(filepath, 'r', encoding='utf-8') as f:
                content = f.read()
            data = deserialize(content)
            self._cache[note_id] = (mtime, copy.deepcopy(data))
            return copy.deepcopy(data)
        except Exception:
            return None

    def set(self, note_id: str, data: Dict[str, Any]) -> None:
        yaml_id = data.get("id")
        if str(note_id) != str(yaml_id):
            raise ValueError(f"ID mismatch: storage key '{note_id}' must equal YAML id '{yaml_id}'")
        # Defensive deep copy: never let the caller's own dict object become
        # the engine's cached representation. If the caller mutates `data`
        # after this call returns, the cache must not silently drift out of
        # sync with what was actually written to disk.
        data = copy.deepcopy(data)
        target_path = resolve_path(self.vault_root, data)
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
