"""Load and validate 04_CONFIG/agent_bridge.json; the bridge is built only from a valid config.

Fail-closed: a missing file, an unknown schema, a security switch turned off
(`require_authenticated_packet`, `require_capability_token`, `require_replay_protection`),
`dangerously_skip_permissions: true`, empty allowlists or out-of-range limits raise
BridgeConfigError, and no bridge is created. There is no permissive default.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Mapping, Tuple

from .policy import BridgePolicy

_ID = re.compile(r"^[A-Za-z0-9_.-]{1,64}$")
MAX_TTL_CEILING = 300


class BridgeConfigError(ValueError):
    pass


@dataclass(frozen=True)
class BridgeConfig:
    bridge_id: str
    transport: str
    pipe_name: str
    allowed_runtimes: Tuple[str, ...]
    allowed_agents: Tuple[str, ...]
    max_packet_bytes: int
    max_result_bytes: int
    min_ttl_seconds: float
    max_ttl_seconds: float

    def policy(self) -> BridgePolicy:
        return BridgePolicy(self.allowed_runtimes, self.allowed_agents)

    def build_bridge(self, *, recipient, verifier, signer, replay_guard,
                     executors: Mapping[str, Callable], clock_skew: float = 30.0):
        from .bridge import SecureBridge
        unknown = set(executors) - set(self.allowed_runtimes)
        if unknown:
            raise BridgeConfigError(f"executors for runtimes not in allowed_runtimes: {sorted(unknown)}")
        if replay_guard.ttl_seconds < self.max_ttl_seconds + clock_skew:
            raise BridgeConfigError("replay guard TTL must cover max_ttl_seconds plus clock skew")
        return SecureBridge(self.bridge_id, recipient, verifier, self.policy(), replay_guard, executors, signer,
                            max_token_ttl=self.max_ttl_seconds, min_token_ttl=self.min_ttl_seconds, clock_skew=clock_skew,
                            max_packet_bytes=self.max_packet_bytes, max_result_bytes=self.max_result_bytes)


def _int(value, name, lo, hi) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or not lo <= value <= hi:
        raise BridgeConfigError(f"{name} must be an integer in [{lo}, {hi}]")
    return value


def load_bridge_config(path: Path) -> BridgeConfig:
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise BridgeConfigError(f"cannot read bridge config: {exc}") from exc
    if data.get("schema_version") != 1:
        raise BridgeConfigError("unsupported bridge config schema_version")
    bridge = data.get("bridge") or {}
    for switch in ("require_authenticated_packet", "require_capability_token", "require_replay_protection"):
        if bridge.get(switch) is not True:
            raise BridgeConfigError(f"bridge.{switch} must be true")
    bridge_id = bridge.get("id")
    if not isinstance(bridge_id, str) or not _ID.fullmatch(bridge_id):
        raise BridgeConfigError("bridge.id must be a plain identifier")
    if bridge.get("transport") != "windows_named_pipe":
        raise BridgeConfigError("bridge.transport must be windows_named_pipe")
    pipe = bridge.get("pipe_name")
    if not isinstance(pipe, str) or not pipe.startswith("LOCAL\\"):
        raise BridgeConfigError("bridge.pipe_name must be a LOCAL\\ (session-local) pipe")
    runtimes = tuple(data.get("allowed_runtimes") or ())
    agents = tuple(data.get("allowed_agents") or ())
    if not runtimes or not agents or not all(isinstance(x, str) and _ID.fullmatch(x) for x in runtimes + agents):
        raise BridgeConfigError("allowed_runtimes and allowed_agents must be non-empty identifier lists")
    if (data.get("antigravity") or {}).get("dangerously_skip_permissions") is not False:
        raise BridgeConfigError("antigravity.dangerously_skip_permissions must be false")
    security = data.get("security") or {}
    lo = _int(security.get("minimum_ttl_seconds"), "security.minimum_ttl_seconds", 1, MAX_TTL_CEILING)
    hi = _int(security.get("maximum_ttl_seconds"), "security.maximum_ttl_seconds", lo, MAX_TTL_CEILING)
    return BridgeConfig(bridge_id, bridge["transport"], pipe, runtimes, agents,
                        _int(bridge.get("max_packet_bytes"), "bridge.max_packet_bytes", 1024, 16 * 1024 * 1024),
                        _int(bridge.get("max_result_bytes"), "bridge.max_result_bytes", 1024, 64 * 1024 * 1024),
                        float(lo), float(hi))
