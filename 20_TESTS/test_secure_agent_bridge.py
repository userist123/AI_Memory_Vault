import base64
import json
import time

import pytest

from agent_bridge.crypto import (
    Ed25519Signer,
    X25519Envelope,
    canonical_json,
)
from agent_bridge.replay import ReplayGuard
from agent_bridge.antigravity import AntigravitySession
from agent_bridge.client import SecureBridgeClient
from agent_bridge.policy import CapabilityToken, BridgePolicy, PolicyError
from routing.models import WorkPacket


def test_envelope_encrypts_and_round_trips_without_plaintext():
    recipient = X25519Envelope.generate()
    plaintext = b"TOP SECRET WORK PACKET"
    envelope = recipient.encrypt(plaintext, aad=b"task-1")
    encoded = json.dumps(envelope).encode()
    assert plaintext not in encoded
    assert recipient.decrypt(envelope, aad=b"task-1") == plaintext


def test_signed_capability_token_is_scoped_and_expires():
    signer = Ed25519Signer.generate()
    token = CapabilityToken.issue(
        signer.private_key,
        bridge_id="bridge-1",
        task_id="task-1",
        runtime="antigravity",
        agent="visual_architect",
        permissions=("execute",),
        expires_at=time.time() + 60,
        nonce="n1",
    )
    verified = CapabilityToken.verify(token, signer.public_key)
    assert verified["runtime"] == "antigravity"
    assert verified["agent"] == "visual_architect"

    with pytest.raises(PolicyError):
        BridgePolicy(("antigravity",), ("visual_architect",)).authorize(
            verified, runtime="codex", agent="visual_architect"
        )


def test_replay_guard_rejects_second_use():
    guard = ReplayGuard(ttl_seconds=60)
    assert guard.claim("task-1", "nonce-1") is True
    assert guard.claim("task-1", "nonce-1") is False


def test_canonical_json_is_deterministic():
    value = {"b": 2, "a": {"z": 1, "x": [3, 2, 1]}}
    assert canonical_json(value) == canonical_json(value)
    assert base64.b64encode(canonical_json(value)).decode()



def test_antigravity_command_is_persistent_and_prompt_free():
    session = AntigravitySession("C:/workspace", model="gemini-3.8-flash-high", effort="high")
    command = session.build_command()
    assert "--input-format" in command
    assert "stream-json" in command
    assert "--output-format" in command
    assert command.count("stream-json") == 2
    assert "gemini-3.8-flash-high" in command
    assert "high" in command
    assert "SECRET PROMPT" not in command


def test_secure_bridge_client_round_trip():
    bridge_keys = X25519Envelope.generate()
    caller_keys = X25519Envelope.generate()
    signer = Ed25519Signer.generate()
    from agent_bridge.bridge import SecureBridge
    bridge = SecureBridge(
        "bridge-1", bridge_keys, signer.public_key,
        BridgePolicy(("antigravity",), ("visual_architect",)), ReplayGuard(),
        {"antigravity": lambda p: {"status": "completed", "response": "ok"}},
    )
    packet = WorkPacket("task-client", "route-client", "router", "visual_architect",
                        "antigravity", "v1", "command", "private work", (), (), (), 60, {})
    token = CapabilityToken.issue(
        signer.private_key, bridge_id="bridge-1", task_id=packet.task_id,
        runtime="antigravity", agent="visual_architect", permissions=("execute",),
        expires_at=time.time() + 60, nonce="client-nonce",
    )
    seen = {}
    def send(request):
        seen.update(request)
        return bridge.handle(request)
    client = SecureBridgeClient("bridge-1", bridge_keys.public_key, caller_keys, send)
    assert client.dispatch(packet, token, "client-nonce")["response"] == "ok"
    assert b"private work" not in json.dumps(seen).encode()
