"""Freeze and verify benchmark artefacts over canonical bytes.

CONTRACT: canonical bytes = the file read as UTF-8 with every CRLF and lone CR
normalised to LF. Both freezing and verification use exactly this
representation, so a checkout that rewrites line endings cannot break the
guard, while any change to an actual character still does.

v1 hashed raw bytes. On a Windows checkout git delivered CRLF, the hash
diverged from the recorded value, and the runner refused to start — a
platform-dependent false alarm that would have passed on Linux CI. It stopped
anyone from discovering that v1's gold references resolved to nothing.
"""
from __future__ import annotations

import hashlib
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ARTEFACTS = ("heldout.json", "dev.json")


def canonical_bytes(path: Path) -> bytes:
    return path.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")


def digest(path: Path) -> str:
    return hashlib.sha256(canonical_bytes(path)).hexdigest()


def hash_path(path: Path) -> Path:
    return path.with_suffix(path.suffix + ".sha256")


def freeze() -> int:
    for name in ARTEFACTS:
        p = HERE / name
        hash_path(p).write_text(digest(p) + "\n", encoding="utf-8", newline="\n")
        print(f"frozen {name} {digest(p)}")
    return 0


def verify() -> int:
    failed = False
    for name in ARTEFACTS:
        p = HERE / name
        recorded = hash_path(p).read_text(encoding="utf-8").strip()
        actual = digest(p)
        if recorded != actual:
            print(f"FROZEN_SET_HASH_MISMATCH:{name}:expected={recorded}:actual={actual}")
            failed = True
        else:
            print(f"ok {name}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(freeze() if "--freeze" in sys.argv else verify())
