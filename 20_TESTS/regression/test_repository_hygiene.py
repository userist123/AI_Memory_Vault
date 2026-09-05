from __future__ import annotations

import importlib.util
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "verification" / "repository_hygiene.py"
SPEC = importlib.util.spec_from_file_location("repository_hygiene", SCRIPT)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def test_numbered_roots_allow_domain_children(tmp_path: Path) -> None:
    (tmp_path / "03_IMPLEMENTATION").mkdir()
    (tmp_path / "03_IMPLEMENTATION" / "packages").mkdir()
    (tmp_path / "20_TESTS").mkdir()
    (tmp_path / "20_TESTS" / "regression").mkdir()
    (tmp_path / "99_META").mkdir()

    assert MODULE.check_numbered_roots(tmp_path) == []


def test_numbered_root_name_is_enforced(tmp_path: Path) -> None:
    (tmp_path / "03_IMPLEMENTATION").mkdir()
    (tmp_path / "3_BROKEN").mkdir()

    assert MODULE.check_numbered_roots(tmp_path) == ["NUMBERED_ROOT_NAME:3_BROKEN"]


def test_inbox_boundary_rejects_tracked_content() -> None:
    assert MODULE.check_inbox(["06_INBOX/README.md", "06_INBOX/.gitkeep"]) == []
    assert MODULE.check_inbox(["06_INBOX/RAW_IMPORTS/report.md"]) == [
        "INBOX_TRACKED:06_INBOX/RAW_IMPORTS/report.md"
    ]


def test_test_path_absolute_windows_reference_is_rejected(tmp_path: Path) -> None:
    test_file = tmp_path / "20_TESTS" / "sample.py"
    test_file.parent.mkdir()
    test_file.write_text('LOG = r"C:\\\\evidence\\\\run.log"\n', encoding="utf-8")

    assert MODULE.check_test_paths(tmp_path, ["20_TESTS/sample.py"]) == [
        "TEST_ABSOLUTE_PATH:20_TESTS/sample.py"
    ]
