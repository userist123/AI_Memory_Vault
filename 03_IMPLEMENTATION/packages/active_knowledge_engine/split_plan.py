"""Dry-run split plan for a note too large for the agent context pack.

Step 1 of the plan recorded in 07_EVALUATION/active_knowledge_engine/DESIGN_DECISIONS.md: compute
a deterministic, section-aware split, prove it, and change nothing. The manifest says how the
parent body would be cut into child notes (character offsets, hashes, heading path) and proves:

- coverage: the children, concatenated in order, are exactly the parent body;
- fit: each child, built the way ``cognitive_read`` builds a one-note pack, passes the real
  ``ContextPackBuilder`` (token and byte budgets, protected content included);
- integrity: every cut falls on a line boundary, so no line (and no MUST/NEVER rule) is split.

A single line longer than the smallest child target makes the plan ``UNSPLITTABLE``; children
that cannot pass the pack for another reason make it ``DOES_NOT_FIT``, with the first failing
child named (both fail closed). A whitespace-only run never becomes a child.
Creating, attesting and promoting the children is the owner's step; nothing here writes a note.
The manifest carries offsets and hashes, never note content.
"""
from __future__ import annotations

import hashlib
from typing import Any, Dict, List, Optional, Tuple

from memory_controller.authorizer import Principal

SCHEMA = "ake.split_plan/1"
#: First greedy target for a child, in characters (the pack's token limit is about 9 KB at
#: 3 chars/token). Each child is then checked against the real pack builder; a child that does
#: not fit makes the planner retry with a smaller target.
INITIAL_TARGET_CHARS = 6000
MIN_TARGET_CHARS = 500
NL = chr(10)


