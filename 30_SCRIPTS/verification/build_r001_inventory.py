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
OUT = ROOT / "99_META" / "repository_map" / "R001_RUNTIME_MIGRATION_INVENTORY.json"


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def classify(path: str) -> tuple[str, str, str, str]:
    parts = path.split("/")
    top = parts[0]
    if top in {"cognitive_core", "memory_controller", "vault_api.py"}:
        return "runtime", "03_IMPLEMENTATION/packages/memory_vault/", "MOVE", "legacy-root-runtime"
    if top in {"XAU_Kinetic.Desktop", "XAU_Kinetic_Standalone", "xau_kinetic"}:
        return "XAU", "03_IMPLEMENTATION/products/xau_kinetic/", "MOVE", "product-runtime"
    if top == "AI_Memory_Vault_OBSIDIAN":
        return "Obsidian", "04_CONFIG/obsidian/", "MOVE", "gitlink-boundary"
    if top == "06_INBOX":
        return "import brut", "09_SECURITY/quarantine/manifests/", "DELETE", "raw-import-boundary"
    if top == "tests" or "/tests/" in path:
        return "test", "20_TESTS/", "MOVE", "test-suite"
    if top == "benchmarks" or "/benchmarks/" in path or path.endswith(".ipynb"):
        return "benchmark", "40_EXPERIMENTS/", "MOVE", "experiment-only"
    if top == "reports" or "/reports/" in path:
        return "report", "07_EVALUATION/reports/", "MOVE", "evaluation-artifact"
    if top in {"audit_log.jsonl", "test_audit_log.jsonl"}:
        return "observability", "08_OBSERVABILITY/", "MOVE", "evidence-log"
    if top.startswith("migration_") or top.startswith("MIGRATION") or top == "99_META":
        return "archive", "99_META/migration_logs/", "ARCHIVE", "migration-history"
    if top in {".env.example", ".gitignore", ".gitleaks.toml", ".pre-commit-config.yaml", "pytest.ini", "requirements-memory-v6.txt"}:
        return "configuration", top, "KEEP", "root-config-allowlist"
    if top.startswith("00_") or top.startswith("01_") or top.startswith("02_") or top.startswith("03_") or top.startswith("04_") or top.startswith("05_") or top.startswith("06_") or top.startswith("07_") or top.startswith("08_") or top.startswith("09_") or top.startswith("10_") or top.startswith("20_") or top.startswith("30_") or top.startswith("40_") or top.startswith("50_") or top.startswith("60_") or top.startswith("70_") or top.startswith("80_") or top.startswith("90_") or top.startswith("99_"):
        return "repository-structure", top + "/", "KEEP", "numbered-spine"
    return "docs", "review", "IGNORE", "root-allowlist-review"


def main() -> int:
    tracked = git("ls-files", "-z").split("\0")
    tracked = [p for p in tracked if p]
    head = git("rev-parse", "HEAD")
    rows: list[dict[str, Any]] = []
    for path in sorted(tracked):
        sha = git("rev-parse", f"HEAD:{path}") if not path.startswith(".") or path in {".gitignore", ".gitleaks.toml", ".pre-commit-config.yaml", ".env.example"} else git("rev-parse", f"HEAD:{path}")
        category, target, status, owner = classify(path)
        rows.append({
            "path": path,
            "sha": sha,
            "category": category,
            "target": target,
            "owner_logic": owner,
            "status": status,
            "import_reference_impact": "REVIEW_REQUIRED" if status in {"MOVE", "DELETE", "ARCHIVE"} else "NONE",
            "test_impact": "RECHECK_ON_R001_BRANCH" if status in {"MOVE", "DELETE", "ARCHIVE"} else "NONE",
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
