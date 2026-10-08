"""agent_bridge: `minimum_ttl_seconds` is enforced, and AGY context never leaks between tasks.

Both were found reviewing PR #211: the config declared `minimum_ttl_seconds` but nothing read
it, and AntigravitySession kept one conversation across packets, so a later task could see an
earlier task's prompts and answers.
"""
from __future__ import annotations

import base64
import json
import os
import stat
import sys
import time
from pathlib import Path

import pytest
from cryptography.hazmat.primitives import serialization

from agent_bridge.antigravity import AntigravitySession
from agent_bridge.bridge import BridgeRequestError
from agent_bridge.config import load_bridge_config
from agent_bridge.crypto import Ed25519Signer, X25519Envelope, packet_bytes, packet_sha256
from agent_bridge.policy import CapabilityToken, PolicyError
from agent_bridge.replay import ReplayGuard
from routing.models import WorkPacket

ROOT = Path(__file__).resolve().parents[1]
BRIDGE_CONFIG = ROOT / "04_CONFIG" / "agent_bridge.json"


# ── minimum_ttl_seconds ──────────────────────────────────────────────────────────────────
def _token(signer, *, ttl, now=None, task_id="task-1"):
    now = time.time() if now is None else now
    return CapabilityToken.issue(
        signer.private_key, bridge_id="local-windows-bridge", task_id=task_id, runtime="antigravity",
        agent="visual_architect", permissions=("execute",), expires_at=now + ttl, nonce="n", iat=now,
        response_public_key="k", packet_sha256="0" * 64)


def test_token_verify_enforces_minimum_ttl():
    signer = Ed25519Signer.generate()
    short, ok = _token(signer, ttl=5), _token(signer, ttl=60)
    assert CapabilityToken.verify(short, signer.public_key)  # min_ttl defaults to 0: unchanged behaviour
    with pytest.raises(PolicyError, match="minimum TTL"):
        CapabilityToken.verify(short, signer.public_key, min_ttl=30)
    assert CapabilityToken.verify(ok, signer.public_key, min_ttl=30)["task_id"] == "task-1"


def test_config_declares_a_minimum_ttl_and_the_built_bridge_enforces_it():
    cfg = load_bridge_config(BRIDGE_CONFIG)
    assert cfg.min_ttl_seconds == 30.0
    keys, signer = X25519Envelope.generate(), Ed25519Signer.generate()
    bridge = cfg.build_bridge(recipient=keys, verifier=signer.public_key, signer=Ed25519Signer.generate(),
                              replay_guard=ReplayGuard(), executors={"antigravity": lambda p: {"status": "completed"}})
    assert bridge.min_token_ttl == 30.0

    response_key = base64.urlsafe_b64encode(keys.public_key.public_bytes(
        serialization.Encoding.Raw, serialization.PublicFormat.Raw)).decode()
    packet = WorkPacket("task-ttl", "route-1", "router", "visual_architect", "antigravity", "v1", "command", "goal", (), (), (), 60, {})
    aad = f"{cfg.bridge_id}:{packet.task_id}".encode()

    def request(ttl, nonce):
        token = CapabilityToken.issue(
            signer.private_key, bridge_id=cfg.bridge_id, task_id=packet.task_id, runtime="antigravity",
            agent="visual_architect", permissions=("execute",), expires_at=time.time() + ttl, nonce=nonce,
            response_public_key=response_key, packet_sha256=packet_sha256(packet))
        return {"version": 1, "bridge_id": cfg.bridge_id, "task_id": packet.task_id, "runtime": "antigravity",
                "agent": "visual_architect", "nonce": nonce, "capability_token": token, "aad": aad.decode(),
                "payload": keys.encrypt(packet_bytes(packet), aad=aad), "response_public_key": response_key}

    with pytest.raises(BridgeRequestError, match="minimum TTL"):
        bridge.handle(request(5, "short"))
    assert bridge.handle(request(60, "long"))["status"] == "completed"


# ── AntigravitySession: no context leak between tasks ───────────────────────────────────
FAKE_AGY = """#!{python}
import json, os, sys
turns = 0
for line in sys.stdin:
    if not line.strip():
        continue
    turns += 1  # this process's whole conversation so far
    print(json.dumps({{"event": "result", "result": {{"response": "turn %d" % turns, "pid": os.getpid()}}}}), flush=True)
"""


@pytest.fixture
def fake_agy(tmp_path):
    exe = tmp_path / "agy"
    exe.write_text(FAKE_AGY.format(python=sys.executable), encoding="utf-8")
    exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
    return str(exe)


pytestmark_posix = pytest.mark.skipif(os.name == "nt", reason="fake agy is a POSIX script")


@pytestmark_posix
def test_a_task_never_inherits_another_tasks_conversation(tmp_path, fake_agy):
    with AntigravitySession(tmp_path, binary=fake_agy) as session:
        first = session.ask("task A, first prompt", 10, task_id="task-a")
        again = session.ask("task A, follow-up", 10, task_id="task-a")
        other = session.ask("task B", 10, task_id="task-b")
        anon_1 = session.ask("no task id", 10)
        anon_2 = session.ask("no task id either", 10)
    assert first["response"] == "turn 1"
    assert again["response"] == "turn 2" and again["pid"] == first["pid"]  # same task keeps its context
    assert other["response"] == "turn 1" and other["pid"] != first["pid"]  # new task: fresh process
    assert anon_1["response"] == "turn 1" and anon_2["response"] == "turn 1"  # unidentified prompts never share


@pytestmark_posix
def test_explicit_reset_discards_the_conversation(tmp_path, fake_agy):
    session = AntigravitySession(tmp_path, binary=fake_agy)
    try:
        a = session.ask("one", 10, task_id="t")
        session.reset()
        b = session.ask("two", 10, task_id="t")
    finally:
        session.close()
    assert a["response"] == "turn 1" and b["response"] == "turn 1" and a["pid"] != b["pid"]


@pytestmark_posix
def test_isolation_can_only_be_relaxed_explicitly(tmp_path, fake_agy):
    session = AntigravitySession(tmp_path, binary=fake_agy, isolate_tasks=False)
    try:
        assert session.ask("one", 10, task_id="x")["response"] == "turn 1"
        assert session.ask("two", 10, task_id="y")["response"] == "turn 2"
    finally:
        session.close()
    assert AntigravitySession(tmp_path).isolate_tasks is True
