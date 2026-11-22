"""AAS identity (DESIGN.md section 1): AAS_* env only, own data root and ports; AWS_* is ignored."""

from __future__ import annotations

from pathlib import Path

import pytest
from studio_core.config import load_settings


@pytest.fixture()
def clean_env(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> Path:
    for k in ("AAS_DATA_DIR", "AAS_API_PORT", "AAS_AGENT_ID", "AAS_DATABASE_URL", "AAS_PREVIEW_PORT"):
        monkeypatch.delenv(k, raising=False)
    local = tmp_path / "Local"
    monkeypatch.setenv("LOCALAPPDATA", str(local))
    return local


def test_defaults(clean_env: Path) -> None:
    s = load_settings()
    assert s.data_dir == (clean_env / "AIAppStudio").resolve()
    assert s.api_port == 17875 and s.preview_port == 17880 and s.api_host == "127.0.0.1"
    assert s.agent_id == "claude-code" and s.database_url is None


def test_aas_env_overrides(clean_env: Path, monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    monkeypatch.setenv("AAS_DATA_DIR", str(tmp_path / "d"))
    monkeypatch.setenv("AAS_API_PORT", "18999")
    monkeypatch.setenv("AAS_AGENT_ID", "x")
    s = load_settings()
    assert s.data_dir == (tmp_path / "d").resolve() and s.api_port == 18999 and s.agent_id == "x"


def test_aws_env_is_ignored(clean_env: Path, monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    monkeypatch.setenv("AWS_DATA_DIR", str(tmp_path / "aws"))
    monkeypatch.setenv("AWS_API_PORT", "17865")
    monkeypatch.setenv("AWS_AGENT_ID", "aws-agent")
    monkeypatch.setenv("AWS_DATABASE_URL", "sqlite:///x.db")
    s = load_settings()
    assert s.data_dir == (clean_env / "AIAppStudio").resolve()
    assert s.api_port == 17875 and s.agent_id == "claude-code" and s.database_url is None
