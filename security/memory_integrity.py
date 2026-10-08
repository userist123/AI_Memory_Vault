"""Append-only integrity ledger for persistent agent memory.

This module does not replace a project's storage engine. It provides a small
policy/integrity layer that can sit in front of an existing memory backend.
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass, field
from typing import Any


@dataclass(frozen=True)
class MemoryRecord:
    version: int
    namespace: str
    payload: dict[str, Any]
    previous_sha256: str | None = None
    sha256: str = field(default="", compare=True)

    def with_hash(self) -> "MemoryRecord":
        canonical = json.dumps(
            {
                "version": self.version,
                "namespace": self.namespace,
                "payload": self.payload,
                "previous_sha256": self.previous_sha256,
            },
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=True,
        ).encode("utf-8")
        return MemoryRecord(
            self.version,
            self.namespace,
            self.payload,
            self.previous_sha256,
            hashlib.sha256(canonical).hexdigest(),
        )


class MemoryLedger:
    def __init__(self) -> None:
        self.records: list[MemoryRecord] = []

    def prepare(self, namespace: str, payload: dict[str, Any]) -> MemoryRecord:
        previous = self.records[-1].sha256 if self.records else None
        return MemoryRecord(
            version=len(self.records) + 1,
            namespace=namespace,
            payload=dict(payload),
            previous_sha256=previous,
        ).with_hash()

    def append(self, namespace: str, payload: dict[str, Any]) -> MemoryRecord:
        record = self.prepare(namespace, payload)
        self.records.append(record)
        return record

    def commit_proposal(
        self,
        record: MemoryRecord,
        *,
        trust_state: str,
        human_approved: bool = False,
    ) -> bool:
        # Strict allowlist of permitted commit states
        if trust_state not in ("TRUSTED", "REVIEW"):
            return False
        if trust_state == "REVIEW" and not human_approved:
            return False
        if record.version != len(self.records) + 1:
            return False
        expected_previous = self.records[-1].sha256 if self.records else None
        if record.previous_sha256 != expected_previous:
            return False
        if record.with_hash().sha256 != record.sha256:
            return False
        self.records.append(record)
        return True

    def replace(self, version: int, payload: dict[str, Any]) -> None:
        raise PermissionError("memory ledger is append-only; create a new version")

    def verify(self) -> bool:
        previous = None
        for index, record in enumerate(self.records, start=1):
            if record.version != index:
                return False
            if record.previous_sha256 != previous:
                return False
            if record.with_hash().sha256 != record.sha256:
                return False
            previous = record.sha256
        return True
