"""S9b review L3 - runtime bundle: only git-tracked app sources, Python deps installed with hashes, no reparse point
anywhere in the stage (python/, ms-playwright/, app/), runtime.version = the full runtime.zip sha256, verified by the
shell (lib.rs) before extraction."""
from __future__ import annotations

import re
from pathlib import Path

PRODUCT = Path(__file__).resolve().parents[2]
SCRIPT = PRODUCT / "scripts" / "build_runtime.ps1"
LIB = PRODUCT / "apps" / "desktop" / "src-tauri" / "src" / "lib.rs"


def test_only_git_tracked_sources_are_staged() -> None:
    s = SCRIPT.read_text(encoding="utf-8")
    assert "ls-files" in s and "-z" in s
    assert "Copy-Item (Join-Path $Root $d) $dst -Recurse" not in s  # no recursive copy of the working tree


def test_dependencies_are_hash_checked() -> None:
    s = SCRIPT.read_text(encoding="utf-8")
    assert "--no-hashes" not in s
    assert "--require-hashes" in s


def test_reparse_check_covers_the_whole_stage() -> None:
    s = SCRIPT.read_text(encoding="utf-8")
    assert re.search(r"Get-ChildItem \$Stage -Recurse -Force \| Where-Object \{ \$_\.Attributes -band \[IO\.FileAttributes\]::ReparsePoint \}", s)


def test_version_file_carries_the_full_sha_and_the_shell_verifies_it() -> None:
    s = SCRIPT.read_text(encoding="utf-8")
    assert re.search(r"Set-Content \(Join-Path \$Out 'runtime\.version'\) \$hash -NoNewline", s)
    lib = LIB.read_text(encoding="utf-8")
    assert "certutil.exe" in lib and "-hashfile" in lib
    assert "fn parse_certutil_sha256" in lib
    i_verify, i_tar = lib.index("zip_sha256(&zip)"), lib.index('.join("tar.exe")')
    assert i_verify < i_tar  # verified before extraction
    assert "does not match runtime.version" in lib
