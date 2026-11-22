"""Dry-run split plan for a note too large for the agent context pack.

Step 1 of the plan recorded in 07_EVALUATION/active_knowledge_engine/DESIGN_DECISIONS.md: compute
a deterministic, section-aware split, prove it, and change nothing. The manifest says how the
parent body would be cut into child notes (character offsets, hashes, heading line numbers) and
proves:

- coverage: the children, concatenated in order, are exactly the parent body;
- integrity: every cut falls right after a "\\n" (only "\\n" is a line boundary; U+2028, U+0085 or
  a form feed never are), so no line, and no MUST/NEVER rule, is split;
- fit: each child, as the owner batch will create it (lifecycle ACTIVE, the parent's own
  provenance, never a fabricated one), passes the real ``ContextPackBuilder`` with
  ``cognitive_read``'s rule for an ACTIVE note (``allow_unverified=True``) **and comes back whole**:
  a child the pack shortens does not fit.

Fail-closed outcomes: ``BLANK_RUN_TOO_LARGE`` (a whitespace-only run larger than the pack; it
can only be served attached to text, which makes that child too large), ``NEEDS_PROVENANCE`` (the parent has no provenance, so the egress contract
would drop every child), ``UNSPLITTABLE`` (one line does not fit the pack even on its own), and
``DOES_NOT_FIT`` (children of whole lines cannot be made to fit; the first failing child is named).
Creating, attesting and promoting the children is the owner's step; nothing here writes a note.
The manifest carries offsets, hashes and line numbers, never note content.
"""
from __future__ import annotations

import hashlib
import uuid
from typing import Any, Dict, List, Optional, Tuple

from memory_controller.authorizer import Principal

SCHEMA = "ake.split_plan/2"
#: First greedy target for a child, in characters (the pack's token limit is about 9 KB at
#: 3 chars/token). Each child is then checked against the real pack builder; while one fails,
#: the target is halved, down to MIN_TARGET_CHARS.
INITIAL_TARGET_CHARS = 6000
MIN_TARGET_CHARS = 500
NL = chr(10)


