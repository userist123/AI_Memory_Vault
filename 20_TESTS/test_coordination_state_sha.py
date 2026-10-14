"""A CURRENT.md must not present an unreachable SHA as live state.

`current_main_sha` / `base_main_sha` in the coordination files pointed at
commits that no longer exist in the repository, while the file still said
ACTIVE. A SHA must either resolve in git, or the file must declare itself a
`HISTORICAL_SNAPSHOT` (the live measured state is 00_GOVERNANCE/VAULT_STATE.md).
"""
from __future__ import annotations

import re
import subprocess
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
COORD = REPO / "00_GOVERNANCE" / "coordination"
_SHA = re.compile(r"^(current_main_sha|base_main_sha):\s*([0-9a-f]{7,40})\s*$", re.M)
_SNAPSHOT = re.compile(r"^(?:snapshot_)?status:\s*HISTORICAL_SNAPSHOT\s*$", re.M)


def reachable(sha: str, repo: Path = REPO) -> bool:
    r = subprocess.run(["git", "-C", str(repo), "cat-file", "-e", f"{sha}^{{commit}}"],
                       capture_output=True)
    return r.returncode == 0


def violations(text: str, repo: Path = REPO) -> list[str]:
    if _SNAPSHOT.search(text):
        return []
    return [sha for _, sha in _SHA.findall(text) if not reachable(sha, repo)]


def _git_available() -> bool:
    return reachable("HEAD")


pytestmark = pytest.mark.skipif(not _git_available(), reason="not a git checkout")

FILES = sorted(COORD.rglob("CURRENT.md"))


@pytest.mark.parametrize("path", FILES, ids=lambda p: p.relative_to(COORD).as_posix())
def test_sha_reachable_or_marked_historical(path):
    bad = violations(path.read_text(encoding="utf-8"))
    assert not bad, f"{path.name}: unreachable SHA(s) {bad} without status: HISTORICAL_SNAPSHOT"


def test_negative_unreachable_sha_without_marker_is_flagged():
    text = "---\ncurrent_main_sha: " + "deadbeef" * 5 + "\nstatus: ACTIVE\n---\n"
    assert violations(text) == ["deadbeef" * 5]
    assert violations(text.replace("ACTIVE", "HISTORICAL_SNAPSHOT")) == []
