"""Central policy engine (spec 16). Decisions are made in code, never by a model.

The policy table is a frozen constant in this module. There is intentionally no API, MCP
tool or service method that changes it at runtime - an agent cannot raise its own authority.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from types import MappingProxyType
from typing import Literal

from .errors import ApprovalRequired, PolicyDenied


class Decision(StrEnum):
    ALLOW = "allow"
    DENY = "deny"
    APPROVAL = "approval"


ActorKind = Literal["human", "agent", "system"]
Channel = Literal["gui", "rest", "mcp", "computer", "system", "test"]


@dataclass(frozen=True)
class Actor:
    kind: ActorKind
    id: str
    channel: Channel

    @property
    def label(self) -> str:
        return f"{self.kind}:{self.id}@{self.channel}"

    @property
    def is_owner(self) -> bool:
        """The human owner, authenticated with the owner token through GUI/REST."""
        return self.kind == "human" and self.channel in ("gui", "rest")


# operation -> (decision for humans, decision for agents)
_TABLE: dict[str, tuple[Decision, Decision]] = {
    "project.read": (Decision.ALLOW, Decision.ALLOW),
    "project.create": (Decision.ALLOW, Decision.ALLOW),
    "project.write": (Decision.ALLOW, Decision.ALLOW),
    "project.export": (Decision.ALLOW, Decision.ALLOW),
    "project.archive": (Decision.ALLOW, Decision.APPROVAL),
    "project.delete": (Decision.APPROVAL, Decision.APPROVAL),
    "visual.capture": (Decision.ALLOW, Decision.ALLOW),
    "visual.capture_desktop": (Decision.ALLOW, Decision.ALLOW),
    "browser.preview": (Decision.ALLOW, Decision.ALLOW),
    "qa.run": (Decision.ALLOW, Decision.ALLOW),
    "app.build": (Decision.ALLOW, Decision.ALLOW),  # AAS: offline Docker build (plan gate + disk gate apply)
    "device.control": (Decision.ALLOW, Decision.ALLOW),  # AAS: studio emulator (owner device setting + gates apply)
    "deps.install": (Decision.APPROVAL, Decision.APPROVAL),
    "ai.run": (Decision.ALLOW, Decision.APPROVAL),  # starting agents spends money: an agent needs the owner's approval
    "ai.vision": (Decision.ALLOW, Decision.ALLOW),  # single bounded vision request (budget-capped)
    "files.import_external": (Decision.ALLOW, Decision.APPROVAL),  # reading a file outside the project workspace
    "computer.input": (Decision.ALLOW, Decision.ALLOW),  # further gated by the computer-use session
    "bim.open_external_editor": (Decision.ALLOW, Decision.APPROVAL),  # starts Blender/Bonsai on the owner's desktop
    "terminal.run": (Decision.ALLOW, Decision.DENY),  # controlled terminal: the owner only, never an agent
    "deploy.publish": (Decision.APPROVAL, Decision.APPROVAL),
    "production.modify": (Decision.APPROVAL, Decision.APPROVAL),
    "approval.decide": (Decision.ALLOW, Decision.DENY),
    "policy.modify": (Decision.DENY, Decision.DENY),
    "secrets.read": (Decision.DENY, Decision.DENY),
    "credentials.read": (Decision.DENY, Decision.DENY),
    "registry.modify": (Decision.DENY, Decision.DENY),
    "admin.execute": (Decision.DENY, Decision.DENY),
    "shell.execute": (Decision.DENY, Decision.DENY),
}
POLICY = MappingProxyType(dict(_TABLE))  # private copy: mutating _TABLE later has no effect
del _TABLE


class PolicyEngine:
    def decide(self, actor: Actor, operation: str) -> Decision:
        entry = POLICY.get(operation)
        if entry is None:
            return Decision.DENY  # default-deny for unknown operations
        human, agent = entry
        if actor.kind == "human":
            # a "human" arriving through MCP or computer-use is treated as an agent
            return human if actor.is_owner else agent
        return agent

    def enforce(self, actor: Actor, operation: str, *, approved: bool = False) -> None:
        d = self.decide(actor, operation)
        if d is Decision.DENY:
            raise PolicyDenied(f"operation '{operation}' is denied for {actor.label}", operation=operation)
        if d is Decision.APPROVAL and not approved:
            raise ApprovalRequired(f"operation '{operation}' requires human approval", operation=operation)
