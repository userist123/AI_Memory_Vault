"""Versioned security-update catalog validation."""
from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any

from .security_update_policy import SecurityUpdate


@dataclass(frozen=True)
class Catalog:
    schema_version: int
    generated_at: str
    minimum_runtime_version: str
    updates: tuple[SecurityUpdate, ...]

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Catalog":
        if data.get("schema_version") != 1:
            raise ValueError("unsupported security update catalog schema")
        updates = tuple(SecurityUpdate.from_dict(item) for item in data.get("updates", []))
        return cls(
            schema_version=1,
            generated_at=str(data["generated_at"]),
            minimum_runtime_version=str(data["minimum_runtime_version"]),
            updates=updates,
        )

    @classmethod
    def from_json(cls, raw: bytes) -> "Catalog":
        value = json.loads(raw.decode("utf-8"))
        if not isinstance(value, dict):
            raise ValueError("catalog must be an object")
        return cls.from_dict(value)
