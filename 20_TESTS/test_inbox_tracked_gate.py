"""`06_INBOX/` is gitleaks-allowlisted, so nothing may be force-added there.

`.gitleaks.toml` skips `06_INBOX/RAW_IMPORTS/`: a file `git add -f`-ed into it
is never secret-scanned. The hygiene validator (run in CI) is the gate: only
the README and the `.gitkeep` may be tracked under `06_INBOX/`.
"""
from __future__ import annotations

import importlib.util
import pathlib
import subprocess

_REPO = pathlib.Path(__file__).resolve().parents[1]
_SPEC = importlib.util.spec_from_file_location(
    "repository_hygiene", _REPO / "30_SCRIPTS" / "verification" / "repository_hygiene.py"
)
hygiene = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(hygiene)


def _repo(tmp_path: pathlib.Path, files: dict[str, str], force: list[str]) -> pathlib.Path:
    subprocess.run(["git", "init", "-q", str(tmp_path)], check=True)
    (tmp_path / ".gitignore").write_text("06_INBOX/RAW_IMPORTS/\n", encoding="utf-8")
    for rel, content in files.items():
        target = tmp_path / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
    subprocess.run(["git", "-C", str(tmp_path), "add", "-A"], check=True)
    for rel in force:
        subprocess.run(["git", "-C", str(tmp_path), "add", "-f", rel], check=True)
    return tmp_path


def test_force_added_raw_import_fails_the_hygiene_run(tmp_path):
    root = _repo(
        tmp_path,
        {"06_INBOX/README.md": "inbox", "06_INBOX/.gitkeep": "",
         "06_INBOX/RAW_IMPORTS/dump.json": '{"token": "synthetic"}'},
        force=["06_INBOX/RAW_IMPORTS/dump.json"],
    )
    assert "INBOX_TRACKED:06_INBOX/RAW_IMPORTS/dump.json" in hygiene.run(root)


def test_only_readme_and_gitkeep_pass(tmp_path):
    root = _repo(tmp_path, {"06_INBOX/README.md": "inbox", "06_INBOX/.gitkeep": ""}, force=[])
    assert not [f for f in hygiene.run(root) if f.startswith("INBOX_TRACKED")]


def test_this_repository_tracks_only_the_inbox_placeholders():
    out = subprocess.run(["git", "ls-files", "06_INBOX"], cwd=_REPO,
                         capture_output=True, text=True, check=True).stdout.split()
    assert set(out) <= {"06_INBOX/README.md", "06_INBOX/.gitkeep"}, out
