"""Treat tool output as untrusted data before it re-enters agent context."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .skill_exfiltration_scanner import scan_text


@dataclass(frozen=True)
class ToolResponseDecision:
    allowed: bool
    reason: str
    verdict: str
    data: Any


def validate_tool_response(tool_name: str, response: Any) -> ToolResponseDecision:
    if not isinstance(response, str):
        return ToolResponseDecision(True, "structured_data", "SAFE", response)

    result = scan_text(__import__("pathlib").Path(f"<tool:{tool_name}>"), response)
    if result.verdict == "BLOCK":
        return ToolResponseDecision(False, "untrusted_tool_output", result.verdict, None)
    if result.verdict == "REVIEW":
        return ToolResponseDecision(False, "tool_output_requires_review", result.verdict, None)
    return ToolResponseDecision(True, "safe_tool_output", result.verdict, response)
