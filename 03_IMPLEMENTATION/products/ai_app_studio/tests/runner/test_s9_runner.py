"""S9a runner hardening: B6 (one live run at a time -> 409) and B7 (session persistence off, auto-updater off, the CLI
resolved to an absolute path once, user/project memory not loaded under the runner flags)."""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import pytest
from aas_runner import claude as rc
from studio_core.errors import ConflictError, Unavailable

from .conftest import OWNER

RESULT = {"type": "result", "subtype": "success", "num_turns": 1, "total_cost_usd": 0.0, "duration_ms": 5, "usage": {}}


def scenario(tmp_path: Path, **kw: object) -> str:
    return json.dumps({"record": str(tmp_path / "rec.json"), **kw})


# ---------------------------------------------------------------- B7 static
def test_argv_disables_session_persistence(tmp_path: Path) -> None:
    argv = rc.build_argv(cli=["C:/x/claude.exe"], model="sonnet", mcp_config=tmp_path / "m.json",
                         settings=tmp_path / "s.json", max_turns=2, max_budget_usd=0.1)
    assert "--no-session-persistence" in argv
    assert argv[argv.index("--setting-sources") + 1] == ""  # unchanged: no user/project/local settings


def test_runner_env_disables_the_auto_updater(tmp_path: Path) -> None:
    env = rc.runner_env(tmp_path, base={"PATH": "x", "DISABLE_AUTOUPDATER": "0"})
    assert env["DISABLE_AUTOUPDATER"] == "1"


def test_resolve_cli_returns_an_absolute_path_or_none(tmp_path: Path) -> None:
    exe = tmp_path / ("claude.exe" if os.name == "nt" else "claude")
    exe.write_bytes(b"")
    exe.chmod(0o755)
    got = rc.resolve_cli(path=str(tmp_path))
    assert got is not None and os.path.isabs(got[0]) and Path(got[0]).name.lower().startswith("claude")
    assert rc.resolve_cli(path=str(tmp_path / "nowhere")) is None