def _sha(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _lines(body: str) -> List[Tuple[int, int, str]]:
    """(start, end, text) per line, split on "\\n" only, end including it; covers the whole body."""
    out = []
    pos = 0
    while pos < len(body):
        nxt = body.find(NL, pos)
        end = len(body) if nxt == -1 else nxt + 1
        out.append((pos, end, body[pos:end]))
        pos = end
    return out


def _heading(text: str) -> Optional[int]:
    stripped = text.rstrip(NL)
    if not stripped.startswith("#"):
        return None
    level = len(stripped) - len(stripped.lstrip("#"))
    return level if stripped[level:].strip() and level <= 6 else None


def _blocks(lines: List[Tuple[int, int, str]]) -> List[Tuple[int, int]]:
    """Line-index ranges [a, b) of sections: each heading starts a new block."""
    starts = [0] + [i for i, (_, _, t) in enumerate(lines) if i and _heading(t)]
    return [(a, b) for a, b in zip(starts, starts[1:] + [len(lines)]) if a < b]


def _blank(lines, a: int, b: int) -> bool:
    return all(not lines[i][2].strip() for i in range(a, b))


def _fits(controller, parent: Dict[str, Any], piece: str, child_id: str) -> bool:
    """The child, in its target state, passes the real pack and is served whole."""
    child = dict(parent, id=child_id, content=piece, lifecycle="ACTIVE")
    try:
        pack = controller.pack_builder.build(
            request_id="ake_split_plan", agent_id=Principal.AI_AGENT.value, budget={},
            results=[child], disclosure_level="full",
            allow_unverified=True,  # cognitive_read's rule for an ACTIVE note (memory/controller.py)
        )
    except Exception:  # noqa: BLE001 - any refusal means "does not fit"
        return False
    results = pack.get("results") or []
    return bool(results) and results[0].get("content") == piece


def _pack_greedy(lines, blocks, target: int) -> List[Tuple[int, int]]:
    """Greedy packing of whole lines into children of about `target` chars.

    A section is kept whole when it fits the target; a larger section is packed line by line, and
    a single line longer than the target becomes a child of its own (the pack decides whether it
    fits). Blank lines are ordinary lines here, so a cut may fall inside a blank run; afterwards a
    whitespace-only child is merged into its neighbour (the pack rejects an empty child).
    """
    children: List[Tuple[int, int]] = []
    cur_start, cur_len = None, 0
    for a, b in blocks:
        block_len = lines[b - 1][1] - lines[a][0]
        units = [(a, b, block_len)] if block_len <= target else             [(i, i + 1, lines[i][1] - lines[i][0]) for i in range(a, b)]
        for ua, _ub, ul in units:
            if cur_start is not None and cur_len + ul > target:
                children.append((cur_start, ua))
                cur_start, cur_len = None, 0
            if cur_start is None:
                cur_start = ua
            cur_len += ul
    if cur_start is not None:
        children.append((cur_start, len(lines)))
    merged: List[Tuple[int, int]] = []
    pending: Optional[int] = None  # start of a leading whitespace-only run
    for a, b in children:
        if _blank(lines, a, b):
            if merged:
                merged[-1] = (merged[-1][0], b)
            elif pending is None:
                pending = a
            continue
        merged.append((pending if pending is not None else a, b))
        pending = None
    return merged


def _longest_blank_run(lines, a: int, b: int) -> Optional[Tuple[int, int, int]]:
    """(first_line_index, last_line_index_exclusive, chars) of the longest whitespace-only run."""
    best, start = None, None
    for i in range(a, b + 1):
        if i < b and not lines[i][2].strip():
            start = i if start is None else start
            continue
        if start is not None:
            chars = lines[i - 1][1] - lines[start][0]
            if best is None or chars > best[2]:
                best = (start, i, chars)
            start = None
    return best


def _heading_lines(lines) -> List[List[int]]:
    """1-based line numbers of the headings in effect at the start of each line."""
    stack: List[Tuple[int, int]] = []
    out = []
    for idx, (_, _, text) in enumerate(lines):
        level = _heading(text)
        if level:
            stack = [s for s in stack if s[0] < level] + [(level, idx + 1)]
        out.append([n for _, n in stack])
    return out


def plan_split(controller, note_id: str) -> Dict[str, Any]:
    parent = controller.storage.get(note_id)
    if not parent:
        raise ValueError(f"note not found: {note_id}")
    body = str(parent.get("content") or "")
    if not body.strip():
        raise ValueError(f"note has no non-blank body: {note_id}")
    lines = _lines(body)
    blocks = _blocks(lines)
    heading_lines = _heading_lines(lines)
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
        "fit_assumption": "each child is checked as the owner batch creates it: lifecycle ACTIVE, the "
                          "parent's provenance, cognitive_read's ACTIVE rule (allow_unverified=True); "
                          "it fits only if the pack serves it whole",
        "notice": "Dry run: nothing was created or changed. Creating, attesting and promoting the "
                  "children is an owner action; re-hash each child against content_sha256 and pass "
                  "it as expected_content_sha256 when attesting.",
    }
    if not isinstance(parent.get("provenance"), dict) and parent.get("source_ref") is None:
        manifest.update(status="NEEDS_PROVENANCE",
                        reason="the parent has no provenance; the egress contract would drop every child "
                               "and none is fabricated")
        return manifest

    target = INITIAL_TARGET_CHARS
    first_failure = None
    while target >= MIN_TARGET_CHARS:
        ranges = _pack_greedy(lines, blocks, target)
        children = []
        for index, (a, b) in enumerate(ranges):
            start, end = lines[a][0], lines[b - 1][1]
            piece = body[start:end]
            # deterministic UUID (the note schema requires one); bound to the parent body, so a
            # re-plan after the parent changes proposes different ids
            child_id = str(uuid.uuid5(uuid.NAMESPACE_URL, f"ake-split/{note_id}/{_sha(body)}/{index + 1}"))
            children.append({
                "index": index,
                "child_id_proposed": child_id,
                "char_start": start,
                "char_end": end,
                "line_start": a + 1,
                "line_end": b,
                "chars": end - start,
                "content_sha256": _sha(piece),
                "heading_lines": heading_lines[a],
                "fits_pack": _fits(controller, parent, piece, child_id),
            })
        bad = next((ch for ch in children if not ch["fits_pack"]), None)
        if bad is None:
            rebuilt = "".join(body[c["char_start"]:c["char_end"]] for c in children)
            if rebuilt != body:  # defensive: never report a plan that does not tile the parent
                manifest.update(status="COVERAGE_FAILED", children=[])
                return manifest
            manifest.update(status="PLANNED", target_chars=target, children=children,
                            coverage={"concatenation_sha256": _sha(rebuilt), "equals_parent": True})
            return manifest
        first_failure = {"index": bad["index"], "chars": bad["chars"], "line_start": bad["line_start"],
                         "line_end": bad["line_end"], "target_chars": target}
        run = _longest_blank_run(lines, bad["line_start"] - 1, bad["line_end"])
        if run is not None and run[2] > target:
            # a whitespace-only run larger than the target can only be served attached to text,
            # which makes that child too large at any target: the owner trims the run
            manifest.update(
                status="BLANK_RUN_TOO_LARGE",
                reason="a run of blank lines is larger than the context pack allows; trim it, then re-plan",
                blank_run={"line_start": run[0] + 1, "line_end": run[1], "chars": run[2]},
                first_failing_child=first_failure,
            )
            return manifest
        if _single_content_line(lines, bad):
            # the failing child is one line (plus blank lines): no smaller target can help
            content_line = _longest_line(lines, bad["line_start"] - 1, bad["line_end"])
            manifest.update(
                status="UNSPLITTABLE",
                reason="one line does not fit the context pack even on its own; splitting would cut it",
                blocking_line={"line_number": content_line + 1,
                               "chars": lines[content_line][1] - lines[content_line][0]},
                first_failing_child=first_failure,
            )
            return manifest
        target //= 2
    manifest.update(
        status="DOES_NOT_FIT",
        reason="children of whole lines were computed but at least one does not pass the context pack whole",
        first_failing_child=first_failure,
    )
    return manifest


def _single_content_line(lines, child: Dict[str, Any]) -> bool:
    a, b = child["line_start"] - 1, child["line_end"]
    return sum(1 for i in range(a, b) if lines[i][2].strip()) <= 1


def _longest_line(lines, a: int, b: int) -> int:
    """The longest non-blank line (a blank line is never named as the blocking line)."""
    candidates = [i for i in range(a, b) if lines[i][2].strip()] or list(range(a, b))
    return max(candidates, key=lambda i: lines[i][1] - lines[i][0])
