from __future__ import annotations

import importlib.util
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "verification" / "repository_hygiene.py"

spec = importlib.util.spec_from_file_location("repository_hygiene", SCRIPT)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def test_numbered_root_allows_only_contract_files(tmp_path: Path):
    target = tmp_path / "20_TESTS"
    target.mkdir()
    (target / "README.md").write_text("contract", encoding="utf-8")
    assert module.check_numbered_roots(tmp_path) == []

    (target / "notes.md").write_text("unexpected", encoding="utf-8")
    failures = module.check_numbered_roots(tmp_path)
    assert failures == ["NUMBERED_ROOT:20_TESTS:unexpected=['notes.md']"]


def test_inbox_rejects_everything_except_contract_files():
    assert module.check_inbox(["06_INBOX/README.md", "06_INBOX/.gitkeep"]) == []
    failures = module.check_inbox([
        "06_INBOX/raw.md",
        "06_INBOX/RAW_IMPORTS/source.md",
    ])
    assert failures == [
        "INBOX_TRACKED:06_INBOX/raw.md",
        "INBOX_TRACKED:06_INBOX/RAW_IMPORTS/source.md",
    ]


def test_production_rejects_runtime_artifacts():
    failures = module.check_production([
        "03_IMPLEMENTATION/packages/memory_vault/__pycache__/x.pyc",
        "03_IMPLEMENTATION/packages/memory_vault/report.ipynb",
        "cognitive_core/cache/output.sqlite3",
    ])
    assert any(item.startswith("PRODUCTION_RUNTIME_ARTIFACT:") for item in failures)
    assert any(item.startswith("PRODUCTION_FORBIDDEN_SUFFIX:") for item in failures)


def test_test_path_scan_detects_absolute_windows_paths(tmp_path: Path):
    test_file = tmp_path / "20_TESTS" / "example.py"
    test_file.parent.mkdir()
    test_file.write_text("LOG = r'C:\\Users\\example\\Desktop\\data.log'\n", encoding="utf-8")
    failures = module.check_test_paths(
        tmp_path,
        ["20_TESTS/example.py"],
    )
    assert failures == ["TEST_ABSOLUTE_PATH:20_TESTS/example.py"]
