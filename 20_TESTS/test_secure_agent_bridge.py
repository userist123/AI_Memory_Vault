import base64
import json
import time

import pytest

from agent_bridge.crypto import (
    Ed25519Signer,
    X25519Envelope,
    canonical_json,
    packet_sha256,
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
        nonce="n1", response_public_key="caller-key", packet_sha256="0" * 64,
    )
    verified = CapabilityToken.verify(token, signer.public_key)
    assert verified["runtime"] == "antigravity"
    assert verified["agent"] == "visual_architect"

    with pytest.raises(PolicyError):
        BridgePolicy(("antigravity",), ("visual_architect",)).authorize(
            verified, runtime="codex", agent="visual_architect"
        )


def test_replay_guard_rejects_second_use():
    guard = ReplayGuard(ttl_seconds=180)
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
    bridge_signer = Ed25519Signer.generate()
    from agent_bridge.bridge import SecureBridge
    bridge = SecureBridge(
        "bridge-1", bridge_keys, signer.public_key,
        BridgePolicy(("antigravity",), ("visual_architect",)), ReplayGuard(),
        {"antigravity": lambda p: {"status": "completed", "response": "ok"}},
        bridge_signer,
    )
    packet = WorkPacket("task-client", "route-client", "router", "visual_architect",
                        "antigravity", "v1", "command", "private work", (), (), (), 60, {})
    response_public_key = base64.urlsafe_b64encode(
        caller_keys.public_key.public_bytes(
            __import__("cryptography").hazmat.primitives.serialization.Encoding.Raw,
            __import__("cryptography").hazmat.primitives.serialization.PublicFormat.Raw,
        )
    ).decode()
    token = CapabilityToken.issue(
        signer.private_key, bridge_id="bridge-1", task_id=packet.task_id,
        runtime="antigravity", agent="visual_architect", permissions=("execute",),
        expires_at=time.time() + 60, nonce="client-nonce",
        response_public_key=response_public_key, packet_sha256=packet_sha256(packet),
    )
    seen = {}
    def send(request):
        seen.update(request)
        return bridge.handle(request)
    client = SecureBridgeClient(
        "bridge-1", bridge_keys.public_key, caller_keys, send, bridge_signer.public_key
    )
    assert client.dispatch(packet, token, "client-nonce")["response"] == "ok"
    assert b"private work" not in json.dumps(seen).encode()


def test_capability_token_rejects_ttl_above_maximum():
    signer = Ed25519Signer.generate()
    token = CapabilityToken.issue(
        signer.private_key, bridge_id="bridge-1", task_id="ttl-1",
        runtime="antigravity", agent="visual_architect", permissions=("execute",),
        expires_at=1121, nonce="ttl-nonce", response_public_key="caller", iat=100,
        packet_sha256="0" * 64,
    )
    with pytest.raises(PolicyError, match="maximum TTL"):
        CapabilityToken.verify(token, signer.public_key, now=100, max_ttl=120)


def test_replay_guard_rejects_reuse_after_memory_window_but_inside_token_lifetime():
    guard = ReplayGuard(ttl_seconds=630, max_ttl_seconds=600, clock_skew_seconds=30)
    assert guard.claim("task-long", "nonce-long", now=1000)
    assert guard.claim("task-long", "nonce-long", now=1301) is False


def test_replay_guard_sqlite_survives_reinstantiation(tmp_path):
    db = tmp_path / "replay.sqlite"
    first = ReplayGuard(path=db, ttl_seconds=180)
    assert first.claim("task-persist", "nonce-persist", now=1000)
    first.close()
    second = ReplayGuard(path=db, ttl_seconds=180)
    assert second.claim("task-persist", "nonce-persist", now=1100) is False
    second.close()
