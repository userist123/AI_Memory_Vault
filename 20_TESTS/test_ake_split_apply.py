"""AKE split apply: the OWNER tool that turns a PLANNED split manifest into served child notes.

Threat model (written before implementation):
- T1 an agent runs the tool: refused, only HUMAN/ADMIN, and the CLI needs an interactive terminal
  plus the parent id typed back (owner presence), as the vault CLIs require for the owner.
- T2 the parent changed after planning: the manifest is stale, refused before any write.
- T3 a tampered manifest (offsets/hashes not matching the parent): refused before any write.
- T4 a child body changed between creation and attestation: attest(expected_content_sha256)
  refuses it; the tool stops at that child and reports it (fail closed, no partial success claim).
- T5 a re-run: children already applied with the same hash are skipped, never duplicated; an
  existing id with other content is a conflict and stops the run.
- T6 success must be observable: every applied child is read back through memory_get as an
  AI_AGENT and must equal its slice exactly.
Children are created RAW (never served to agents before attestation), attested with the
manifest's hash, reviewed, promoted. The parent is never modified.
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import uuid
from pathlib import Path

import pytest

from active_knowledge_engine.split_apply import ManifestError, apply_split
from active_knowledge_engine.split_plan import plan_split

REPO = Path(__file__).resolve().parents[1]


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "a" * 64)


def _controller(vault):
    from memory_controller.controller import MemoryController
    from memory_controller.storage.file_engine import FileStorageEngine

    return MemoryController(storage=FileStorageEngine(str(vault)))


def _sections(n=16, lines_per=25):
    parts = ["# Handoff"]
    for s in range(n):
        parts.append(f"## Section {s}")
        parts.extend(f"Rule {s}.{i}: the agent MUST keep clause {s}.{i} intact." for i in range(lines_per))
        parts.append("")
    return chr(10).join(parts) + chr(10)


def _write_parent(vault, nid):
    lines = ["---", f"id: {nid}", "type: knowledge", "category: test", "tags: [t]",
             "created: 2026-11-22", "updated: 2026-11-22", "provenance:", "  source_type: user",
             "  source_ref: fixture", "confidence: high", "verification: verified", "relations: []",
             "lifecycle: ACTIVE", "---", _sections()]
    path = vault / "01_ARCHITECTURE" / "knowledge" / "parent.md"
    path.write_text(chr(10).join(lines), encoding="utf-8")
    return path


@pytest.fixture
def setup(tmp_path):
    from memory_vault_fixture import make_vault

    vault = make_vault(tmp_path)
    nid = str(uuid.uuid4())
    _write_parent(vault, nid)
    c = _controller(vault)
    plan = plan_split(c, nid)
    assert plan["status"] == "PLANNED"
    return vault, c, nid, plan


def _yes(parent_id, count):
    return True


def _sha(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def test_owner_apply_serves_every_child_exactly(setup):
    from interfaces import memory_access
    from memory_controller.authorizer import Principal

    vault, c, nid, plan = setup
    parent_before = c.storage.get(nid)["content"]
    result = apply_split(c, plan, principal=Principal.HUMAN, confirm=_yes)
    assert result["status"] == "APPLIED"
    assert len(result["children"]) == len(plan["children"])
    for ch in plan["children"]:
        stored = c.storage.get(ch["child_id_proposed"])
        piece = parent_before[ch["char_start"]:ch["char_end"]]
        assert str(stored["lifecycle"]).endswith("ACTIVE") and stored["verification"] == "verified"
        assert stored["attested_content_sha256"] == ch["content_sha256"]
        assert {"type": "part_of", "target_id": nid} in stored["relations"]
        assert memory_access.get(c, ch["child_id_proposed"])["content"] == piece
    assert c.storage.get(nid)["content"] == parent_before  # the parent is never modified


def test_agent_cannot_run_it(setup):
    from memory_controller.authorizer import Principal

    vault, c, nid, plan = setup
    with pytest.raises(PermissionError):
        apply_split(c, plan, principal=Principal.AI_AGENT, confirm=_yes)
    assert all(c.storage.get(ch["child_id_proposed"]) is None for ch in plan["children"])


def test_stale_manifest_is_refused_before_any_write(setup):
    from memory_controller.authorizer import Principal

    vault, c, nid, plan = setup
    note = c.storage.get(nid)
    note["content"] = note["content"] + "one more line" + chr(10)
    c.storage.set(nid, note)
    with pytest.raises(ManifestError, match="stale"):
        apply_split(c, plan, principal=Principal.HUMAN, confirm=_yes)
    assert all(c.storage.get(ch["child_id_proposed"]) is None for ch in plan["children"])


def test_tampered_manifest_is_refused_before_any_write(setup):
    from memory_controller.authorizer import Principal

    vault, c, nid, plan = setup
    bad = json.loads(json.dumps(plan))
    bad["children"][1]["char_start"] += 5
    with pytest.raises(ManifestError):
        apply_split(c, bad, principal=Principal.HUMAN, confirm=_yes)
    assert all(c.storage.get(ch["child_id_proposed"]) is None for ch in plan["children"])


def test_not_confirmed_writes_nothing(setup):
    from memory_controller.authorizer import Principal

    vault, c, nid, plan = setup
    result = apply_split(c, plan, principal=Principal.HUMAN, confirm=lambda p, n: False)
    assert result["status"] == "NOT_CONFIRMED"
    assert all(c.storage.get(ch["child_id_proposed"]) is None for ch in plan["children"])


def test_rerun_skips_applied_children_and_never_duplicates(setup):
    from memory_controller.authorizer import Principal

    vault, c, nid, plan = setup
    apply_split(c, plan, principal=Principal.HUMAN, confirm=_yes)
    again = apply_split(c, plan, principal=Principal.HUMAN, confirm=_yes)
    assert again["status"] == "APPLIED"
    assert all(ch["action"] == "already_applied" for ch in again["children"])


def test_a_child_changed_before_attestation_stops_the_run(setup, monkeypatch):
    """T4: an agent swaps a RAW child between creation and attestation."""
    from memory_controller.authorizer import Principal

    vault, c, nid, plan = setup
    target = plan["children"][1]["child_id_proposed"]
    real_attest = c.attest

    def swap_then_attest(principal, note_id, **kw):
        if note_id == target:
            c.update(Principal.AI_AGENT, note_id, {"content": "SWAPPED BY AN AGENT"})
        return real_attest(principal, note_id, **kw)

    monkeypatch.setattr(c, "attest", swap_then_attest)
    result = apply_split(c, plan, principal=Principal.HUMAN, confirm=_yes)
    assert result["status"] == "FAILED"
    assert result["failed_child"]["index"] == 1 and "changed" in result["failed_child"]["error"]
    assert str(c.storage.get(target)["verification"]) == "unverified"
    later = plan["children"][2]["child_id_proposed"]
    assert c.storage.get(later) is None  # nothing after the failure


def test_cli_dry_run_writes_nothing_and_execute_needs_a_terminal(setup, tmp_path):
    vault, c, nid, plan = setup
    manifest = tmp_path / "plan.json"
    manifest.write_text(json.dumps(plan), encoding="utf-8")
    env = {**os.environ, "PYTHONPATH": str(REPO / "03_IMPLEMENTATION" / "packages"),
           "AI_MEMORY_VAULT_HOME": str(tmp_path / "state")}
    base = [sys.executable, "-m", "active_knowledge_engine", "apply-split", "--manifest", str(manifest),
            "--vault-root", str(vault)]
    dry = subprocess.run(base, capture_output=True, text=True, timeout=300, env=env, stdin=subprocess.DEVNULL)
    assert dry.returncode == 0, dry.stderr
    assert "DRY RUN" in dry.stdout
    assert all(_controller(vault).storage.get(ch["child_id_proposed"]) is None for ch in plan["children"])
    run = subprocess.run(base + ["--execute"], capture_output=True, text=True, timeout=300, env=env,
                         stdin=subprocess.DEVNULL)
    assert run.returncode == 2 and "terminal" in run.stderr
    assert all(_controller(vault).storage.get(ch["child_id_proposed"]) is None for ch in plan["children"])
