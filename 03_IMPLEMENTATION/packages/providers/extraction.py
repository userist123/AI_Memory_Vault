"""Atomic-memory candidate extraction with optional local-LLM adapter."""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from hashlib import sha256
from typing import Callable, Dict, Iterable, List, Optional
import re
import uuid


CANDIDATE_TYPES = {"fact", "decision", "preference", "task", "lesson", "procedure"}


@dataclass
class MemoryCandidate:
    candidate_id: str
    type: str
    category: str
    content: str
    confidence: str
    lifecycle: str
    verification: str
    tags: List[str]
    provenance: Dict[str, str]
    source_event_ids: List[str] = field(default_factory=list)
    created_at: str = ""
    content_hash: str = ""

    def to_dict(self) -> dict:
        return asdict(self)


class AtomicMemoryExtractor:
    """Produces RAW, unverified candidates only; canonical writes happen elsewhere."""

    _patterns = (
        ("decision", re.compile(r"^(?:am decis|decizia este|vom folosi|se folose\u0219te)\s*[:\-]?\s*(.+)$", re.I)),
        ("preference", re.compile(r"^(?:prefer|\u00eemi place|nu vreau|vreau)\s*[:\-]?\s*(.+)$", re.I)),
        ("task", re.compile(r"^(?:todo|task|de f\u0103cut|trebuie s\u0103)\s*[:\-]?\s*(.+)$", re.I)),
        ("lesson", re.compile(r"^(?:lec\u021bie|am \u00eenv\u0103\u021bat|lesson learned)\s*[:\-]?\s*(.+)$", re.I)),
        ("fact", re.compile(r"^(?:fapt|observa\u021bie|problema este)\s*[:\-]?\s*(.+)$", re.I)),
    )

    def __init__(self, local_llm: Optional[Callable[[str], Iterable[dict]]] = None):
        self.local_llm = local_llm

    @staticmethod
    def _candidate(kind: str, content: str, source_ref: str,
                   source_event_ids: Optional[List[str]] = None,
                   category: str = "session") -> MemoryCandidate:
        now = datetime.now(timezone.utc).isoformat()
        normalized = " ".join(content.split())
        digest = sha256(f"{kind}|{normalized}|{source_ref}".encode("utf-8")).hexdigest()
        return MemoryCandidate(
            candidate_id=str(uuid.uuid4()), type=kind, category=category,
            content=normalized, confidence="medium", lifecycle="RAW",
            verification="unverified", tags=[kind, "extracted"],
            provenance={"source_type": "inference", "source_ref": source_ref,
                        "extractor": "atomic-memory-v6", "sha256": digest},
            source_event_ids=source_event_ids or [], created_at=now, content_hash=digest,
        )

    def _deterministic_extract(self, text: str, source_ref: str,
                               source_event_ids: Optional[List[str]]) -> List[MemoryCandidate]:
        results: List[MemoryCandidate] = []
        for raw in re.split(r"(?:\n+|(?<=[.!?])\s+)", text.strip()):
            line = raw.strip(" -\u2022\t")
            if len(line) < 8:
                continue
            for kind, pattern in self._patterns:
                match = pattern.match(line)
                if match:
                    results.append(self._candidate(kind, match.group(1), source_ref, source_event_ids))
                    break
        return results

    def extract(self, text: str, source_ref: str,
                source_event_ids: Optional[List[str]] = None) -> List[MemoryCandidate]:
        if not text or not text.strip():
            return []
        candidates = self._deterministic_extract(text, source_ref, source_event_ids)
        if self.local_llm is not None:
            for raw in self.local_llm(text):
                kind = str(raw.get("type", "fact")).lower()
                content = str(raw.get("content", "")).strip()
                if kind in CANDIDATE_TYPES and content:
                    candidates.append(self._candidate(
                        kind, content, source_ref, source_event_ids,
                        str(raw.get("category", "session")),
                    ))
        unique: Dict[str, MemoryCandidate] = {}
        for candidate in candidates:
            unique[candidate.content_hash] = candidate
        return list(unique.values())
