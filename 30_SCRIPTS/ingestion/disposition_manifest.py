"""The machine-readable manifest governing dispositions for every row in ontology slots.

Schema: ontology-row-disposition.v1
"""
from __future__ import annotations

import json
import pathlib
from dataclasses import dataclass
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Set, Tuple

DISPOSITION_SCHEMA_VERSION = "ontology-row-disposition.v1"

DISPOSITION_KEEP_PROMOTED = "KEEP_PROMOTED"
DISPOSITION_KEEP_PROPOSED = "KEEP_PROPOSED"
DISPOSITION_DELETE = "DELETE"
DISPOSITION_MERGE_INTO = "MERGE_INTO"
DISPOSITION_SPLIT = "SPLIT"
DISPOSITION_UNDECIDED = "UNDECIDED"

KNOWN_DISPOSITIONS = frozenset({
    DISPOSITION_KEEP_PROMOTED,
    DISPOSITION_KEEP_PROPOSED,
    DISPOSITION_DELETE,
    DISPOSITION_MERGE_INTO,
    DISPOSITION_SPLIT,
    DISPOSITION_UNDECIDED,
})


class DispositionError(ValueError):
    """The disposition manifest is malformed, invalid, or violates ontology invariants."""


@dataclass(frozen=True)
class RowDisposition:
    slot_file: str
    line: int
    concept: str
    status: str
    disposition: str
    reason: str
    merge_into: Optional[str] = None

    def validate(self) -> None:
        if not self.slot_file.strip():
            raise DispositionError("RowDisposition must specify slot_file")
        if self.line <= 0:
            raise DispositionError(f"{self.slot_file}:{self.line}: line must be positive integer")
        if not self.concept.strip():
            raise DispositionError(f"{self.slot_file}:{self.line}: concept cannot be empty")
        if self.disposition not in KNOWN_DISPOSITIONS:
            raise DispositionError(
                f"{self.slot_file}:{self.line} '{self.concept}': unknown disposition {self.disposition!r}. "
                f"Known: {sorted(KNOWN_DISPOSITIONS)}"
            )
        if not self.reason.strip():
            raise DispositionError(
                f"{self.slot_file}:{self.line} '{self.concept}': reason is mandatory on every row"
            )
        if self.status == "promoted" and self.disposition == DISPOSITION_DELETE:
            raise DispositionError(
                f"{self.slot_file}:{self.line} '{self.concept}': promoted rows cannot receive DELETE"
            )
        if self.disposition == DISPOSITION_MERGE_INTO and not (self.merge_into or "").strip():
            raise DispositionError(
                f"{self.slot_file}:{self.line} '{self.concept}': MERGE_INTO must specify merge_into target"
            )

    def to_dict(self) -> Dict[str, Any]:
        d: Dict[str, Any] = {
            "slot_file": self.slot_file,
            "line": self.line,
            "concept": self.concept,
            "status": self.status,
            "disposition": self.disposition,
            "reason": self.reason,
        }
        if self.merge_into:
            d["merge_into"] = self.merge_into
        return d


def _normalise(name: str) -> str:
    return " ".join(name.strip().strip("*").strip("`").strip().lower().split())


