"""Frontmatter, title, headings and line-exact sections of a Markdown document."""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Tuple

import yaml

from .canonical import anchor_for

_HEADING_RE = re.compile(r"^(#{1,6})[ \t]+(.+?)[ \t#]*$")
_FENCE_RE = re.compile(r"^\s*(```|~~~)")


@dataclass(frozen=True)
class Section:
    anchor: str
    title: str
    level: int
    line_start: int  # 1-based, the heading line
    line_end: int    # 1-based, inclusive


def parse_frontmatter(text: str) -> Tuple[Dict[str, Any], int, bool]:
    """(frontmatter dict, number of lines it occupies, parsed cleanly).

    `ok` is False when the text opens a frontmatter block that cannot be read completely
    (unterminated within the text given, invalid YAML, not a mapping). Callers that derive access
    labels from frontmatter must then FAIL CLOSED: an unreadable label is not an absent label.
    """
    if text.startswith("﻿"):
        text = text[1:]
    if not text.startswith("---"):
        return {}, 0, True
    lines = text.split("\n")
    if lines[0].strip() != "---":
        return {}, 0, True
    for idx in range(1, len(lines)):
        if lines[idx].strip() in ("---", "..."):
            try:
                data = yaml.safe_load("\n".join(lines[1:idx]))
            except yaml.YAMLError:
                return {}, idx + 1, False
            if data is None:
                return {}, idx + 1, True
            if not isinstance(data, dict):
                return {}, idx + 1, False
            return data, idx + 1, True
    return {}, 0, False


def split_frontmatter(text: str) -> Tuple[Dict[str, Any], int]:
    """(frontmatter dict, number of lines it occupies). Bad YAML yields ({}, lines)."""
    data, lines, _ = parse_frontmatter(text)
    return data, lines


def sections(text: str) -> List[Section]:
    lines = text.split("\n")
    _, fm_lines = split_frontmatter(text)
    found: List[Tuple[int, int, str]] = []
    in_fence = False
    for no, line in enumerate(lines, start=1):
        if no <= fm_lines:
            continue
        if _FENCE_RE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        m = _HEADING_RE.match(line)
        if m:
            found.append((no, len(m.group(1)), m.group(2).strip()))
    total = len(lines)
    out: List[Section] = []
    seen: Dict[str, int] = {}
    for i, (no, level, title) in enumerate(found):
        end = total
        for later_no, later_level, _ in found[i + 1:]:
            if later_level <= level:
                end = later_no - 1
                break
        anchor = anchor_for(title)
        if anchor in seen:
            seen[anchor] += 1
            anchor = f"{anchor}-{seen[anchor]}"
        else:
            seen[anchor] = 1
        out.append(Section(anchor, title, level, no, max(no, end)))
    return out


def title_of(text: str, frontmatter: Dict[str, Any], fallback: str) -> str:
    title = frontmatter.get("title")
    if isinstance(title, str) and title.strip():
        return title.strip()[:200]
    for sec in sections(text):
        if sec.level == 1:
            return sec.title[:200]
    for sec in sections(text):
        return sec.title[:200]
    return fallback


def aliases_of(frontmatter: Dict[str, Any]) -> List[str]:
    raw = frontmatter.get("aliases") or []
    if isinstance(raw, str):
        raw = [raw]
    return [str(a)[:120] for a in raw if isinstance(a, (str, int, float))][:20]


def find_section(text: str, anchor: str) -> Optional[Section]:
    for sec in sections(text):
        if sec.anchor == anchor:
            return sec
    return None
