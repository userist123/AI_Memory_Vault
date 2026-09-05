"""vault_hygiene.py contract tests (kept alongside the P1.2/P2.1 suite since
it shares cognitive_core/vault_index.py; triage ownership is Antigravity's
P0.3 front). Fully offline. `apply` is exercised only against tmp_path
copies, never the real vault."""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

_SPEC = importlib.util.spec_from_file_location(
    "vault_hygiene_under_test",
    Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "knowledge" / "vault_hygiene.py",
)
vault_hygiene = importlib.util.module_from_spec(_SPEC)
sys.modules["vault_hygiene_under_test"] = vault_hygiene
_SPEC.loader.exec_module(vault_hygiene)

from cognitive_core.vault_index import VaultIndex  # noqa: E402


def _write(root: Path, rel: str, fm: str, body: str) -> Path:
    path = root / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f"---\n{fm}\n---\n{body}", encoding="utf-8")
    return path


def test_boilerplate_note_is_classified(tmp_path):
    _write(tmp_path, "01_ARCHITECTURE/a.md", "id: a\ntype: lesson",
           "Action blocked by Autonomy Policy")
    idx = VaultIndex.load(tmp_path, drop_navigation=False)
    buckets = vault_hygiene.classify(idx)
    assert any(r["id"] == "a" for r in buckets.get("boilerplate", []))


def test_apply_archive_preserves_other_frontmatter_fields(tmp_path):
    path = _write(tmp_path, "01_ARCHITECTURE/a.md",
                  "id: a\ntype: lesson\ncustom_field: keep-me",
                  "Action blocked by Autonomy Policy")
    idx = VaultIndex.load(tmp_path, drop_navigation=False)
    buckets = vault_hygiene.classify(idx)
    recs = buckets.get("boilerplate", [])
    n = vault_hygiene.apply_archive(tmp_path, recs, "boilerplate")
    assert n == 1
    text = path.read_text(encoding="utf-8")
    assert "lifecycle: ARCHIVED" in text
    assert "archived_reason: boilerplate" in text
    assert "custom_field: keep-me" in text
    assert "Action blocked by Autonomy Policy" in text  # body untouched


def test_report_command_never_writes(tmp_path):
    path = _write(tmp_path, "01_ARCHITECTURE/a.md", "id: a\ntype: lesson", "short")
    before = path.read_text(encoding="utf-8")
    idx = VaultIndex.load(tmp_path, drop_navigation=False)
    vault_hygiene.classify(idx)  # report path never calls apply_archive
    after = path.read_text(encoding="utf-8")
    assert before == after


def test_ensure_utf8_stdout_does_not_raise():
    vault_hygiene._ensure_utf8_stdout()
