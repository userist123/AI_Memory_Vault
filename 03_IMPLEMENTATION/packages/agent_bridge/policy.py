from __future__ import annotations

import base64
import json
import time
from typing import Iterable

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey

from .crypto import canonical_json


class PolicyError(PermissionError):
    pass


class CapabilityToken:
    @staticmethod
    def issue(
        private_key: Ed25519PrivateKey,
        *,
        bridge_id: str,
        task_id: str,
        runtime: str,
        agent: str,
        permissions: Iterable[str],
        expires_at: float,
        nonce: str,
    ) -> str:
        payload = {
            "v": 1,
            "bridge_id": bridge_id,
            "task_id": task_id,
            "runtime": runtime,
            "agent": agent,
            "permissions": sorted(set(permissions)),
            "expires_at": float(expires_at),
            "nonce": nonce,
        }
        signature = private_key.sign(canonical_json(payload))
        envelope = {"payload": payload, "signature": base64.urlsafe_b64encode(signature).decode("ascii")}
        return base64.urlsafe_b64encode(canonical_json(envelope)).decode("ascii")

    @staticmethod
    def verify(token: str, public_key: Ed25519PublicKey, now: float | None = None) -> dict:
        try:
            raw = base64.urlsafe_b64decode(token.encode("ascii"))
            envelope = json.loads(raw.decode("utf-8"))
            payload = envelope["payload"]
            signature = base64.urlsafe_b64decode(envelope["signature"].encode("ascii"))
            public_key.verify(signature, canonical_json(payload))
        except Exception as exc:
            raise PolicyError("invalid capability token") from exc
        if payload.get("v") != 1:
            raise PolicyError("unsupported capability token version")
        if float(payload.get("expires_at", 0)) <= (time.time() if now is None else now):
            raise PolicyError("capability token expired")
        return payload


class BridgePolicy:
    def __init__(self, allowed_runtimes: Iterable[str], allowed_agents: Iterable[str]):
        self.allowed_runtimes = frozenset(allowed_runtimes)
        self.allowed_agents = frozenset(allowed_agents)

    def authorize(self, token: dict, *, runtime: str, agent: str, permission: str = "execute") -> None:
        if runtime not in self.allowed_runtimes:
            raise PolicyError("runtime not allowed")
        if agent not in self.allowed_agents:
            raise PolicyError("agent not allowed")
        if token.get("runtime") != runtime or token.get("agent") != agent:
            raise PolicyError("capability scope mismatch")
        if permission not in token.get("permissions", []):
            raise PolicyError("permission not granted")
