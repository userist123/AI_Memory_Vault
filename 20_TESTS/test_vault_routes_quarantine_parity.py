"""A note flagged `quarantined: true` is refused by every agent route, not only by memory_get.

memory_get already refuses it; the direct routes (vault_read, vault_get_metadata,
vault_check_quotes) ask MemoryController through `_note_eligibility` and used to accept
the body-withheld record `_readable` returns for a quarantined ACTIVE note. Both adapters
(the MCP server and interfaces/vault_cli, which the Telegram bot reuses) are checked.
"""
from __future__ import annotations

import importlib.util
import os
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

_spec = importlib.util.spec_from_file_location("memory_vault_fixture", REPO / "20_TESTS" / "memory_vault_fixture.py")
fx = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fx)

from interfaces import memory_access as ma  # noqa: E402
from interfaces import memory_mcp_server as server  # noqa: E402
from interfaces import vault_cli  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from vault_access.audit import AuditLog  # noqa: E402
from vault_access.core import VaultAccess  # noqa: E402
from vault_access.policy import AccessPolicy  # noqa: E402
from vault_access.router import DomainRouter  # noqa: E402

QUARANTINED = ("active_quarantined", "review_quarantined")
SERVED = ("active_verified", "active_unstamped", "review_unverified")


@pytest.fixture
def world(tmp_path, monkeypatch):
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_path / "artifacts"))
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path / "telemetry"))
    vault = fx.make_vault(tmp_path)
    seeded = fx.seed_real_distribution(vault)
    controller = MemoryController(FileStorageEngine(str(vault)))

    cfg = REPO / "04_CONFIG"
    policy = AccessPolicy.load(cfg / "access_policy.yaml")
    router = DomainRouter(vault, cfg / "vault_domains.yaml", policy, cache_path=False)
    access = VaultAccess("cloud_cli.claude_code", "mcp", repo_root=vault, policy=policy, router=router,
                         audit=AuditLog(enabled=False), note_eligibility=server._note_eligibility)
    monkeypatch.setattr(server, "_controller", controller)
    monkeypatch.setattr(server, "_access", access)
    router.build()
    router.load_all_meta()
    uris = {r.note_id: r.uri for r in router.routes.values() if r.note_id}
    return controller, seeded, uris


def _uri(world, kind):
    _, seeded, uris = world
    return uris[seeded[kind][0]]


def _quote(world, kind):
    _, seeded, _ = world
    return [{"uri": _uri(world, kind), "quote": f"contine cuvantul {seeded[kind][1]}"}]


@pytest.mark.parametrize("kind", QUARANTINED)
def test_a_quarantined_note_is_refused_by_memory_get_and_every_vault_route(world, kind):
    controller, seeded, _ = world
    note_id, word = seeded[kind]
    with pytest.raises(ValueError):
        ma.get(controller, note_id)

    uri = _uri(world, kind)
    read = server.vault_read(uri)
    assert not read["ok"] and read["code"] == "NOT_FOUND", read
    assert word not in str(read)
    meta = server.vault_get_metadata(uri)
    assert not meta["ok"] and meta["code"] == "NOT_FOUND", meta
    checks = server.vault_check_quotes(_quote(world, kind))
    assert checks["all_ok"] is False and checks["results"][0]["reason"] == "NOT_FOUND", checks

    assert server._note_eligibility(note_id) is False


@pytest.mark.parametrize("kind", SERVED)
def test_a_note_that_is_not_quarantined_is_still_served(world, kind):
    _, seeded, _ = world
    note_id, word = seeded[kind]
    read = server.vault_read(_uri(world, kind))
    assert read["ok"], read
    assert any(word in e["text"] for e in read["evidence"])
    assert server.vault_get_metadata(_uri(world, kind))["ok"]
    assert server.vault_check_quotes(_quote(world, kind))["all_ok"] is True
    assert server._note_eligibility(note_id) is True


@pytest.mark.parametrize("kind,expected", [(k, False) for k in QUARANTINED] + [(k, True) for k in SERVED])
def test_vault_cli_eligibility_matches_the_mcp_server(world, monkeypatch, kind, expected):
    """interfaces/vault_cli._note_eligibility (also the Telegram bot's) gives the same verdict."""
    controller, seeded, _ = world
    from interfaces import recall_cli, vault_runtime
    monkeypatch.setattr(recall_cli, "get_memory_controller", lambda: controller)
    # Runtime setup (HMAC secret file, runtime dirs) is not under test here; other suites
    # leave the process environment in states where it raises, which hides the verdict.
    monkeypatch.setattr(vault_runtime, "ensure_secret_in_env", lambda: None)
    monkeypatch.setattr(vault_runtime, "configure_runtime_dirs", lambda: None)
    assert vault_cli._note_eligibility(seeded[kind][0]) is expected


def test_a_verdict_that_raises_is_a_refusal(world, monkeypatch):
    """Fail closed: if the controller is there but the verdict cannot be computed, refuse."""
    controller, seeded, _ = world
    monkeypatch.setattr(ma, "_readable", lambda *_a, **_k: (_ for _ in ()).throw(RuntimeError("boom")))
    assert vault_cli.eligible_for_agent(controller, seeded["active_verified"][0]) is False


def test_without_a_controller_verdict_the_frontmatter_flag_still_withholds(world, monkeypatch):
    """R-02: when no MemoryController verdict is available (None), `quarantined: true` in the
    note's own frontmatter is still enough to refuse it; a clean note is still served."""
    controller, seeded, uris = world
    access = server._access
    monkeypatch.setattr(access, "note_eligibility", lambda _note_id: None)
    refused = access.read(uris[seeded["active_quarantined"][0]])
    assert not refused["ok"] and refused["code"] == "NOT_FOUND", refused
    assert access.read(uris[seeded["active_verified"][0]])["ok"]
