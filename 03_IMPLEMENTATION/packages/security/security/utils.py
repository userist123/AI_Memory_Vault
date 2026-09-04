import re

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
    """Prevent paths that escape the repository root or use absolute paths."""
    normalized = path.replace("\\", "/")
    if ".." in normalized:
        raise ValueError("Path traversal detected in path: " + path)
    if normalized.startswith("/") or re.match(r"^[a-zA-Z]:/", normalized) or re.match(r"^[a-zA-Z]:\\", path):
        raise ValueError("Absolute paths not allowed in note_id: " + path)

def detect_cache_poisoning(key: str, value) -> None:
    """Detect anomalous cache entries or poisoned keys."""
    if not re.fullmatch(r"[a-f0-9]{64}", key):
        raise ValueError("Invalid cache key format")
    
    # Calculate approximate size for lists and dicts
    size = 0
    if isinstance(value, (str, bytes)):
        size = len(value)
    elif isinstance(value, (list, dict)):
        import json
        try:
            size = len(json.dumps(value))
        except Exception:
            pass
            
    if size > 1_000_000:
        raise ValueError("Cache entry value exceeds size limit")
