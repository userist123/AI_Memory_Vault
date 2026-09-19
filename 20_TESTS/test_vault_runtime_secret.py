"""The HMAC secret: generated locally once, outside the repo, private, never printed or logged.

`recall_cli` used to fail on first run with a raw "environment variable is missing" and nothing
in the repository said where the secret should come from. These tests pin the new behaviour:
`--init-secret` creates it, the environment still wins, and a missing secret is a message that
names the command to run.
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from interfaces import vault_runtime as vr  # noqa: E402

CLI = [sys.executable, "-m", "cognitive_core.recall_cli"]


@pytest.fixture
def home(tmp_path, monkeypatch):
    monkeypatch.setenv(vr.HOME_ENV, str(tmp_path / "state"))
    monkeypatch.delenv(vr.SECRET_ENV, raising=False)
    return tmp_path / "state"


def _cli_env(home_dir: Path, **extra):
    env = {k: v for k, v in os.environ.items() if k != vr.SECRET_ENV}
    env[vr.HOME_ENV] = str(home_dir)
    env.update(extra)
    return env


def leaks(secret: str, *texts: str) -> bool:
    """True if the secret (or its first 16 characters) appears in any of the texts."""
    return any(secret in t or secret[:16] in t for t in texts)


# --- creation --------------------------------------------------------------------------

def test_init_creates_a_long_secret_once_and_never_overwrites(home):
    path, created = vr.init_secret()
    assert created and path == home / "hmac.key"
    first = path.read_text(encoding="utf-8").strip()
    assert len(first) >= vr.MIN_SECRET_LENGTH
    again, created_again = vr.init_secret()
    assert not created_again and again.read_text(encoding="utf-8").strip() == first


def test_force_replaces_the_secret(home):
    path, _ = vr.init_secret()
    before = path.read_text(encoding="utf-8")
    vr.init_secret(force=True)
    assert path.read_text(encoding="utf-8") != before


def test_secret_file_is_readable_only_by_the_user(home):
    path, _ = vr.init_secret()
    private, why = vr.secret_file_is_private(path)
    assert private, why


def test_negative_control_a_world_readable_secret_is_refused(home):
    if sys.platform == "win32":
        pytest.skip("checked through icacls; the POSIX mode check is exercised on Linux")
    path, _ = vr.init_secret()
    os.chmod(path, 0o644)
    assert not vr.secret_file_is_private(path)[0]
    with pytest.raises(vr.VaultSecretInvalid, match="not private"):
        vr.load_secret()


def test_negative_control_a_secret_open_to_everyone_is_detected_on_windows(home):
    if sys.platform != "win32":
        pytest.skip("Windows access lists")
    path, _ = vr.init_secret()
    subprocess.run(["icacls", str(path), "/grant", "Everyone:R"], capture_output=True, check=True)
    private, why = vr.secret_file_is_private(path)
    assert not private and "Everyone" in why


# --- precedence and failure messages ----------------------------------------------------

def test_environment_wins_over_the_file(home, monkeypatch):
    vr.init_secret()
    monkeypatch.setenv(vr.SECRET_ENV, "e" * 40)
    value, source = vr.load_secret()
    assert (value, source) == ("e" * 40, "env")


def test_file_is_used_when_the_environment_is_empty(home):
    path, _ = vr.init_secret()
    value, source = vr.load_secret()
    assert source == "file" and value == path.read_text(encoding="utf-8").strip()
    assert vr.ensure_secret_in_env() == "file" and os.environ[vr.SECRET_ENV] == value


def test_missing_everywhere_names_the_command(home):
    with pytest.raises(vr.VaultSecretMissing) as err:
        vr.load_secret()
    assert vr.INIT_COMMAND in str(err.value) and vr.SECRET_ENV in str(err.value)


def test_a_too_short_environment_value_is_invalid_and_not_echoed(home, monkeypatch):
    monkeypatch.setenv(vr.SECRET_ENV, "short-secret-value")
    with pytest.raises(vr.VaultSecretInvalid) as err:
        vr.load_secret()
    assert "short-secret-value" not in str(err.value)


def test_cli_without_a_secret_is_a_message_not_a_traceback(tmp_path):
    res = subprocess.run(CLI + ["--query", "anything"], capture_output=True, text=True,
                         cwd=REPO, env=_cli_env(tmp_path / "empty"))
    assert res.returncode == 2
    assert "--init-secret" in res.stderr and vr.SECRET_ENV in res.stderr
    assert "Traceback" not in res.stderr


# --- the secret never leaves the machine's private directory ------------------------------

def test_secret_is_not_in_any_output_log_or_the_repo(tmp_path):
    state = tmp_path / "state"
    env = _cli_env(state)
    init = subprocess.run(CLI + ["--init-secret"], capture_output=True, text=True, cwd=REPO, env=env)
    assert init.returncode == 0, init.stderr
    secret = (state / "hmac.key").read_text(encoding="utf-8").strip()
    assert secret and not leaks(secret, init.stdout, init.stderr)

    search = subprocess.run(CLI + ["--query", "invariante de incredere", "--max", "2"],
                            capture_output=True, text=True, cwd=REPO, env=env, timeout=600)
    assert search.returncode == 0, search.stderr
    assert not leaks(secret, search.stdout, search.stderr)

    # everything the run wrote, except the key itself: usage log, audit log, traces
    written = [p for p in state.rglob("*") if p.is_file() and p.name != "hmac.key"]
    assert (state / "usage.jsonl") in written
    for path in written:
        assert not leaks(secret, path.read_text(encoding="utf-8", errors="replace")), path.name

    tracked = subprocess.run(["git", "ls-files", "-z"], capture_output=True, cwd=REPO).stdout.split(b"\0")
    assert not any(name.endswith(b"hmac.key") for name in tracked)


def test_negative_control_the_leak_check_catches_a_leak():
    assert leaks("s" * 48, "the value is " + "s" * 48)
    assert leaks("abcdefghijklmnopqrstuvwxyz0123456789", "prefix abcdefghijklmnop only")
    assert not leaks("s" * 48, "nothing here")


def test_init_secret_output_names_the_path_but_not_the_value(tmp_path):
    state = tmp_path / "state"
    res = subprocess.run(CLI + ["--init-secret"], capture_output=True, text=True, cwd=REPO, env=_cli_env(state))
    assert str(state / "hmac.key") in res.stdout
    res2 = subprocess.run(CLI + ["--init-secret"], capture_output=True, text=True, cwd=REPO, env=_cli_env(state))
    assert "deja existent" in res2.stdout
