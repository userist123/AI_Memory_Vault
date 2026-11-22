"""Per-project workspace with path confinement (spec 15, 19.6).

resolve() rejects: absolute paths, drive letters, UNC, '..' traversal, alternate data
streams, reserved device names, and any existing component of the path that is a symlink
or junction (reparse point) - so a link planted inside the workspace cannot escape it.
"""

from __future__ import annotations

import hashlib
import os
import re
import stat
import threading
import time
from pathlib import Path, PurePosixPath

from .errors import WorkspaceViolation

_RESERVED = re.compile(r"^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\..*)?$", re.I)
MAX_WRITE_BYTES = 20 * 1024 * 1024


def _is_link(p: Path) -> bool:
    try:
        st = os.lstat(p)
    except FileNotFoundError:
        return False
    if stat.S_ISLNK(st.st_mode):
        return True
    attrs = getattr(st, "st_file_attributes", 0)
    return bool(attrs & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400))


class Workspace:
    def __init__(self, root: Path, *, create: bool = True) -> None:
        if create:
            root.mkdir(parents=True, exist_ok=True)
        elif not root.is_dir():
            raise WorkspaceViolation("workspace does not exist")
        if _is_link(root):
            raise WorkspaceViolation("workspace root must not be a link")
        self.root = root.resolve(strict=True)

    def resolve(self, rel: str) -> Path:
        if not isinstance(rel, str) or not rel or len(rel) > 400 or any(ord(ch) < 0x20 for ch in rel):
            raise WorkspaceViolation("invalid path")
        norm = rel.replace("\\", "/")
        if norm.startswith("/") or re.match(r"^[a-zA-Z]:", norm) or norm.startswith("//"):
            raise WorkspaceViolation("absolute paths are not allowed", path=rel)
        parts = PurePosixPath(norm).parts
        for part in parts:
            if part in ("..", ".") or ":" in part or _RESERVED.match(part) or part.endswith((" ", ".")):
                raise WorkspaceViolation("path component not allowed", path=rel, component=part)
        cur = self.root
        for part in parts:
            cur = cur / part
            if _is_link(cur):
                raise WorkspaceViolation("links inside the workspace are not followed", path=rel)
        final = cur.resolve(strict=False)
        if final != self.root and self.root not in final.parents:
            raise WorkspaceViolation("path escapes workspace", path=rel)
        return final

    def write_bytes(self, rel: str, data: bytes) -> Path:
        if len(data) > MAX_WRITE_BYTES:
            raise WorkspaceViolation("file too large", size=len(data))
        p = self.resolve(rel)
        p.parent.mkdir(parents=True, exist_ok=True)
        tmp = p.with_name(f"{p.name}.{os.getpid()}.{threading.get_ident()}.tmp")
        tmp.write_bytes(data)
        # Windows: os.replace fails with PermissionError while another process (browser, preview
        # server) holds the target open for reading; retry briefly instead of failing the revision.
        for attempt in range(40):
            try:
                os.replace(tmp, p)
                return p
            except PermissionError:
                if attempt == 39:
                    tmp.unlink(missing_ok=True)
                    raise
                time.sleep(0.025)
        return p

    def write_text(self, rel: str, text: str) -> Path:
        return self.write_bytes(rel, text.encode("utf-8"))

    def read_bytes(self, rel: str, max_bytes: int = MAX_WRITE_BYTES) -> bytes:
        p = self.resolve(rel)
        size = p.stat().st_size  # check size BEFORE reading
        if size > max_bytes:
            raise WorkspaceViolation("file too large to read", size=size)
        return p.read_bytes()

    def iter_files(self, rel_dir: str) -> list[str]:
        """Regular files under rel_dir. Links (symlinks, junctions) are neither followed nor listed."""
        start = self.resolve(rel_dir)
        out: list[str] = []
        if not start.is_dir():
            return out
        for dirpath, dirnames, filenames in os.walk(start, followlinks=False):
            d = Path(dirpath)
            dirnames[:] = sorted(n for n in dirnames if not _is_link(d / n))
            for n in sorted(filenames):
                p = d / n
                if _is_link(p) or not p.is_file():
                    continue
                rel = p.relative_to(self.root).as_posix()
                self.resolve(rel)  # re-check every component
                out.append(rel)
        return out

    def relative(self, p: Path) -> str:
        return p.resolve().relative_to(self.root).as_posix()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()
