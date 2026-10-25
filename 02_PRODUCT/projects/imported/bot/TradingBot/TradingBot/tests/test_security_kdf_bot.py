"""Tests for the salted credential KDF and the legacy-format migration.

security.py is loaded from its file with a stubbed ``core.config`` so the test
never touches the real ~/.tradingbot directory.
"""
import base64
import hashlib
import importlib.util
import json
import sys
import types
from pathlib import Path

import pytest
from cryptography.fernet import Fernet

SECURITY_PY = Path(__file__).resolve().parent.parent / "core" / "security.py"


@pytest.fixture
def security(tmp_path, monkeypatch):
    cfg = types.ModuleType("core.config")
    cfg.CREDENTIALS_FILE = tmp_path / "credentials.enc"
    monkeypatch.setitem(sys.modules, "core.config", cfg)
    if "core" not in sys.modules:
        monkeypatch.setitem(sys.modules, "core", types.ModuleType("core"))
    spec = importlib.util.spec_from_file_location(f"_sec_{id(tmp_path)}", SECURITY_PY)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _legacy_blob(data, password):
    key = base64.urlsafe_b64encode(hashlib.sha256(password.encode()).digest())
    return Fernet(key).encrypt(json.dumps(data).encode())


def test_new_format_roundtrip_uses_random_salt(security):
    data = {"api_key": "k", "secret": "s"}
    security.save_credentials(data, "pw")
    first = security.CREDENTIALS_FILE.read_bytes()
    magic, iterations, salt_b64, _ = first.split(b"$", 3)
    assert magic == b"tbkdf2"
    assert int(iterations) >= 600_000
    assert len(base64.urlsafe_b64decode(salt_b64)) == 16
    assert security.load_credentials("pw") == data
    assert security.load_credentials("wrong") == {}
    security.save_credentials(data, "pw")
    assert security.CREDENTIALS_FILE.read_bytes().split(b"$")[2] != salt_b64


def test_legacy_file_still_decrypts_and_is_migrated(security):
    data = {"broker": "binance", "api_key": "abc"}
    security.CREDENTIALS_FILE.write_bytes(_legacy_blob(data, "pw"))
    assert security.load_credentials("wrong") == {}
    assert not security.CREDENTIALS_FILE.read_bytes().startswith(b"tbkdf2$")
    assert security.load_credentials("pw") == data
    assert security.CREDENTIALS_FILE.read_bytes().startswith(b"tbkdf2$")
    assert security.load_credentials("pw") == data


@pytest.mark.parametrize("rounds", [b"99999", b"10000001", b"999999999999", b"-5", b"abc"])
def test_tampered_iteration_count_is_rejected(security, rounds):
    security.save_credentials({"api_key": "k"}, "pw")
    magic, _, salt_b64, token = security.CREDENTIALS_FILE.read_bytes().split(b"$", 3)
    security.CREDENTIALS_FILE.write_bytes(b"$".join([magic, rounds, salt_b64, token]))
    assert security.load_credentials("pw") == {}


def test_failed_migration_is_logged_and_keeps_legacy_file(security, monkeypatch, caplog):
    data = {"api_key": "abc"}
    blob = _legacy_blob(data, "pw")
    security.CREDENTIALS_FILE.write_bytes(blob)

    def fail(*_args, **_kwargs):
        raise OSError("disk full")

    monkeypatch.setattr(security, "save_credentials", fail)
    with caplog.at_level("WARNING"):
        assert security.load_credentials("pw") == data
    assert "migration failed" in caplog.text
    assert security.CREDENTIALS_FILE.read_bytes() == blob
