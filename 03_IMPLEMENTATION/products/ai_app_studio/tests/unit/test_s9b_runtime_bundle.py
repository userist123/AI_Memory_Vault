"""S9b item 7 - installed-mode packaging: scripts/build_runtime.ps1 stages python + locked deps + Chromium headless
shell + the app sources (kit, images metadata; device host setup stays in the repo) into runtime.zip + runtime.version, smoke-tests imports from the
stage only; the release Tauri config bundles exactly those two files (the dev config needs none of them)."""
from __future__ import annotations

import json
import re
from pathlib import Path

PRODUCT = Path(__file__).resolve().parents[2]
SCRIPT = PRODUCT / "scripts" / "build_runtime.ps1"
TAURI = PRODUCT / "apps" / "desktop" / "src-tauri"


def test_script_stages_everything_the_backend_needs() -> None:
    s = SCRIPT.read_text(encoding="utf-8")
    for d in (r"core\studio_core", r"core\aas_build", r"core\aas_device", r"core\aas_runner", r"apps\backend",
              r"apps\mcp-app", r"apps\device_host", "'kit'", "'images'"):
        assert d in s, d
    assert "uv export --frozen --no-dev" in s and "--link-mode copy" in s
    assert "chromium-headless-shell" in s and "browsers.json" in s
    assert "node_modules" in s  # stripped from the staged kit
    assert "ReparsePoint" in s  # no link enters the zip
    assert "RUNTIME.json" in s and "DIGESTS.json" in s and "kitLockSha256" in s
    assert "-I $smoke" in s and "outside the runtime" in s
    assert "runtime.version" in s and "Substring(0, 16)" in s
    assert "tauri.release.conf.json" in s


def test_release_config_bundles_only_the_runtime() -> None:
    rel = json.loads((TAURI / "tauri.release.conf.json").read_text(encoding="utf-8"))
    assert rel["bundle"]["resources"] == {"runtime/runtime.zip": "runtime/runtime.zip",
                                          "runtime/runtime.version": "runtime/runtime.version"}
    base = json.loads((TAURI / "tauri.conf.json").read_text(encoding="utf-8"))
    assert "resources" not in base["bundle"]  # cargo test / dev builds need no runtime


def test_runtime_outputs_are_ignored_by_git() -> None:
    gi = (PRODUCT / "apps" / "desktop" / ".gitignore").read_text(encoding="utf-8")
    assert re.search(r"^src-tauri/runtime/$", gi, re.M) and re.search(r"^src-tauri/runtime-stage/$", gi, re.M)


def test_shell_reads_the_names_the_script_writes() -> None:
    lib = (TAURI / "src" / "lib.rs").read_text(encoding="utf-8")
    for name in ('"runtime.zip"', '"runtime.version"', '"python.exe"', '"__main__.py"', '"ms-playwright"'):
        assert name in lib, name
