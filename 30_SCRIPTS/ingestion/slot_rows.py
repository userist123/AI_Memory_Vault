"""One place that knows what a slot row is, and refuses to ignore the rest.

## Why this exists

Ten rows sat in the ontology for five days carrying `status=unverified_source`
and no audit saw them. Every tool that read the slot files — the state-accuracy
test, the conflict checker, the purge catalogue, the ad-hoc scripts written
while investigating — filtered on `proposed` and `promoted`, because those were
the statuses anyone knew about.

A filter on a hardcoded pair does not report what it skipped. It reports a
smaller number and looks correct. The count came back 203 against a real 213,
consistently, from several independent tools, because they all shared the same
assumption rather than the same bug.

So the vocabulary lives here, once, and reading a row with a status outside it
raises. A new status is then a decision someone makes on purpose, in this file,
instead of a category that quietly exists for no one.

## The statuses

`proposed`           extracted and written to a slot, judged by nobody
`promoted`           has a REVIEW note and edges in the graph
`unverified_source`  extracted by a tool that was later replaced, and the
                     replacement could not reproduce it from the source text

The third is the one that hid. It is not a weaker `proposed` — it means the
provenance chain broke, and a row whose extractor no longer exists cannot be
re-derived or defended. Keeping it visible is the point.
"""
from __future__ import annotations

import pathlib
import re
from dataclasses import dataclass
from typing import Iterator, Optional, Sequence

STATUS_PROPOSED = "proposed"
STATUS_PROMOTED = "promoted"
STATUS_UNVERIFIED_SOURCE = "unverified_source"

#: Every status a slot row may carry. Adding one is a deliberate edit here,
#: which is the entire mechanism: a status nobody declared cannot appear in a
#: file and be silently skipped by everything that reads it.
KNOWN_STATUSES = frozenset({
    STATUS_PROPOSED,
    STATUS_PROMOTED,
    STATUS_UNVERIFIED_SOURCE,
})

#: Rows a tool should act on without a human decision. Deliberately not the
#: same as KNOWN_STATUSES: `unverified_source` is known and not actionable,
#: and collapsing the two is how it became invisible.
ACTIONABLE_STATUSES = frozenset({STATUS_PROPOSED})

SLOT_DIRECTORY = pathlib.Path("01_ARCHITECTURE/ontology/slots")

_SEPARATOR = re.compile(r"^\|[\s\-:|]+\|$")


class UnknownSlotStatus(ValueError):
    """A slot row carries a status no tool declared.

    Raised rather than skipped. The alternative is what happened: ten rows
    present on disk, absent from every count, for five days.
    """


@dataclass(frozen=True)
class SlotRow:
    slot_file: str
    line_number: int
    concept: str
    source_book: str
    confidence: str
    status: str
    date_added: str
    promoted_note_id: str
    evidence: str
    occurrences: Optional[int]

    @property
    def is_actionable(self) -> bool:
        return self.status in ACTIONABLE_STATUSES

    @property
    def has_note(self) -> bool:
        return bool(self.promoted_note_id.strip())


def _clean(cell: str) -> str:
    """Strip the markdown emphasis a row may have picked up.

    Concept names arrive as ``**`architecture`**`` in some documents and
    ``architecture`` in others. A comparison that misses the difference reports
    zero overlap between two lists of the same concepts, which is a mistake
    this repository has made twice.
    """
    return cell.strip().strip("*").strip("`").strip()


def parse_row(line: str, slot_file: str, line_number: int) -> Optional[SlotRow]:
    """One table row, or None when the line is not one.

    Raises `UnknownSlotStatus` for a row whose status is not declared above —
    the case this module exists for.
    """
    if not line.startswith("|") or _SEPARATOR.match(line.strip()):
        return None
    cells = [c.strip() for c in line.split("|")]
    #: `| a | b |` splits to ['', 'a', 'b', ''] — a data row needs at least the
    #: eight columns through occurrences.
    if len(cells) < 10:
        return None
    if cells[4].lower() in ("status", ""):
        return None

    status = cells[4].strip()
    if status not in KNOWN_STATUSES:
        raise UnknownSlotStatus(
            f"{slot_file}:{line_number} carries status {status!r}, which no "
            f"tool declares. Known: {sorted(KNOWN_STATUSES)}. Add it to "
            "KNOWN_STATUSES deliberately, or fix the row — do not let a "
            "status exist that every audit skips."
        )

    occurrences = cells[8].strip()
    return SlotRow(
        slot_file=slot_file,
        line_number=line_number,
        concept=_clean(cells[1]),
        source_book=cells[2].strip(),
        confidence=cells[3].strip(),
        status=status,
        date_added=cells[5].strip(),
        promoted_note_id=cells[6].strip(),
        evidence=cells[7].strip(),
        occurrences=int(occurrences) if occurrences.isdigit() else None,
    )


def read_slot_file(path: pathlib.Path) -> list[SlotRow]:
    rows: list[SlotRow] = []
    for number, line in enumerate(
        path.read_text(encoding="utf-8").splitlines(), start=1
    ):
        row = parse_row(line, path.name, number)
        if row is not None:
            rows.append(row)
    return rows


def read_all(slots_dir: pathlib.Path | str = SLOT_DIRECTORY) -> list[SlotRow]:
    """Every row in every slot file, with nothing filtered out.

    Filtering is the caller's job and should be visible at the call site. A
    helper that quietly returns only the interesting rows is the shape of the
    problem this replaces.
    """
    directory = pathlib.Path(slots_dir)
    rows: list[SlotRow] = []
    for path in sorted(directory.glob("*.md")):
        rows.extend(read_slot_file(path))
    return rows


def count_by_status(rows: Sequence[SlotRow]) -> dict[str, int]:
    """Counts for every status present, not for the ones a caller expected."""
    counts: dict[str, int] = {}
    for row in rows:
        counts[row.status] = counts.get(row.status, 0) + 1
    return counts


def iter_actionable(rows: Sequence[SlotRow]) -> Iterator[SlotRow]:
    return (row for row in rows if row.is_actionable)


__all__ = [
    "STATUS_PROPOSED",
    "STATUS_PROMOTED",
    "STATUS_UNVERIFIED_SOURCE",
    "KNOWN_STATUSES",
    "ACTIONABLE_STATUSES",
    "UnknownSlotStatus",
    "SlotRow",
    "parse_row",
    "read_slot_file",
    "read_all",
    "count_by_status",
    "iter_actionable",
]
