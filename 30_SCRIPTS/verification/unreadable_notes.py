"""Find markdown files the FileStorageEngine cannot read, and compare readings.

The engine indexes ``*.md`` under ``CANONICAL_FOLDERS`` (minus RAW_IMPORTS and
Obsidian paths) and silently skips, with a ``storage_error`` audit entry, every
file whose frontmatter ``memory.storage.serializer.deserialize`` rejects. This
module reproduces exactly that scan so the list of skipped files is a fact
that can be re-derived, not a memory of one run.

Kinds:
    no_frontmatter          the file does not open with a ``---`` line
    unclosed_frontmatter    it opens with ``---`` but the block never closes
    yaml_error              it opens and closes, but the YAML inside is invalid
    not_a_mapping           the YAML parses but is not a mapping

Usage:
    python 30_SCRIPTS/verification/unreadable_notes.py [--out FILE.json]
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

import yaml  # noqa: E402

from memory.storage.serializer import deserialize  # noqa: E402

_SKIP_MARKERS = ("RAW_IMPORTS", "Obsidian")


def engine_roots() -> tuple:
    """The folders the engine scans, read from the engine itself."""
    from memory.storage.file_engine import CANONICAL_FOLDERS
    return tuple(CANONICAL_FOLDERS)


def iter_engine_files(vault_root: Path, roots: Optional[tuple] = None):
    """Same enumeration as FileStorageEngine._initialize_index: ``glob`` (which
    does not descend into dot-directories), not ``Path.rglob``."""
    roots = roots if roots is not None else engine_roots()
    for folder in roots:
        base = vault_root / folder
        if not base.exists():
            continue
        pattern = str(base)
        found = set(glob.glob(os.path.join(pattern, "*.md"))).union(
            glob.glob(os.path.join(pattern, "**", "*.md"), recursive=True)
        )
        for name in sorted(found):
            if any(marker in name for marker in _SKIP_MARKERS):
                continue
            yield Path(name)


def classify(text: str) -> Optional[str]:
    """None if the engine can read the text, else the kind of failure."""
    try:
        deserialize(text)
        return None
    except ValueError as exc:
        message = str(exc)
    if "must be a dictionary" in message:
        return "not_a_mapping"
    if "Missing opening" in message:
        return "unclosed_frontmatter" if text.startswith("---") else "no_frontmatter"
    return "yaml_error"


def find_unreadable(vault_root: Path, roots: Optional[tuple] = None) -> List[Dict[str, str]]:
    found = []
    for path in iter_engine_files(vault_root, roots):
        kind = classify(path.read_text(encoding="utf-8"))
        if kind:
            found.append({"path": path.relative_to(vault_root).as_posix(), "kind": kind})
    return found


# --------------------------------------------------------------------------
# Tolerant (no YAML parser) vs strict reading of a frontmatter-like block.
# --------------------------------------------------------------------------

_KEY = re.compile(r"^([A-Za-z_][\w-]*):(?:[ \t]+(.*?))?[ \t]*$")
_ITEM = re.compile(r"^[ \t]+-[ \t]+(.*?)[ \t]*$")


def _normalize(text: str) -> str:
    return text.replace("\r\n", "\n")


def tolerant_fields(text: str) -> Dict[str, Any]:
    """Read ``key: value`` and ``  - item`` lines of the leading block, line by
    line, without a YAML parser. The block starts after an opening ``---`` line
    and stops at a closing ``---`` or at the first markdown heading. Values are
    returned as the raw text after the colon / dash."""
    lines = _normalize(text).split("\n")
    if not lines or lines[0].strip() != "---":
        return {}
    fields: Dict[str, Any] = {}
    current: Optional[str] = None
    for line in lines[1:]:
        if line.strip() == "---" or line.startswith("#"):
            break
        item = _ITEM.match(line)
        if item and current is not None:
            if not isinstance(fields[current], list):
                fields[current] = []
            fields[current].append(item.group(1))
            continue
        key = _KEY.match(line)
        if key:
            current = key.group(1)
            value = key.group(2)
            fields[current] = [] if value in (None, "") else ("[]" if value == "[]" else value)
            if value == "[]":
                fields[current] = []
    return fields


def strict_fields(text: str) -> Dict[str, Any]:
    """The frontmatter as the engine's parser reads it, with every scalar kept
    as a string (``BaseLoader``) so it is comparable with the tolerant reading
    and no date/number coercion hides a difference."""
    match = re.match(r"^---\r?\n(.*?)\r?\n---\r?\n?(.*)", text, re.DOTALL)
    if not match:
        raise ValueError("no closed frontmatter block")
    return yaml.load(match.group(1), Loader=yaml.BaseLoader)


def unquote(value: str) -> str:
    """Undo YAML double/single quoting of one scalar written by a repair."""
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return yaml.load(value, Loader=yaml.BaseLoader)
    return value


def diff_readings(tolerant: Dict[str, Any], strict: Dict[str, Any]) -> List[str]:
    """Human-readable differences; empty when the two readings are identical."""
    problems = []
    for key in sorted(set(tolerant) | set(strict)):
        if key not in strict:
            problems.append(f"{key}: missing in strict reading")
            continue
        if key not in tolerant:
            problems.append(f"{key}: missing in tolerant reading")
            continue
        t, s = tolerant[key], strict[key]
        if isinstance(t, list):
            t = [unquote(x) for x in t]
        elif isinstance(t, str):
            t = unquote(t)
        if t != s:
            problems.append(f"{key}: tolerant={t!r} strict={s!r}")
    return problems


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--out", help="write the list as JSON to this path")
    args = parser.parse_args(argv)
    found = find_unreadable(REPO)
    counts: Dict[str, int] = {}
    for item in found:
        counts[item["kind"]] = counts.get(item["kind"], 0) + 1
    payload = {"roots": list(engine_roots()), "count": len(found), "by_kind": counts, "files": found}
    if args.out:
        Path(args.out).parent.mkdir(parents=True, exist_ok=True)
        Path(args.out).write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n",
                                  encoding="utf-8", newline="\n")
    print(f"UNREADABLE_COUNT={len(found)}")
    print(f"BY_KIND={json.dumps(counts, sort_keys=True)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
