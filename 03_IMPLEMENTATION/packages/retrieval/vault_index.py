"""VaultIndex — deterministic, read-only loading of vault "neurons" (canonical notes).

Front: P1.2 Semantic Synaptogenesis (owner: claude-code). Status: EXPERIMENTAL,
NOT wired into any runtime path.

The source of truth remains the Markdown/frontmatter. This module NEVER mutates
anything; it builds a reconstructible in-memory index consumed by
hybrid_retrieval, synapse_store, brain_pack and the 30_SCRIPTS/knowledge tools.

This is a deliberately separate, offline reading path. It does NOT go through
MemoryController and carries no authorization/lifecycle enforcement of its own —
for any production read path use MemoryController.search()/cognitive_read()
(see memory_controller/controller.py, cognitive_core/activation.py). Do not wire
this module into MemoryController.search(), cognitive_core/tool_router.py, or
cognitive_core/activation.py.
"""
from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional

try:
    import yaml
except ImportError:  # pragma: no cover
    yaml = None

FRONTMATTER_RE = re.compile(r"^---\n(.*?)\n---\n?", re.S)
WIKILINK_RE = re.compile(r"\[\[([^\]|#]+)")
UUID_RE = re.compile(r"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}")
ENTITY_RE = re.compile(
    r"\b(?:[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+"              # CamelCase
    r"|[A-Z]{2,}(?:[-_][A-Z0-9]+)*"                         # ACRONYM, TOKEN-1, BM25, RRF
    r"|[A-Z][0-9]*(?:[-_][A-Z0-9]+)+"                       # I-001, P0-015, SHA-256
    r"|[a-z_]+_[a-z_]+"                                     # snake_case
    r"|\d+\.\d+\.\d+)\b"                                    # three-part versions only
)
OBSIDIAN_TAIL_RE = re.compile(r"\n---\s*\n##\s*🔗[^\n]*\n(?:- \[\[[^\n]*\n?)*", re.S)

DEFAULT_ROOTS = ("01_ARCHITECTURE", "02_PRODUCT", "10_DOCUMENTATION", "00_GOVERNANCE")

# Purely navigational notes: MOCs, indexes, maps. Useful to a human, noise to an agent.
NAVIGATION_TYPES = {"moc", "index", "map"}

#: Path fragments whose notes are export residue rather than canonical memory.
#:
#: CLAUDE.md states that Obsidian is a projection over the canonical vault and
#: that a second canonical database must not be created inside it. The export
#: directory had become exactly that: 97 of 939 indexed notes (10.3%) lived
#: there, 41 of them without an `id:` at all, and several were byte-near copies
#: of canonical notes carrying a DIFFERENT id — so the same content was indexed
#: twice under two identities, splitting its edges and letting retrieval return
#: the same note twice against one context budget.
#:
#: Excluding them removes 133 of 411 graph edges. Those edges are not lost
#: signal; they were connectivity attributed to duplicates.
EXPORT_RESIDUE_MARKERS = (
    ("Obsidian", "Artifacts"),
)


def _is_export_residue(path: Path) -> bool:
    parts = set(path.parts)
    return any(all(m in parts for m in markers) for markers in EXPORT_RESIDUE_MARKERS)


@dataclass
class Note:
    id: str
    path: Path
    title: str
    body: str
    meta: Dict[str, Any] = field(default_factory=dict)

    @property
    def type(self) -> str:
        return str(self.meta.get("type") or "unknown").lower()

    @property
    def lifecycle(self) -> str:
        return str(self.meta.get("lifecycle") or "NONE").upper()

    @property
    def verification(self) -> str:
        return str(self.meta.get("verification") or "unverified").lower()

    @property
    def confidence(self) -> str:
        return str(self.meta.get("confidence") or "unknown").lower()

    @property
    def tags(self) -> List[str]:
        raw = self.meta.get("tags") or []
        if isinstance(raw, str):
            raw = [raw]
        return [str(t).lower() for t in raw]

    @property
    def category(self) -> str:
        return str(self.meta.get("category") or "").lower()

    @property
    def updated(self) -> Optional[datetime]:
        for key in ("updated", "created"):
            val = self.meta.get(key)
            if not val:
                continue
            try:
                return datetime.fromisoformat(str(val)[:19])
            except ValueError:
                continue
        return None

    @property
    def text(self) -> str:
        """Title + body, without the Obsidian navigation tail."""
        return f"{self.title}\n{self.body}"

    @property
    def content_hash(self) -> str:
        norm = re.sub(r"\s+", " ", self.body).strip().lower()
        return hashlib.sha256(norm.encode("utf-8")).hexdigest()[:16]

    @property
    def entities(self) -> List[str]:
        raw_ents = {e.lower() for e in ENTITY_RE.findall(self.text)}
        return sorted(raw_ents | set(self.tags))

    @property
    def is_raw(self) -> bool:
        if self.lifecycle == "RAW":
            return True
        p = str(self.path).replace("\\", "/").lower()
        return "06_inbox" in p or "/raw" in p or "raw/" in p

    @property
    def is_archived(self) -> bool:
        if self.lifecycle == "ARCHIVED":
            return True
        p = str(self.path).replace("\\", "/").lower()
        return "05_archive" in p or "/archive" in p or "archive/" in p

    @property
    def is_experimental(self) -> bool:
        if "experimental" in self.tags or self.type == "experimental":
            return True
        return "experimental" in str(self.path).replace("\\", "/").lower()

    @property
    def is_canonical(self) -> bool:
        if self.is_raw or self.is_archived:
            return False
        return self.lifecycle in {"ACTIVE", "REVIEW", "NORMALIZED", "CLASSIFIED", "NONE"}

    def outgoing_ids(self) -> List[str]:
        """Target IDs explicitly declared in `relations` (real, declared synapses)."""
        out = []
        for rel in self.meta.get("relations") or []:
            if not isinstance(rel, dict):
                continue
            target = rel.get("target_id")
            if not target:
                m = UUID_RE.search(str(rel.get("target", "")))
                target = m.group(0) if m else None
            if target:
                out.append(str(target))
        return out

    def relations(self) -> List[Dict[str, Any]]:
        rels = []
        for rel in self.meta.get("relations") or []:
            if isinstance(rel, dict):
                rels.append(rel)
        return rels

    def wikilinks(self) -> List[str]:
        return [w.strip() for w in WIKILINK_RE.findall(self.body)]


