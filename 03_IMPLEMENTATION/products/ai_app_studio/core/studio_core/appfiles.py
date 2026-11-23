"""Positive write allow-list for app projects (DESIGN.md 3.2; probes P1a, P5d, P8a).

A path is accepted only if it has the exact shape

    src/<dir>{0,4}/<name>.<ext>      ext in SRC_EXT
    public/<dir>{0,3}/<name>.<ext>   ext in PUBLIC_EXT

    tests/flows/<name>.spec.ts                 (W3: agent flow tests; data run by the studio's Playwright, never
                                               copied into a build context)

where every ``dir`` and ``name`` matches ``^[A-Za-z0-9][A-Za-z0-9_-]{0,40}$`` - so there is no dot, colon (ADS),
tilde (8.3 short name), space, trailing dot/space, backslash, or hidden file in any segment, and no ``..``.
Everything else (package.json, lockfiles, *.config.*, index.html, .npmrc, android/**, AGENTS.md, CLAUDE.md,
.mcp.json, .claude/**, .vscode/**) is refused by construction, not by a deny-list. The prefix must be the exact
lowercase ``src``/``public`` (a differently-cased prefix would be a second directory on a case-sensitive
filesystem); the extension is compared case-insensitively. Studio-owned control names are refused on top.
"""

from __future__ import annotations

import re

from .errors import ValidationFailed

SRC_EXT = frozenset({"ts", "tsx", "css", "json", "svg"})
PUBLIC_EXT = frozenset({"png", "jpg", "jpeg", "webp", "svg", "ico", "woff2", "json", "txt"})
MAX_FILES = 300
MAX_FILE_BYTES = 200_000
MAX_TOTAL_BYTES = 4 * 1024 * 1024
MAX_PATH_CHARS = 200

_SEG = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{0,40}$")
_RESERVED = re.compile(r"^(con|prn|aux|nul|clock\$|com[0-9]|lpt[0-9])$", re.I)
# names the studio owns even though their extension would pass (a project must not shadow them)
_CONTROL_STEMS = frozenset({"package", "package-lock", "tsconfig", "jsconfig", "claude", "agents", "gemini", "mcp"})
_MAX_DIRS = {"src": 4, "public": 3}
_EXT = {"src": SRC_EXT, "public": PUBLIC_EXT}
# W3: the only shape accepted under tests/ - exact lowercase dirs, one flat folder, a single-segment name, .spec.ts
_FLOW_SPEC = re.compile(r"^tests/flows/([A-Za-z0-9][A-Za-z0-9_-]{0,40})\.(?i:spec\.ts)$")


def validate_path(path: str) -> str:
    """Return ``path`` unchanged when it is on the allow-list, else raise ValidationFailed."""
    if not isinstance(path, str) or not path or len(path) > MAX_PATH_CHARS:
        raise ValidationFailed("invalid file path")
    parts = path.split("/")
    root = parts[0]
    if root == "tests":
        m = _FLOW_SPEC.fullmatch(path)
        if not m:
            raise ValidationFailed("under tests/ only tests/flows/<name>.spec.ts is allowed", path=path)
        if _RESERVED.fullmatch(m.group(1)):
            raise ValidationFailed("reserved device name", path=path)
        if m.group(1).casefold() in _CONTROL_STEMS:
            raise ValidationFailed("this file name is reserved for the studio", path=path)
        return path
    if root not in _EXT or len(parts) < 2:
        raise ValidationFailed("files must live under src/ or public/ (flow tests: tests/flows/<name>.spec.ts)",
                               path=path)
    dirs, leaf = parts[1:-1], parts[-1]
    if len(dirs) > _MAX_DIRS[root]:
        raise ValidationFailed(f"at most {_MAX_DIRS[root]} folders below {root}/", path=path)
    stem, dot, ext = leaf.rpartition(".")
    if not dot or ext.lower() not in _EXT[root]:
        raise ValidationFailed(f"extension not allowed under {root}/ (allowed: {', '.join(sorted(_EXT[root]))})", path=path)
    for seg in (*dirs, stem):
        if not _SEG.fullmatch(seg):
            raise ValidationFailed("path segment not allowed (letters, digits, _ and - only, max 41 chars)", path=path)
        if _RESERVED.fullmatch(seg):
            raise ValidationFailed("reserved device name", path=path)
    if stem.casefold() in _CONTROL_STEMS:
        raise ValidationFailed("this file name is reserved for the studio", path=path)
    return path


def validate_content(path: str, content: str) -> int:
    """Text content check; returns the UTF-8 size."""
    if not isinstance(content, str) or "\x00" in content:
        raise ValidationFailed("content must be text without NUL characters", path=path)
    size = len(content.encode("utf-8"))
    if size > MAX_FILE_BYTES:
        raise ValidationFailed(f"file exceeds {MAX_FILE_BYTES} bytes", path=path)
    return size


LANGUAGES = {"ts": "typescript", "tsx": "typescript", "css": "css", "json": "json", "svg": "xml", "txt": "text",
             "png": "binary", "jpg": "binary", "jpeg": "binary", "webp": "binary", "ico": "binary", "woff2": "binary"}


def language_of(path: str) -> str:
    return LANGUAGES.get(path.rpartition(".")[2].lower(), "text")
