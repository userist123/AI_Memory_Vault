"""Recovery: online SQLite backup (as scripts/update.ps1 does) -> loss -> restore -> state, history and audit intact."""

from __future__ import annotations

import sqlite3
from pathlib import Path

from studio_core.config import Settings
from studio_core.services import Studio

from tests.conftest import HUMAN


def _backup(src: Path, dst: Path) -> None:
    s, d = sqlite3.connect(src), sqlite3.connect(dst)
    s.backup(d)
    d.close()
    s.close()


def test_backup_restore_roundtrip(tmp_path: Path) -> None:
    settings = Settings(data_dir=tmp_path / "data")
    st = Studio(settings)
    pid = st.create_project(HUMAN, "Recover")["id"]  # AAS: NullKind, no website template
    st.write_file(HUMAN, pid, 1, "hero.txt", "Before backup")  # AAS: file instead of the hero component
    backup = tmp_path / "studio-backup.db"
    _backup(settings.db_path, backup)

    st.write_file(HUMAN, pid, 2, "hero.txt", "After backup (lost)")
    st.engine.dispose()
    for suffix in ("", "-wal", "-shm"):
        Path(str(settings.db_path) + suffix).unlink(missing_ok=True)  # simulate loss of the database

    backup.replace(settings.db_path)
    st2 = Studio(settings)
    assert st2.verify_audit_chain()["valid"] is False  # anchor detects the rollback ...
    st2.acknowledge_restore(HUMAN)  # ... until the owner acknowledges it
    p = st2.get_project(HUMAN, pid)
    assert p["revision"] == 2
    assert st2.read_file(HUMAN, pid, "hero.txt")["content"] == "Before backup"
    assert [h["revision"] for h in st2.history(HUMAN, pid)][:2] == [2, 1]
    assert st2.verify_audit_chain()["valid"] is True
    # the project keeps working after recovery (site is re-rendered on the next revision)
    st2.write_file(HUMAN, pid, 2, "hero.txt", "Recovered")
    html = (st2.site_dir(pid) / "index.html").read_text(encoding="utf-8")
    assert st2.read_file(HUMAN, pid, "hero.txt")["content"] == "Recovered" and 'data-revision="3"' in html


def test_snapshot_recovery_restores_any_revision(tmp_path: Path) -> None:
    st = Studio(Settings(data_dir=tmp_path / "data"))
    pid = st.create_project(HUMAN, "Snap")["id"]  # AAS: NullKind; rev 2 holds the file (was the template hero)
    st.write_file(HUMAN, pid, 1, "hero.txt", "hero")
    st.delete_file(HUMAN, pid, 2, "hero.txt")  # rev 3: hero gone
    st.restore_revision(HUMAN, pid, 3, target=2)  # rev 4 == rev 2
    assert any(f["path"] == "hero.txt" for f in st.list_files(HUMAN, pid))
