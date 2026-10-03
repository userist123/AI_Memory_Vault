"""Treat tool output as untrusted data before it re-enters agent context."""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .skill_exfiltration_scanner import scan_text


@dataclass(frozen=True)
class ToolResponseDecision:
    allowed: bool
    reason: str
    verdict: str
    data: Any


def _scan_strings(value: Any) -> list[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, dict):
        out: list[str] = []
        for key, item in value.items():
            out.extend(_scan_strings(key))
            out.extend(_scan_strings(item))
        return out
    if isinstance(value, (list, tuple)):
        out: list[str] = []
        for item in value:
            out.extend(_scan_strings(item))
        return out
    return []


def validate_tool_response(tool_name: str, response: Any) -> ToolResponseDecision:
    strings = _scan_strings(response)
    worst = "SAFE"
    for item in strings:
        result = scan_text(Path(f"<tool:{tool_name}>"), item)
        if result.verdict == "BLOCK":
            return ToolResponseDecision(False, "untrusted_tool_output", "BLOCK", None)
        if result.verdict == "REVIEW":
            worst = "REVIEW"

    if worst == "REVIEW":
        return ToolResponseDecision(False, "tool_output_requires_review", "REVIEW", None)

    # Structured output is still data, but only after every embedded string
    # passes the same content boundary.
    return ToolResponseDecision(True, "validated_tool_data", "SAFE", response)
