"""Generate the R001 machine-readable tracked-file inventory.

The generator is intentionally based on Git's index/tree rather than the
working directory so the inventory is reproducible for an exact commit.
"""
from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
START_SHA = "814ec77485a0621c388dac5f61cf0bb091707c0a"
OUT = ROOT / "03_IMPLEMENTATION" / "products" / "xau_kinetic" / "repository_map" / "R001_RUNTIME_MIGRATION_INVENTORY.json"


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def classify(path: str) -> tuple[str, str, str, str]:
    parts = path.split("/")
    top = parts[0]

    if path.startswith("03_IMPLEMENTATION/packages/"):
        return "implementation", "03_IMPLEMENTATION/packages/", "KEEP", "runtime-classification"
    if path.startswith("03_IMPLEMENTATION/products/xau_kinetic/"):
        return "XAU", "03_IMPLEMENTATION/products/xau_kinetic/", "KEEP", "product-runtime"
    if path.startswith("04_CONFIG/"):
        return "configuration", "04_CONFIG/", "KEEP", "configuration-boundary"
    if path.startswith("06_INBOX/"):
        return "import brut", "09_SECURITY/quarantine/manifests/", "DELETE", "raw-import-boundary"
    if path.startswith("20_TESTS/") or "/tests/" in path:
        return "test", "20_TESTS/", "KEEP", "test-suite"
    if path.startswith("40_EXPERIMENTS/") or path.endswith(".ipynb"):
        return "benchmark", "40_EXPERIMENTS/", "KEEP", "experiment-only"
    if path.startswith("07_EVALUATION/reports/") or "/reports/" in path:
        return "report", "07_EVALUATION/reports/", "KEEP", "evaluation-artifact"
    if path.startswith("08_OBSERVABILITY/"):
        return "observability", "08_OBSERVABILITY/", "KEEP", "evidence-log"
    if top == "99_META" or path.startswith("99_META/"):
        return "archive", "99_META/", "KEEP", "migration-history"
    if top.startswith(("00_", "01_", "02_", "03_", "04_", "05_", "06_", "07_", "08_", "09_", "10_", "20_", "30_", "40_", "50_", "60_", "70_", "80_", "90_", "99_")):
        return "repository-structure", top + "/", "KEEP", "numbered-spine"
    if top in {".env.example", ".gitignore", ".gitleaks.toml", ".pre-commit-config.yaml", "pytest.ini", "requirements-memory-v6.txt", "pyproject.toml"}:
        return "configuration", top, "KEEP", "root-config-allowlist"
    return "docs", "review", "IGNORE", "root-allowlist-review"


def main() -> int:
    tracked = git("ls-files", "-z").split("\0")
    tracked = [p for p in tracked if p]
    head = git("rev-parse", "HEAD")
    rows: list[dict[str, Any]] = []
    for path in sorted(tracked):
        sha = git("rev-parse", f"HEAD:{path}")
        category, target, status, owner = classify(path)
        rows.append({
            "path": path,
            "sha": sha,
            "category": category,
            "target": target,
            "owner_logic": owner,
            "status": status,
            "import_reference_impact": "REVIEW_REQUIRED" if status in {"MOVE", "DELETE", "ARCHIVE"} else "NONE",
            "test_impact": "RECHECK_ON_MAIN" if status in {"MOVE", "DELETE", "ARCHIVE"} else "NONE",
        })
    payload = {
        "schema_version": "R001.v1",
        "start_sha": START_SHA,
        "generated_from_sha": head,
        "generator": "30_SCRIPTS/verification/build_r001_inventory.py",
        "source": "git ls-files + git rev-parse HEAD:<path>",
        "tracked_file_count": len(rows),
        "entries": rows,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"INVENTORY_WRITTEN={OUT}")
    print(f"START_SHA={START_SHA}")
    print(f"GENERATED_FROM_SHA={head}")
    print(f"TRACKED_FILE_COUNT={len(rows)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
