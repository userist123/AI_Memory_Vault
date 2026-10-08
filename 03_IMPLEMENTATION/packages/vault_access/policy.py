"""Principals, channels and classifications: who may read which route (04_CONFIG/access_policy.yaml).

Deny-first and fail-closed. The decision for a (principal, route) pair is, in order:

  1. denylist           — the path matches a hard-denied pattern     -> DENIED_POLICY
  2. domain             — the domain is not allowed for the principal -> DENIED_POLICY
  3. trust              — e.g. untrusted inbox, archived material     -> DENIED_TRUST
  4. classification     — above min(clearance, channel ceiling)       -> DENIED_CLASSIFICATION

A principal asserted through an interface whose channels do not include the principal's channel
is refused (an MCP client can never claim to be the human owner). An unknown principal becomes
`default_principal`, the most restrictive one.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import yaml

from .canonical import glob_match
from .errors import ErrorCode, VaultAccessError

TRUST_LEVELS = ("verified", "unverified", "untrusted", "archived")


@dataclass(frozen=True)
class Principal:
    name: str
    channel: str
    clearance: str
    domains: Tuple[str, ...]
    trust: Tuple[str, ...]


@dataclass(frozen=True)
class Decision:
    allowed: bool
    code: ErrorCode
    reason: str = ""


@dataclass
class AccessPolicy:
    classifications: Tuple[str, ...]
    channels: Dict[str, str]
    interfaces: Dict[str, Tuple[str, ...]]
    principals: Dict[str, Principal]
    default_principal: str
    denylist: Tuple[str, ...]
    default_principals: Dict[str, str] = field(default_factory=dict)
    limits: Dict[str, int] = field(default_factory=dict)
    telegram: Dict[str, object] = field(default_factory=dict)
    source: Optional[Path] = None

    # ── loading ──────────────────────────────────────────────────────────────────────────
    @classmethod
    def load(cls, path: Path) -> "AccessPolicy":
        data = yaml.safe_load(Path(path).read_text(encoding="utf-8")) or {}
        return cls.from_dict(data, source=Path(path))

    @classmethod
    def from_dict(cls, data: dict, source: Optional[Path] = None) -> "AccessPolicy":
        if data.get("version") != 1:
            raise ValueError("access policy: unsupported version")
        classifications = tuple(data.get("classifications") or ())
        if not classifications:
            raise ValueError("access policy: classifications missing")
        channels = {str(k): str(v) for k, v in (data.get("channels") or {}).items()}
        for ch, ceiling in channels.items():
            if ceiling not in classifications:
                raise ValueError(f"access policy: channel {ch} has unknown ceiling {ceiling}")
        interfaces = {str(k): tuple(v or ()) for k, v in (data.get("interfaces") or {}).items()}
        principals: Dict[str, Principal] = {}
        for name, spec in (data.get("principals") or {}).items():
            spec = spec or {}
            channel = spec.get("channel")
            clearance = spec.get("clearance")
            if channel not in channels:
                raise ValueError(f"access policy: principal {name} has unknown channel {channel}")
            if clearance not in classifications:
                raise ValueError(f"access policy: principal {name} has unknown clearance {clearance}")
            trust = tuple(spec.get("trust") or ("verified",))
            for t in trust:
                if t not in TRUST_LEVELS:
                    raise ValueError(f"access policy: principal {name} has unknown trust {t}")
            principals[str(name)] = Principal(str(name), channel, clearance,
                                              tuple(str(d) for d in spec.get("domains") or ()), trust)
        default = str(data.get("default_principal") or "")
        if default not in principals:
            raise ValueError("access policy: default_principal must name a principal")
        defaults = {str(k): str(v) for k, v in (data.get("default_principals") or {}).items()}
        for iface, pname in defaults.items():
            if iface not in interfaces or pname not in principals:
                raise ValueError(f"access policy: default_principals.{iface} is invalid")
            if principals[pname].channel not in interfaces[iface]:
                raise ValueError(f"access policy: default principal {pname} cannot be used through {iface}")
        return cls(classifications=classifications, channels=channels, interfaces=interfaces,
                   principals=principals, default_principal=default,
                   denylist=tuple(str(p) for p in data.get("denylist") or ()),
                   default_principals=defaults,
                   limits={str(k): int(v) for k, v in (data.get("limits") or {}).items()},
                   telegram=dict(data.get("telegram") or {}), source=source)

    # ── principals ───────────────────────────────────────────────────────────────────────
    def principal(self, name: Optional[str], interface: str) -> Principal:
        """The principal `name` as asserted through `interface`, or a refusal."""
        allowed_channels = self.interfaces.get(interface)
        if allowed_channels is None:
            raise VaultAccessError(ErrorCode.DENIED_POLICY, f"unknown interface {interface}")
        fallback = self.default_principals.get(interface, self.default_principal)
        chosen = self.principals.get(name or "") or self.principals[fallback]
        if chosen.channel not in allowed_channels:
            raise VaultAccessError(ErrorCode.DENIED_POLICY,
                                   f"principal {chosen.name} cannot be asserted through {interface}")
        return chosen

    def effective_clearance(self, principal: Principal) -> str:
        ceiling = self.channels[principal.channel]
        return min(principal.clearance, ceiling, key=self.classifications.index)

    # ── decisions ────────────────────────────────────────────────────────────────────────
    def denied_path(self, rel_posix: str) -> bool:
        return any(glob_match(rel_posix, pat) for pat in self.denylist)

    @staticmethod
    def domain_allowed(patterns: Sequence[str], domain: str) -> bool:
        def hit(pat: str) -> bool:
            return pat == "*" or pat == domain or domain.startswith(pat + ".") or (
                any(c in pat for c in "*?[") and glob_match(domain, pat))
        positive = [p for p in patterns if not p.startswith("!")]
        negative = [p[1:] for p in patterns if p.startswith("!")]
        return any(hit(p) for p in positive) and not any(hit(p) for p in negative)

    def rank(self, classification: str) -> int:
        try:
            return self.classifications.index(classification)
        except ValueError:
            return len(self.classifications)  # unknown label = above everything

    def decide(self, principal: Principal, *, domain: str, rel_path: str, classification: str,
               trust: str) -> Decision:
        if self.denied_path(rel_path):
            return Decision(False, ErrorCode.DENIED_POLICY, "denylisted path")
        if not self.domain_allowed(principal.domains, domain):
            return Decision(False, ErrorCode.DENIED_POLICY, "domain not allowed")
        if trust not in principal.trust:
            return Decision(False, ErrorCode.DENIED_TRUST, f"trust {trust} not allowed")
        if self.rank(classification) > self.rank(self.effective_clearance(principal)):
            return Decision(False, ErrorCode.DENIED_CLASSIFICATION, "classification above clearance")
        return Decision(True, ErrorCode.OK)

    def visible_domains(self, principal: Principal, domains: Iterable[str]) -> List[str]:
        return [d for d in domains if self.domain_allowed(principal.domains, d)]
