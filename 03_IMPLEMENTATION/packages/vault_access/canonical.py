"""Strict vault:// URIs, slugs, globs and path containment.

Grammar (anything else is INVALID_URI):

    vault://<domain>[/<slug>][#<anchor>][?v=<sha12>]
    domain  = seg ("." seg)*            seg = [a-z0-9_]+
    slug    = part ("/" part)*          part = [a-z0-9][a-z0-9._-]*   (never "..", never empty)
    anchor  = [a-z0-9-]{1,120}
    sha12   = [0-9a-f]{12}

There is no percent-decoding, no backslash, no drive letter, no NUL and no `:` after the
scheme, so a URI can never smuggle a Windows path, an alternate data stream or a traversal.
"""
from __future__ import annotations

import os
import re
import unicodedata
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path, PurePosixPath
from typing import Optional

from .errors import ErrorCode, VaultAccessError

MAX_URI = 512
_URI_RE = re.compile(
    r"^vault://(?P<domain>[a-z0-9_]+(?:\.[a-z0-9_]+)*)"
    r"(?:/(?P<slug>[a-z0-9][a-z0-9._-]*(?:/[a-z0-9][a-z0-9._-]*)*))?"
    r"(?:#(?P<anchor>[a-z0-9-]{1,120}))?"
    r"(?:\?v=(?P<version>[0-9a-f]{12}))?$"
)
_WINDOWS_RESERVED = {"con", "prn", "aux", "nul", *(f"com{i}" for i in range(1, 10)), *(f"lpt{i}" for i in range(1, 10))}


@dataclass(frozen=True)
class VaultUri:
    domain: str
    slug: Optional[str]
    anchor: Optional[str]
    version: Optional[str]

    @property
    def base(self) -> str:
        return f"vault://{self.domain}" + (f"/{self.slug}" if self.slug else "")


def parse_uri(value: object) -> VaultUri:
    if not isinstance(value, str) or not value:
        raise VaultAccessError(ErrorCode.INVALID_URI, "uri must be a non-empty string")
    if len(value) > MAX_URI:
        raise VaultAccessError(ErrorCode.INVALID_URI, "uri too long")
    if any(ch in value for ch in ("\\", "%", "\x00", "..")) or value != value.strip():
        raise VaultAccessError(ErrorCode.INVALID_URI, "uri contains forbidden characters")
    if unicodedata.normalize("NFC", value) != value or not value.isascii():
        raise VaultAccessError(ErrorCode.INVALID_URI, "uri must be plain ASCII")
    match = _URI_RE.match(value)
    if not match:
        raise VaultAccessError(ErrorCode.INVALID_URI, "uri does not match vault://<domain>/<slug>")
    slug = match.group("slug")
    if slug:
        for part in slug.split("/"):
            stem = part.split(".", 1)[0]
            if stem in _WINDOWS_RESERVED or part.endswith((".", "-")):
                raise VaultAccessError(ErrorCode.INVALID_URI, "uri contains a reserved name")
    return VaultUri(match.group("domain"), slug, match.group("anchor"), match.group("version"))


def fold(text: str) -> str:
    """casefold + strip diacritics (ș→s, ț→t, ă→a, î→i, â→a)."""
    decomposed = unicodedata.normalize("NFKD", text.casefold())
    return "".join(ch for ch in decomposed if not unicodedata.combining(ch))


def slugify(text: str, keep_dots: bool = True) -> str:
    allowed = r"[^a-z0-9._-]+" if keep_dots else r"[^a-z0-9_-]+"
    slug = re.sub(allowed, "-", fold(text)).strip("-._")
    slug = re.sub(r"-{2,}", "-", slug)
    return slug


def anchor_for(heading: str) -> str:
    return slugify(heading, keep_dots=False).replace("_", "-")[:120] or "section"


def domain_id(text: str) -> str:
    return re.sub(r"[^a-z0-9_]+", "_", fold(text)).strip("_") or "x"


@lru_cache(maxsize=512)
def glob_regex(pattern: str) -> "re.Pattern[str]":
    """Glob with `**` (any depth), `*` (one segment), `?` and `[...]`, on POSIX-style paths."""
    out, i, n = [], 0, len(pattern)
    while i < n:
        c = pattern[i]
        if pattern.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
        elif pattern.startswith("**", i):
            out.append(".*")
            i += 2
        elif c == "*":
            out.append("[^/]*")
            i += 1
        elif c == "?":
            out.append("[^/]")
            i += 1
        elif c == "[":
            j = pattern.find("]", i + 1)
            if j == -1:
                out.append(re.escape(c))
                i += 1
            else:
                body = pattern[i + 1:j].replace("\\", "\\\\")
                if body.startswith("!"):
                    body = "^" + body[1:]
                out.append("[" + body + "]")
                i = j + 1
        else:
            out.append(re.escape(c))
            i += 1
    return re.compile("^" + "".join(out) + "$", re.IGNORECASE)


def glob_match(rel_posix: str, pattern: str) -> bool:
    return bool(glob_regex(pattern).match(rel_posix))


def has_glob(pattern: str) -> bool:
    return any(ch in pattern for ch in "*?[")


def static_prefix(pattern: str) -> str:
    """The directory part of a glob before its first wildcard segment."""
    parts = PurePosixPath(pattern).parts
    fixed = []
    for part in parts:
        if has_glob(part):
            break
        fixed.append(part)
    return "/".join(fixed)


def _is_link(path: Path) -> bool:
    if path.is_symlink():
        return True
    is_junction = getattr(path, "is_junction", None)
    if is_junction is not None:
        try:
            return bool(is_junction())
        except OSError:
            return True
    if os.name == "nt":  # Python < 3.12: inspect the reparse-point attribute directly
        try:
            return bool(os.lstat(path).st_file_attributes & 0x400)  # FILE_ATTRIBUTE_REPARSE_POINT
        except (OSError, AttributeError):
            return False
    return False


def contained_path(base: Path, rel_posix: str) -> Path:
    """`base/rel` as an absolute path, guaranteed inside `base` and free of links.

    Raises OUT_OF_ROOT when the relative path is absolute, contains `..`, crosses a symlink or
    junction, or resolves outside the base.
    """
    if not rel_posix or rel_posix.startswith(("/", "\\")) or "\\" in rel_posix or ":" in rel_posix:
        raise VaultAccessError(ErrorCode.OUT_OF_ROOT, "path is not a plain relative path")
    parts = PurePosixPath(rel_posix).parts
    if any(p in ("..", ".", "") for p in parts):
        raise VaultAccessError(ErrorCode.OUT_OF_ROOT, "path contains a traversal segment")
    root = base.resolve()
    current = root
    for part in parts:
        current = current / part
        if _is_link(current):
            raise VaultAccessError(ErrorCode.OUT_OF_ROOT, "path crosses a link")
    resolved = current.resolve()
    if not resolved.is_relative_to(root):
        raise VaultAccessError(ErrorCode.OUT_OF_ROOT, "path escapes its root")
    return resolved
