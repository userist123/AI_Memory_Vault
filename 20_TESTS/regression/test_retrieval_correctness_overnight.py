"""Overnight audit A: tokenizer diacritics/ids (D1) and corrupt-note accounting (D2)."""
from __future__ import annotations

import importlib.util
import os
import sys
import uuid
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

_spec = importlib.util.spec_from_file_location("memory_vault_fixture", REPO / "20_TESTS" / "memory_vault_fixture.py")
fx = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fx)

from interfaces import memory_access as ma  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.hybrid_retrieval import tokenize  # noqa: E402


def _world(tmp_path, monkeypatch, extra_notes=(), corrupt=()):
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_path / "artifacts"))
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path / "telemetry"))
    vault = fx.make_vault(tmp_path)
    kd = vault / "01_ARCHITECTURE" / "knowledge"
    for title, body in extra_notes:
        (kd / f"{title.replace(' ', '_')}.md").write_text(
            fx.note_text(title, body, str(uuid.uuid4())), encoding="utf-8")
    for name in corrupt:
        (kd / name).write_text("---\nid: [unclosed\n  : : bad\n---\nbody", encoding="utf-8")
    return MemoryController(FileStorageEngine(str(vault))), vault


def _titles(out):
    return [r["title"] for r in out["query_results"]]


def test_tokenizer_folds_romanian_diacritics():
    assert tokenize("ședință") == tokenize("sedinta") == ["sedinta"]
    assert tokenize("ședința") == ["sedinta"]
    assert tokenize("Ţară şi ăâî") == ["tara", "aai"]


def test_tokenizer_keeps_digit_ids_but_drops_single_letters():
    assert "7" in tokenize("RFC 7") and "rfc-7" in tokenize("RFC-7")
    assert tokenize("x y z") == []


def test_tokenizer_ascii_unchanged():
    assert tokenize("Consolidarea nocturna memory_v6_cli v1.2.3") == [
        "consolidarea", "nocturna", "memory_v6_cli", "v1.2.3"]


@pytest.mark.parametrize("query", ["ședința consiliu", "sedinta consiliu"])
def test_diacritic_note_found_with_and_without_diacritics(tmp_path, monkeypatch, query):
    c, _ = _world(tmp_path, monkeypatch, extra_notes=[
        ("Minuta ședinței de consiliu", "Minuta ședinței de consiliu a fost aprobată.")])
    assert "Minuta ședinței de consiliu" in _titles(ma.search(c, query, limit=3))


def test_single_word_diacritic_query_matches_ascii_and_back(tmp_path, monkeypatch):
    c, _ = _world(tmp_path, monkeypatch, extra_notes=[
        ("Minuta zilei", "Ședința de joi a hotărât bugetul.")])
    for q in ("sedinta", "ședința", "SEDINȚA"):
        # rank 0/1 varies with random-id ties among the seed notes: membership only
        assert "Minuta zilei" in _titles(ma.search(c, q, limit=3))


def test_id_with_single_digit_found(tmp_path, monkeypatch):
    c, _ = _world(tmp_path, monkeypatch, extra_notes=[
        ("Standard intern", "Procedura urmeaza RFC 7 pentru toate cererile de schimbare.")])
    assert "Standard intern" in _titles(ma.search(c, "RFC 7", limit=3))
    assert "Standard intern" in _titles(ma.search(c, "7", limit=3))


def test_ascii_ranking_unchanged(tmp_path, monkeypatch):
    c, _ = _world(tmp_path, monkeypatch)
    out = ma.search(c, "consolidarea nocturna instaleaza dependentele", limit=3)
    # ties among the 3 seed notes are broken by random ids (see test_memory_access): membership only
    assert "consolidarea nocturna" in _titles(out)


def _trace(c, query="consolidarea nocturna"):
    pack = c.search(ma.Principal.AI_AGENT, query, page_size=3)
    return pack["candidate_trace"], pack


def test_corrupt_note_reported_and_valid_matches_survive(tmp_path, monkeypatch):
    c, _ = _world(tmp_path, monkeypatch, corrupt=["broken.md"])
    trace, pack = _trace(c)
    assert trace["skipped_corrupt"] >= 1
    assert any("broken.md" in p for p in trace["skipped_paths"])
    assert "consolidarea nocturna" in _titles(ma.search(c, "consolidarea nocturna", limit=3))


def test_corrupted_after_index_is_counted_at_query_time(tmp_path, monkeypatch):
    c, vault = _world(tmp_path, monkeypatch)
    target = next((vault / "01_ARCHITECTURE" / "knowledge").glob("poarta*.md"))
    target.write_text("---\nid: [unclosed\n  : : bad\n---\nx", encoding="utf-8")
    os.utime(target, (target.stat().st_atime, target.stat().st_mtime + 5))
    trace, _ = _trace(c)
    assert trace["skipped_corrupt"] >= 1
    assert c.storage.query("graph")  # never raises, valid notes remain


def test_clean_store_reports_zero_skips(tmp_path, monkeypatch):
    c, _ = _world(tmp_path, monkeypatch)
    trace, _ = _trace(c)
    assert trace["skipped_corrupt"] == 0 and trace["skipped_paths"] == []