def _parse(path: Path) -> Optional[Note]:
    try:
        raw = path.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return None
    m = FRONTMATTER_RE.match(raw)
    meta: Dict[str, Any] = {}
    body = raw
    if m:
        body = raw[m.end():]
        if yaml is not None:
            try:
                parsed = yaml.safe_load(m.group(1))
                if isinstance(parsed, dict):
                    meta = parsed
            except Exception:
                meta = {}
    body = OBSIDIAN_TAIL_RE.sub("\n", body).strip()
    title = str(meta.get("title") or "").strip()
    if not title:
        heading = re.search(r"^#\s+(.+)$", body, re.M)
        title = heading.group(1).strip() if heading else path.stem.replace("_", " ")
    note_id = str(meta.get("id") or "").strip() or f"path:{path.as_posix()}"
    return Note(id=note_id, path=path, title=title, body=body, meta=meta)


class VaultIndex:
    def __init__(self, notes: List[Note]):
        self.notes = notes
        self.by_id: Dict[str, Note] = {n.id: n for n in notes}
        self.by_title: Dict[str, Note] = {n.title.lower(): n for n in notes}
        # Obsidian wikilinks reference the FILE NAME, not the note title, and
        # the two frequently differ (a note's title comes from frontmatter or
        # its first heading). Without a filename index, `[[08 Memory
        # Subsystems Map]]` fails to resolve even though the file exists.
        # First writer wins, so a title match is never shadowed by a stem.
        self.by_slug: Dict[str, Note] = {}
        for n in notes:
            stem = n.path.stem.strip().lower()
            self.by_slug.setdefault(stem, n)
            self.by_slug.setdefault(stem.replace("_", " "), n)
            self.by_slug.setdefault(stem.replace("-", " "), n)

    @classmethod
    def load(
        cls,
        vault_root: Path | str,
        roots: Iterable[str] = DEFAULT_ROOTS,
        lifecycles: Optional[Iterable[str]] = None,
        drop_navigation: bool = True,
        include_raw: bool = False,
        include_archived: bool = False,
        exclude_export_residue: bool = True,
    ) -> "VaultIndex":
        vault_root = Path(vault_root)
        allowed = {l.upper() for l in lifecycles} if lifecycles else None
        notes: List[Note] = []
        for root in roots:
            base = vault_root / root
            if not base.exists():
                continue
            for path in sorted(base.rglob("*.md")):
                if exclude_export_residue and _is_export_residue(path):
                    continue
                note = _parse(path)
                if note is None:
                    continue
                if drop_navigation and (note.type in NAVIGATION_TYPES):
                    continue
                if not include_raw and note.is_raw and (allowed is None or "RAW" not in allowed):
                    continue
                if not include_archived and note.is_archived and (allowed is None or "ARCHIVED" not in allowed):
                    continue
                if allowed and note.lifecycle not in allowed:
                    continue
                notes.append(note)
        return cls(notes)

    def resolve(self, ref: str) -> Optional[Note]:
        """Resolve a reference by id, then title, then file name.

        The filename fallback is what makes Obsidian `[[wikilinks]]`
        resolvable; id and title lookups keep their previous precedence.
        """
        if not ref:
            return None
        key = ref.strip().lower()
        return (
            self.by_id.get(ref)
            or self.by_title.get(key)
            or self.by_slug.get(key)
        )

    def __len__(self) -> int:
        return len(self.notes)


def stats(index: VaultIndex) -> Dict[str, Any]:
    from collections import Counter

    edges = sum(len(n.outgoing_ids()) for n in index.notes)
    resolvable = sum(
        1 for n in index.notes for t in n.outgoing_ids() if t in index.by_id
    )
    return {
        "notes": len(index),
        "canonical_notes": sum(1 for n in index.notes if n.is_canonical),
        "experimental_notes": sum(1 for n in index.notes if n.is_experimental),
        "lifecycle": dict(Counter(n.lifecycle for n in index.notes)),
        "type": dict(Counter(n.type for n in index.notes).most_common(10)),
        "verification": dict(Counter(n.verification for n in index.notes)),
        "edges_declared": edges,
        "edges_resolvable": resolvable,
        "edges_per_note": round(edges / max(len(index), 1), 3),
    }
