"""Scoped, short-lived capabilities for tool execution.

Capabilities are policy data only. They never execute tools and do not grant
authority to external content.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone


@dataclass(frozen=True)
class Capability:
    actor: str
    tool_name: str
    scope: str
    expires_at: datetime

    def allows(self, actor: str, tool_name: str, scope: str, now: datetime) -> bool:
        if self.expires_at.tzinfo is None:
            raise ValueError("expires_at must be timezone-aware")
        current = now if now.tzinfo is not None else now.replace(tzinfo=timezone.utc)
        return (
            self.actor == actor
            and self.tool_name == tool_name
            and self.scope == scope
            and current < self.expires_at
        )


class CapabilitySet:
    def __init__(self, capabilities: list[Capability] | tuple[Capability, ...] = ()):
        self._capabilities = tuple(capabilities)

    def allows(self, actor: str, tool_name: str, scope: str, now: datetime) -> bool:
        return any(cap.allows(actor, tool_name, scope, now) for cap in self._capabilities)
