"""Runtime configuration. Paths default to <repo>/data and can be overridden by AWS_DATA_DIR."""

from __future__ import annotations

import os
import secrets
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


@dataclass(frozen=True)
class Settings:
    data_dir: Path
    api_host: str = "127.0.0.1"
    api_port: int = 17865
    max_body_bytes: int = 2 * 1024 * 1024
    allowed_origins: tuple[str, ...] = (
        "tauri://localhost",
        "http://tauri.localhost",
        "https://tauri.localhost",
        "http://localhost:1420",
        "http://127.0.0.1:1420",
    )
    agent_id: str = "claude-code"
    capture_timeout_ms: int = 15000
    visual_loop_max_iterations: int = 5
    visual_loop_max_seconds: int = 600
    extra: dict[str, str] = field(default_factory=dict)
    database_url: str | None = None
    git_enabled: bool = True  # commit every revision to the project's git repo (spec §11)

    @property
    def db_path(self) -> Path:
        return self.data_dir / "studio.db"

    @property
    def db_url(self) -> str:
        """SQLite by default; AWS_DATABASE_URL (e.g. postgresql+psycopg://...) selects another engine."""
        return self.database_url or f"sqlite:///{self.db_path.as_posix()}"

    @property
    def projects_dir(self) -> Path:
        return self.data_dir / "projects"

    @property
    def config_dir(self) -> Path:
        return self.data_dir / "config"

    def ensure_dirs(self) -> None:
        for d in (self.data_dir, self.projects_dir, self.config_dir):
            d.mkdir(parents=True, exist_ok=True)

    def api_token(self) -> str:
        """Per-install owner token. Created once with 256 bits of entropy; never logged."""
        self.ensure_dirs()
        path = self.config_dir / "api_token"
        if not path.exists():
            path.write_text(secrets.token_urlsafe(32), encoding="utf-8")
        return path.read_text(encoding="utf-8").strip()


def load_settings() -> Settings:
    data_dir = Path(os.environ.get("AWS_DATA_DIR", REPO_ROOT / "data")).resolve()
    port = int(os.environ.get("AWS_API_PORT", "17865"))
    agent = os.environ.get("AWS_AGENT_ID", "claude-code")
    return Settings(data_dir=data_dir, api_port=port, agent_id=agent, database_url=os.environ.get("AWS_DATABASE_URL") or None)
