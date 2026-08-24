import os
import glob
import tempfile
import sys
from typing import Dict, Any, List, Optional
# Lazy import of Lifecycle moved inside query method to avoid circular import
from memory_controller.audit.logger import audit_event
from .serializer import serialize, deserialize
from .path_resolver import resolve_path

class FileStorageEngine:
    def __init__(self, vault_root: str):
        self.vault_root = vault_root
        self.id_to_path: Dict[str, str] = {}
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
            
            for filepath in glob.glob(os.path.join(folder_path, "**", "*.md"), recursive=True):
                # Double check to prevent RAW_IMPORTS or Obsidian MOC leakage
                if "RAW_IMPORTS" in filepath or "Obsidian" in filepath:
                    continue
                try:
                    with open(filepath, 'r', encoding='utf-8') as f:
                        content = f.read()
                    data = deserialize(content)
                    note_id = data.get("id")
                    if note_id:
                        if note_id in self.id_to_path:
                            # DUPLICATE UUID => FATAL INTEGRITY ERROR
                            raise ValueError(f"Duplicate UUID found: {note_id} in {filepath} and {self.id_to_path[note_id]}")
                        self.id_to_path[note_id] = filepath
                except Exception as e:
                    if "Duplicate UUID" in str(e):
                        raise e
                    if "Malformed YAML" in str(e):
                        # SKIP + AUDIT
                        audit_event("storage_error", "system", "unknown", success=False, 
                                    details={"error": "Malformed YAML", "path": filepath, "message": str(e)})
                        continue
                    # Ignored
                    continue

    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        filepath = self.id_to_path.get(note_id)
        if not filepath or not os.path.exists(filepath):
            return None
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        return deserialize(content)

    def set(self, note_id: str, data: Dict[str, Any]) -> None:
        # INVARIANT: storage key == data["id"]
        yaml_id = data.get("id")
        if str(note_id) != str(yaml_id):
            raise ValueError(f"ID mismatch: storage key '{note_id}' must equal YAML id '{yaml_id}'")
            
        target_path = resolve_path(self.vault_root, data)
        serialized_content = serialize(data)
        
        # Ensure directory exists
        os.makedirs(os.path.dirname(target_path), exist_ok=True)
        
        # ATOMIC WRITE: Write to a temporary file in the same directory, then replace
        dir_name = os.path.dirname(target_path)
        fd, temp_path = tempfile.mkstemp(dir=dir_name, prefix=".tmp_")
        try:
            with os.fdopen(fd, 'w', encoding='utf-8') as f:
                f.write(serialized_content)
                f.flush()
                os.fsync(f.fileno())
            # Replace target atomically
            os.replace(temp_path, target_path)
        except Exception as e:
            if os.path.exists(temp_path):
                os.remove(temp_path)
            raise e
            
        # Update/Rename semantic: if old path is different, delete old file
        existing_path = self.id_to_path.get(note_id)
        if existing_path and existing_path != target_path:
            if os.path.exists(existing_path):
                os.remove(existing_path)
                
        self.id_to_path[note_id] = target_path

    def delete(self, note_id: str) -> None:
        filepath = self.id_to_path.get(note_id)
        if filepath:
            # Re-verify we don't accidentally delete outside
            if "06_INBOX" in filepath:
                raise ValueError("Cannot delete from RAW_IMPORTS")
            if os.path.exists(filepath):
                os.remove(filepath)
            del self.id_to_path[note_id]

    def query(self, intent: str, lifecycle: List[str] = None, types: List[str] = None) -> List[Dict[str, Any]]:
        """Query notes, excluding RAW notes."""
        # Lazy import to avoid circular dependency
        from memory_controller.controller import Lifecycle

        results = []
        for note_id, filepath in self.id_to_path.items():
            try:
                note = self.get(note_id)
                if not note:
                    continue
                # Exclude RAW notes from normal queries
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

