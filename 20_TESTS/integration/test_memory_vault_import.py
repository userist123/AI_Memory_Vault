from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_clean_checkout_import_smoke():
    repo = Path(__file__).resolve().parents[2]
    env = os.environ.copy()
    env["PYTHONPATH"] = str(repo / "03_IMPLEMENTATION" / "packages")
    result = subprocess.run(
        [sys.executable, "-c", "import memory_vault; import memory_vault.memory; import memory_vault.graph"],
        cwd=repo,
        env=env,
        text=True,
        capture_output=True,
    )
    assert result.returncode == 0, result.stdout + "\n" + result.stderr
