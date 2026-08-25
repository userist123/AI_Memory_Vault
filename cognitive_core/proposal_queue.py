"""Durable queue for extracted RAW candidates; no automatic canonical promotion."""
from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, Iterable, List
import json

from .extraction import MemoryCandidate


class MemoryProposalQueue:
    def __init__(self, queue_path: str | Path):
        self.path = Path(queue_path)
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def _load(self) -> List[dict]:
        if not self.path.exists():
            return []
        records = []
        for line in self.path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                records.append(json.loads(line))
        return records

    def _write(self, records: Iterable[dict]) -> None:
        payload = "\n".join(json.dumps(item, ensure_ascii=False, sort_keys=True) for item in records)
        self.path.write_text(payload + ("\n" if payload else ""), encoding="utf-8")

    def enqueue(self, candidates: Iterable[MemoryCandidate]) -> int:
        records = self._load()
        hashes = {item.get("content_hash") for item in records}
        added = 0
        for candidate in candidates:
            if candidate.content_hash in hashes:
                continue
            record = candidate.to_dict()
            record["queue_status"] = "PENDING_REVIEW"
            record["queued_at"] = datetime.now(timezone.utc).isoformat()
            records.append(record)
            hashes.add(candidate.content_hash)
            added += 1
        self._write(records)
        return added

    def pending(self) -> List[dict]:
        return [item for item in self._load() if item.get("queue_status") == "PENDING_REVIEW"]

    def mark(self, candidate_id: str, status: str, reviewer: str = "human") -> None:
        allowed = {"APPROVED", "REJECTED", "PROMOTED"}
        if status not in allowed:
            raise ValueError(f"status must be one of {sorted(allowed)}")
        records = self._load()
        for item in records:
            if item.get("candidate_id") == candidate_id:
                item["queue_status"] = status
                item["reviewed_by"] = reviewer
                item["reviewed_at"] = datetime.now(timezone.utc).isoformat()
                self._write(records)
                return
        raise KeyError(f"candidate not found: {candidate_id}")

    def status(self) -> Dict[str, int]:
        counts: Dict[str, int] = {}
        for item in self._load():
            key = item.get("queue_status", "UNKNOWN")
            counts[key] = counts.get(key, 0) + 1
        return counts
