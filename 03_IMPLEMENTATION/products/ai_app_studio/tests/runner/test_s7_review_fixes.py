"""S7 review L2 (stale grants purged only after the API port is bound, and never those of a live run) and L4."""
from __future__ import annotations

import socket
import threading
from pathlib import Path

import pytest
import uvicorn
from aas_runner import claude
from aas_runner.claude import Runner
from apps.backend import main as backend
from studio_core import run_grants
from studio_core.config import Settings
from studio_core.services import Studio

from .conftest import OWNER

LIVE, STALE = "run_aaaaaaaaaaaaaaaa", "run_bbbbbbbbbbbbbbbb"
TOKEN = "t" * 40


def _project(studio: Studio) -> str:
    return studio.create_project(OWNER, "alpha", "blank", "")["id"]


def test_purge_keeps_the_grant_of_a_run_that_is_alive(studio: Studio, fake_cli: list[str]) -> None:
    pid = _project(studio)
    r = Runner(studio, cli=fake_cli)
    stop = threading.Event()
    t = threading.Thread(target=stop.wait, daemon=True)
    t.start()
    live = claude._Run(LIVE, pid)
    live.thread = t
    r._runs[LIVE] = live
    run_grants.issue(studio, LIVE, pid)
    run_grants.issue(studio, STALE, pid)
    root = studio.settings.data_dir / "runner"
    root.mkdir(parents=True, exist_ok=True)
    for rid in (LIVE, STALE):
        (root / f"{rid}.mcp.json").write_text("{}", encoding="utf-8")
        (root / "runs" / rid).mkdir(parents=True)
    r.purge_stale()
    stop.set()
    assert run_grants.project_of(studio, LIVE) == pid and run_grants.project_of(studio, STALE) is None
    assert (root / f"{LIVE}.mcp.json").exists() and not (root / f"{STALE}.mcp.json").exists()
    assert (root / "runs" / LIVE).exists() and not (root / "runs" / STALE).exists()


def _app(studio: Studio, fake_cli: list[str], port: int):  # type: ignore[no-untyped-def]
    st = Settings(data_dir=studio.settings.data_dir, api_port=port)
    return backend.create_app(st, TOKEN, runner_cli=fake_cli)


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return int(s.getsockname()[1])


def test_second_backend_that_cannot_bind_leaves_a_live_grant_alone(studio: Studio, fake_cli: list[str],
                                                                   monkeypatch: pytest.MonkeyPatch) -> None:
    pid = _project(studio)
    run_grants.issue(studio, LIVE, pid)  # the first backend's live run
    port = _free_port()
    first = backend.bind_api_socket(port)  # the first backend owns the port
    try:
        app = _app(studio, fake_cli, port)  # building the app (a Runner) must not purge anything
        assert run_grants.project_of(studio, LIVE) == pid
        monkeypatch.setattr(uvicorn.Server, "run", lambda self, sockets=None: pytest.fail("must not serve"))
        with pytest.raises(SystemExit):
            backend.serve(app, port)
        assert run_grants.project_of(studio, LIVE) == pid
    finally:
        first.close()


def test_the_backend_that_owns_the_port_purges_stale_grants(studio: Studio, fake_cli: list[str],
                                                            monkeypatch: pytest.MonkeyPatch) -> None:
    pid = _project(studio)
    run_grants.issue(studio, STALE, pid)  # left by a crashed backend
    port = _free_port()
    seen: dict[str, object] = {}
    monkeypatch.setattr(uvicorn.Server, "run", lambda self, sockets=None: seen.update(
        addr=sockets[0].getsockname(), grants=run_grants.count(studio)))  # type: ignore[index]
    backend.serve(_app(studio, fake_cli, port), port)
    assert seen == {"addr": ("127.0.0.1", port), "grants": 0}