def _sha(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _lines(body: str) -> List[Tuple[int, int, str]]:
    """(start, end, text) for every line, end including its newline, covering the whole body."""
    out = []
    pos = 0
    for raw in body.splitlines(keepends=True):
        out.append((pos, pos + len(raw), raw))
        pos += len(raw)
    return out


def _heading(text: str) -> Optional[Tuple[int, str]]:
    stripped = text.rstrip(NL)
    if not stripped.startswith("#"):
        return None
    level = len(stripped) - len(stripped.lstrip("#"))
    title = stripped[level:].strip()
    return (level, title) if title and level <= 6 else None


def _blocks(lines: List[Tuple[int, int, str]]) -> List[Tuple[int, int]]:
    """Line-index ranges [a, b) of sections: each heading starts a new block."""
    starts = [0] + [i for i, (_, _, t) in enumerate(lines) if i and _heading(t)]
    return [(a, b) for a, b in zip(starts, starts[1:] + [len(lines)]) if a < b]


def _fits(controller, parent: Dict[str, Any], piece: str, child_id: str) -> bool:
    child = dict(parent)
    child["id"] = child_id
    child["content"] = piece
    if not isinstance(child.get("provenance"), dict):
        child["provenance"] = {"source_type": "derived", "source_ref": str(parent.get("id"))}
    try:
        pack = controller.pack_builder.build(
            request_id="ake_split_plan", agent_id=Principal.AI_AGENT.value, budget={},
            results=[child], disclosure_level="full", allow_unverified=True,
        )
    except Exception:  # noqa: BLE001 - any refusal means "does not fit"
        return False
    return bool(pack.get("results"))


def _pack_greedy(lines, blocks, target: int) -> Optional[List[Tuple[int, int]]]:
    """Greedy packing of whole lines into children of at most `target` chars.

    Blocks (sections) are kept together when they fit; a block too large is packed line by line.
    Returns line-index ranges, or None when a single line exceeds the target.
    """
    children: List[Tuple[int, int]] = []
    cur_start, cur_len = None, 0

    def blank(a, b):
        return all(not lines[i][2].strip() for i in range(a, b))

    def flush(end):
        # A whitespace-only run is never a child (the pack rejects it): it is carried into the
        # next child, or appended to the previous one at the very end.
        nonlocal cur_start, cur_len
        if cur_start is not None and cur_start < end:
            if blank(cur_start, end):
                return
            children.append((cur_start, end))
        cur_start, cur_len = None, 0

    for a, b in blocks:
        block_len = lines[b - 1][1] - lines[a][0]
        if block_len <= target:
            if cur_start is not None and cur_len + block_len > target:
                flush(a)
            if cur_start is None:
                cur_start = a
            cur_len += block_len
            continue
        flush(a)
        for i in range(a, b):
            ln = lines[i][1] - lines[i][0]
            if ln > target:
                return None
            if cur_start is not None and cur_len + ln > target:
                flush(i)
            if cur_start is None:
                cur_start = i
            cur_len += ln
        flush(b)
    flush(len(lines))
    if cur_start is not None and cur_start < len(lines):  # trailing whitespace-only run
        if children:
            children[-1] = (children[-1][0], len(lines))
        else:
            return []
    return children


def _heading_paths(lines) -> List[List[str]]:
    """Heading stack in effect at the start of each line."""
    stack: List[Tuple[int, str]] = []
    paths = []
    for _, _, text in lines:
        h = _heading(text)
        if h:
            level, title = h
            stack = [s for s in stack if s[0] < level] + [(level, title)]
        paths.append([t for _, t in stack])
    return paths


def plan_split(controller, note_id: str) -> Dict[str, Any]:
    parent = controller.storage.get(note_id)
    if not parent:
        raise ValueError(f"note not found: {note_id}")
    body = str(parent.get("content") or "")
    if not body:
        raise ValueError(f"note has no body: {note_id}")
    lines = _lines(body)
    blocks = _blocks(lines)
    paths = _heading_paths(lines)
    manifest: Dict[str, Any] = {
        "schema": SCHEMA,
        "mode": "DRY_RUN",
        "parent_id": note_id,
        "parent_lifecycle": str(parent.get("lifecycle")),
        "parent_verification": str(parent.get("verification")),
        "parent_content_sha256": _sha(body),
        "parent_chars": len(body),
        "parent_lines": len(lines),
        "children": [],
        "notice": "Dry run: nothing was created or changed. Creating, attesting and promoting "
                  "the children is an owner action; each child must be re-hashed against "
                  "content_sha256 before attestation.",
    }
    target = INITIAL_TARGET_CHARS
    first_failure = None
    while target >= MIN_TARGET_CHARS:
        ranges = _pack_greedy(lines, blocks, target)
        if ranges is None:
            break  # a single line exceeds the target: smaller targets cannot help
        children = []
        all_fit = True
        for index, (a, b) in enumerate(ranges):
            start, end = lines[a][0], lines[b - 1][1]
            piece = body[start:end]
            child_id = f"{note_id}--part-{index + 1:03d}"
            fits = _fits(controller, parent, piece, child_id)
            all_fit &= fits
            children.append({
                "index": index,
                "child_id_proposed": child_id,
                "char_start": start,
                "char_end": end,
                "line_start": a + 1,
                "line_end": b,
                "chars": end - start,
                "content_sha256": _sha(piece),
                "heading_path": paths[a],
                "fits_pack": fits,
            })
        if not all_fit:
            bad = next(ch for ch in children if not ch["fits_pack"])
            first_failure = {"index": bad["index"], "chars": bad["chars"], "line_start": bad["line_start"],
                             "line_end": bad["line_end"], "target_chars": target}
        if all_fit:
            rebuilt = "".join(body[c["char_start"]:c["char_end"]] for c in children)
            manifest.update(
                status="PLANNED",
                target_chars=target,
                children=children,
                coverage={"concatenation_sha256": _sha(rebuilt), "equals_parent": rebuilt == body},
            )
            if rebuilt != body:  # defensive: never report a plan that does not tile the parent
                manifest.update(status="COVERAGE_FAILED", children=[])
            return manifest
        target //= 2
    if ranges is None:
        longest = max(range(len(lines)), key=lambda i: lines[i][1] - lines[i][0])
        manifest.update(
            status="UNSPLITTABLE",
            reason="a single line is longer than the smallest child target; splitting would cut it",
            blocking_line={"line_number": longest + 1, "chars": lines[longest][1] - lines[longest][0]},
            first_failing_child=first_failure,
        )
    else:
        manifest.update(
            status="DOES_NOT_FIT",
            reason="children of whole lines were computed but at least one does not pass the context pack",
            first_failing_child=first_failure,
        )
    return manifest
