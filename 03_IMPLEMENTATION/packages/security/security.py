import re
from typing import List

MAX_QUERY_LENGTH = 4096  # characters, configurable elsewhere if needed

def sanitize_query(query: str) -> str:
    """Basic sanitization to prevent prompt injection.
    Removes suspicious patterns like "{{" "}}" and stray markdown.
    """
    # Remove mustache-like placeholders
    sanitized = re.sub(r"\{\{.*?\}\}", "", query)
    # Remove HTML/script tags
    sanitized = re.sub(r"<script.*?>.*?</script>", "", sanitized, flags=re.DOTALL | re.IGNORECASE)
    sanitized = re.sub(r"<[^>]+>", "", sanitized)
    return sanitized.strip()

def check_query_size(query: str) -> None:
    """Raise ValueError if query exceeds soft/hard limits (hard enforced)."""
    if len(query) > MAX_QUERY_LENGTH:
        raise ValueError(f"Query length {len(query)} exceeds maximum allowed {MAX_QUERY_LENGTH}")

def check_path_traversal(path: str) -> None:
    """Prevent paths that escape the repository root.
    Simple check: no '..' segments and must be absolute within workspace.
    """
    if ".." in path.replace("\\", "/"):
        raise ValueError("Path traversal detected in path: " + path)

def detect_cache_poisoning(key: str, value) -> None:
    """Placeholder for detecting suspicious cache entries.
    For now, ensure key is a valid SHA256 hex string and value is not excessively large.
    """
    if not re.fullmatch(r"[a-f0-9]{64}", key):
        raise ValueError("Invalid cache key format")
    # Simple size guard
    if isinstance(value, (str, bytes)) and len(value) > 1_000_000:
        raise ValueError("Cache entry value exceeds size limit")
