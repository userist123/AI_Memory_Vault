from __future__ import annotations

import subprocess
import sys
from pathlib import Path


def test_repository_layout_validator_passes_for_this_checkout():
    repo = Path(__file__).resolve().parents[2]
    script = repo / "30_SCRIPTS" / "verification" / "validate_repository_layout.py"
    result = subprocess.run(
        [sys.executable, str(script), "--repo", str(repo)],
        cwd=repo,
        text=True,
        capture_output=True,
    )
    assert result.returncode == 0, result.stdout + "\n" + result.stderr
