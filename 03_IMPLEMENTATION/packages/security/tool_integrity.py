"""Cryptographic pinning for tool definitions to detect silent changes."""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class ToolDefinition:
    server_id: str
    name: str
    description: str
    input_schema: dict[str, Any]

    def canonical_bytes(self) -> bytes:
        payload = {
            "server_id": self.server_id,
            "name": self.name,
            "description": self.description,
            "input_schema": self.input_schema,
        }
        return json.dumps(
            payload,
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=True,
        ).encode("utf-8")

    def sha256(self) -> str:
        return hashlib.sha256(self.canonical_bytes()).hexdigest()


@dataclass(frozen=True)
class ToolPin:
    server_id: str
    tool_name: str
    definition_sha256: str


def pin_tool(tool: ToolDefinition) -> ToolPin:
    return ToolPin(tool.server_id, tool.name, tool.sha256())


def verify_tool(tool: ToolDefinition, pin: ToolPin) -> bool:
    return (
        tool.server_id == pin.server_id
        and tool.name == pin.tool_name
        and tool.sha256() == pin.definition_sha256
    )
