"""Frontmatter, title, headings and line-exact sections of a Markdown document."""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Tuple

import yaml

from .canonical import anchor_for

# LibYAML's C loader is ~10x faster than the pure-Python SafeLoader and builds the same safe data
# model. It is only used when the interpreter's PyYAML was built with it (the PyPI wheels are);
# otherwise the pure-Python SafeLoader is the fallback, so behaviour never depends on the build.
_YAML_LOADER = getattr(yaml, "CSafeLoader", yaml.SafeLoader)

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
                data = yaml.load("\n".join(lines[1:idx]), Loader=_YAML_LOADER)  # noqa: S506 - safe loader
            except (yaml.YAMLError, ValueError, TypeError, OverflowError):
                # ValueError: well-formed YAML with an impossible scalar (`date: 2026-13-45`).
                # One such note must make ITS label unreadable (fail closed), not the whole table.
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


Heading = Tuple[int, int, str]  # (1-based line number, level, title)


def heading_scan(text: str, fm_lines: Optional[int] = None) -> List[Heading]:
    """Every ATX heading outside code fences and frontmatter. `fm_lines` is the number of
    frontmatter lines when the caller has already parsed the text (it then is not parsed again);
    None parses it here. Cheap: no anchors, no ranges."""
    if fm_lines is None:
        _, fm_lines = split_frontmatter(text)
    found: List[Heading] = []
    in_fence = False
    for no, line in enumerate(text.split("\n"), start=1):
        if no <= fm_lines:
            continue
        # substring/first-char tests are the cheap pre-filters of the two regexes (same matches)
        if ("```" in line or "~~~" in line) and _FENCE_RE.match(line):
            in_fence = not in_fence
            continue
        if in_fence or line[:1] != "#":
            continue
        m = _HEADING_RE.match(line)
        if m:
            found.append((no, len(m.group(1)), m.group(2).strip()))
    return found


def sections(text: str, fm_lines: Optional[int] = None) -> List[Section]:
    """Headings with line-exact ranges (a section ends before the next heading of the same or a
    shallower level)."""
    found = heading_scan(text, fm_lines)
    total = text.count("\n") + 1
    # end of heading i = line before the next heading with level <= its own: one backward pass
    ends = [total] * len(found)
    stack: List[Tuple[int, int]] = []  # (level, line) of later headings, levels strictly increasing
    for i in range(len(found) - 1, -1, -1):
        no, level, _ = found[i]
        while stack and stack[-1][0] > level:
            stack.pop()
        if stack:
            ends[i] = stack[-1][1] - 1
        stack.append((level, no))
    out: List[Section] = []
    seen: Dict[str, int] = {}
    for (no, level, title), end in zip(found, ends):
        anchor = anchor_for(title)
        if anchor in seen:
            seen[anchor] += 1
            anchor = f"{anchor}-{seen[anchor]}"
        else:
            seen[anchor] = 1
        out.append(Section(anchor, title, level, no, max(no, end)))
    return out


def title_of(text: str, frontmatter: Dict[str, Any], fallback: str,
             headings: Optional[List[Heading]] = None) -> str:
    title = frontmatter.get("title")
    if isinstance(title, str) and title.strip():
        return title.strip()[:200]
    if headings is None:
        headings = heading_scan(text)
    for _, level, htitle in headings:
        if level == 1:
            return htitle[:200]
    for _, _, htitle in headings:
        return htitle[:200]
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