class DispositionManifest:
    """Loaded disposition manifest with indexing and validation methods."""

    def __init__(
        self,
        rows: Iterable[RowDisposition],
        *,
        source_disposition: str = "",
        source_verdicts: str = "",
        decided_by: str = "",
        decided_at: str = ""
    ) -> None:
        self.source_disposition = source_disposition
        self.source_verdicts = source_verdicts
        self.decided_by = decided_by
        self.decided_at = decided_at
        self.rows: List[RowDisposition] = []
        self._by_loc: Dict[Tuple[str, int], RowDisposition] = {}
        self._by_file_concept: Dict[Tuple[str, str], RowDisposition] = {}

        for row in rows:
            row.validate()
            self.rows.append(row)
            file_name = pathlib.Path(row.slot_file).name
            loc_key = (file_name, row.line)
            if loc_key in self._by_loc:
                raise DispositionError(
                    f"Duplicate entry for location {file_name}:{row.line}"
                )
            self._by_loc[loc_key] = row

            concept_key = (file_name, _normalise(row.concept))
            if concept_key in self._by_file_concept:
                raise DispositionError(
                    f"Duplicate entry for concept {concept_key} in {file_name}"
                )
            self._by_file_concept[concept_key] = row

    def __len__(self) -> int:
        return len(self.rows)

    def counts(self) -> Dict[str, int]:
        c: Dict[str, int] = {}
        for r in self.rows:
            c[r.disposition] = c.get(r.disposition, 0) + 1
        return c

    def get_by_loc(self, slot_file: str, line: int) -> Optional[RowDisposition]:
        return self._by_loc.get((pathlib.Path(slot_file).name, line))

    def get_by_concept(self, slot_file: str, concept: str) -> Optional[RowDisposition]:
        return self._by_file_concept.get((pathlib.Path(slot_file).name, _normalise(concept)))

    def delete_rows(self) -> List[RowDisposition]:
        return [r for r in self.rows if r.disposition == DISPOSITION_DELETE]

    def merge_rows(self) -> List[RowDisposition]:
        return [r for r in self.rows if r.disposition == DISPOSITION_MERGE_INTO]

    def to_dict(self) -> Dict[str, Any]:
        return {
            "schema_version": DISPOSITION_SCHEMA_VERSION,
            "source_disposition": self.source_disposition,
            "source_verdicts": self.source_verdicts,
            "decided_at": self.decided_at,
            "decided_by": self.decided_by,
            "rows": [r.to_dict() for r in self.rows],
        }

    def save(self, path: pathlib.Path | str) -> None:
        p = pathlib.Path(path)
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(json.dumps(self.to_dict(), indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    def validate_against_slots(self, slots_dir: pathlib.Path | str) -> None:
        """Verifies that all rows in manifest match disk bit-for-bit and all merge targets exist as promoted."""
        try:
            from .slot_rows import read_all, STATUS_PROMOTED
        except ImportError:
            from slot_rows import read_all, STATUS_PROMOTED

        disk_rows = read_all(slots_dir)
        if len(self.rows) != len(disk_rows):
            raise DispositionError(
                f"Manifest row count ({len(self.rows)}) does not match slot rows on disk ({len(disk_rows)})"
            )

        promoted_on_disk = {
            _normalise(r.concept): r for r in disk_rows if r.status == STATUS_PROMOTED
        }

        # Check each disk row has an exact anchor in manifest
        for dr in disk_rows:
            file_name = pathlib.Path(dr.slot_file).name
            loc_key = (file_name, dr.line_number)
            entry = self._by_loc.get(loc_key)
            if entry is None:
                raise DispositionError(
                    f"Slot row {file_name}:{dr.line_number} '{dr.concept}' missing from disposition manifest"
                )
            if _normalise(entry.concept) != _normalise(dr.concept):
                raise DispositionError(
                    f"Slot row {file_name}:{dr.line_number} concept mismatch: disk '{dr.concept}' vs manifest '{entry.concept}'"
                )
            if entry.status.lower() != dr.status.lower():
                raise DispositionError(
                    f"Slot row {file_name}:{dr.line_number} status mismatch: disk '{dr.status}' vs manifest '{entry.status}'"
                )

        # Check MERGE_INTO targets exist as promoted on disk
        for mr in self.merge_rows():
            target_norm = _normalise(mr.merge_into or "")
            if target_norm not in promoted_on_disk:
                raise DispositionError(
                    f"{mr.slot_file}:{mr.line} '{mr.concept}': MERGE_INTO target '{mr.merge_into}' "
                    "does not exist as a promoted concept on disk"
                )


def load_manifest(path: pathlib.Path | str) -> DispositionManifest:
    data = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    if not isinstance(data, Mapping):
        raise DispositionError("A disposition manifest must be a JSON object")
    if data.get("schema_version") != DISPOSITION_SCHEMA_VERSION:
        raise DispositionError(
            f"Unsupported schema version {data.get('schema_version')!r}; expected {DISPOSITION_SCHEMA_VERSION}"
        )
    rows_data = data.get("rows")
    if not isinstance(rows_data, list) or not rows_data:
        raise DispositionError("Manifest must contain a non-empty 'rows' list")

    return DispositionManifest(
        (
            RowDisposition(
                slot_file=str(r.get("slot_file", "")),
                line=int(r.get("line", 0)),
                concept=str(r.get("concept", "")),
                status=str(r.get("status", "")),
                disposition=str(r.get("disposition", "")).strip().upper(),
                reason=str(r.get("reason", "")),
                merge_into=r.get("merge_into"),
            )
            for r in rows_data
        ),
        source_disposition=str(data.get("source_disposition", "")),
        source_verdicts=str(data.get("source_verdicts", "")),
        decided_by=str(data.get("decided_by", "")),
        decided_at=str(data.get("decided_at", "")),
    )


__all__ = [
    "DISPOSITION_SCHEMA_VERSION",
    "DISPOSITION_KEEP_PROMOTED",
    "DISPOSITION_KEEP_PROPOSED",
    "DISPOSITION_DELETE",
    "DISPOSITION_MERGE_INTO",
    "DISPOSITION_SPLIT",
    "DISPOSITION_UNDECIDED",
    "KNOWN_DISPOSITIONS",
    "DispositionError",
    "RowDisposition",
    "DispositionManifest",
    "load_manifest",
]
