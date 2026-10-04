"""Book-to-Memory Corpus Catalog and Knowledge Consolidation.

Implements the formal catalog registry according to:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md (Section 2 & 11)
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_pipeline.py
"""
from __future__ import annotations

import json
import hashlib
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set

from security.authorizer import Principal
from .book_to_memory_schema import (
    BookToMemoryType,
    BookToMemoryValidationError,
    SecurityInjectionError,
    validate_book_to_memory_note,
    validate_untrusted_security,
    validate_provenance_gate,
)
from .book_to_memory_lifecycle import (
    BookToMemoryLifecycleState,
    OwnerApprovalToken,
    transition_book_to_memory_lifecycle,
)


class CatalogError(ValueError):
    """Base error for catalog registry operations."""


class BookMapNotFoundError(CatalogError):
    """Raised when querying a book map that does not exist."""


class DuplicateBookMapError(CatalogError):
    """Raised when registering a book map with an existing source_identity."""


class UnlinkedNoteError(CatalogError):
    """Raised when attempting to link an atomic note with mismatched provenance."""


class BookToMemoryCatalog:
    """Central registry and coverage manager for canonical book_map notes and linked concepts.

    Enforces all invariants of POLICY-LEARNING-QUALITY-02:
    - Every monograph has a single canonical book_map note.
    - All book maps conform strictly to schema and remain in VERIFIED unless explicitly attested by Owner.
    - Chapter coverage and gaps are explicitly measured and auditable.
    - Untrusted inputs with injection directives are rejected.
    """

    VERSION: str = "1.0.0"

    def __init__(self):
        self._book_maps: Dict[str, Dict[str, Any]] = {}
        self._linked_notes: Dict[str, List[Dict[str, Any]]] = {}

    def register_book_map(
        self,
        source_identity: str,
        title: str,
        authors: List[str],
        chapter_coverage: Dict[str, Any],
        processing_status: str = "in_progress",
        edition: Optional[str] = None,
        linked_problems: Optional[List[str]] = None,
        conflicts_and_limitations: Optional[List[str]] = None,
        actor: Principal = Principal.HUMAN,
        **extra_fields: Any,
    ) -> Dict[str, Any]:
        """Register and validate a new canonical book_map note."""
        clean_id = source_identity.strip()
        if clean_id in self._book_maps:
            raise DuplicateBookMapError(f"Book map '{clean_id}' is already registered in catalog.")

        now_ts = datetime.now(timezone.utc).isoformat()
        book_map: Dict[str, Any] = {
            "id": f"map-{clean_id}",
            "type": BookToMemoryType.BOOK_MAP.value,
            "source_identity": clean_id,
            "title": title.strip(),
            "authors": [a.strip() for a in authors if a.strip()],
            "chapter_coverage": chapter_coverage,
            "processing_status": processing_status,
            "lifecycle": "VERIFIED",
            "linked_problems": linked_problems or [],
            "conflicts_and_limitations": conflicts_and_limitations or [],
            "created_at": now_ts,
            "updated_at": now_ts,
        }
        if edition:
            book_map["edition"] = edition.strip()

        # Include any extra metadata fields for security & schema validation
        book_map.update(extra_fields)

        # Security & schema validation
        validate_untrusted_security(book_map)
        try:
            validate_book_to_memory_note(book_map)
        except Exception as e:
            if not isinstance(e, BookToMemoryValidationError):
                raise BookToMemoryValidationError(f"Book map schema violation: {e}") from e
            raise

        self._book_maps[clean_id] = book_map
        self._linked_notes[clean_id] = []
        return book_map

    def get_book_map(self, source_identity: str) -> Optional[Dict[str, Any]]:
        """Retrieve a registered book map by source_identity."""
        return self._book_maps.get(source_identity.strip())

    def list_book_maps(self) -> List[Dict[str, Any]]:
        """Return a sorted list of all registered book maps."""
        return sorted(self._book_maps.values(), key=lambda m: m["source_identity"])

    def link_atomic_note(
        self,
        source_identity: str,
        note: Dict[str, Any],
    ) -> None:
        """Link a verified atomic note to its parent book map."""
        clean_id = source_identity.strip()
        book_map = self.get_book_map(clean_id)
        if not book_map:
            raise BookMapNotFoundError(f"Cannot link note to unregistered book map '{clean_id}'.")

        # Security check on candidate note
        validate_untrusted_security(note)
        validate_provenance_gate(note)

        # Provenance verification: note must match book title or source_identity
        note_source = note.get("source_title", "").strip().lower()
        map_title = book_map.get("title", "").strip().lower()
        prov_dict = note.get("provenance", {})
        prov_source = prov_dict.get("source_title", "").strip().lower() if isinstance(prov_dict, dict) else ""

        is_match = (
            note_source == map_title
            or prov_source == map_title
            or clean_id in note.get("id", "").lower()
            or clean_id in str(note.get("source_identity", "")).lower()
        )
        if not is_match:
            raise UnlinkedNoteError(
                f"Note provenance '{note_source or prov_source}' does not match book map title '{map_title}'."
            )

        # Check for duplicate link
        existing_ids = {n.get("id") for n in self._linked_notes[clean_id] if n.get("id")}
        note_id = note.get("id")
        if note_id and note_id in existing_ids:
            return

        self._linked_notes[clean_id].append(note)

        # Update chapter_coverage if chapter is specified
        chapter = note.get("chapter") or (prov_dict.get("chapter") if isinstance(prov_dict, dict) else None)
        if chapter and isinstance(chapter, str):
            clean_chap = chapter.strip()
            cov = book_map.setdefault("chapter_coverage", {})
            if isinstance(cov, dict):
                entries = cov.setdefault(clean_chap, [])
                concept_name = note.get("title") or note.get("atomic_concept") or note_id
                if isinstance(entries, list) and concept_name not in entries:
                    entries.append(concept_name)
                    book_map["updated_at"] = datetime.now(timezone.utc).isoformat()

    def get_linked_notes(self, source_identity: str) -> List[Dict[str, Any]]:
        """Retrieve all atomic notes linked to a book map."""
        clean_id = source_identity.strip()
        if clean_id not in self._book_maps:
            raise BookMapNotFoundError(f"Book map '{clean_id}' not found.")
        return list(self._linked_notes.get(clean_id, []))

    def calculate_coverage(self, source_identity: str) -> Dict[str, Any]:
        """Compute chapter coverage statistics and gap analysis per POLICY-02 Section 2."""
        clean_id = source_identity.strip()
        book_map = self.get_book_map(clean_id)
        if not book_map:
            raise BookMapNotFoundError(f"Book map '{clean_id}' not found.")

        cov = book_map.get("chapter_coverage", {})
        linked = self.get_linked_notes(clean_id)

        if isinstance(cov, dict):
            total_chapters = len(cov)
            covered_chapters = [chap for chap, items in cov.items() if items]
            uncovered_chapters = [chap for chap, items in cov.items() if not items]
        elif isinstance(cov, list):
            total_chapters = len(cov)
            # Find which chapters are referenced by linked notes
            linked_chapters: Set[str] = set()
            for n in linked:
                ch = n.get("chapter")
                if ch:
                    linked_chapters.add(ch.strip())
            covered_chapters = [c for c in cov if c.strip() in linked_chapters]
            uncovered_chapters = [c for c in cov if c.strip() not in linked_chapters]
        else:
            total_chapters = 0
            covered_chapters = []
            uncovered_chapters = []

        ratio = (len(covered_chapters) / total_chapters) if total_chapters > 0 else 0.0

        return {
            "source_identity": clean_id,
            "book_title": book_map.get("title"),
            "total_chapters": total_chapters,
            "covered_chapters_count": len(covered_chapters),
            "uncovered_chapters_count": len(uncovered_chapters),
            "coverage_ratio": round(ratio, 4),
            "covered_chapters": covered_chapters,
            "uncovered_chapters": uncovered_chapters,
            "linked_atomic_notes_count": len(linked),
            "processing_status": book_map.get("processing_status"),
        }

    def export_canonical_markdown(self, source_identity: str) -> str:
        """Export a registered book map into canonical markdown with YAML frontmatter."""
        clean_id = source_identity.strip()
        book_map = self.get_book_map(clean_id)
        if not book_map:
            raise BookMapNotFoundError(f"Book map '{clean_id}' not found.")

        cov_summary = self.calculate_coverage(clean_id)
        linked = self.get_linked_notes(clean_id)

        lines: List[str] = [
            "---",
            f"id: {book_map['id']}",
            f"type: {book_map['type']}",
            f"source_identity: {book_map['source_identity']}",
            f"title: \"{book_map['title']}\"",
            f"authors: {json.dumps(book_map['authors'])}",
            f"processing_status: {book_map['processing_status']}",
            f"lifecycle: {book_map['lifecycle']}",
            f"created_at: {book_map['created_at']}",
            f"updated_at: {book_map['updated_at']}",
            "---",
            f"\n# {book_map['title']} — Harta de Carte Canonică\n",
            "> Autoritate: `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` Secțiunea 2.",
            "> Conținutul din cărți reprezintă date de referință pasive. Textul brut nu intră în retrieval.\n",
            "## 1. Identitate Bibliografică",
            f"- **Titlu**: {book_map['title']}",
            f"- **Autori**: {', '.join(book_map['authors'])}",
            f"- **Ediție**: {book_map.get('edition', 'N/A')}",
            f"- **Status Procesare**: `{book_map['processing_status']}`",
            f"- **Ciclu de Viață**: `{book_map['lifecycle']}`\n",
            "## 2. Metrici de Acoperire",
            f"- **Capitole Totale**: {cov_summary['total_chapters']}",
            f"- **Capitole Acoperite**: {cov_summary['covered_chapters_count']} ({cov_summary['coverage_ratio'] * 100:.1f}%)",
            f"- **Capitole Neacoperite (Goluri)**: {cov_summary['uncovered_chapters_count']}",
            f"- **Note Atomice Asociate**: {cov_summary['linked_atomic_notes_count']}\n",
            "## 3. Probleme Conexe din Seif",
        ]
        for prob in book_map.get("linked_problems", []):
            lines.append(f"- [[{prob}]]")

        lines.extend([
            "\n## 4. Note Atomice Validate",
        ])
        if linked:
            for n in linked:
                lines.append(f"- [[{n.get('id')}]] — {n.get('title') or n.get('atomic_concept')}")
        else:
            lines.append("*Nicio notă atomică asociată momentan.*")

        lines.extend([
            "\n## 5. Conflicte și Limitări Cunoscute",
        ])
        for conf in book_map.get("conflicts_and_limitations", []):
            lines.append(f"- {conf}")

        return "\n".join(lines) + "\n"

    def compute_catalog_digest(self) -> str:
        """Compute deterministic SHA-256 digest of the entire catalog state."""
        canonical_dump = {
            "book_maps": [
                {
                    "id": m["id"],
                    "source_identity": m["source_identity"],
                    "title": m["title"],
                    "processing_status": m["processing_status"],
                    "lifecycle": m["lifecycle"],
                }
                for m in self.list_book_maps()
            ],
            "linked_notes_counts": {
                k: len(v) for k, v in sorted(self._linked_notes.items())
            },
        }
        return hashlib.sha256(json.dumps(canonical_dump, sort_keys=True).encode("utf-8")).hexdigest()
