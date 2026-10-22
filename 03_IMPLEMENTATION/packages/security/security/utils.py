import re

MAX_QUERY_LENGTH = 4096  # characters, configurable elsewhere if needed

def sanitize_query(query: str) -> str:
    """Return the search query as data, verbatim apart from surrounding whitespace.

    Owner-approved contract change 2026-10-22: the query is no longer stripped of HTML tags or
    `{{...}}`. The sink audit (00_GOVERNANCE/coordination/tasks/todo-claude-overnight-reliability.md,
    PHASE 7) found no consumer that renders it as HTML, interprets templates or builds an LLM
    prompt from it; stripping only deleted legitimate text ("a < b", "List<String>", "<DRIVE>:").
    Any code that renders a query must escape it at that render site. Size is enforced by
    check_query_size.
    """
    return query.strip()

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
