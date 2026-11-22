"""One link test shared by vault_access and the storage/index scans.

True for a symlink or an NTFS junction. On Python < 3.12 (no ``Path.is_junction`` /
``os.path.isjunction``) the Windows reparse-point attribute is inspected directly.
"""
from __future__ import annotations

import os
from pathlib import Path


def is_link(path: Path | str) -> bool:
    path = Path(path)
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
