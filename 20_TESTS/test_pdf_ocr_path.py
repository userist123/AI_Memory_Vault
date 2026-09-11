"""OCR is opt-in, and it must fail loudly rather than convert to nothing.

Two scanned books in this corpus — Ashby's Introduction to Cybernetics and
Minsky's Society of Mind, 492 pages between them — have no text layer at all.
They were unusable until Tesseract was installed.

These tests do not require Tesseract. What they guard is the behaviour around
it: that a missing installation stops the run instead of producing empty
books, and that OCR is never chosen silently.
"""

from __future__ import annotations

import pathlib
import subprocess
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
_SCRIPT = _REPO / "30_SCRIPTS" / "ingestion" / "convert_pdf_to_text.py"
sys.path.insert(0, str(_SCRIPT.parent))

import convert_pdf_to_text as C  # noqa: E402


def test_tessdata_is_found_without_the_environment_variable(monkeypatch, tmp_path):
    """TESSDATA_PREFIX was unset even after a clean install on this machine.

    PyMuPDF takes the path directly, so the converter looks for it rather
    than requiring the user to export anything.
    """
    fake = tmp_path / "tessdata"
    fake.mkdir()
    monkeypatch.delenv("TESSDATA_PREFIX", raising=False)
    monkeypatch.setattr(C, "_TESSDATA_CANDIDATES", (str(fake),))
    assert C.find_tessdata() == str(fake)


def test_the_environment_variable_still_wins_when_it_points_somewhere_real(
    monkeypatch, tmp_path
):
    chosen = tmp_path / "explicit"
    chosen.mkdir()
    fallback = tmp_path / "fallback"
    fallback.mkdir()
    monkeypatch.setenv("TESSDATA_PREFIX", str(chosen))
    monkeypatch.setattr(C, "_TESSDATA_CANDIDATES", (str(fallback),))
    assert C.find_tessdata() == str(chosen)


def test_a_stale_environment_variable_is_ignored(monkeypatch, tmp_path):
    """Pointing at a directory that does not exist must not win over a real one."""
    real = tmp_path / "real"
    real.mkdir()
    monkeypatch.setenv("TESSDATA_PREFIX", str(tmp_path / "gone"))
    monkeypatch.setattr(C, "_TESSDATA_CANDIDATES", (str(real),))
    assert C.find_tessdata() == str(real)


def test_no_tesseract_anywhere_reports_nothing_rather_than_guessing(
    monkeypatch, tmp_path
):
    monkeypatch.delenv("TESSDATA_PREFIX", raising=False)
    monkeypatch.setattr(C, "_TESSDATA_CANDIDATES", (str(tmp_path / "absent"),))
    assert C.find_tessdata() == ""


def test_ocr_requested_without_tesseract_stops_the_run(tmp_path, monkeypatch):
    """A silent no-op here reproduces exactly what --ocr exists to remove.

    Without this the run would report a scanned book as converted and write
    an empty file, which is the failure shape this lineage keeps producing.
    """
    (tmp_path / "x.pdf").write_bytes(b"%PDF-1.4\n%%EOF\n")
    env = dict(**{k: v for k, v in __import__("os").environ.items()})
    env["TESSDATA_PREFIX"] = str(tmp_path / "definitely-not-here")
    env["PYTHONIOENCODING"] = "utf-8"
    #: Point the candidate list at nothing via a sitecustomize-free route:
    #: run with a cwd where no real tessdata path resolves is not possible,
    #: so assert on the message only when Tesseract is genuinely absent.
    if C.find_tessdata():
        import pytest

        pytest.skip("Tesseract is installed here; the absent-path case cannot run")

    result = subprocess.run(
        [sys.executable, str(_SCRIPT), str(tmp_path), "--ocr"],
        capture_output=True, text=True, env=env,
    )
    assert result.returncode != 0
    assert "tessdata could not be found" in (result.stdout + result.stderr)


def test_ocr_is_off_by_default():
    """OCR text is a different quality of input from a real text layer.

    Which one produced a book has to be a recorded decision, not a fallback
    that happens when extraction comes back empty.
    """
    source = _SCRIPT.read_text(encoding="utf-8")
    assert '"--ocr", action="store_true"' in source
    assert 'if density < MIN_CHARS_PER_PAGE and ocr_tessdata:' in source
    #: and the mode reaches the report, so a reader can tell them apart
    assert 'mode="ocr"' in source


def test_the_module_imports_without_pymupdf():
    """A module that refuses to be imported takes the whole suite with it.

    This module called sys.exit() at import when pymupdf was missing. From
    module scope that does not skip a test, it kills pytest's collector: both
    CI gates ended with "mainloop: caught unexpected SystemExit!" and "no
    tests ran", reporting failure having executed nothing.

    The dependency is now checked at the point of use.
    """
    source = _SCRIPT.read_text(encoding="utf-8")
    assert "    pymupdf = None" in source, (
        "a missing pymupdf must leave the module importable"
    )
    assert "def require_pymupdf" in source
    assert "    require_pymupdf()" in source, (
        "the check has to run somewhere, or a missing dependency becomes an "
        "AttributeError deep in a conversion"
    )
    #: and no exit may run at import. Checked against code rather than text:
    #: the comment explaining this defect contains the word `sys.exit(`, and
    #: a naive substring search matches the explanation instead of the bug.
    head = source.split("def require_pymupdf", 1)[0]
    code = [
        line for line in head.splitlines()
        if line.strip() and not line.lstrip().startswith(("#", '"""', "'''"))
    ]
    assert not any("sys.exit(" in line for line in code), (
        "module scope must not exit; it kills pytest's collector"
    )


def test_requiring_pymupdf_fails_loudly_when_it_is_absent(monkeypatch):
    monkeypatch.setattr(C, "pymupdf", None)
    with pytest.raises(SystemExit) as excinfo:
        C.require_pymupdf()
    assert "pymupdf is required" in str(excinfo.value)