def test_runner_refuses_a_relative_cli(studio, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p", "blank", "")["id"]
    with pytest.raises(Unavailable):
        rc.Runner(studio, cli=["claude"]).start(pid, "hi")
    with pytest.raises(Unavailable):
        rc.Runner(studio, cli=[]).start(pid, "hi")


def test_backend_resolves_the_cli_once_at_startup(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    from apps.backend.main import create_app
    from studio_core.config import Settings
    calls: list[int] = []

    def fake_resolve(path: str | None = None) -> list[str] | None:
        calls.append(1)
        return [str(tmp_path / "claude.exe")]
    monkeypatch.setattr(rc, "resolve_cli", fake_resolve)
    app = create_app(Settings(data_dir=tmp_path / "d"), "t" * 40)
    assert calls == [1]
    assert app.state.runner.cli == [str(tmp_path / "claude.exe")]


# ---------------------------------------------------------------- B6
def test_a_second_live_run_is_refused_with_409(studio, fake_cli, tmp_path: Path) -> None:
    a = studio.create_project(OWNER, "a", "blank", "")["id"]
    b = studio.create_project(OWNER, "b", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    first = r.start(a, scenario(tmp_path, sleep=20, events=[RESULT]))
    try:
        with pytest.raises(ConflictError) as e:
            r.start(b, scenario(tmp_path / "x", events=[RESULT]))
        assert e.value.http_status == 409
        with pytest.raises(ConflictError):
            r.start(a, scenario(tmp_path / "y", events=[RESULT]))
    finally:
        r.stop(first)
        r.wait(first, 30)
    # after the first run ended a new one starts (positive control)
    second = r.start(b, scenario(tmp_path, events=[RESULT]))
    assert r.wait(second, 30) == "success"


def test_parallel_starts_admit_exactly_one(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    out: list[object] = []
    barrier = threading.Barrier(4)

    def go(i: int) -> None:
        barrier.wait()
        try:
            out.append(r.start(pid, scenario(tmp_path / str(i), sleep=5, events=[RESULT])))
        except ConflictError as e:
            out.append(e)
    for d in range(4):
        (tmp_path / str(d)).mkdir()
    ts = [threading.Thread(target=go, args=(i,)) for i in range(4)]
    [t.start() for t in ts]
    [t.join(30) for t in ts]
    started = [x for x in out if isinstance(x, str)]
    assert len(started) == 1 and sum(isinstance(x, ConflictError) for x in out) == 3
    r.stop(started[0])
    r.wait(started[0], 30)


def test_rest_second_run_is_409(tmp_path: Path, fake_cli) -> None:  # type: ignore[no-untyped-def]
    from fastapi.testclient import TestClient

    from apps.backend.main import create_app
    from studio_core.config import Settings
    tok = "k" * 40
    app = create_app(Settings(data_dir=tmp_path / "d"), tok, runner_cli=fake_cli)
    h = {"Authorization": f"Bearer {tok}", "Host": "127.0.0.1:17875"}
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        pid = c.post("/api/v1/projects", json={"name": "p"}, headers=h).json()["id"]
        r1 = c.post("/api/v1/runs", json={"projectId": pid, "prompt": scenario(tmp_path, sleep=20, events=[RESULT])},
                    headers=h)
        assert r1.status_code == 202
        r2 = c.post("/api/v1/runs", json={"projectId": pid, "prompt": scenario(tmp_path, events=[RESULT])}, headers=h)
        assert r2.status_code == 409
        c.post(f"/api/v1/runs/{r1.json()['runId']}/stop", headers=h)


# ---------------------------------------------------------------- B7 live probe against the installed CLI (no cost)
_SSE = [
    ("message_start", {"type": "message_start", "message": {
        "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-sonnet-4-5", "content": [],
        "stop_reason": None, "stop_sequence": None, "usage": {"input_tokens": 1, "output_tokens": 1}}}),
    ("content_block_start", {"type": "content_block_start", "index": 0, "content_block": {"type": "text", "text": ""}}),
    ("content_block_delta", {"type": "content_block_delta", "index": 0, "delta": {"type": "text_delta", "text": "ok"}}),
    ("content_block_stop", {"type": "content_block_stop", "index": 0}),
    ("message_delta", {"type": "message_delta", "delta": {"stop_reason": "end_turn", "stop_sequence": None},
                       "usage": {"output_tokens": 1}}),
    ("message_stop", {"type": "message_stop"}),
]


class _Capture:
    """Loopback stand-in for the model API: records every request body, answers with a one-word stream."""

    def __init__(self) -> None:
        self.bodies: list[bytes] = []
        cap = self

        class H(BaseHTTPRequestHandler):
            def log_message(self, *_a: object) -> None:
                pass

            def do_POST(self) -> None:  # noqa: N802
                cap.bodies.append(self.rfile.read(int(self.headers.get("content-length", "0"))))
                if "count_tokens" in self.path:
                    b = b'{"input_tokens": 10}'
                    self.send_response(200)
                    self.send_header("content-type", "application/json")
                    self.send_header("content-length", str(len(b)))
                    self.end_headers()
                    self.wfile.write(b)
                    return
                self.send_response(200)
                self.send_header("content-type", "text/event-stream")
                self.end_headers()
                for ev, d in _SSE:
                    self.wfile.write(f"event: {ev}\ndata: {json.dumps(d)}\n\n".encode())

        self.srv = ThreadingHTTPServer(("127.0.0.1", 0), H)
        self.port = self.srv.server_address[1]
        threading.Thread(target=self.srv.serve_forever, daemon=True).start()

    def text(self) -> str:
        return b"\n".join(self.bodies).decode("utf-8", "replace")


REAL = rc.resolve_cli()


@pytest.mark.skipif(REAL is None, reason="Claude Code CLI not installed")
def test_installed_cli_loads_no_user_or_project_memory_and_saves_no_session(tmp_path: Path) -> None:
    """Fixture user home with ~/.claude/CLAUDE.md (marker U) and a CLAUDE.md in a parent of the cwd (marker P); the
    model API is a loopback capture server and the key is a dummy (no login, no cost). Positive control: a plain
    ``claude -p`` sends both markers and writes a session transcript; the runner's argv + env sends neither and
    writes no transcript."""
    assert REAL is not None
    home = tmp_path / "home"
    (home / ".claude").mkdir(parents=True)
    (home / ".claude" / "CLAUDE.md").write_text("MARKER_U_5d1c remember", encoding="utf-8")
    proj = tmp_path / "proj"
    (proj / "runs").mkdir(parents=True)
    (proj / "CLAUDE.md").write_text("MARKER_P_5d1c parent memory", encoding="utf-8")
    mcp = tmp_path / "mcp.json"
    mcp.write_text(json.dumps({"mcpServers": {}}), encoding="utf-8")
    st = tmp_path / "settings.json"
    st.write_text(json.dumps(rc.SETTINGS_JSON), encoding="utf-8")

    def run(argv: list[str], cwd: Path) -> tuple[_Capture, subprocess.CompletedProcess[bytes]]:
        cap = _Capture()
        base = {k: os.environ[k] for k in ("SYSTEMROOT", "WINDIR", "SYSTEMDRIVE", "PATH", "PATHEXT", "COMSPEC")
                if k in os.environ}
        base.update(USERPROFILE=str(home), HOME=str(home), APPDATA=str(home / "AppData" / "Roaming"),
                    LOCALAPPDATA=str(home / "AppData" / "Local"))
        env = rc.runner_env(tmp_path, base=base)
        # test-only: dummy key + loopback API (the runner itself never passes these)
        env.update(ANTHROPIC_API_KEY="sk-ant-dummy-probe", ANTHROPIC_BASE_URL=f"http://127.0.0.1:{cap.port}",
                   CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC="1")
        try:
            p = subprocess.run(argv, input=b"say ok", cwd=cwd, env=env, capture_output=True, timeout=180,  # noqa: S603
                               check=False)
        finally:
            cap.srv.shutdown()
        return cap, p

    def sessions() -> list[Path]:
        d = home / ".claude" / "projects"
        return list(d.rglob("*.jsonl")) if d.is_dir() else []

    c1 = proj / "runs" / "plain"
    c1.mkdir()
    cap, p = run([*REAL, "-p", "--output-format", "stream-json", "--verbose"], c1)
    assert p.returncode == 0, p.stderr[-500:]
    assert "MARKER_U_5d1c" in cap.text() and "MARKER_P_5d1c" in cap.text(), "positive control failed"
    assert sessions(), "positive control: a plain run writes a session transcript"
    for s in sessions():
        s.unlink()

    c2 = proj / "runs" / "runner"
    c2.mkdir()
    argv = rc.build_argv(cli=REAL, model="sonnet", mcp_config=mcp, settings=st, max_turns=2, max_budget_usd=0.05)
    cap, p = run(argv, c2)
    assert p.returncode == 0, p.stderr[-500:]
    assert cap.bodies, "the CLI never reached the loopback API"
    assert "MARKER_U_5d1c" not in cap.text()
    assert "MARKER_P_5d1c" not in cap.text()
    assert sessions() == []
    init = json.loads(next(ln for ln in p.stdout.decode().splitlines() if '"subtype":"init"' in ln))
    assert init["tools"] == [] and init["mcp_servers"] == []


@pytest.mark.skipif(shutil.which("claude") is None, reason="Claude Code CLI not installed")
def test_installed_cli_documents_the_flags() -> None:
    assert REAL is not None
    out = subprocess.run([*REAL, "--help"], capture_output=True, timeout=60, check=False).stdout.decode()  # noqa: S603
    assert "--no-session-persistence" in out and "--setting-sources" in out and "--strict-mcp-config" in out
    assert sys.platform != "win32" or REAL[0].lower().endswith(".exe")
