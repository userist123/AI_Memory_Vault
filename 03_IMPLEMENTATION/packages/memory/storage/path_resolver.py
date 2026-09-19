import os
import re

def sanitize_filename(category: str) -> str:
    """Sanitizes the category to ensure safe filenames."""
    if not category or not isinstance(category, str):
        category = "untitled"
        
    # Remove characters that are unsafe on Windows/Unix
    # : * ? " < > | \ /
    safe = re.sub(r'[:*?"<>|\\/]', '_', category)
    
    # Remove non-printable characters
    safe = re.sub(r'[\x00-\x1f\x7f-\x9f]', '', safe)
    
    # Trim trailing dots and spaces (Windows issue)
    safe = safe.strip('. ')
    
    # Windows reserved names
    reserved = {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    }
    
    if safe.upper() in reserved:
        safe = f"{safe}_"
        
    # Enforce maximum length to prevent PathTooLong errors
    # NTFS max filename is 255 chars, we limit category to 100 to leave room for UUID
    if len(safe) > 100:
        safe = safe[:100].strip('. ')
        
    if not safe:
        safe = "untitled"
        
    return safe

#: Where NEW notes go when the vault has the content tree.
#:
#: The write path was never migrated: every type below used to land in the legacy
#: tree (01_KNOWLEDGE, 03_PROCEDURES, ...) while the corpus lives in the content
#: roots, so a note written today was invisible to anything that reads the corpus
#: layout and sat in a second taxonomy. The destinations below are where the
#: existing notes of each type already are (measured on the vault: lesson, error
#: and preference in 01_ARCHITECTURE/memory; knowledge in 01_ARCHITECTURE/knowledge;
#: procedure in 10_DOCUMENTATION/procedures; project in 02_PRODUCT/projects).
#:
#: A folder is used only if it exists in the vault being written to. A vault without
#: the content tree (the temporary vaults of the unit tests, or an older layout) keeps
#: the legacy destination, so this is a change of where the real vault writes, not a
#: change of what a path means. Notes that already exist are not touched: they keep
#: their exact path (FileStorageEngine._target_path_for, db08b847).
#: Types not listed keep their legacy folder; moving them needs the same evidence.
CONTENT_TREE_FOR_TYPE = {
    "knowledge": "01_ARCHITECTURE/knowledge",
    "lesson": "01_ARCHITECTURE/memory",
    "error": "01_ARCHITECTURE/memory",
    "preference": "01_ARCHITECTURE/memory",
    "procedure": "10_DOCUMENTATION/procedures",
    "project": "02_PRODUCT/projects",
}


def _destination_folder(vault_root: str, note_type: str, legacy_folder: str) -> str:
    content_folder = CONTENT_TREE_FOR_TYPE.get(note_type)
    if content_folder and os.path.isdir(os.path.join(vault_root, *content_folder.split("/"))):
        return content_folder
    return legacy_folder


def resolve_path(vault_root: str, note: dict, prefer_content_tree: bool = True) -> str:
    """Resolves the physical directory for a note based on its type and guarantees containment.

    `prefer_content_tree=False` gives the legacy destination, for a note that already lives in the legacy tree.
    """
    note_type = str(note.get("type", "knowledge")).lower()
    
    mapping = {
        "knowledge": "01_KNOWLEDGE",
        "project": "02_PROJECTS",
        "procedure": "03_PROCEDURES",
        "decision": "04_MEMORY",
        "experience": "04_MEMORY",
        "error": "04_MEMORY",
        "lesson": "04_MEMORY",
        "preference": "04_MEMORY",
        "hypothesis": "04_MEMORY",
        "resource": "05_RESOURCES",
        "system": "99_SYSTEM",
        "index": "99_SYSTEM",
        "core": "00_CORE"
    }
    
    folder = mapping.get(note_type, "04_MEMORY") # default to memory if unknown
    if prefer_content_tree:
        folder = _destination_folder(vault_root, note_type, folder)
    
    # Strict exclusion for RAW_IMPORTS mutation
    if "06_INBOX" in folder:
        raise ValueError("FileStorageEngine cannot mutate RAW_IMPORTS")
    
    note_id = str(note.get("id", ""))
    if not note_id:
        raise ValueError("Cannot resolve path for note without id")
        
    # Sanitize category for the filename
    category = sanitize_filename(str(note.get("category", "unknown")))
    
    # Prevent traversal payload in ID
    if ".." in note_id or "/" in note_id or "\\" in note_id:
        raise ValueError("Path traversal attempt in note id")
        
    filename = f"{category}_{note_id[:8]}.md"
    
    # Compute paths
    target_path = os.path.join(vault_root, folder, filename)
    resolved_target = os.path.realpath(target_path)
    resolved_root = os.path.realpath(vault_root)
    
    # Path Containment check
    try:
        common = os.path.commonpath([resolved_target, resolved_root])
        if common != resolved_root:
            raise ValueError("Path traversal attempt detected: Resolved path outside Vault root")
    except ValueError as e:
        # commonpath raises ValueError if paths are on different drives on Windows
        raise ValueError(f"Path traversal attempt detected: {e}")
        
    return target_path
